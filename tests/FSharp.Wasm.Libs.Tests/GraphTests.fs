/// The tests of FSharp.Wasm.Graph: the engine (sources and calcs, once each and in order, the early cut-off, reads that differ from run to run, cycles, a long chain), the document
/// (patches, their inverses, the same graph in any order, lists, columns) and its history.  Written with Wasmecto, run under .NET and as WebAssembly GC.
module FSharp.Wasm.Libs.GraphTests

open Wasmecto
open FSharp.Wasm.Graph

let private quiet (_: int) (_: int) : unit = ()
let private calc (e: Engine) (f: unit -> int) : Calc<int> = Calc<int>(e, Same.int, -1, quiet, f)
let private source (e: Engine) (v: int) : Source<int> = Source<int>(e, v, Same.int)

let engine =
    testList "Graph.Engine" [
        testCase "a calc is made of what it reads, and runs again when that changes" <| fun () ->
            let e = Engine()
            let a = source e 2
            let b = source e 3
            let sum = calc e (fun () -> a.Value + b.Value)
            Expect.equal sum.Value 5 "first value"
            a.Set 10 |> ignore
            Expect.equal sum.Value 5 "nothing moves before the flush"
            let f = e.Flush()
            Expect.equal sum.Value 13 "after it"
            Expect.equal (f.Reached, f.Ran, f.Changed) (1, 1, 1) "one calc reached, ran, changed"
            Expect.isFalse (a.Set 10) "the same value is no change"
            Expect.equal (e.Flush()).Reached 0 "and reaches nothing"
        testCase "a calc runs once after the cells it reads, also in a diamond" <| fun () ->
            let e = Engine()
            let x = source e 1
            let mutable runs = 0
            let left = calc e (fun () -> x.Value + 1)
            let right = calc e (fun () -> x.Value * 10)
            let bottom = calc e (fun () -> runs <- runs + 1; left.Value + right.Value)
            runs <- 0
            x.Set 2 |> ignore
            let f = e.Flush()
            Expect.equal runs 1 "the bottom ran once, not twice"
            Expect.equal bottom.Value 23 "with both new values"
            Expect.equal f.Ran 3 "three runs"
        testCase "early cut-off: what reads a calc that came out the same does not run" <| fun () ->
            let e = Engine()
            let x = source e 5
            let positive = calc e (fun () -> if x.Value > 0 then 1 else 0)
            let mutable runs = 0
            let label = calc e (fun () -> runs <- runs + 1; positive.Value * 100)
            runs <- 0
            x.Set 6 |> ignore
            let f = e.Flush()
            Expect.equal runs 0 "label was not run: positive is still 1"
            Expect.equal (f.Reached, f.Ran, f.Changed) (2, 1, 0) "two reached, one ran, none changed: the cut-off saved one"
            x.Set -1 |> ignore
            e.Flush() |> ignore
            Expect.equal (runs, label.Value) (1, 0) "now it ran, and it is 0"
        testCase "what a calc reads can differ from run to run: the branch not taken is not a dependency" <| fun () ->
            let e = Engine()
            let useA = source e 1
            let a = source e 10
            let b = source e 20
            let mutable runs = 0
            let pick = calc e (fun () -> runs <- runs + 1; if useA.Value = 1 then a.Value else b.Value)
            runs <- 0
            b.Set 21 |> ignore
            e.Flush() |> ignore
            Expect.equal runs 0 "b is not read while a is picked"
            useA.Set 0 |> ignore
            e.Flush() |> ignore
            Expect.equal pick.Value 21 "now b"
            runs <- 0
            a.Set 11 |> ignore
            e.Flush() |> ignore
            Expect.equal runs 0 "and a is not read any more"
        testCase "calcs that read each other have the value of a cycle, and are themselves again when it is broken" <| fun () ->
            let e = Engine()
            let t = source e 1
            let other : Calc<int> option ref = ref None
            let b = calc e (fun () -> match other.Value with Some a -> a.Value + 1 | None -> 0)
            let a = calc e (fun () -> t.Value + b.Value)
            Expect.equal (a.Value, b.Value) (1, 0) "no cycle yet"
            other.Value <- Some a
            b.Invalidate()
            e.Flush() |> ignore
            Expect.equal (a.Value, b.Value) (-1, -1) "both are the cycle"
            other.Value <- None
            b.Invalidate()
            e.Flush() |> ignore
            Expect.equal (a.Value, b.Value) (1, 0) "the cycle is gone with its cause"
        testCase "a calc that reads itself is a cycle" <| fun () ->
            let e = Engine()
            let self : Calc<int> option ref = ref None
            let t = source e 1
            let c = calc e (fun () -> match self.Value with Some s -> s.Value + t.Value | None -> t.Value)
            self.Value <- Some c
            t.Set 2 |> ignore
            e.Flush() |> ignore
            Expect.equal c.Value -1 "itself"
        testCase "onChange hears the old and the new value, in the order of the flush, and not the first one" <| fun () ->
            let e = Engine()
            let x = source e 1
            let heard = ResizeArray<string>()
            let first = Calc<int>(e, Same.int, -1, (fun a b -> heard.Add("first " + string a + ">" + string b)), fun () -> x.Value + 1)
            let second = Calc<int>(e, Same.int, -1, (fun a b -> heard.Add("second " + string a + ">" + string b)), fun () -> first.Value * 2)
            Expect.equal heard.Count 0 "making them tells nobody"
            x.Set 5 |> ignore
            e.Flush() |> ignore
            Expect.equal (String.concat "; " heard) "first 2>6; second 4>12" "dependency order"
        testCase "a chain a hundred thousand long is a walk and not a call stack" <| fun () ->
            let e = Engine()
            let x = source e 0
            let n = 100000
            let chain = Array.zeroCreate<Calc<int>> n
            chain.[0] <- calc e (fun () -> x.Value + 1)
            for i in 1 .. n - 1 do
                let prev = chain.[i - 1]
                chain.[i] <- calc e (fun () -> prev.Value + 1)
            Expect.equal chain.[n - 1].Value n "made"
            x.Set 10 |> ignore
            let f = e.Flush()
            Expect.equal chain.[n - 1].Value (n + 10) "every one moved"
            Expect.equal f.Ran n "each ran once"
        testCase "invalidate: what the engine cannot see (a name of the host) is told" <| fun () ->
            let e = Engine()
            let mutable host = 1
            let c = calc e (fun () -> host * 10)
            let d = calc e (fun () -> c.Value + 1)
            host <- 2
            Expect.equal d.Value 11 "nothing moves by itself"
            c.Invalidate()
            e.Flush() |> ignore
            Expect.equal d.Value 21 "told, it does"
    ]

