/// The tests of FSharp.Wasm.Edit: operations on a tree, their inverses, where a drop lands, the history and the edit in place.
/// Written with Wasmecto, run under .NET and as WebAssembly GC.
module FSharp.Wasm.Libs.EditTests

open Wasmecto
open FSharp.Wasm.Edit

type Node = { Name: string; Sub: Node list }

let private shape : Shape<Node> = { Kids = (fun n -> n.Sub); WithKids = (fun n k -> { n with Sub = k }) }
let private leaf (n: string) : Node = { Name = n; Sub = [] }
let private branch (n: string) (k: Node list) : Node = { Name = n; Sub = k }

/// a  b( c  d( e ) )  f
let private doc : Node list = [ leaf "a"; branch "b" [ leaf "c"; branch "d" [ leaf "e" ] ]; leaf "f" ]

let rec private show (items: Node list) : string =
    items |> List.map (fun n -> if List.isEmpty n.Sub then n.Name else n.Name + "(" + show n.Sub + ")") |> String.concat " "

let rec private paths (prefix: Path) (items: Node list) : Path list =
    items |> List.mapi (fun i n -> (prefix @ [ i ]) :: paths (prefix @ [ i ]) n.Sub) |> List.concat

let rec private names (items: Node list) : string list =
    items |> List.collect (fun n -> n.Name :: names n.Sub)

let private pathOf (name: string) (items: Node list) : Path option =
    paths [] items |> List.tryFind (fun p -> (match Ops.get shape p items with Some n -> n.Name = name | None -> false))

let private ok (r: Result<Node list, string>) : Node list =
    match r with
    | Ok v -> v
    | Error e -> failwith e

let private next (s: int) : int = (s * 1103515245 + 12345) &&& 0x7fffffff

let operations =
    testList "Operations" [
        testCase "insert, remove and replace, at the top and nested" <| fun () ->
            Expect.equal (show (ok (Ops.apply shape (Insert ([ 1; 1 ], leaf "x")) doc))) "a b(c x d(e)) f" "insert nested"
            Expect.equal (show (ok (Ops.apply shape (Insert ([ 3 ], leaf "x")) doc))) "a b(c d(e)) f x" "insert at the end"
            Expect.equal (show (ok (Ops.apply shape (Remove ([ 1; 1; 0 ], leaf "e")) doc))) "a b(c d) f" "remove the only child"
            Expect.equal (show (ok (Ops.apply shape (Replace ([ 1; 0 ], leaf "c", leaf "z")) doc))) "a b(z d(e)) f" "replace"
        testCase "a place that does not exist is an error and changes nothing" <| fun () ->
            let bad (r: Result<Node list, string>) = (match r with Error _ -> true | Ok _ -> false)
            Expect.isTrue (bad (Ops.apply shape (Insert ([ 9 ], leaf "x")) doc)) "insert past the end"
            Expect.isTrue (bad (Ops.apply shape (Insert ([ 5; 0 ], leaf "x")) doc)) "insert under nothing"
            Expect.isTrue (bad (Ops.apply shape (Remove ([ 7 ], leaf "q")) doc)) "remove nothing"
            Expect.isTrue (bad (Ops.apply shape (Replace ([ 1; 5 ], leaf "q", leaf "r")) doc)) "replace nothing"
            Expect.isTrue (bad (Ops.apply shape (Move ([ 7 ], [ 0 ])) doc)) "move nothing"
        testCase "a move takes the item out and puts it where the target reads without it" <| fun () ->
            Expect.equal (show (ok (Ops.apply shape (Move ([ 0 ], [ 2 ])) doc))) "b(c d(e)) f a" "to the end"
            Expect.equal (show (ok (Ops.apply shape (Move ([ 2 ], [ 0 ])) doc))) "f a b(c d(e))" "to the front"
            Expect.equal (show (ok (Ops.apply shape (Move ([ 1; 0 ], [ 0 ])) doc))) "c a b(d(e)) f" "out of a parent"
            Expect.equal (show (ok (Ops.apply shape (Move ([ 0 ], [ 0; 0 ])) doc))) "b(a c d(e)) f" "into a sibling, which shifted"
        testCase "many operations are done in order, and undone in the reverse order" <| fun () ->
            let op = Many [ Insert ([ 0 ], leaf "x"); Move ([ 0 ], [ 3 ]); Replace ([ 0 ], leaf "a", leaf "A") ]
            let after = ok (Ops.apply shape op doc)
            Expect.equal (show after) "A b(c d(e)) f x" "forward"
            Expect.equal (show (ok (Ops.apply shape (Ops.invert op) after))) (show doc) "back"
            Expect.equal (Ops.invert (Ops.invert op)) op "invert twice is the operation"
        testCase "random operations are all undone by their inverse (3000 steps)" <| fun () ->
            let mutable seed = 12345
            let mutable d = doc
            let mutable fresh = 0
            let pick (n: int) : int = (seed <- next seed; (seed >>> 8) % (max n 1))
            let mutable failures = 0
            for _ in 1 .. 3000 do
                let ps = paths [] d
                let count = List.length ps
                let kind = pick 5
                let op : Op<Node> option =
                    if count = 0 || kind = 0 || (kind = 4 && count < 6) then
                        fresh <- fresh + 1
                        let under = if count = 0 || pick 2 = 0 then [] else List.item (pick count) ps
                        let n = (match Ops.get shape under d with Some p when not (List.isEmpty under) -> List.length p.Sub | _ -> List.length d)
                        Some (Insert (under @ [ pick (n + 1) ], leaf ("n" + string fresh)))
                    elif kind = 1 || (kind = 4 && count > 10) then Ops.remove shape (List.item (pick count) ps) d
                    elif kind = 2 then Ops.change shape (List.item (pick count) ps) (fun n -> { n with Name = n.Name + "'" }) d
                    else
                        let src = List.item (pick count) ps
                        let target = List.item (pick count) ps
                        let place = (match pick 3 with 0 -> Before target | 1 -> After target | _ -> Into target)
                        Ops.drop shape src place d
                match op with
                | None -> ()
                | Some o ->
                    match Ops.apply shape o d with
                    | Error _ -> failures <- failures + 1
                    | Ok d2 ->
                        (match Ops.apply shape (Ops.invert o) d2 with
                         | Ok back -> if back <> d then failures <- failures + 1
                         | Error _ -> failures <- failures + 1)
                        d <- d2
            Expect.equal failures 0 "every inverse gives the document back"
    ]

