namespace FSharp.Wasm.Formula

open FSharp.Wasm.Data

/// What a spreadsheet cell can be wrong with.
type ErrorKind =
    /// #DIV/0!
    | DivZero
    /// #NAME?: a name or a function nobody knows
    | UnknownName
    /// #VALUE!: text where a number is needed, a range where a value is
    | WrongType
    /// #REF!: a cell that is not on the sheet
    | BadRef
    /// #NUM!: a result that is not a number (the root of a negative number), or one that is too big
    | BadNumber
    /// #CYCLE!: the cell depends on itself
    | Cycle
    /// #ERROR!: the formula does not parse
    | Syntax

/// What a cell holds.
type Value =
    | Number of float
    | Text of string
    | Logical of bool
    | Err of ErrorKind
    /// An empty cell: 0 in arithmetic, "" in text.
    | Blank

module Value =
    let errorText (k: ErrorKind) : string =
        match k with
        | DivZero -> "#DIV/0!"
        | UnknownName -> "#NAME?"
        | WrongType -> "#VALUE!"
        | BadRef -> "#REF!"
        | BadNumber -> "#NUM!"
        | Cycle -> "#CYCLE!"
        | Syntax -> "#ERROR!"

    /// A number as text: whole numbers without a point, the rest with up to ten digits after it and no trailing zeros.
    let numberText (v: float) : string =
        if not (Numbers.isFinite v) then "#NUM!"
        elif v = System.Math.Floor v && abs v < 1.0e15 then string (int64 v)
        else
            let sign = if v < 0.0 then "-" else ""
            let scaled = System.Math.Floor(abs v * 1.0e10 + 0.5)
            let whole = System.Math.Floor(scaled / 1.0e10)
            let frac = int64 (scaled - whole * 1.0e10)
            if frac = 0L then sign + string (int64 whole)
            else
                let digits = String.replicate (10 - (string frac).Length) "0" + string frac
                sign + string (int64 whole) + "." + digits.TrimEnd('0')

    let toText (v: Value) : string =
        match v with
        | Number n -> numberText n
        | Text s -> s
        | Logical b -> if b then "TRUE" else "FALSE"
        | Err k -> errorText k
        | Blank -> ""

    let isError (v: Value) : bool = match v with Err _ -> true | _ -> false

/// What the host gives a formula: the cells it can read, the names it knows, and functions of its own.
type Env =
    { /// The value of a cell (a cell of the sheet that holds nothing is `Blank`).
      Cell: CellRef -> Value
      /// A name, for the cell that holds the formula: `Actual` in the cell of the plan that stands for a month of an account.  None: #NAME?.
      Name: CellRef -> string -> Value option
      /// A function that is not one of the spreadsheet's own: its arguments, its value.  None: #NAME?.
      Custom: string -> (Value[] -> Value) option }

module Env =
    /// An environment that has nothing: no cell holds anything, no name and no function exists.
    let empty : Env = { Cell = (fun _ -> Blank); Name = (fun _ _ -> None); Custom = (fun _ -> None) }

