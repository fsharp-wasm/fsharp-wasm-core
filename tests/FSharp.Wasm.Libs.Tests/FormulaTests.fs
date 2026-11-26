/// The tests of FSharp.Wasm.Formula: references, the parser and its positions, the printer, the evaluator and its errors, and the book that recalculates only what a change reaches.  Written with
/// Wasmecto, run under .NET and as WebAssembly GC.
module FSharp.Wasm.Libs.FormulaTests

open Wasmecto
open FSharp.Wasm.Formula

let private parse (s: string) : Expr =
    match Parser.parse s with
    | Ok e -> e
    | Error e -> failwith ("did not parse: " + s + " (" + e.Message + ")")

let private a1 = CellRef.create 0 0

let private num (n: float) : Value = Number n

/// the value of a formula with nothing around it
let private calc (s: string) : Value = Eval.eval Env.empty a1 (parse s)

let private shows (v: Value) : string = Value.toText v

let references =
    testList "Formula.CellRef" [
        testCase "columns are letters" <| fun () ->
            Expect.equal (CellRef.columnName 0) "A" "A"
            Expect.equal (CellRef.columnName 25) "Z" "Z"
            Expect.equal (CellRef.columnName 26) "AA" "AA"
            Expect.equal (CellRef.columnName 701) "ZZ" "ZZ"
            Expect.equal (CellRef.columnName 702) "AAA" "AAA"
            Expect.equal (CellRef.columnName 16383) "XFD" "the last column"
        testCase "a cell is parsed and printed back" <| fun () ->
            for s in [ "A1"; "B7"; "$A$1"; "$C5"; "D$9"; "XFD16384"; "AA100" ] do
                match CellRef.tryParse s with
                | Some r -> Expect.equal (CellRef.toString r) s s
                | None -> failwith ("not a cell: " + s)
            Expect.equal (CellRef.tryParse "b2" |> Option.map CellRef.toString) (Some "B2") "lower case is the same cell"
        testCase "what is not a cell is not" <| fun () ->
            for s in [ ""; "A"; "1"; "A0"; "AAAA1"; "A1B"; "XFD16385"; "A1.5"; "$"; "A$" ] do
                Expect.isTrue (CellRef.tryParse s |> Option.isNone) s
        testCase "the key of a cell is the cell" <| fun () ->
            let r = CellRef.create 27 301
            Expect.equal (CellRef.ofKey (CellRef.key r)) r "round trip"
        testCase "a copied reference moves, an absolute one stays" <| fun () ->
            let moved = Parser.shift 2 3 (parse "=A1+$B2+C$3+$D$4")
            Expect.equal (Parser.print moved) "C4+$B5+E$3+$D$4" "relative parts move"
            Expect.equal (Parser.print (Parser.shift -1 0 (parse "=B2"))) "A2" "left"
    ]