let drops =
    testList "Drops" [
        testCase "a drop lands before, after or into its target, whatever the shift of indices" <| fun () ->
            let all = paths [] doc
            let mutable checkedDrops = 0
            let mutable wrong = 0
            for src in all do
                for target in all do
                    for place in [ Before target; After target; Into target ] do
                        match Ops.drop shape src place doc with
                        | None -> ()
                        | Some op ->
                            checkedDrops <- checkedDrops + 1
                            let moved = (Ops.get shape src doc |> Option.map (fun n -> n.Name))
                            let anchor = (Ops.get shape target doc |> Option.map (fun n -> n.Name))
                            match moved, anchor with
                            | Some m, Some a ->
                                let d2 = ok (Ops.apply shape op doc)
                                let sameItems = (List.sort (names d2) = List.sort (names doc))
                                let pm = pathOf m d2
                                let pa = pathOf a d2
                                let placed =
                                    match pm, pa, place with
                                    | Some pm, Some pa, Before _ -> List.truncate (List.length pm - 1) pm = List.truncate (List.length pa - 1) pa && List.last pm + 1 = List.last pa
                                    | Some pm, Some pa, After _ -> List.truncate (List.length pm - 1) pm = List.truncate (List.length pa - 1) pa && List.last pm = List.last pa + 1
                                    | Some pm, Some pa, Into _ ->
                                        List.truncate (List.length pm - 1) pm = pa && (match Ops.get shape pa d2 with Some p -> List.last pm = List.length p.Sub - 1 | None -> false)
                                    | _ -> false
                                if not (sameItems && placed) then wrong <- wrong + 1
                            | _ -> wrong <- wrong + 1
            Expect.equal wrong 0 "every possible drop did what it said"
            Expect.isTrue (checkedDrops > 40) "and there were many of them"
        testCase "a drop that changes nothing, or goes into the item itself, is none" <| fun () ->
            Expect.isNone (Ops.drop shape [ 0 ] (Before [ 0 ]) doc) "before itself"
            Expect.isNone (Ops.drop shape [ 0 ] (After [ 0 ]) doc) "after itself"
            Expect.isNone (Ops.drop shape [ 0 ] (Before [ 1 ]) doc) "before the next one: where it is"
            Expect.isNone (Ops.drop shape [ 1 ] (Into [ 1 ]) doc) "into itself"
            Expect.isNone (Ops.drop shape [ 1 ] (Into [ 1; 1 ]) doc) "into its own child"
            Expect.isNone (Ops.drop shape [ 1 ] (After [ 1; 1; 0 ]) doc) "next to its own grandchild"
            Expect.isNone (Ops.drop shape [ 9 ] (Before [ 0 ]) doc) "no such item"
            Expect.isSome (Ops.drop shape [ 0 ] (After [ 1 ]) doc) "after the next one: a real move"
        testCase "a new item lands before, after or into its target" <| fun () ->
            let fresh = leaf "new"
            let all = paths [] doc
            let mutable wrong = 0
            for target in all do
                for place in [ Before target; After target; Into target ] do
                    match Ops.insertAt shape place fresh doc, Ops.get shape target doc with
                    | Some op, Some anchor ->
                        let d2 = ok (Ops.apply shape op doc)
                        let pn, pa = pathOf "new" d2, pathOf anchor.Name d2
                        let placed =
                            match pn, pa, place with
                            | Some pn, Some pa, Before _ -> List.truncate (List.length pn - 1) pn = List.truncate (List.length pa - 1) pa && List.last pn + 1 = List.last pa
                            | Some pn, Some pa, After _ -> List.truncate (List.length pn - 1) pn = List.truncate (List.length pa - 1) pa && List.last pn = List.last pa + 1
                            | Some pn, Some pa, Into _ -> List.truncate (List.length pn - 1) pn = pa
                            | _ -> false
                        if not (placed && List.length (names d2) = List.length (names doc) + 1 && show (ok (Ops.apply shape (Ops.invert op) d2)) = show doc) then wrong <- wrong + 1
                    | _ -> wrong <- wrong + 1
            Expect.equal wrong 0 "every place took the new item where it said, and the insert undoes"
            Expect.isNone (Ops.insertAt shape (Before [ 9 ]) fresh doc) "no such item to put it before"
            Expect.isNone (Ops.insertAt shape (After [ 1; 9 ]) fresh doc) "no such sibling"
            Expect.isNone (Ops.insertAt shape (Into [ 7 ]) fresh doc) "no such parent"
            Expect.equal (show (ok (Ops.apply shape (Option.get (Ops.insertAt shape (After [ 2 ]) fresh doc)) doc))) "a b(c d(e)) f new" "behind the last one"
            Expect.equal (show (ok (Ops.apply shape (Option.get (Ops.insertAt shape (Into [ 1 ]) fresh doc)) doc))) "a b(c d(e) new) f" "into a branch appends"
        testCase "dropping into a branch appends, and a move back restores the document" <| fun () ->
            let op = Ops.drop shape [ 0 ] (Into [ 1 ]) doc
            let d2 = ok (Ops.apply shape (Option.get op) doc)
            Expect.equal (show d2) "b(c d(e) a) f" "last child"
            Expect.equal (show (ok (Ops.apply shape (Ops.invert (Option.get op)) d2))) (show doc) "undone"
    ]

