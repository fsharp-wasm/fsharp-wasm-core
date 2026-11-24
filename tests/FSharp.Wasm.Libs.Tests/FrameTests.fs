/// The tests of FSharp.Wasm.Frame: hierarchies, the cube of prefix sums against a plain loop, pivoting, drill-down and the formatting of amounts.  Written with Wasmecto, run
/// under .NET and as WebAssembly GC.
module FSharp.Wasm.Libs.FrameTests

open Wasmecto
open FSharp.Wasm.Frame

// a small chart of accounts and two years of months, as paths
let private accounts =
    [| [| "Revenue"; "Sales"; "4000 Sales EU" |]
       [| "Revenue"; "Sales"; "4010 Sales US" |]
       [| "Revenue"; "Services"; "4100 Consulting" |]
       [| "Expenses"; "People"; "6000 Salaries" |]
       [| "Expenses"; "People"; "6010 Training" |]
       [| "Expenses"; "Office"; "6200 Rent" |] |]

let private months =
    [| for y in [ "2025"; "2026" ] do
         for q in 1 .. 4 do
           for m in 1 .. 3 do
             yield [| y; "Q" + string q; (let n = (q - 1) * 3 + m in (if n < 10 then "0" else "") + string n) |] |]

/// A deterministic stream of numbers: the tests must give the same facts under .NET and as WasmGC.
let private lcg (seed: int) =
    let mutable s = uint32 seed
    fun () ->
        s <- s * 1664525u + 1013904223u
        int (s >>> 8)

let private facts (n: int) : Facts =
    let rows = Hierarchy.ofPaths accounts
    let cols = Hierarchy.ofPaths months
    let next = lcg 42
    let account = Array.init n (fun _ -> next () % rows.Leaves)
    let month = Array.init n (fun _ -> next () % cols.Leaves)
    // whole cents, so that sums are exact in floating point whatever the order
    let actual = Array.init n (fun _ -> float (next () % 200001 - 50000) / 100.0)
    let budget = Array.init n (fun i -> actual.[i] * 0.5)
    { Count = n
      Dimensions = [| { Name = "Account"; Tree = rows; Leaf = account }; { Name = "Period"; Tree = cols; Leaf = month } |]
      Measures = [| { Name = "Actual"; OverRows = Sum; OverCols = Sum }
                    { Name = "Closing"; OverRows = Sum; OverCols = Last }
                    { Name = "Opening"; OverRows = Sum; OverCols = First }
                    { Name = "Average"; OverRows = Avg; OverCols = Avg } |]
      Values = [| actual; actual; actual; budget |] }

/// What a cell is, by its definition: the leaves of the group, one by one.
let private naive (f: Facts) (m: int) (rowDim: int) (rowNode: int) (colDim: int) (colNode: int) : float =
    let r = f.Dimensions.[rowDim].Tree.Nodes.[rowNode]
    let k = f.Dimensions.[colDim].Tree.Nodes.[colNode]
    let me = f.Measures.[m]
    let nr = f.Dimensions.[rowDim].Tree.Leaves
    let nc = f.Dimensions.[colDim].Tree.Leaves
    let leaf : float[] = Array.zeroCreate (nr * nc)
    for i in 0 .. f.Count - 1 do
        let a = f.Dimensions.[rowDim].Leaf.[i]
        let b = f.Dimensions.[colDim].Leaf.[i]
        leaf.[a * nc + b] <- leaf.[a * nc + b] + f.Values.[m].[i]
    let rows = match me.OverRows with First -> [ r.Lo ] | Last -> [ r.Hi - 1 ] | _ -> [ r.Lo .. r.Hi - 1 ]
    let cols = match me.OverCols with First -> [ k.Lo ] | Last -> [ k.Hi - 1 ] | _ -> [ k.Lo .. k.Hi - 1 ]
    let mutable sum = 0.0
    for a in rows do
        for b in cols do
            sum <- sum + leaf.[a * nc + b]
    let sum = if me.OverRows = Avg then sum / float (r.Hi - r.Lo) else sum
    if me.OverCols = Avg then sum / float (k.Hi - k.Lo) else sum

