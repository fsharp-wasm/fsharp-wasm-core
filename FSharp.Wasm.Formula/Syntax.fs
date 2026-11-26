namespace FSharp.Wasm.Formula

open FSharp.Wasm.Data

/// A cell address, columns and rows from 0 (A1 is column 0, row 0), with the `$` of an absolute column or row.
type CellRef = { Col: int; Row: int; AbsCol: bool; AbsRow: bool }

/// The size of a sheet: columns A to XFD (16 384) and as many rows.  A cell is one number, `Row * 16384 + Col`, which is what a dictionary of cells keys on.
module CellRef =
    [<Literal>]
    let Limit = 16384

    let create (col: int) (row: int) : CellRef = { Col = col; Row = row; AbsCol = false; AbsRow = false }
    let key (r: CellRef) : int = r.Row * Limit + r.Col
    let ofKey (k: int) : CellRef = create (k % Limit) (k / Limit)
    let valid (r: CellRef) : bool = r.Col >= 0 && r.Col < Limit && r.Row >= 0 && r.Row < Limit

    /// The letters of a column: 0 is A, 25 Z, 26 AA.
    let columnName (col: int) : string =
        let mutable n = col + 1
        let mutable s = ""
        while n > 0 do
            let r = (n - 1) % 26
            s <- string (char (int 'A' + r)) + s
            n <- (n - 1) / 26
        s

    /// A1, $B$7.
    let toString (r: CellRef) : string = (if r.AbsCol then "$" else "") + columnName r.Col + (if r.AbsRow then "$" else "") + string (r.Row + 1)

    /// `A1` or `$A$1` and nothing else; None for anything that is not a cell of the sheet.
    let tryParse (s: string) : CellRef option =
        let mutable i = 0
        let absCol = i < s.Length && s.[i] = '$'
        if absCol then i <- i + 1
        let mutable col = 0
        let mutable letters = 0
        while i < s.Length && ((s.[i] >= 'A' && s.[i] <= 'Z') || (s.[i] >= 'a' && s.[i] <= 'z')) do
            col <- col * 26 + (int (System.Char.ToUpperInvariant s.[i]) - int 'A' + 1)
            letters <- letters + 1
            i <- i + 1
        let absRow = i < s.Length && s.[i] = '$'
        if absRow then i <- i + 1
        let mutable row = 0
        let mutable digits = 0
        while i < s.Length && s.[i] >= '0' && s.[i] <= '9' && digits < 7 do
            row <- row * 10 + (int s.[i] - int '0')
            digits <- digits + 1
            i <- i + 1
        if letters = 0 || letters > 3 || digits = 0 || i <> s.Length || row = 0 then None
        else
            let r = { Col = col - 1; Row = row - 1; AbsCol = absCol; AbsRow = absRow }
            if valid r then Some r else None

    /// The reference as it reads when the formula that holds it is copied `dCol` columns and `dRow` rows away: what is absolute stays.
    let shift (dCol: int) (dRow: int) (r: CellRef) : CellRef =
        { r with Col = (if r.AbsCol then r.Col else r.Col + dCol); Row = (if r.AbsRow then r.Row else r.Row + dRow) }

type BinOp =
    | Add
    | Sub
    | Mul
    | Div
    | Pow
    | Concat
    | Eq
    | Ne
    | Lt
    | Le
    | Gt
    | Ge

/// A formula.
type Expr =
    | Num of float
    | Str of string
    | Bool of bool
    | Cell of CellRef
    /// A1:B5, from one corner to the other (in either order).
    | Range of CellRef * CellRef
    /// A name the host resolves (Actual, Budget): a measure, a constant.
    | Name of string
    | Neg of Expr
    /// 5% is 0.05.
    | Percent of Expr
    | Bin of BinOp * Expr * Expr
    | Call of string * Expr list

/// Where a formula went wrong, and how.
type ParseError = { Position: int; Message: string }

type private Tok =
    | TNum of float
    | TStr of string
    | TId of string
    | TOp of string
    | TEnd