let history =
    testList "History" [
        testCase "commit, undo and redo" <| fun () ->
            let h0 : History<Node> = History.create 50
            let (d1, h1) = (match History.commit shape "" (Insert ([ 0 ], leaf "x")) doc h0 with Ok r -> r | Error e -> failwith e)
            let (d2, h2) = (match History.commit shape "" (Remove ([ 3 ], leaf "f")) d1 h1 with Ok r -> r | Error e -> failwith e)
            Expect.equal (show d2) "x a b(c d(e))" "two edits"
            let (u1, h3) = Option.get (History.undo shape d2 h2)
            Expect.equal (show u1) "x a b(c d(e)) f" "undo the removal"
            let (u2, h4) = Option.get (History.undo shape u1 h3)
            Expect.equal (show u2) (show doc) "undo the insertion"
            Expect.isFalse (History.canUndo h4) "nothing left to undo"
            let (r1, h5) = Option.get (History.redo shape u2 h4)
            let (r2, h6) = Option.get (History.redo shape r1 h5)
            Expect.equal (show r2) (show d2) "both redone"
            Expect.isFalse (History.canRedo h6) "nothing left to redo"
        testCase "a new edit ends the future" <| fun () ->
            let h0 : History<Node> = History.create 50
            let (d1, h1) = (match History.commit shape "" (Insert ([ 0 ], leaf "x")) doc h0 with Ok r -> r | Error e -> failwith e)
            let (u, h2) = Option.get (History.undo shape d1 h1)
            Expect.isTrue (History.canRedo h2) "can redo after undo"
            let (_, h3) = (match History.commit shape "" (Insert ([ 0 ], leaf "y")) u h2 with Ok r -> r | Error e -> failwith e)
            Expect.isFalse (History.canRedo h3) "a new edit clears it"
        testCase "typing in one place is one step, and undo ends it" <| fun () ->
            let h0 : History<Node> = History.create 50
            let typeInto (text: string) (d: Node list) (h: History<Node>) =
                let op = Ops.change shape [ 0 ] (fun n -> { n with Name = text }) d
                match History.commit shape "name:0" (Option.get op) d h with Ok r -> r | Error e -> failwith e
            let (d1, h1) = typeInto "a1" doc h0
            let (d2, h2) = typeInto "a12" d1 h1
            let (d3, h3) = typeInto "a123" d2 h2
            Expect.equal (List.length h3.Past) 1 "three keystrokes, one step"
            let (u, h4) = Option.get (History.undo shape d3 h3)
            Expect.equal (show u) (show doc) "one undo takes back all of them"
            let (d5, h5) = typeInto "b1" u h4
            let (_, h6) = typeInto "b12" d5 h5
            Expect.equal (List.length h6.Past) 1 "typing after an undo is a step of its own, folded only with itself"
            Expect.equal (List.length h6.Future) 0 "and it ended the future"
        testCase "another place, or another key, is another step" <| fun () ->
            let h0 : History<Node> = History.create 50
            let edit (key: string) (p: Path) (text: string) (d: Node list) (h: History<Node>) =
                let op = Ops.change shape p (fun n -> { n with Name = text }) d
                match History.commit shape key (Option.get op) d h with Ok r -> r | Error e -> failwith e
            let (d1, h1) = edit "k" [ 0 ] "a1" doc h0
            let (d2, h2) = edit "k" [ 2 ] "f1" d1 h1
            Expect.equal (List.length h2.Past) 2 "another place"
            let (_, h3) = edit "other" [ 2 ] "f2" d2 h2
            Expect.equal (List.length h3.Past) 3 "another key"
        testCase "only the last N steps are kept" <| fun () ->
            let h0 : History<Node> = History.create 3
            let mutable d = doc
            let mutable h = h0
            for i in 1 .. 5 do
                let (d2, h2) = (match History.commit shape "" (Insert ([ 0 ], leaf ("n" + string i))) d h with Ok r -> r | Error e -> failwith e)
                d <- d2
                h <- h2
            Expect.equal (List.length h.Past) 3 "three kept"
    ]

let inlineEdit =
    testList "Editing in place" [
        testCase "an edit that changes nothing commits nothing" <| fun () ->
            let s = Inline.start [ 0 ] (leaf "a")
            Expect.isNone (Inline.commit s) "unchanged"
            Expect.isNone (Inline.commit (Inline.change (leaf "a") s)) "typed and typed back"
        testCase "a changed draft commits one replace, which the history can undo" <| fun () ->
            let s = Inline.start [ 0 ] (leaf "a") |> Inline.change (leaf "alpha")
            let op = Option.get (Inline.commit s)
            Expect.equal op (Replace ([ 0 ], leaf "a", leaf "alpha")) "a replace of what was there"
            let h0 : History<Node> = History.create 10
            let (d1, h1) = (match History.commit shape "" op doc h0 with Ok r -> r | Error e -> failwith e)
            Expect.equal (show d1) "alpha b(c d(e)) f" "applied"
            Expect.equal (show (fst (Option.get (History.undo shape d1 h1)))) (show doc) "undone"
    ]

let suites : Test list = [ operations; drops; history; inlineEdit ]