let syntax =
    testList "Formula.Parser" [
        testCase "precedence: * before +, comparison last" <| fun () ->
            Expect.equal (parse "1+2*3") (Bin (Add, Num 1.0, Bin (Mul, Num 2.0, Num 3.0))) "1+2*3"
            Expect.equal (parse "1*2+3") (Bin (Add, Bin (Mul, Num 1.0, Num 2.0), Num 3.0)) "1*2+3"
            Expect.equal (parse "1+2=3") (Bin (Eq, Bin (Add, Num 1.0, Num 2.0), Num 3.0)) "1+2=3"
            Expect.equal (parse "\"a\"&1+2") (Bin (Concat, Str "a", Bin (Add, Num 1.0, Num 2.0))) "& is below +"
        testCase "the operators go from the left, and a minus binds tighter than ^ (as in a spreadsheet)" <| fun () ->
            Expect.equal (parse "10-4-3") (Bin (Sub, Bin (Sub, Num 10.0, Num 4.0), Num 3.0)) "left"
            Expect.equal (parse "2^3^2") (Bin (Pow, Bin (Pow, Num 2.0, Num 3.0), Num 2.0)) "^ from the left"
            Expect.equal (parse "-2^2") (Bin (Pow, Neg (Num 2.0), Num 2.0)) "-2^2 is (-2)^2"
            Expect.equal (parse "2^-1") (Bin (Pow, Num 2.0, Neg (Num 1.0))) "a minus after ^"
        testCase "parentheses, a leading =, spaces" <| fun () ->
            Expect.equal (parse "=(1+2)*3") (Bin (Mul, Bin (Add, Num 1.0, Num 2.0), Num 3.0)) "grouped"
            Expect.equal (parse "  = 1 +  2 ") (Bin (Add, Num 1.0, Num 2.0)) "spaces"
        testCase "numbers: decimals, exponents, percent" <| fun () ->
            Expect.equal (parse "1.5") (Num 1.5) "1.5"
            Expect.equal (parse ".5") (Num 0.5) ".5"
            Expect.equal (parse "2e3") (Num 2000.0) "2e3"
            Expect.equal (parse "1.5E-2") (Num 0.015) "1.5E-2"
            Expect.equal (parse "50%") (Percent (Num 50.0)) "50%"
            Expect.equal (parse "10*5%") (Bin (Mul, Num 10.0, Percent (Num 5.0))) "10*5%"
        testCase "text, with a doubled quote" <| fun () ->
            Expect.equal (parse "\"a\"\"b\"") (Str "a\"b") "doubled quote"
            Expect.equal (parse "\"\"") (Str "") "empty"
        testCase "cells, ranges, names, booleans" <| fun () ->
            Expect.equal (parse "B2") (Cell (CellRef.create 1 1)) "B2"
            Expect.equal (parse "A1:B5") (Range (CellRef.create 0 0, CellRef.create 1 4)) "A1:B5"
            Expect.equal (parse "Actual") (Name "Actual") "a name"
            Expect.equal (parse "true") (Bool true) "true"
            Expect.equal (parse "FALSE") (Bool false) "FALSE"
        testCase "calls: names in any case, any number of arguments, ; as well as ," <| fun () ->
            Expect.equal (parse "sum(A1:A3, 4)") (Call ("SUM", [ Range (CellRef.create 0 0, CellRef.create 0 2); Num 4.0 ])) "sum"
            Expect.equal (parse "IF(1;2;3)") (Call ("IF", [ Num 1.0; Num 2.0; Num 3.0 ])) "semicolons"
            Expect.equal (parse "NOW()") (Call ("NOW", [])) "none"
        testCase "an error says where" <| fun () ->
            let at (s: string) = match Parser.parse s with Error e -> Some e.Position | Ok _ -> None
            Expect.equal (at "1+") (Some 2) "the end after +"
            Expect.equal (at "=1+") (Some 3) "the = is counted"
            Expect.equal (at "1 2") (Some 2) "a value after a value"
            Expect.equal (at "(1+2") (Some 4) "an open parenthesis"
            Expect.equal (at "SUM(1 2)") (Some 6) "arguments without a comma"
            Expect.equal (at "1+@") (Some 2) "a character that is nothing"
            Expect.equal (at "\"abc") (Some 0) "text that is not closed"
            Expect.equal (at "A1:") (Some 3) "a range without its end"
            Expect.equal (at "A1:5") (Some 3) "a range that ends in a number"
            Expect.equal (at "") (Some 0) "nothing"
        testCase "printing keeps the meaning, with the fewest parentheses" <| fun () ->
            for s in [ "1+2*3"; "(1+2)*3"; "10-(4-3)"; "10-4-3"; "-(1+2)"; "2^(3^2)"; "SUM(A1:B2,3)*2"; "IF(A1>0,\"yes\",\"no\")"; "50%*A1"; "(A1+1)%"; "\"a\"&\"b\"=\"ab\"" ] do
                let e = parse s
                Expect.equal (parse (Parser.print e)) e ("round trip of " + s)
            Expect.equal (Parser.print (parse "(1+2)+3")) "1+2+3" "no parentheses that mean nothing"
            Expect.equal (Parser.print (parse "1-(2-3)")) "1-(2-3)" "the ones that do"
        testCase "what a formula reads" <| fun () ->
            let cells = ResizeArray<CellRef>()
            let ranges = ResizeArray<CellRef * CellRef>()
            Parser.references (parse "A1+SUM(B1:B3,C2)*IF(D4>0,E5,1)") cells ranges
            Expect.equal (cells |> Seq.map CellRef.toString |> List.ofSeq) [ "A1"; "C2"; "D4"; "E5" ] "single cells in order"
            Expect.equal ranges.Count 1 "one range"
    ]