module Parser =
    let private isDigit (c: char) = c >= '0' && c <= '9'
    let private isLetter (c: char) = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || c = '_' || c = '$'

    /// The tokens with the place each one starts at; or the first character that belongs to none.
    let private tokenize (s: string) : Result<(Tok * int)[], ParseError> =
        let out = ResizeArray<Tok * int>()
        let mutable i = 0
        let mutable error : ParseError option = None
        while error.IsNone && i < s.Length do
            let c = s.[i]
            let start = i
            if c = ' ' || c = '\t' then i <- i + 1
            elif isDigit c || (c = '.' && i + 1 < s.Length && isDigit s.[i + 1]) then
                while i < s.Length && (isDigit s.[i] || s.[i] = '.') do i <- i + 1
                // an exponent: e or E, an optional sign, digits
                if i < s.Length && (s.[i] = 'e' || s.[i] = 'E') then
                    let mutable j = i + 1
                    if j < s.Length && (s.[j] = '+' || s.[j] = '-') then j <- j + 1
                    if j < s.Length && isDigit s.[j] then
                        while j < s.Length && isDigit s.[j] do j <- j + 1
                        i <- j
                // the number parser of the data library wants a digit first (.5 is JSON's error, a spreadsheet's 0.5)
                let digits = s.Substring(start, i - start)
                match Numbers.parseFloat (if digits.StartsWith "." then "0" + digits else digits) with
                | Some v -> out.Add((TNum v, start))
                | None -> error <- Some { Position = start; Message = "not a number: " + s.Substring(start, i - start) }
            elif c = '"' then
                // text in double quotes; a quote inside is doubled
                let sb = System.Text.StringBuilder()
                i <- i + 1
                let mutable closed = false
                while not closed && i < s.Length do
                    if s.[i] = '"' then
                        if i + 1 < s.Length && s.[i + 1] = '"' then
                            sb.Append('"') |> ignore
                            i <- i + 2
                        else
                            closed <- true
                            i <- i + 1
                    else
                        sb.Append(s.[i]) |> ignore
                        i <- i + 1
                if closed then out.Add((TStr(sb.ToString()), start)) else error <- Some { Position = start; Message = "text is not closed" }
            elif isLetter c then
                while i < s.Length && (isLetter s.[i] || isDigit s.[i] || s.[i] = '.') do i <- i + 1
                out.Add((TId(s.Substring(start, i - start)), start))
            elif (c = '<' || c = '>') && i + 1 < s.Length && (s.[i + 1] = '=' || (c = '<' && s.[i + 1] = '>')) then
                out.Add((TOp(s.Substring(i, 2)), start))
                i <- i + 2
            elif "+-*/^&=<>%(),;:".IndexOf c >= 0 then
                out.Add((TOp(string c), start))
                i <- i + 1
            else error <- Some { Position = i; Message = "unexpected " + string c }
        match error with
        | Some e -> Error e
        | None ->
            out.Add((TEnd, s.Length))
            Ok (out.ToArray())

    // How tightly an operator binds: a comparison least, then &, + -, * /, ^.  As in a spreadsheet, -2^2 is 4 (a minus binds tighter than ^) and ^ goes from the left.
    let private binary (op: string) : (BinOp * int) option =
        match op with
        | "=" -> Some (Eq, 10)
        | "<>" -> Some (Ne, 10)
        | "<" -> Some (Lt, 10)
        | "<=" -> Some (Le, 10)
        | ">" -> Some (Gt, 10)
        | ">=" -> Some (Ge, 10)
        | "&" -> Some (Concat, 20)
        | "+" -> Some (Add, 30)
        | "-" -> Some (Sub, 30)
        | "*" -> Some (Mul, 40)
        | "/" -> Some (Div, 40)
        | "^" -> Some (Pow, 50)
        | _ -> None

    [<Literal>]
    let private PrefixPower = 60

    /// A formula, with or without the `=` it starts with.
    let parse (source: string) : Result<Expr, ParseError> =
        let text = source.Trim()
        let offset = source.Length - source.TrimStart().Length
        let body = if text.StartsWith "=" then text.Substring 1 else text
        let shiftBy = offset + (if text.StartsWith "=" then 1 else 0)
        match tokenize body with
        | Error e -> Error { e with Position = e.Position + shiftBy }
        | Ok toks ->
            let mutable at = 0
            let mutable failure : ParseError option = None
            let peek () = fst toks.[at]
            let place () = snd toks.[at] + shiftBy
            let fail (message: string) : Expr =
                if failure.IsNone then failure <- Some { Position = place (); Message = message }
                Num 0.0
            let advance () = if at < toks.Length - 1 then at <- at + 1
            let describe (t: Tok) =
                match t with
                | TEnd -> "the end of the formula"
                | TOp o -> "'" + o + "'"
                | TId n -> n
                | TNum _ -> "a number"
                | TStr _ -> "text"
            let rec expr (minPower: int) : Expr =
                let mutable left = prefix ()
                let mutable go = failure.IsNone
                while go do
                    match peek () with
                    | TOp "%" ->
                        advance ()
                        left <- Percent left
                    | TOp o ->
                        match binary o with
                        | Some (op, power) when power >= minPower ->
                            advance ()
                            let right = expr (power + 1)
                            left <- Bin (op, left, right)
                        | _ -> go <- false
                    | _ -> go <- false
                    if failure.IsSome then go <- false
                left
            and prefix () : Expr =
                match peek () with
                | TOp "-" ->
                    advance ()
                    Neg (expr PrefixPower)
                | TOp "+" ->
                    advance ()
                    expr PrefixPower
                | TNum v ->
                    advance ()
                    Num v
                | TStr s ->
                    advance ()
                    Str s
                | TOp "(" ->
                    advance ()
                    let inner = expr 0
                    (match peek () with
                     | TOp ")" -> advance ()
                     | t -> fail ("expected ')' and found " + describe t) |> ignore)
                    inner
                | TId name ->
                    let here = place ()
                    advance ()
                    (match peek () with
                     | TOp "(" ->
                        advance ()
                        let args = ResizeArray<Expr>()
                        (match peek () with
                         | TOp ")" -> advance ()
                         | _ ->
                            let mutable more = true
                            while more && failure.IsNone do
                                args.Add(expr 0)
                                match peek () with
                                | TOp "," | TOp ";" -> advance ()
                                | TOp ")" ->
                                    advance ()
                                    more <- false
                                | t ->
                                    fail ("expected ',' or ')' and found " + describe t) |> ignore
                                    more <- false)
                        Call (name.ToUpperInvariant(), List.ofSeq args)
                     | _ ->
                        match name.ToUpperInvariant() with
                        | "TRUE" -> Bool true
                        | "FALSE" -> Bool false
                        | _ ->
                            match CellRef.tryParse name with
                            | Some a ->
                                (match peek () with
                                 | TOp ":" ->
                                    advance ()
                                    (match peek () with
                                     | TId other ->
                                        (match CellRef.tryParse other with
                                         | Some b ->
                                            advance ()
                                            Range (a, b)
                                         | None -> fail ("expected a cell after ':' and found " + other))
                                     | t -> fail ("expected a cell after ':' and found " + describe t))
                                 | _ -> Cell a)
                            | None ->
                                if failure.IsNone && name.Length > 0 && name.[0] = '$' then failure <- Some { Position = here; Message = "not a cell: " + name }
                                Name name)
                | t -> fail ("expected a value and found " + describe t)
            let e = expr 0
            match failure with
            | Some f -> Error f
            | None ->
                match peek () with
                | TEnd -> Ok e
                | t -> Error { Position = place (); Message = "unexpected " + describe t }

    /// The formula back as text, with the minimum of parentheses that keeps its meaning.
    let rec print (e: Expr) : string =
        let power (e: Expr) =
            match e with
            | Bin (op, _, _) -> (match op with Eq | Ne | Lt | Le | Gt | Ge -> 10 | Concat -> 20 | Add | Sub -> 30 | Mul | Div -> 40 | Pow -> 50)
            | Neg _ -> PrefixPower
            | _ -> 100
        let wrap (inner: Expr) (min: int) = if power inner < min then "(" + print inner + ")" else print inner
        match e with
        | Num v -> if v - System.Math.Floor v = 0.0 && abs v < 1.0e15 then string (int64 v) else string v
        | Str s -> "\"" + s.Replace("\"", "\"\"") + "\""
        | Bool b -> if b then "TRUE" else "FALSE"
        | Cell r -> CellRef.toString r
        | Range (a, b) -> CellRef.toString a + ":" + CellRef.toString b
        | Name n -> n
        | Neg x -> "-" + wrap x PrefixPower
        | Percent x -> wrap x 100 + "%"
        | Call (name, args) -> name + "(" + String.concat "," (args |> List.map print) + ")"
        | Bin (op, a, b) ->
            let (symbol, p) =
                match op with
                | Eq -> ("=", 10) | Ne -> ("<>", 10) | Lt -> ("<", 10) | Le -> ("<=", 10) | Gt -> (">", 10) | Ge -> (">=", 10)
                | Concat -> ("&", 20) | Add -> ("+", 30) | Sub -> ("-", 30) | Mul -> ("*", 40) | Div -> ("/", 40) | Pow -> ("^", 50)
            // from the left: the right operand of an operator of the same power needs its parentheses
            wrap a p + symbol + wrap b (p + 1)

    /// What a formula reads: its cells, and its ranges (as two corners).  A cell read by a range is not listed twice.
    let rec references (e: Expr) (cells: ResizeArray<CellRef>) (ranges: ResizeArray<CellRef * CellRef>) : unit =
        match e with
        | Cell r -> cells.Add r
        | Range (a, b) -> ranges.Add((a, b))
        | Neg x | Percent x -> references x cells ranges
        | Bin (_, a, b) ->
            references a cells ranges
            references b cells ranges
        | Call (_, args) -> for a in args do references a cells ranges
        | Num _ | Str _ | Bool _ | Name _ -> ()

    /// The formula as it reads when it is copied `dCol` columns and `dRow` rows away (what is absolute stays).
    let rec shift (dCol: int) (dRow: int) (e: Expr) : Expr =
        match e with
        | Cell r -> Cell (CellRef.shift dCol dRow r)
        | Range (a, b) -> Range (CellRef.shift dCol dRow a, CellRef.shift dCol dRow b)
        | Neg x -> Neg (shift dCol dRow x)
        | Percent x -> Percent (shift dCol dRow x)
        | Bin (op, a, b) -> Bin (op, shift dCol dRow a, shift dCol dRow b)
        | Call (n, args) -> Call (n, args |> List.map (shift dCol dRow))
        | other -> other
