/// The tests of FSharp.Wasm.Live: filter and group by over the columns of a graph (a table that only grew costs its new rows; a change in the parameter or in what was there is a scan of the
/// whole), and the pivot, the plan written over a cube and the lines that are shown.  Written with Wasmecto, run under .NET and as WebAssembly GC.
module FSharp.Wasm.Libs.LiveTests

open Wasmecto
open FSharp.Wasm.Graph
open FSharp.Wasm.Frame
open FSharp.Wasm.Live

/// A table in a graph: a node with the columns `amount` and `group`.
let private table () : Doc * int * Columns =
    let d = Doc(1)
    let mutable id = 0
    d.Edit("make", fun tx -> id <- tx.Create "Table") |> ignore
    (d, id, Columns.ofDoc d id [| "amount"; "group" |])

let private append (d: Doc) (id: int) (amounts: float list) (groups: float list) =
    d.Edit("append", fun tx -> tx.Append(id, "amount", Array.ofList amounts); tx.Append(id, "group", Array.ofList groups)) |> ignore

let private chosen (r: Rows) : int list = [ for k in 0 .. r.Count - 1 -> Rows.at r k ]

let rows =
    testList "Live.Rows" [
        testCase "a filter chooses the rows that pass, and a table that only grew costs the new rows" <| fun () ->
            let (d, id, cols) = table ()
            let threshold = Source<float>(d.Engine, 10.0, Same.float)
            let mutable tested = 0
            let big = Live.filter d.Engine cols threshold (fun limit c -> let a = c.Column "amount" in fun i -> tested <- tested + 1; a.[i] > limit)
            append d id [ 5.0; 15.0; 20.0 ] [ 1.0; 1.0; 2.0 ]
            Expect.equal (chosen big.Value) [ 1; 2 ] "rows 1 and 2"
            Expect.equal tested 3 "three rows tested"
            append d id [ 11.0; 3.0 ] [ 2.0; 1.0 ]
            Expect.equal (chosen big.Value) [ 1; 2; 3 ] "and the new one that passes"
            Expect.equal tested 5 "two more rows tested, not five"
            let epoch = big.Value.Epoch
            threshold.Set 12.0 |> ignore
            d.Engine.Flush() |> ignore
            Expect.equal (chosen big.Value) [ 1; 2 ] "a new limit"
            Expect.equal tested 10 "the whole table again"
            Expect.isTrue (big.Value.Epoch <> epoch) "and the epoch says what was chosen before is gone"
            d.Edit("fix", fun tx -> tx.Write(id, "amount", 0, [| 99.0 |])) |> ignore
            Expect.equal (chosen big.Value) [ 0; 1; 2 ] "a number that was there changed: the whole table again"
        testCase "group by: the rows of each key, a measure each, in the order of the keys, and the new rows only when the table grew" <| fun () ->
            let (d, id, cols) = table ()
            let everything = Live.all d.Engine cols
            let measures = [| ("amount", Sum); ("amount", Min); ("amount", Max); ("amount", Mean); ("amount", Count) |]
            let g = Live.groupBy d.Engine cols (fun () -> everything.Value) "group" measures
            append d id [ 10.0; 30.0; 5.0; 20.0 ] [ 2.0; 1.0; 2.0; 1.0 ]
            let a = g.Value
            Expect.equal (a.Keys, a.Sizes, a.Count) ([| 1; 2 |], [| 2; 2 |], 2) "two groups of two, by key"
            Expect.equal (a.Values.[0], a.Values.[1], a.Values.[2]) ([| 50.0; 15.0 |], [| 20.0; 5.0 |], [| 30.0; 10.0 |]) "sum, min and max"
            Expect.equal (a.Values.[3], a.Values.[4]) ([| 25.0; 7.5 |], [| 2.0; 2.0 |]) "mean and count"
            append d id [ 40.0; 1.0 ] [ 3.0; 2.0 ]
            let b = g.Value
            Expect.equal (b.Keys, b.Sizes) ([| 1; 2; 3 |], [| 2; 3; 1 |]) "a third group, and one more row in the second"
            Expect.equal (b.Values.[0], b.Values.[1]) ([| 50.0; 16.0; 40.0 |], [| 20.0; 1.0; 40.0 |]) "sums and mins"
            d.Edit("fix", fun tx -> tx.Write(id, "amount", 1, [| 100.0 |])) |> ignore
            Expect.equal g.Value.Values.[0] [| 120.0; 16.0; 40.0 |] "a number that was there changed: counted again"
        testCase "group by over a filter: only the rows that passed, and the filter's new limit is a new count" <| fun () ->
            let (d, id, cols) = table ()
            let limit = Source<float>(d.Engine, 0.0, Same.float)
            let passed = Live.filter d.Engine cols limit (fun l c -> let a = c.Column "amount" in fun i -> a.[i] > l)
            let g = Live.groupBy d.Engine cols (fun () -> passed.Value) "group" [| ("amount", Count) |]
            append d id [ 1.0; 20.0; 3.0; 40.0; 50.0 ] [ 1.0; 1.0; 2.0; 2.0; 2.0 ]
            Expect.equal (g.Value.Keys, g.Value.Values.[0]) ([| 1; 2 |], [| 2.0; 3.0 |]) "all five rows"
            limit.Set 10.0 |> ignore
            d.Engine.Flush() |> ignore
            Expect.equal (g.Value.Keys, g.Value.Values.[0]) ([| 1; 2 |], [| 1.0; 2.0 |]) "three of them"
            append d id [ 60.0; 2.0 ] [ 3.0; 3.0 ]
            Expect.equal (g.Value.Keys, g.Value.Values.[0]) ([| 1; 2; 3 |], [| 1.0; 2.0; 1.0 |]) "a new group from a row that passed, none from one that did not"
        testCase "columns of plain arrays work the same, and a column that is not there is said" <| fun () ->
            let e = Engine()
            let cols = Columns.ofArrays [ ("v", [| 1.0; 2.0; 3.0 |]); ("k", [| 0.0; 1.0; 0.0 |]) ]
            let g = Live.groupBy e cols (fun () -> { Index = [||]; Count = 3; Epoch = 0; Identity = true }) "k" [| ("v", Sum) |]
            Expect.equal g.Value.Values.[0] [| 4.0; 2.0 |] "sums by key"
    ]