let evaluation =
    testList "Formula.Eval" [
        testCase "arithmetic" <| fun () ->
            Expect.equal (calc "1+2*3") (num 7.0) "1+2*3"
            Expect.equal (calc "(1+2)*3") (num 9.0) "(1+2)*3"
            Expect.equal (calc "10/4") (num 2.5) "division"
            Expect.equal (calc "2^10") (num 1024.0) "power"
            Expect.equal (calc "-2^2") (num 4.0) "-2^2 is 4 in a spreadsheet"
            Expect.equal (calc "50%") (num 0.5) "percent"
            Expect.equal (calc "200*10%") (num 20.0) "of"
        testCase "errors are values, and the first one is the answer" <| fun () ->
            Expect.equal (calc "1/0") (Err DivZero) "#DIV/0!"
            Expect.equal (calc "1+1/0") (Err DivZero) "it travels"
            Expect.equal (calc "Missing+1") (Err UnknownName) "#NAME?"
            Expect.equal (calc "NOPE(1)") (Err UnknownName) "an unknown function"
            Expect.equal (calc "\"a\"+1") (Err WrongType) "#VALUE!"
            Expect.equal (calc "SQRT(-1)") (Err BadNumber) "#NUM!"
            Expect.equal (calc "(-8)^0.5") (Err BadNumber) "a fractional power of a negative number"
            Expect.equal (calc "0^-1") (Err DivZero) "zero to a negative power"
            Expect.equal (calc "1e308*10") (Err BadNumber) "too big"
            Expect.equal (shows (Err DivZero)) "#DIV/0!" "its text"
        testCase "text: & and what coerces" <| fun () ->
            Expect.equal (calc "\"a\"&\"b\"") (Text "ab") "&"
            Expect.equal (calc "\"n=\"&1.5") (Text "n=1.5") "a number as text"
            Expect.equal (calc "\"3\"+4") (num 7.0) "text that is a number is one"
            Expect.equal (calc "TRUE+1") (num 2.0) "a logical is 1"
            Expect.equal (calc "CONCAT(\"a\",1,\"b\")") (Text "a1b") "CONCAT"
        testCase "comparisons: numbers, text without regard to case, text above numbers" <| fun () ->
            Expect.equal (calc "1<2") (Logical true) "1<2"
            Expect.equal (calc "2<=2") (Logical true) "2<=2"
            Expect.equal (calc "3<>3") (Logical false) "3<>3"
            Expect.equal (calc "\"abc\"=\"ABC\"") (Logical true) "case"
            Expect.equal (calc "\"a\"<\"b\"") (Logical true) "text order"
            Expect.equal (calc "\"a\">5") (Logical true) "text is above numbers"
        testCase "SUM, AVERAGE, MIN, MAX, COUNT, PRODUCT over values" <| fun () ->
            Expect.equal (calc "SUM(1,2,3)") (num 6.0) "SUM"
            Expect.equal (calc "AVERAGE(2,4)") (num 3.0) "AVERAGE"
            Expect.equal (calc "AVERAGE()") (Err DivZero) "AVERAGE of nothing"
            Expect.equal (calc "MIN(3,1,2)") (num 1.0) "MIN"
            Expect.equal (calc "MAX(3,1,2)") (num 3.0) "MAX"
            Expect.equal (calc "COUNT(1,2,3)") (num 3.0) "COUNT"
            Expect.equal (calc "PRODUCT(2,3,4)") (num 24.0) "PRODUCT"
        testCase "ABS, INT, ROUND, MOD, POWER, SQRT" <| fun () ->
            Expect.equal (calc "ABS(-3)") (num 3.0) "ABS"
            Expect.equal (calc "INT(2.7)") (num 2.0) "INT"
            Expect.equal (calc "INT(-2.5)") (num -3.0) "INT goes down"
            Expect.equal (calc "ROUND(2.5,0)") (num 3.0) "half away from zero"
            Expect.equal (calc "ROUND(-2.5,0)") (num -3.0) "and below zero"
            Expect.equal (calc "ROUND(1234.5678,2)") (num 1234.57) "two places"
            Expect.equal (calc "ROUND(1234.5678,-2)") (num 1200.0) "to hundreds"
            Expect.equal (calc "MOD(7,3)") (num 1.0) "MOD"
            Expect.equal (calc "MOD(-7,3)") (num 2.0) "MOD has the sign of the divisor"
            Expect.equal (calc "MOD(1,0)") (Err DivZero) "MOD by zero"
            Expect.equal (calc "POWER(2,8)") (num 256.0) "POWER"
            Expect.equal (calc "SQRT(16)") (num 4.0) "SQRT"
        testCase "IF takes one branch only, IFERROR catches" <| fun () ->
            Expect.equal (calc "IF(1>0,\"yes\",\"no\")") (Text "yes") "true"
            Expect.equal (calc "IF(1<0,\"yes\",\"no\")") (Text "no") "false"
            Expect.equal (calc "IF(1<0,5)") (Logical false) "no else"
            Expect.equal (calc "IF(1=1,0,1/0)") (num 0.0) "the other branch is not evaluated"
            Expect.equal (calc "IFERROR(1/0,7)") (num 7.0) "IFERROR"
            Expect.equal (calc "IFERROR(5,7)") (num 5.0) "no error"
        testCase "AND, OR, NOT" <| fun () ->
            Expect.equal (calc "AND(1<2,2<3)") (Logical true) "AND"
            Expect.equal (calc "AND(1<2,3<2)") (Logical false) "AND false"
            Expect.equal (calc "OR(1>2,2<3)") (Logical true) "OR"
            Expect.equal (calc "NOT(1>2)") (Logical true) "NOT"
        testCase "names and functions of the host" <| fun () ->
            let env : Env =
                { Env.empty with
                    Name = (fun at n -> if n = "Actual" then Some (Number (float (at.Col * 100 + at.Row))) else None)
                    Custom = (fun n -> if n = "DOUBLE" then Some (fun (a: Value[]) -> match a with [| Number x |] -> Number (2.0 * x) | _ -> Err WrongType) else None) }
            Expect.equal (Eval.eval env (CellRef.create 3 5) (parse "Actual*2")) (num 610.0) "a name is read for the cell of the formula"
            Expect.equal (Eval.eval env a1 (parse "DOUBLE(21)")) (num 42.0) "a function of the host"
            Expect.equal (Eval.eval env a1 (parse "Budget")) (Err UnknownName) "a name it does not know"
        testCase "numbers as text" <| fun () ->
            Expect.equal (Value.numberText 3.0) "3" "whole"
            Expect.equal (Value.numberText 0.1) "0.1" "0.1"
            Expect.equal (Value.numberText (0.1 + 0.2)) "0.3" "no noise"
            Expect.equal (Value.numberText -2.5) "-2.5" "negative"
            Expect.equal (Value.numberText 1234567.125) "1234567.125" "digits"
            Expect.equal (Value.numberText (1.0 / 3.0)) "0.3333333333" "ten digits"
    ]