let private text (a: Atom) : string = Atom.toText a
let private itemsOf (d: Doc) (id: int) (f: string) : string = d.Items(id, f) |> Array.map text |> String.concat ","

/// Two actors that have both seen the node `id` (made by the first).
let private pair () : Doc * Doc * int =
    let a = Doc(1)
    let b = Doc(2)
    let made = a.Edit("make", fun tx -> tx.Create "Thing" |> ignore)
    b.Apply made.Patch |> ignore
    let id = match made.Patch.Ops with Create (id, _) :: _ -> id | _ -> -1
    (a, b, id)

let doc =
    testList "Graph.Doc" [
        testCase "a node has a kind and fields, and an edit is a patch with every edit stamped" <| fun () ->
            let d = Doc(1)
            let mutable id = 0
            let applied =
                d.Edit("name it", fun tx ->
                    id <- tx.Create "Account"
                    tx.Set(id, "name", Text "Revenue")
                    tx.Set(id, "level", Num 1.0)
                    tx.Set(id, "open", Bool true))
            Expect.equal (d.Kind id) "Account" "its kind"
            Expect.equal (d.Get(id, "name")) (Text "Revenue") "a text"
            Expect.equal (d.Get(id, "level")) (Num 1.0) "a number"
            Expect.equal (d.Get(id, "missing")) Nil "what was never set is nothing"
            Expect.equal applied.Patch.Ops.Length 4 "four edits"
            Expect.isTrue (applied.Patch.Ops |> List.forall (fun op -> match op with Set (_, _, _, s) -> s <> Stamp.none | _ -> true)) "every one stamped"
            Expect.equal (id % 1024) 1 "the id says who made it"
        testCase "the inverse of a patch takes the step back, and its inverse does it again" <| fun () ->
            let d = Doc(1)
            let mutable id = 0
            d.Edit("make", fun tx -> id <- tx.Create "Thing"; tx.Set(id, "x", Num 1.0)) |> ignore
            let step = d.Edit("change", fun tx -> tx.Set(id, "x", Num 2.0); tx.Set(id, "y", Text "new"))
            Expect.equal (d.Get(id, "x"), d.Get(id, "y")) (Num 2.0, Text "new") "changed"
            let back = d.Apply step.Inverse
            Expect.equal (d.Get(id, "x"), d.Get(id, "y")) (Num 1.0, Nil) "back"
            d.Apply back.Inverse |> ignore
            Expect.equal (d.Get(id, "x"), d.Get(id, "y")) (Num 2.0, Text "new") "and again"
        testCase "a patch taken elsewhere gives the same graph" <| fun () ->
            let a = Doc(1)
            let b = Doc(2)
            let mutable id = 0
            let p1 = a.Edit("one", fun tx -> id <- tx.Create "Thing"; tx.Set(id, "x", Num 1.0); tx.Put(id, "m", "k", Text "v"); tx.Insert(id, "l", 0, Text "a"))
            let p2 = a.Edit("two", fun tx -> tx.Append(id, "c", [| 1.0; 2.0 |]))
            b.Apply p1.Patch |> ignore
            b.Apply p2.Patch |> ignore
            Expect.equal (b.Get(id, "x"), b.Entry(id, "m", "k"), itemsOf b id "l") (Num 1.0, Text "v", "a") "register, map, list"
            Expect.equal (b.Count(id, "c")) 2 "column"
            Expect.equal (b.Apply p1.Patch).Inverse.Ops [] "the same patch again changes nothing: a stamp that is not newer, an item that is there"
        testCase "two edits of one field that did not see each other: the later stamp wins, in any order" <| fun () ->
            let (a, b, id) = pair ()
            let pa = a.Edit("a", fun tx -> tx.Set(id, "x", Text "from a"))
            let pb = b.Edit("b", fun tx -> tx.Set(id, "x", Text "from b"))
            a.Apply pb.Patch |> ignore
            b.Apply pa.Patch |> ignore
            Expect.equal (a.Get(id, "x")) (b.Get(id, "x")) "the same on both"
            Expect.equal (a.Get(id, "x")) (Text "from b") "the higher actor at the same clock"
            // and a later edit of the loser wins over both
            let later = a.Edit("later", fun tx -> tx.Set(id, "x", Text "later"))
            b.Apply later.Patch |> ignore
            Expect.equal (b.Get(id, "x")) (Text "later") "a clock that has seen both goes on from there"
        testCase "a list: places, and an item taken out" <| fun () ->
            let d = Doc(1)
            let mutable id = 0
            d.Edit("fill", fun tx -> id <- tx.Create "List"; (for i in 0 .. 2 do tx.Insert(id, "l", i, Text (string i)))) |> ignore
            Expect.equal (itemsOf d id "l") "0,1,2" "in order"
            d.Edit("front", fun tx -> tx.Insert(id, "l", 0, Text "f")) |> ignore
            Expect.equal (itemsOf d id "l") "f,0,1,2" "at the front"
            d.Edit("middle", fun tx -> tx.Insert(id, "l", 2, Text "m")) |> ignore
            Expect.equal (itemsOf d id "l") "f,0,m,1,2" "in the middle"
            let gone = d.Edit("remove", fun tx -> tx.Remove(id, "l", 2))
            Expect.equal (itemsOf d id "l") "f,0,1,2" "an item taken out"
            d.Apply gone.Inverse |> ignore
            Expect.equal (itemsOf d id "l") "f,0,m,1,2" "and put back where it was"
        testCase "concurrent inserts after the same item end in the same order whichever arrives first" <| fun () ->
            let (a, b, id) = pair ()
            let seed = a.Edit("seed", fun tx -> tx.Insert(id, "l", 0, Text "x"))
            b.Apply seed.Patch |> ignore
            let pa = a.Edit("a", fun tx -> tx.Insert(id, "l", 1, Text "A1"); tx.Insert(id, "l", 2, Text "A2"))
            let pb = b.Edit("b", fun tx -> tx.Insert(id, "l", 1, Text "B1"))
            a.Apply pb.Patch |> ignore
            b.Apply pa.Patch |> ignore
            Expect.equal (itemsOf a id "l") (itemsOf b id "l") "the same list on both"
            Expect.equal ((itemsOf a id "l").Split(',').Length) 4 "all four are there"
            Expect.stringStarts (itemsOf a id "l") "x," "after the item they were put behind"
        testCase "a map: keys, an entry that is read before it is there, and a key that goes" <| fun () ->
            let d = Doc(1)
            let mutable id = 0
            d.Edit("make", fun tx -> id <- tx.Create "Map") |> ignore
            let seen = ResizeArray<string>()
            let reader = Calc<string>(d.Engine, Same.text, "cycle", (fun _ n -> seen.Add n), fun () -> text (d.Entry(id, "m", "b")))
            let count = Calc<int>(d.Engine, Same.int, -1, (fun _ _ -> ()), fun () -> (d.Keys(id, "m")).Length)
            d.Edit("a", fun tx -> tx.Put(id, "m", "a", Num 1.0)) |> ignore
            Expect.equal seen.Count 0 "a key that the calc does not read does not reach it"
            Expect.equal count.Value 1 "but the keys changed"
            d.Edit("b", fun tx -> tx.Put(id, "m", "b", Text "here")) |> ignore
            Expect.equal (String.concat "," seen) "here" "the key it waited for"
            Expect.equal (String.concat "," (d.Keys(id, "m"))) "a,b" "the keys, in order"
            d.Edit("b gone", fun tx -> tx.Put(id, "m", "b", Nil)) |> ignore
            Expect.equal (String.concat "," (d.Keys(id, "m"))) "a" "a key that holds nothing is not a key"
            Expect.equal reader.Value "" "and the reader is empty again"
        testCase "a column: append, write, truncate, and what changed for a calc that follows the end" <| fun () ->
            let d = Doc(1)
            let mutable id = 0
            d.Edit("make", fun tx -> id <- tx.Create "Series") |> ignore
            // a calc that sums a column and keeps what it has summed: it adds only the new numbers while the column has only grown
            let mutable seen = 0
            let mutable total = 0.0
            let mutable epoch = -1
            let mutable scanned = 0
            let sum =
                Calc<float>(d.Engine, Same.float, nan, (fun _ _ -> ()), fun () ->
                    let n = d.Count(id, "v")
                    let e = d.Epoch(id, "v")
                    if e <> epoch || n < seen then
                        seen <- 0
                        total <- 0.0
                        epoch <- e
                    let data = d.Data(id, "v")
                    for i in seen .. n - 1 do
                        total <- total + data.[i]
                        scanned <- scanned + 1
                    seen <- n
                    total)
            d.Edit("one", fun tx -> tx.Append(id, "v", [| 1.0; 2.0; 3.0 |])) |> ignore
            d.Edit("two", fun tx -> tx.Append(id, "v", [| 4.0 |])) |> ignore
            Expect.equal (sum.Value, scanned) (10.0, 4) "each number was added once"
            d.Edit("over", fun tx -> tx.Write(id, "v", 1, [| 20.0 |])) |> ignore
            Expect.equal sum.Value 28.0 "a write is a new sum"
            Expect.equal scanned 8 "the whole column again"
            let cut = d.Edit("cut", fun tx -> tx.Truncate(id, "v", 2))
            Expect.equal (sum.Value, d.Count(id, "v")) (21.0, 2) "truncated"
            d.Apply cut.Inverse |> ignore
            Expect.equal (sum.Value, d.Count(id, "v")) (28.0, 4) "and the inverse puts the numbers back"
            for i in 1 .. 100 do d.Edit("grow", fun tx -> tx.Append(id, "v", [| 1.0 |])) |> ignore
            Expect.equal (d.Count(id, "v")) 104 "the array grew"
        testCase "an edit that arrives before the creation of its node is kept for it" <| fun () ->
            let a = Doc(1)
            let b = Doc(2)
            let mutable id = 0
            let create = a.Edit("create", fun tx -> id <- tx.Create "Thing")
            let set = a.Edit("set", fun tx -> tx.Set(id, "x", Num 5.0))
            b.Apply set.Patch |> ignore
            Expect.equal (b.Get(id, "x")) (Num 5.0) "kept"
            b.Apply create.Patch |> ignore
            Expect.equal (b.Kind id, b.Get(id, "x")) ("Thing", Num 5.0) "and the node is what it was"
        testCase "a calc over the graph runs for the edits that reach it, and the answer of the edit says how many it left alone" <| fun () ->
            let d = Doc(1)
            let mutable id = 0
            d.Edit("make", fun tx -> id <- tx.Create "Thing"; tx.Set(id, "x", Num 3.0)) |> ignore
            let positive = Calc<bool>(d.Engine, (fun a b -> a = b), false, (fun _ _ -> ()), fun () -> (match d.Get(id, "x") with Num n -> n > 0.0 | _ -> false))
            let label = Calc<string>(d.Engine, Same.text, "", (fun _ _ -> ()), fun () -> if positive.Value then "up" else "down")
            let a = d.Edit("4", fun tx -> tx.Set(id, "x", Num 4.0))
            Expect.equal (a.Flushed.Reached, a.Flushed.Ran, a.Flushed.Changed) (2, 1, 0) "positive ran, came out the same, and label was left alone"
            let b = d.Edit("-1", fun tx -> tx.Set(id, "x", Num -1.0))
            Expect.equal (b.Flushed.Reached, b.Flushed.Ran, b.Flushed.Changed) (2, 2, 2) "now both"
            Expect.equal label.Value "down" "label"
    ]