module Eval =
    // ---- coercions: a number out of what a cell holds -------------------------------------------------------------------------------------------------------------------------

    let private asNumber (v: Value) : Result<float, ErrorKind> =
        match v with
        | Number n -> Ok n
        | Blank -> Ok 0.0
        | Logical b -> Ok (if b then 1.0 else 0.0)
        | Text s ->
            (match Numbers.parseFloat (s.Trim()) with
             | Some n -> Ok n
             | None -> Error WrongType)
        | Err k -> Error k

    /// The values a call is given: a range is its cells, row by row.  A range that is larger than the sheet has the #REF! of its cells that are not on it.
    let private expand (env: Env) (a: CellRef) (b: CellRef) : Value[] =
        let c0 = min a.Col b.Col
        let c1 = max a.Col b.Col
        let r0 = min a.Row b.Row
        let r1 = max a.Row b.Row
        let cols = c1 - c0 + 1
        let rows = r1 - r0 + 1
        Array.init (cols * rows) (fun i -> env.Cell (CellRef.create (c0 + i % cols) (r0 + i / cols)))

    let private fromBool (b: bool) : Value = Logical b

    let private check (n: float) : Value = if Numbers.isFinite n then Number n else Err BadNumber

    let private compareValues (a: Value) (b: Value) : Result<int, ErrorKind> =
        match a, b with
        | Err k, _ | _, Err k -> Error k
        | Text x, Text y -> Ok (System.String.CompareOrdinal(x.ToUpperInvariant(), y.ToUpperInvariant()))
        | Text _, _ -> Ok 1      // text is greater than numbers, as in a spreadsheet
        | _, Text _ -> Ok -1
        | _ ->
            match asNumber a, asNumber b with
            | Ok x, Ok y -> Ok (if x < y then -1 elif x > y then 1 else 0)
            | Error k, _ | _, Error k -> Error k

    // ---- the evaluator --------------------------------------------------------------------------------------------------------------------------------------------------------------

    /// The value of a formula in the cell `at`: an error is a value (the first one found, left to right, is the answer), never an exception.
    let rec eval (env: Env) (at: CellRef) (e: Expr) : Value =
        match e with
        | Num n -> Number n
        | Str s -> Text s
        | Bool b -> Logical b
        | Cell r -> if CellRef.valid r then env.Cell r else Err BadRef
        | Range _ -> Err WrongType
        | Name n ->
            (match env.Name at n with
             | Some v -> v
             | None -> Err UnknownName)
        | Neg x ->
            (match asNumber (eval env at x) with
             | Ok n -> Number (-n)
             | Error k -> Err k)
        | Percent x ->
            (match asNumber (eval env at x) with
             | Ok n -> Number (n / 100.0)
             | Error k -> Err k)
        | Bin (op, a, b) -> binary env at op a b
        | Call (name, args) -> call env at name args

    and private arithmetic (env: Env) (at: CellRef) (a: Expr) (b: Expr) (f: float -> float -> Value) : Value =
        match asNumber (eval env at a) with
        | Error k -> Err k
        | Ok x ->
            match asNumber (eval env at b) with
            | Error k -> Err k
            | Ok y -> f x y

    and private binary (env: Env) (at: CellRef) (op: BinOp) (a: Expr) (b: Expr) : Value =
        match op with
        | Add -> arithmetic env at a b (fun x y -> check (x + y))
        | Sub -> arithmetic env at a b (fun x y -> check (x - y))
        | Mul -> arithmetic env at a b (fun x y -> check (x * y))
        | Div -> arithmetic env at a b (fun x y -> if y = 0.0 then Err DivZero else check (x / y))
        | Pow ->
            arithmetic env at a b (fun x y ->
                // a power of a negative number with a fraction is not a number; zero to a negative power is a division by zero
                if x = 0.0 && y < 0.0 then Err DivZero
                elif x < 0.0 && y <> System.Math.Floor y then Err BadNumber
                else check (System.Math.Pow(x, y)))
        | Concat ->
            let x = eval env at a
            let y = eval env at b
            (match x, y with
             | Err k, _ | _, Err k -> Err k
             | _ -> Text (Value.toText x + Value.toText y))
        | Eq | Ne | Lt | Le | Gt | Ge ->
            match compareValues (eval env at a) (eval env at b) with
            | Error k -> Err k
            | Ok c ->
                fromBool (
                    match op with
                    | Eq -> c = 0
                    | Ne -> c <> 0
                    | Lt -> c < 0
                    | Le -> c <= 0
                    | Gt -> c > 0
                    | _ -> c >= 0)

    /// The numbers among the arguments of SUM and its kin: a range gives its numbers (text and blanks in a range are left out), an argument that is a value is coerced (text that is not a
    /// number is #VALUE!).  The first error is the answer.
    and private numbers (env: Env) (at: CellRef) (args: Expr list) : Result<float[], ErrorKind> =
        let out = ResizeArray<float>()
        let mutable failure : ErrorKind option = None
        for arg in args do
            if failure.IsNone then
                match arg with
                | Range (a, b) ->
                    for v in expand env a b do
                        if failure.IsNone then
                            match v with
                            | Number n -> out.Add n
                            | Logical _ | Text _ | Blank -> ()
                            | Err k -> failure <- Some k
                | _ ->
                    match asNumber (eval env at arg) with
                    | Ok n -> out.Add n
                    | Error k -> failure <- Some k
        match failure with
        | Some k -> Error k
        | None -> Ok (out.ToArray())

    and private truth (v: Value) : Result<bool, ErrorKind> =
        match v with
        | Logical b -> Ok b
        | Err k -> Error k
        | Blank -> Ok false
        | Text _ -> Error WrongType
        | Number n -> Ok (n <> 0.0)

    and private call (env: Env) (at: CellRef) (name: string) (args: Expr list) : Value =
        let one (f: float -> Value) =
            match args with
            | [ x ] ->
                (match asNumber (eval env at x) with
                 | Ok n -> f n
                 | Error k -> Err k)
            | _ -> Err WrongType
        match name with
        | "SUM" -> (match numbers env at args with Ok xs -> check (Array.sum xs) | Error k -> Err k)
        | "AVERAGE" | "AVG" ->
            (match numbers env at args with
             | Ok xs -> if xs.Length = 0 then Err DivZero else check (Array.sum xs / float xs.Length)
             | Error k -> Err k)
        | "MIN" -> (match numbers env at args with Ok xs -> Number (if xs.Length = 0 then 0.0 else Array.min xs) | Error k -> Err k)
        | "MAX" -> (match numbers env at args with Ok xs -> Number (if xs.Length = 0 then 0.0 else Array.max xs) | Error k -> Err k)
        | "COUNT" -> (match numbers env at args with Ok xs -> Number (float xs.Length) | Error k -> Err k)
        | "PRODUCT" -> (match numbers env at args with Ok xs -> check (Array.fold (fun acc x -> acc * x) 1.0 xs) | Error k -> Err k)
        | "ABS" -> one (fun n -> Number (abs n))
        | "INT" -> one (fun n -> Number (System.Math.Floor n))
        | "SQRT" -> one (fun n -> if n < 0.0 then Err BadNumber else Number (System.Math.Sqrt n))
        | "ROUND" ->
            (match args with
             | [ x; d ] ->
                (match asNumber (eval env at x), asNumber (eval env at d) with
                 | Ok n, Ok digits ->
                    let k = System.Math.Pow(10.0, System.Math.Floor digits)
                    // half away from zero, as a spreadsheet rounds
                    check ((if n < 0.0 then -(System.Math.Floor(-n * k + 0.5)) else System.Math.Floor(n * k + 0.5)) / k)
                 | Error k, _ | _, Error k -> Err k)
             | _ -> Err WrongType)
        | "MOD" ->
            (match args with
             | [ x; y ] ->
                (match asNumber (eval env at x), asNumber (eval env at y) with
                 | Ok a, Ok b -> if b = 0.0 then Err DivZero else Number (a - b * System.Math.Floor(a / b))
                 | Error k, _ | _, Error k -> Err k)
             | _ -> Err WrongType)
        | "POWER" ->
            (match args with
             | [ x; y ] -> binary env at Pow x y
             | _ -> Err WrongType)
        | "IF" ->
            // only the branch that is taken is evaluated, so that IF(B1=0, 0, A1/B1) is not a division by zero
            (match args with
             | [ c; t ] | [ c; t; _ ] ->
                (match truth (eval env at c) with
                 | Ok true -> eval env at t
                 | Ok false -> (match args with [ _; _; f ] -> eval env at f | _ -> Logical false)
                 | Error k -> Err k)
             | _ -> Err WrongType)
        | "IFERROR" ->
            (match args with
             | [ x; fallback ] ->
                (match eval env at x with
                 | Err _ -> eval env at fallback
                 | v -> v)
             | _ -> Err WrongType)
        | "AND" | "OR" ->
            let mutable failure : ErrorKind option = None
            let mutable seen = false
            let mutable all = true
            let mutable any = false
            for arg in args do
                if failure.IsNone then
                    let values = match arg with Range (a, b) -> expand env a b | _ -> [| eval env at arg |]
                    for v in values do
                        match v with
                        | Blank | Text _ when (match arg with Range _ -> true | _ -> false) -> ()
                        | _ ->
                            match truth v with
                            | Ok b ->
                                seen <- true
                                if b then any <- true else all <- false
                            | Error k -> if failure.IsNone then failure <- Some k
            (match failure with
             | Some k -> Err k
             | None -> if not seen then Err WrongType elif name = "AND" then Logical all else Logical any)
        | "NOT" ->
            (match args with
             | [ x ] -> (match truth (eval env at x) with Ok b -> Logical (not b) | Error k -> Err k)
             | _ -> Err WrongType)
        | "CONCAT" ->
            let parts = args |> List.collect (fun a -> match a with Range (x, y) -> List.ofArray (expand env x y) | _ -> [ eval env at a ])
            (match parts |> List.tryPick (fun v -> match v with Err k -> Some k | _ -> None) with
             | Some k -> Err k
             | None -> Text (String.concat "" (parts |> List.map Value.toText)))
        | _ ->
            match env.Custom name with
            | Some f ->
                let values = args |> List.collect (fun a -> match a with Range (x, y) -> List.ofArray (expand env x y) | _ -> [ eval env at a ])
                f (Array.ofList values)
            | None -> Err UnknownName