// a small ledger: two accounts in a group, three months
let private ledger () : Facts =
    let rows = Hierarchy.ofPaths [| [| "Revenue"; "A" |]; [| "Revenue"; "B" |]; [| "Cost"; "C" |] |]
    let cols = Hierarchy.ofPaths [| [| "Q1"; "Jan" |]; [| "Q1"; "Feb" |]; [| "Q2"; "Mar" |] |]
    // every account has 1 in every month, so a leaf is 1, a group of two accounts is 2 per month
    let account = [| for a in 0 .. 2 do for _ in 0 .. 2 -> a |]
    let month = [| for _ in 0 .. 2 do for m in 0 .. 2 -> m |]
    let ones = Array.create 9 1.0
    { Count = 9
      Dimensions = [| { Name = "Account"; Tree = rows; Leaf = account }; { Name = "Period"; Tree = cols; Leaf = month } |]
      Measures = [| { Name = "Actual"; OverRows = Agg.Sum; OverCols = Agg.Sum }; { Name = "Budget"; OverRows = Agg.Sum; OverCols = Agg.Sum } |]
      Values = [| ones; ones |] }

let cubes =
    testList "Live.Cubes" [
        testCase "the pivot is built for the pair of dimensions, and turning the table is a new pair" <| fun () ->
            let e = Engine()
            let f = ledger ()
            let axes = Source<int * int>(e, (0, 1), (fun a b -> fst a = fst b && snd a = snd b))
            let cube = Cubes.pivot e f axes [||]
            Expect.equal (Cube.value cube.Value 0 0 0) 9.0 "nine in all"
            Expect.equal cube.Value.RowTree.Leaves 3 "accounts on the rows"
            axes.Set((1, 0)) |> ignore
            e.Flush() |> ignore
            Expect.equal cube.Value.RowTree.Leaves 3 "months on the rows now"
            Expect.equal (cube.Value.RowTree.Nodes.[1].Label) "Q1" "and they are the periods"
        testCase "facts written into in place: the cube is built again when the revision is raised, and not before" <| fun () ->
            let e = Engine()
            let f = ledger ()
            let axes = Source<int * int>(e, (0, 1), (fun a b -> fst a = fst b && snd a = snd b))
            let revision = Source<int>(e, 0, Same.int)
            let cube = Cubes.pivotOver e f axes (fun () -> revision.Value) [||]
            Expect.equal (Cube.value cube.Value 0 0 0) 9.0 "nine ones"
            // line 0 is account 0 in month 0: it is 5 now
            f.Values.[0].[0] <- 5.0
            e.Flush() |> ignore
            Expect.equal (Cube.value cube.Value 0 0 0) 9.0 "nobody said so: the cube is the one it was"
            revision.Set 1 |> ignore
            e.Flush() |> ignore
            Expect.equal (Cube.value cube.Value 0 0 0) 13.0 "the revision: nine ones, one of them five"
            Expect.equal (Cube.leaf cube.Value 0 0 0) 5.0 "in the cell of the line"
        testCase "a plan over the budget: only the cells that changed are written, and the totals above them move by the difference" <| fun () ->
            let e = Engine()
            let f = ledger ()
            let axes = Source<int * int>(e, (0, 1), (fun a b -> fst a = fst b && snd a = snd b))
            let raw = Cubes.pivot e f axes [||]
            let plan = Source<(int * int * float)[]>(e, [||], Same.reference)
            let cube = Cubes.overlay e raw 1 (fun () -> plan.Value)
            Expect.isTrue (obj.ReferenceEquals(cube.Value, raw.Value)) "no plan: the cube of the facts itself"
            plan.Set [| (0, 0, 10.0) |] |> ignore
            e.Flush() |> ignore
            Expect.equal (Cube.leaf cube.Value 1 0 0) 10.0 "the cell"
            Expect.equal (Cube.value cube.Value 1 0 0) 18.0 "the grand total: nine ones, one of them ten"
            Expect.equal (Cube.value cube.Value 0 0 0) 9.0 "what was booked is not touched"
            let first = cube.Value
            plan.Set [| (0, 0, 10.0); (2, 2, 4.0) |] |> ignore
            e.Flush() |> ignore
            Expect.equal (Cube.value cube.Value 1 0 0) 21.0 "a second cell: 9 + 9 + 3"
            Expect.equal (Cube.leaf cube.Value 1 0 0) 10.0 "the first is still there"
            plan.Set [| (2, 2, 4.0) |] |> ignore
            e.Flush() |> ignore
            Expect.equal (Cube.leaf cube.Value 1 0 0) 1.0 "a cell that left the plan is the number of the facts again"
            Expect.equal (Cube.value cube.Value 1 0 0) 12.0 "9 + 3"
            Expect.isFalse (obj.ReferenceEquals(first, cube.Value)) "every change is a cube of its own"
        testCase "turning the table writes the plan again into the new cube" <| fun () ->
            let e = Engine()
            let f = ledger ()
            let axes = Source<int * int>(e, (0, 1), (fun a b -> fst a = fst b && snd a = snd b))
            let raw = Cubes.pivot e f axes [||]
            // account 1, month 2 = 7: on the turned table that is row 2, column 1
            let plan () : (int * int * float)[] = if axes.Value = (0, 1) then [| (1, 2, 7.0) |] else [| (2, 1, 7.0) |]
            let cube = Cubes.overlay e raw 1 plan
            Expect.equal (Cube.value cube.Value 1 0 0) 15.0 "9 + 6"
            axes.Set((1, 0)) |> ignore
            e.Flush() |> ignore
            Expect.equal (Cube.value cube.Value 1 0 0) 15.0 "the same total on the turned table"
            Expect.equal (Cube.leaf cube.Value 1 2 1) 7.0 "in the cell where the plan puts it"
        testCase "the lines that are shown follow what is open" <| fun () ->
            let e = Engine()
            let tree = Hierarchy.ofPaths [| [| "Revenue"; "A" |]; [| "Revenue"; "B" |]; [| "Cost"; "C" |] |]
            let isOpen = Source<bool[]>(e, tree.Nodes |> Array.mapi (fun i _ -> i = 0), Same.reference)
            let lines = Cubes.visible e tree isOpen
            Expect.equal lines.Value.Length 3 "the total and the two groups"
            isOpen.Set(tree.Nodes |> Array.map (fun _ -> true)) |> ignore
            e.Flush() |> ignore
            Expect.equal lines.Value.Length 6 "everything open: the total, two groups and three accounts"
            isOpen.Set [| false; true; true; true; true |] |> ignore
            e.Flush() |> ignore
            Expect.equal lines.Value.Length 1 "the total folded is one line"
    ]

let suites = [ rows; cubes ]