let timeline =
    testList "Graph.Timeline" [
        testCase "steps are undone and done again, and what was done again is gone after a new step" <| fun () ->
            let d = Doc(1)
            let t = Timeline(d)
            let mutable id = 0
            t.Do("make", fun tx -> id <- tx.Create "Thing"; tx.Set(id, "x", Num 1.0)) |> ignore
            t.Do("two", fun tx -> tx.Set(id, "x", Num 2.0)) |> ignore
            t.Do("three", fun tx -> tx.Set(id, "x", Num 3.0)) |> ignore
            Expect.equal (d.Get(id, "x")) (Num 3.0) "three steps"
            t.Undo() |> ignore
            Expect.equal (d.Get(id, "x")) (Num 2.0) "one back"
            t.Undo() |> ignore
            Expect.equal (d.Get(id, "x")) (Num 1.0) "two back: an earlier step undone after a later one on the same field"
            t.Redo() |> ignore
            Expect.equal (d.Get(id, "x")) (Num 2.0) "forward"
            t.Do("branch", fun tx -> tx.Set(id, "x", Num 9.0)) |> ignore
            Expect.isFalse t.CanRedo "a new step ends what could be done again"
            t.Undo() |> ignore
            t.Undo() |> ignore
            Expect.equal (d.Get(id, "x")) (Num 1.0) "back to the start of the field"
            t.Undo() |> ignore
            Expect.equal (d.Exists id) false "the first step took the node away"
            Expect.isFalse t.CanUndo "nothing more"
        testCase "the tape names an undo and a redo by the step they take back or do again" <| fun () ->
            let d = Doc(1)
            let t = Timeline(d)
            let mutable id = 0
            t.Do("make", fun tx -> id <- tx.Create "Thing") |> ignore
            t.Do("set x", fun tx -> tx.Set(id, "x", Num 1.0)) |> ignore
            t.Undo() |> ignore
            t.Redo() |> ignore
            t.Undo() |> ignore
            Expect.equal (t.Tape |> Array.map (fun e -> e.Label) |> String.concat ",") "make,set x,undo set x,redo set x,undo set x" "not undo undo, and not an undo for a redo"
        testCase "the tape has every step with its patch and its flush, and a step from elsewhere is on it and stays out of the undo" <| fun () ->
            let a = Doc(1)
            let ta = Timeline(a)
            let b = Doc(2)
            let tb = Timeline(b)
            let mutable id = 0
            let made = ta.Do("make", fun tx -> id <- tx.Create "Thing")
            tb.Receive made.Patch |> ignore
            let theirs = tb.Do("theirs", fun tx -> tx.Set(id, "x", Text "b"))
            ta.Receive theirs.Patch |> ignore
            ta.Do("mine", fun tx -> tx.Set(id, "y", Text "a")) |> ignore
            let tape = ta.Tape
            Expect.equal (tape |> Array.map (fun e -> e.Label) |> String.concat ",") "make,theirs,mine" "in order"
            Expect.equal (tape |> Array.map (fun e -> e.Remote) |> Array.toList) [ false; true; false ] "who"
            Expect.equal (tape |> Array.map (fun e -> e.Number) |> Array.toList) [ 1; 2; 3 ] "numbered"
            ta.Undo() |> ignore
            Expect.equal (a.Get(id, "y"), a.Get(id, "x")) (Nil, Text "b") "undo takes my step and not theirs"
            ta.Undo() |> ignore
            Expect.equal (a.Exists id, a.Get(id, "x")) (false, Text "b") "the next one is my own first step, not theirs"
    ]

let suites = [ engine; doc; timeline ]
