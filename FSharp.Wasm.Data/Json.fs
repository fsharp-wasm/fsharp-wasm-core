namespace FSharp.Wasm.Data

open System.Text

/// JSON, written out so that nothing depends on System.Text.Json: a value type, a writer and a parser for what an API or a document of
/// this size exchanges.  Numbers are doubles; the parser takes standard JSON (RFC 8259: strings with \uXXXX escapes, no raw control
/// characters in strings, nested arrays and objects up to a depth of 64) and reports anything else with the position of the first
/// problem — with no exceptions, which the WebAssembly target carries without a payload.
type Json =
    | JNull
    | JBool of bool
    | JNum of float
    | JStr of string
    | JArr of Json list
    | JObj of (string * Json) list

module Json =
    /// Objects and arrays nest at most this deep: what a hostile document can cost the stack is bounded.
    [<Literal>]
    let MaxDepth = 64

    // ───────────────────────────── writing ─────────────────────────────
    let private hexDigit (n: int) : string = "0123456789abcdef".Substring(n &&& 0xF, 1)

    let private writeString (sb: StringBuilder) (s: string) : unit =
        sb.Append('"') |> ignore
        for c in s do
            if c = '"' then sb.Append("\\\"") |> ignore
            elif c = '\\' then sb.Append("\\\\") |> ignore
            elif c = '\n' then sb.Append("\\n") |> ignore
            elif c = '\r' then sb.Append("\\r") |> ignore
            elif c = '\t' then sb.Append("\\t") |> ignore
            elif int c < 0x20 then sb.Append("\\u00").Append(hexDigit (int c >>> 4)).Append(hexDigit (int c)) |> ignore
            else sb.Append(c) |> ignore
        sb.Append('"') |> ignore

    let private writeNumber (sb: StringBuilder) (n: float) : unit =
        // integers print without a fraction; JSON has no NaN or Infinity
        if not (Numbers.isFinite n) then sb.Append("null") |> ignore
        elif n = System.Math.Floor n && abs n < 1e15 then sb.Append(string (int64 n)) |> ignore
        else sb.Append(string n) |> ignore

    /// One writer for both forms: `pretty` puts every element on its own line, two spaces per level.
    let rec private write (sb: StringBuilder) (pretty: bool) (depth: int) (v: Json) : unit =
        let newline (d: int) =
            if pretty then
                sb.Append('\n') |> ignore
                for _ in 1 .. d do sb.Append("  ") |> ignore
        match v with
        | JNull -> sb.Append("null") |> ignore
        | JBool true -> sb.Append("true") |> ignore
        | JBool false -> sb.Append("false") |> ignore
        | JNum n -> writeNumber sb n
        | JStr s -> writeString sb s
        | JArr [] -> sb.Append("[]") |> ignore
        | JArr items ->
            sb.Append('[') |> ignore
            let mutable first = true
            for item in items do
                if not first then sb.Append(',') |> ignore
                first <- false
                newline (depth + 1)
                write sb pretty (depth + 1) item
            newline depth
            sb.Append(']') |> ignore
        | JObj [] -> sb.Append("{}") |> ignore
        | JObj fields ->
            sb.Append('{') |> ignore
            let mutable first = true
            for (key, value) in fields do
                if not first then sb.Append(',') |> ignore
                first <- false
                newline (depth + 1)
                writeString sb key
                sb.Append(if pretty then ": " else ":") |> ignore
                write sb pretty (depth + 1) value
            newline depth
            sb.Append('}') |> ignore

    /// The value as compact JSON text.
    let stringify (v: Json) : string =
        let sb = StringBuilder()
        write sb false 0 v
        sb.ToString()

    /// The value as JSON text indented by two spaces: what a person reads and a file keeps.
    let pretty (v: Json) : string =
        let sb = StringBuilder()
        write sb true 0 v
        sb.ToString()

    // ───────────────────────────── parsing ─────────────────────────────
    /// The parser keeps its position and the first problem; a failure anywhere makes the whole parse fail.
    type private Parser(s: string) =
        let mutable pos = 0
        let mutable error = ""

        member _.Failed = error <> ""
        member _.Error = error
        member _.AtEnd = pos >= s.Length
        member _.Fail(message: string) : Json =
            if error = "" then error <- message + " at position " + string pos
            JNull
        member this.FailUnit(message: string) : unit = this.Fail message |> ignore
        member _.SkipWhitespace() =
            while pos < s.Length && (s.[pos] = ' ' || s.[pos] = '\n' || s.[pos] = '\r' || s.[pos] = '\t') do
                pos <- pos + 1
        member _.Peek : char = if pos < s.Length then s.[pos] else '\000'
        member _.Advance() = pos <- pos + 1
        member _.Literal(word: string) : bool =
            if pos + word.Length <= s.Length && s.Substring(pos, word.Length) = word then
                pos <- pos + word.Length
                true
            else false
        member this.Hex4() : int =
            if pos + 4 > s.Length then
                this.FailUnit "bad \\u escape"
                0
            else
                let mutable v = 0
                for i in 0 .. 3 do
                    let c = s.[pos + i]
                    let d =
                        if c >= '0' && c <= '9' then int c - int '0'
                        elif c >= 'a' && c <= 'f' then int c - int 'a' + 10
                        elif c >= 'A' && c <= 'F' then int c - int 'A' + 10
                        else -1
                    if d < 0 then this.FailUnit "bad \\u escape" else v <- v * 16 + d
                pos <- pos + 4
                v
        member this.String() : string =
            // the opening quote is at pos
            pos <- pos + 1
            let sb = StringBuilder()
            let mutable closed = false
            while not closed && not this.Failed do
                if pos >= s.Length then this.FailUnit "unterminated string"
                else
                    let c = s.[pos]
                    pos <- pos + 1
                    if c = '"' then closed <- true
                    elif c = '\\' then
                        if pos >= s.Length then this.FailUnit "unterminated string"
                        else
                            let e = s.[pos]
                            pos <- pos + 1
                            if e = '"' then sb.Append('"') |> ignore
                            elif e = '\\' then sb.Append('\\') |> ignore
                            elif e = '/' then sb.Append('/') |> ignore
                            elif e = 'n' then sb.Append('\n') |> ignore
                            elif e = 'r' then sb.Append('\r') |> ignore
                            elif e = 't' then sb.Append('\t') |> ignore
                            elif e = 'b' then sb.Append('\b') |> ignore
                            elif e = 'f' then sb.Append('\012') |> ignore
                            elif e = 'u' then sb.Append(char (this.Hex4())) |> ignore
                            else this.FailUnit "bad escape"
                    elif int c < 0x20 then this.FailUnit "control character in a string"
                    else sb.Append(c) |> ignore
            sb.ToString()
        member this.Number() : Json =
            let start = pos
            if pos < s.Length && s.[pos] = '-' then pos <- pos + 1
            while pos < s.Length && ((s.[pos] >= '0' && s.[pos] <= '9') || s.[pos] = '.' || s.[pos] = 'e' || s.[pos] = 'E' || s.[pos] = '+' || s.[pos] = '-') do
                pos <- pos + 1
            let text = s.Substring(start, pos - start)
            match Numbers.parseFloat text with
            | Some n -> JNum n
            | None ->
                pos <- start
                this.Fail "bad number"

    let rec private parseValue (p: Parser) (depth: int) : Json =
        p.SkipWhitespace()
        if depth > MaxDepth then p.Fail "nesting too deep"
        elif p.AtEnd then p.Fail "unexpected end"
        else
            match p.Peek with
            | '"' -> JStr(p.String())
            | '{' ->
                p.Advance()
                let fields = ResizeArray<string * Json>()
                p.SkipWhitespace()
                if p.Peek = '}' then p.Advance()
                else
                    let mutable more = true
                    while more && not p.Failed do
                        p.SkipWhitespace()
                        if p.Peek <> '"' then
                            more <- false
                            p.FailUnit "expected a string key"
                        else
                            let key = p.String()
                            p.SkipWhitespace()
                            if p.Peek <> ':' then
                                more <- false
                                p.FailUnit "expected ':'"
                            else
                                p.Advance()
                                let value = parseValue p (depth + 1)
                                fields.Add((key, value))
                                p.SkipWhitespace()
                                if p.Peek = ',' then p.Advance()
                                elif p.Peek = '}' then
                                    p.Advance()
                                    more <- false
                                else
                                    more <- false
                                    p.FailUnit "expected ',' or '}'"
                JObj(List.ofSeq fields)
            | '[' ->
                p.Advance()
                let items = ResizeArray<Json>()
                p.SkipWhitespace()
                if p.Peek = ']' then p.Advance()
                else
                    let mutable more = true
                    while more && not p.Failed do
                        items.Add(parseValue p (depth + 1))
                        p.SkipWhitespace()
                        if p.Peek = ',' then p.Advance()
                        elif p.Peek = ']' then
                            p.Advance()
                            more <- false
                        else
                            more <- false
                            p.FailUnit "expected ',' or ']'"
                JArr(List.ofSeq items)
            | 't' -> if p.Literal "true" then JBool true else p.Fail "unexpected character"
            | 'f' -> if p.Literal "false" then JBool false else p.Fail "unexpected character"
            | 'n' -> if p.Literal "null" then JNull else p.Fail "unexpected character"
            | c when c = '-' || (c >= '0' && c <= '9') -> p.Number()
            | _ -> p.Fail "unexpected character"

    /// Parses JSON text.  The error names the position of the first problem.
    let parse (text: string) : Result<Json, string> =
        let p = Parser text
        let v = parseValue p 0
        p.SkipWhitespace()
        if not p.Failed && not p.AtEnd then Error("unexpected text after the value at position " + string (text.Length))
        elif p.Failed then Error p.Error
        else Ok v

    /// Parses JSON text; None for anything that is not JSON.
    let tryParse (text: string) : Json option =
        match parse text with
        | Ok v -> Some v
        | Error _ -> None

    // ───────────────────────────── reading a value ─────────────────────────────
    /// The value of a field of an object.
    let field (name: string) (v: Json) : Json option =
        match v with
        | JObj fields -> fields |> List.tryFind (fun (k, _) -> k = name) |> Option.map snd
        | _ -> None

    let asString (v: Json option) : string option = match v with Some(JStr s) -> Some s | _ -> None
    let asNumber (v: Json option) : float option = match v with Some(JNum n) -> Some n | _ -> None
    let asBool (v: Json option) : bool option = match v with Some(JBool b) -> Some b | _ -> None

    // A document with a missing field should still load: these take a fallback.
    let stringOr (name: string) (fallback: string) (j: Json) : string = match field name j with Some(JStr s) -> s | _ -> fallback
    let numberOr (name: string) (fallback: float) (j: Json) : float = match field name j with Some(JNum n) -> n | _ -> fallback
    let intOr (name: string) (fallback: int) (j: Json) : int = match field name j with Some(JNum n) -> int n | _ -> fallback
    let boolOr (name: string) (fallback: bool) (j: Json) : bool = match field name j with Some(JBool b) -> b | _ -> fallback
    let listOf (name: string) (j: Json) : Json list = match field name j with Some(JArr items) -> items | _ -> []