let hierarchy =
    testList "Frame.Hierarchy" [
        testCase "paths become nodes whose leaves are contiguous ranges" <| fun () ->
            let h = Hierarchy.ofPaths accounts
            Expect.equal h.Leaves 6 "six accounts"
            let find (label: string) = h.Nodes |> Array.find (fun n -> n.Label = label)
            let sales = find "Sales"
            Expect.equal (sales.Lo, sales.Hi) (0, 2) "Sales covers the first two leaves"
            let expenses = find "Expenses"
            Expect.equal (expenses.Lo, expenses.Hi) (3, 6) "Expenses covers the last three"
            Expect.equal h.Nodes.[0].Hi 6 "the root covers all"
            Expect.equal (find "Revenue").Level 0 "top level"
            Expect.equal (find "4000 Sales EU").Level 2 "a leaf is two levels down"
            Expect.equal (Hierarchy.leafOf h [| "Expenses"; "Office"; "6200 Rent" |]) 5 "the leaf number of a path"
            Expect.equal (Hierarchy.leafOf h [| "Expenses"; "Office"; "nope" |]) -1 "none for a path that is not there"
            Expect.isTrue (Hierarchy.isLeaf h h.LeafNodes.[0]) "a leaf node"
        testCase "visible lists a node, and its children when it is open" <| fun () ->
            let h = Hierarchy.ofPaths accounts
            let labels (open': int -> bool) = Hierarchy.visible h open' |> Array.map (fun i -> h.Nodes.[i].Label) |> List.ofArray
            Expect.equal (labels (fun _ -> false)) [ "Revenue"; "Expenses" ] "everything folded: the top level"
            let revenue = (h.Nodes |> Array.find (fun n -> n.Label = "Revenue")).Id
            Expect.equal (labels (fun i -> i = revenue)) [ "Revenue"; "Sales"; "Services"; "Expenses" ] "one group open: its children appear after it"
            Expect.equal (labels (fun _ -> true)).Length 12 "everything open: 2 + 4 groups + 6 leaves"
        testCase "the same path twice in a row is one leaf; a group that is opened twice, or a path inside a path, is refused" <| fun () ->
            Expect.equal (Hierarchy.ofPaths [| [| "A"; "x" |]; [| "A"; "x" |]; [| "A"; "y" |] |]).Leaves 2 "the repeat merges"
            Expect.throws (fun () -> Hierarchy.ofPaths [| [| "A"; "x" |]; [| "B"; "y" |]; [| "A"; "z" |] |] |> ignore) "A is opened twice"
            Expect.throws (fun () -> Hierarchy.ofPaths [| [| "A" |]; [| "A"; "x" |] |] |> ignore) "A is a leaf and a group"
            Expect.throws (fun () -> Hierarchy.ofPaths [| [||] |] |> ignore) "an empty path"
            Expect.equal (Hierarchy.ofPaths [||]).Leaves 0 "no paths, no leaves"
        testCase "a single leaf is its own root's only child" <| fun () ->
            let h = Hierarchy.ofPaths [| [| "only" |] |]
            Expect.equal h.Leaves 1 "one"
            Expect.equal (Hierarchy.visible h (fun _ -> true)) [| 1 |] "the leaf"
            Expect.equal (h.Nodes.[1].Lo, h.Nodes.[1].Hi) (0, 1) "its range"
    ]

let cube =
    testList "Frame.Cube" [
        testCase "every pair of nodes agrees with the plain sum of its leaves, for every way to make a total" <| fun () ->
            let f = facts 3000
            let c = Facts.pivot f 0 1 [||]
            let mutable checked' = 0
            for m in 0 .. f.Measures.Length - 1 do
                for r in 1 .. f.Dimensions.[0].Tree.Nodes.Length - 1 do
                    for k in 1 .. f.Dimensions.[1].Tree.Nodes.Length - 1 do
                        let expected = naive f m 0 r 1 k
                        let got = Cube.value c m r k
                        if abs (expected - got) > 1e-6 then
                            failwith (sprintf "measure %s, row node %d, col node %d: %f instead of %f" f.Measures.[m].Name r k got expected)
                        checked' <- checked' + 1
            Expect.equal checked' 1632 "12 row nodes × 34 column nodes × 4 measures were compared"
        testCase "turning the table: rows and columns swapped give the same numbers" <| fun () ->
            let f = facts 2000
            let straight = Facts.pivot f 0 1 [||]
            let turned = Facts.pivot { f with Measures = f.Measures |> Array.map (fun m -> { m with OverRows = m.OverCols; OverCols = m.OverRows }) } 1 0 [||]
            // Sum over both axes does not depend on the order
            for r in 1 .. 6 do
                for k in 1 .. 8 do
                    Expect.approxEqual (Cube.value straight 0 r k) (Cube.value turned 0 k r) 1e-6 "the same total"
        testCase "the root of both is the total of everything; a leaf is itself" <| fun () ->
            let f = facts 1000
            let c = Facts.pivot f 0 1 [||]
            let everything = Array.sum f.Values.[0]
            Expect.approxEqual (Cube.value c 0 0 0) everything 1e-6 "all facts"
            let leafRow = f.Dimensions.[0].Tree.LeafNodes.[2]
            let leafCol = f.Dimensions.[1].Tree.LeafNodes.[5]
            let mutable direct = 0.0
            for i in 0 .. f.Count - 1 do
                if f.Dimensions.[0].Leaf.[i] = 2 && f.Dimensions.[1].Leaf.[i] = 5 then direct <- direct + f.Values.[0].[i]
            Expect.approxEqual (Cube.value c 0 leafRow leafCol) direct 1e-9 "one cell"
        testCase "formulas are computed from the totals; a division by zero is not a number" <| fun () ->
            let f = facts 1500
            let c = Facts.pivot f 0 1 [| "Variance", Sub(M "Actual", M "Average"); "Ratio", Div(M "Average", M "Actual"); "Zero", Div(Num 1.0, Sub(M "Actual", M "Actual")) |]
            let variance = Cube.measureIndex c "Variance"
            Expect.equal variance 4 "after the four measures"
            Expect.approxEqual (Cube.value c variance 1 1) (Cube.value c 0 1 1 - Cube.value c 3 1 1) 1e-9 "actual minus average"
            let zero = Cube.measureIndex c "Zero"
            Expect.isTrue (let x = Cube.value c zero 1 1 in x <> x) "nan"
            Expect.throws (fun () -> Facts.pivot f 0 1 [| "Bad", M "Nope" |] |> ignore) "a formula about a measure that is not there"
        testCase "entries lists the facts under a cell, and they add up to its total" <| fun () ->
            let f = facts 4000
            let c = Facts.pivot f 0 1 [||]
            let revenue = (f.Dimensions.[0].Tree.Nodes |> Array.find (fun n -> n.Label = "Revenue")).Id
            let q3 = (f.Dimensions.[1].Tree.Nodes |> Array.find (fun n -> n.Label = "Q3" && n.Parent > 0 && f.Dimensions.[1].Tree.Nodes.[n.Parent].Label = "2026")).Id
            let found = Facts.entries f 0 revenue 1 q3
            Expect.isTrue (found.Length > 0) "there are some"
            Expect.approxEqual (found |> Array.sumBy (fun i -> f.Values.[0].[i])) (Cube.value c 0 revenue q3) 1e-6 "they add up"
            Expect.equal (Facts.entries f 0 0 1 0).Length f.Count "the root is every fact"
        testCase "an empty cube has zeros" <| fun () ->
            let rows = Hierarchy.ofPaths accounts
            let cols = Hierarchy.ofPaths months
            let c = Cube.build rows cols [| { Name = "Actual"; OverRows = Sum; OverCols = Sum } |] [||] [||] [||] [| [||] |]
            Expect.equal (Cube.value c 0 1 1) 0.0 "nothing"
    ]

let format =
    testList "Frame.Format" [
        testCase "amounts: thousands, two decimals, rounding, the sign" <| fun () ->
            Expect.equal (Format.amount 0.0) "0.00" "zero"
            Expect.equal (Format.amount 1234567.891) "1,234,567.89" "grouped"
            Expect.equal (Format.amount 999.999) "1,000.00" "rounds up across a group"
            Expect.equal (Format.amount -42.5) "-42.50" "negative"
            Expect.equal (Format.amount -0.004) "0.00" "no minus on zero"
            Expect.equal (Format.amount 100.0) "100.00" "three digits need no comma"
            Expect.equal (Format.amount 1000.0) "1,000.00" "four digits"
            Expect.equal (Format.amount 12345678901.23) "12,345,678,901.23" "billions"
            Expect.equal (Format.amount nan) "–" "not a number"
        testCase "counts and times" <| fun () ->
            Expect.equal (Format.count 0L) "0" "zero"
            Expect.equal (Format.count 999L) "999" "no comma"
            Expect.equal (Format.count 1000000L) "1,000,000" "a million"
            Expect.equal (Format.count -12345L) "-12,345" "negative"
            Expect.equal (Format.millis 3.44) "3.4" "one decimal"
            Expect.equal (Format.millis 0.04) "0.0" "below a tenth"
            Expect.equal (Format.millis 12.0) "12.0" "whole"
            Expect.equal (Format.millis 99.96) "100.0" "rounds up across a ten"
    ]

let suites = [ hierarchy; cube; format ]