/// a sheet with the changes of an edit as text, in order: "A1 B1"
let private names (changes: Change[]) : string = changes |> Array.map (fun c -> CellRef.toString c.Cell) |> String.concat " "

let private at (c: string) : int * int =
    match CellRef.tryParse c with
    | Some r -> (r.Col, r.Row)
    | None -> failwith c

let private put (b: Book) (cell: string) (text: string) : Change[] =
    let (c, r) = at cell
    b.Set(c, r, text)

let private get (b: Book) (cell: string) : Value =
    let (c, r) = at cell
    b.Get(c, r)

let book =
    testList "Formula.Book" [
        testCase "numbers, text and formulas" <| fun () ->
            let b = Book()
            put b "A1" "2" |> ignore
            put b "A2" "3" |> ignore
            put b "A3" "=A1*A2" |> ignore
            put b "B1" "hello" |> ignore
            Expect.equal (get b "A3") (num 6.0) "a formula"
            Expect.equal (get b "B1") (Text "hello") "text"
            Expect.equal (get b "C9") Blank "an empty cell"
            Expect.equal (b.Source(at "A3" |> fst, at "A3" |> snd)) "=A1*A2" "what was typed is kept"
            Expect.equal b.Count 4 "four cells"
        testCase "an edit names the cells that changed, in dependency order, and only those" <| fun () ->
            let b = Book()
            put b "A1" "2" |> ignore
            put b "A2" "3" |> ignore
            put b "A3" "=A1*A2" |> ignore
            put b "B1" "=A3+1" |> ignore
            put b "C1" "=A2*10" |> ignore
            let changes = put b "A1" "5"
            Expect.equal (names changes) "A1 A3 B1" "A1, then what reads it, then what reads that: not C1"
            Expect.equal (get b "A3") (num 15.0) "A3"
            Expect.equal (get b "B1") (num 16.0) "B1"
            Expect.equal (get b "C1") (num 30.0) "C1 did not move"
            let c = changes.[1]
            Expect.equal (c.Before, c.After) (num 6.0, num 15.0) "a change has what the cell was"
        testCase "a value that is the same again is no change" <| fun () ->
            let b = Book()
            put b "A1" "2" |> ignore
            put b "A2" "=A1*0" |> ignore
            put b "A3" "=A2+1" |> ignore
            Expect.equal (names (put b "A1" "9")) "A1" "A2 is 0 as before, and so A3 is not recalculated into a change"
            Expect.equal (names (put b "A1" "9")) "" "the same text is nothing at all"
        testCase "a cell is recalculated once, after the cells it reads (a diamond)" <| fun () ->
            let b = Book()
            put b "A1" "1" |> ignore
            put b "B1" "=A1+1" |> ignore
            put b "C1" "=A1+2" |> ignore
            put b "D1" "=B1+C1" |> ignore
            let changes = put b "A1" "10"
            Expect.equal changes.Length 4 "four cells, each once"
            Expect.equal (names changes |> fun s -> s.StartsWith "A1" && s.EndsWith "D1") true "D1 is last"
            Expect.equal (get b "D1") (num 23.0) "D1 is made of the new B1 and C1"
        testCase "a range is read, and an edit inside it reaches the cell that reads it" <| fun () ->
            let b = Book()
            for i in 1 .. 5 do put b ("A" + string i) (string i) |> ignore
            put b "B1" "=SUM(A1:A5)" |> ignore
            put b "B2" "=AVERAGE(A1:A5)" |> ignore
            Expect.equal (get b "B1") (num 15.0) "sum"
            Expect.equal (get b "B2") (num 3.0) "average"
            Expect.equal (names (put b "A3" "13")) "A3 B1 B2" "the ranges that hold A3"
            Expect.equal (get b "B1") (num 25.0) "sum again"
            Expect.equal (names (put b "A9" "1")) "A9" "a cell outside the range reaches nothing"
        testCase "text and blanks in a range are left out of the sums" <| fun () ->
            let b = Book()
            put b "A1" "1" |> ignore
            put b "A2" "n/a" |> ignore
            put b "A4" "3" |> ignore
            put b "B1" "=SUM(A1:A4)" |> ignore
            put b "B2" "=COUNT(A1:A4)" |> ignore
            Expect.equal (get b "B1") (num 4.0) "1 + 3"
            Expect.equal (get b "B2") (num 2.0) "two numbers"
        testCase "cells that read each other are #CYCLE!, and the cycle is gone when it is broken" <| fun () ->
            let b = Book()
            put b "A1" "=B1" |> ignore
            let changes = put b "B1" "=A1"
            Expect.equal (get b "A1") (Err Cycle) "A1"
            Expect.equal (get b "B1") (Err Cycle) "B1"
            Expect.equal changes.Length 2 "both named"
            put b "C1" "=A1+1" |> ignore
            Expect.equal (get b "C1") (Err Cycle) "what reads a cycle has its error"
            let fixedUp = put b "B1" "7"
            Expect.equal (get b "A1") (num 7.0) "A1 is B1 again"
            Expect.equal (get b "C1") (num 8.0) "and C1"
            Expect.isTrue (fixedUp.Length >= 3) "B1, A1 and C1 changed"
        testCase "a cell that reads itself, and a cycle of three" <| fun () ->
            let b = Book()
            put b "A1" "=A1+1" |> ignore
            Expect.equal (get b "A1") (Err Cycle) "itself"
            put b "X1" "=X2" |> ignore
            put b "X2" "=X3" |> ignore
            put b "X3" "=X1" |> ignore
            Expect.equal [ get b "X1"; get b "X2"; get b "X3" ] [ Err Cycle; Err Cycle; Err Cycle ] "all three"
        testCase "a formula that does not parse is #ERROR! and is kept to be corrected" <| fun () ->
            let b = Book()
            put b "A1" "=1+" |> ignore
            Expect.equal (get b "A1") (Err Syntax) "#ERROR!"
            Expect.equal (b.Source(0, 0)) "=1+" "the text stays"
            put b "A1" "=1+2" |> ignore
            Expect.equal (get b "A1") (num 3.0) "corrected"
        testCase "errors flow through the cells that read them, and IFERROR stops them" <| fun () ->
            let b = Book()
            put b "A1" "0" |> ignore
            put b "A2" "=1/A1" |> ignore
            put b "A3" "=A2+1" |> ignore
            put b "A4" "=IFERROR(A2,-1)" |> ignore
            Expect.equal (get b "A3") (Err DivZero) "travels"
            Expect.equal (get b "A4") (num -1.0) "caught"
            put b "A1" "4" |> ignore
            Expect.equal (get b "A3") (num 1.25) "and is gone with its cause"
            Expect.equal (get b "A4") (num 0.25) "A4"
        testCase "clearing a cell recalculates what read it" <| fun () ->
            let b = Book()
            put b "A1" "5" |> ignore
            put b "B1" "=A1*2" |> ignore
            let changes = put b "A1" ""
            Expect.equal (names changes) "A1 B1" "A1 and B1"
            Expect.equal (get b "A1") Blank "empty"
            Expect.equal (get b "B1") (num 0.0) "a blank is 0"
            Expect.equal b.Count 1 "A1 is gone"
            Expect.equal (names (put b "Z9" "")) "" "clearing what is empty is nothing"
        testCase "a cell that is overwritten stops being read by what it read" <| fun () ->
            let b = Book()
            put b "A1" "1" |> ignore
            put b "B1" "=A1+1" |> ignore
            put b "B1" "=99" |> ignore
            Expect.equal (names (put b "A1" "2")) "A1" "B1 no longer reads A1"
        testCase "names of the host: a change behind them is told with Invalidate" <| fun () ->
            let mutable factor = 2.0
            let b = Book((fun _ n -> if n = "Factor" then Some (Number factor) else None), (fun _ -> None))
            put b "A1" "=Factor*10" |> ignore
            put b "A2" "=A1+1" |> ignore
            Expect.equal (get b "A2") (num 21.0) "with the factor 2"
            factor <- 3.0
            Expect.equal (get b "A2") (num 21.0) "nothing moved by itself"
            let changes = b.Invalidate [| CellRef.create 0 0 |]
            Expect.equal (names changes) "A1 A2" "A1 and what reads it"
            Expect.equal (get b "A2") (num 31.0) "with the factor 3"
            factor <- 4.0
            Expect.equal (b.Recalculate().Length) 2 "every cell again"
            Expect.equal (get b "A2") (num 41.0) "with the factor 4"
        testCase "a column of running totals as long as the sheet is a chain, not a stack" <| fun () ->
            let b = Book()
            let n = CellRef.Limit - 1
            put b "A1" "1" |> ignore
            for r in 2 .. n do put b ("A" + string r) ("=A" + string (r - 1) + "+1") |> ignore
            Expect.equal (b.Get(0, n - 1)) (num (float n)) "the last of the chain"
            let changes = put b "A1" "11"
            Expect.equal changes.Length n "every cell of the chain changed"
            Expect.equal (b.Get(0, n - 1)) (num (float (n + 10))) "and the last is 10 more"
    ]

let suites = [ references; syntax; evaluation; book ]
