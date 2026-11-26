namespace FSharp.Wasm.Formula

open System.Collections.Generic

/// A cell whose value changed.
type Change = { Cell: CellRef; Before: Value; After: Value }

/// What a cell holds: what was typed, what it parsed to, what it is worth, and what it reads.
type private Slot =
    { mutable Source: string
      mutable Formula: Expr option
      mutable Value: Value
      /// the keys of the single cells the formula reads
      mutable Reads: int[]
      /// the ranges it reads, as (first column, first row, last column, last row)
      mutable Ranges: (int * int * int * int)[] }

/// A step of the walk through the cells that a change reaches: a cell, what it reads, and how many of those are done.
type private Frame = { Node: int; Reads: int[]; mutable Next: int }

/// A sheet: cells that hold a number, text or a formula, what depends on what, and the recalculation of what a change reaches.
///
/// `Set` takes the text a person types: a number, `=` and a formula, or text.  It parses it, finds what the cell reads, and recalculates the cell and then, in dependency order, only the
/// cells that depend on it, directly or through others.  The answer is the cells whose value changed (with what they were), which is what a view repaints and what travels to another
/// place as the patch of this edit.  Cells that depend on each other are #CYCLE!, and are so named by the cells of the cycle and not by an endless loop.
///
/// A name or a function of the host (`Actual`, the plan's measures) is outside the book: `Recalculate` and `Invalidate` say that the data behind them changed.
type Book(resolve: CellRef -> string -> Value option, custom: string -> (Value[] -> Value) option) =
    let slots = Dictionary<int, Slot>()
    /// for a cell, the formulas that read it as a single cell
    let readers = Dictionary<int, HashSet<int>>()
    /// the formulas that read a range: few, so they are checked one by one
    let rangeReaders = HashSet<int>()

    let valueAt (r: CellRef) : Value =
        match slots.TryGetValue(CellRef.key r) with
        | true, s -> s.Value
        | _ -> Blank

    let env : Env = { Cell = valueAt; Name = resolve; Custom = custom }

    let sameValue (a: Value) (b: Value) : bool =
        match a, b with
        | Number x, Number y -> x = y || (x - x <> 0.0 && y - y <> 0.0)
        | _ -> a = b

    let unlink (k: int) (s: Slot) =
        for r in s.Reads do
            match readers.TryGetValue r with
            | true, set ->
                set.Remove k |> ignore
                if set.Count = 0 then readers.Remove r |> ignore
            | _ -> ()
        rangeReaders.Remove k |> ignore

    let link (k: int) (s: Slot) =
        for r in s.Reads do
            match readers.TryGetValue r with
            | true, set -> set.Add k |> ignore
            | _ ->
                let set = HashSet<int>()
                set.Add k |> ignore
                readers.[r] <- set
        if s.Ranges.Length > 0 then rangeReaders.Add k |> ignore

    /// The formulas that read the cell `k`: directly, or by a range that holds it.
    let dependentsOf (k: int) : int list =
        let c = k % CellRef.Limit
        let r = k / CellRef.Limit
        let direct = match readers.TryGetValue k with | true, set -> List.ofSeq set | _ -> []
        let byRange =
            [ for f in rangeReaders do
                match slots.TryGetValue f with
                | true, s -> if s.Ranges |> Array.exists (fun (c0, r0, c1, r1) -> c >= c0 && c <= c1 && r >= r0 && r <= r1) then yield f
                | _ -> () ]
        direct @ byRange

    /// The cells to recalculate when `starts` changed: them and everything that reads them, in an order in which a cell comes after the cells it reads; and the ones that are in a cycle.
    let order (starts: int list) : int list * HashSet<int> =
        // what is reached
        let reached = HashSet<int>()
        let stack = Stack<int>()
        for s in starts do
            if reached.Add s then stack.Push s
        while stack.Count > 0 do
            let k = stack.Pop()
            for d in dependentsOf k do
                if reached.Add d then stack.Push d
        // a depth-first post order of the reached cells along "reads", with a stack of its own (a column of running totals is a chain a hundred thousand long): colour 1 is on the way, 2 is done
        let colour = Dictionary<int, int>()
        let sorted = ResizeArray<int>()
        let cyclic = HashSet<int>()
        let readsOf (k: int) : int[] =
            match slots.TryGetValue k with
            | true, s ->
                let single = s.Reads |> Array.filter (fun r -> reached.Contains r)
                let viaRange =
                    [| for (c0, r0, c1, r1) in s.Ranges do
                        for q in reached do
                            let qc = q % CellRef.Limit
                            let qr = q / CellRef.Limit
                            if qc >= c0 && qc <= c1 && qr >= r0 && qr <= r1 then yield q |]
                Array.append single viaRange
            | _ -> [||]
        for root in reached do
            if not (colour.ContainsKey root) then
                let frames = Stack<Frame>()
                colour.[root] <- 1
                frames.Push { Node = root; Reads = readsOf root; Next = 0 }
                while frames.Count > 0 do
                    let f = frames.Peek()
                    if f.Next < f.Reads.Length then
                        let r = f.Reads.[f.Next]
                        f.Next <- f.Next + 1
                        match colour.TryGetValue r with
                        | true, 1 ->
                            // a cell that reads one that is still on the way: everything on the way from that one is a cycle
                            let mutable stop = false
                            for fr in frames do
                                if not stop then
                                    cyclic.Add fr.Node |> ignore
                                    if fr.Node = r then stop <- true
                        | true, _ -> ()
                        | _ ->
                            colour.[r] <- 1
                            frames.Push { Node = r; Reads = readsOf r; Next = 0 }
                    else
                        colour.[f.Node] <- 2
                        sorted.Add f.Node
                        frames.Pop() |> ignore
        (List.ofSeq sorted, cyclic)

    let evaluate (s: Slot) (k: int) : Value =
        match s.Formula with
        | Some f -> Eval.eval env (CellRef.ofKey k) f
        | None -> s.Value

    /// Recalculate `starts` and what reads them; the changes are what differs from before.
    let recalculate (starts: int list) (before: Dictionary<int, Value>) : Change[] =
        let (sorted, cyclic) = order starts
        let changes = ResizeArray<Change>()
        for k in sorted do
            match slots.TryGetValue k with
            | true, s ->
                let old = match before.TryGetValue k with | true, v -> v | _ -> s.Value
                let v = if cyclic.Contains k then Err Cycle else evaluate s k
                s.Value <- v
                if not (sameValue old v) then changes.Add { Cell = CellRef.ofKey k; Before = old; After = v }
            | _ -> ()
        changes.ToArray()

    new() = Book((fun _ _ -> None), (fun _ -> None))

    /// What the cell shows.
    member _.Get(col: int, row: int) : Value = valueAt (CellRef.create col row)

    /// What was typed into the cell ("" when nothing was).
    member _.Source(col: int, row: int) : string =
        match slots.TryGetValue(CellRef.key (CellRef.create col row)) with
        | true, s -> s.Source
        | _ -> ""

    /// The cell's formula, when it has one.
    member _.Formula(col: int, row: int) : Expr option =
        match slots.TryGetValue(CellRef.key (CellRef.create col row)) with
        | true, s -> s.Formula
        | _ -> None

    /// How many cells hold something.
    member _.Count : int = slots.Count

    /// The keys of the cells that hold something, as cells.
    member _.Cells : CellRef[] = [| for k in slots.Keys -> CellRef.ofKey k |]

    /// Put what a person typed into a cell: the cell and what depends on it are recalculated, and the cells whose value changed are returned.  An empty text clears the cell.  A text that
    /// starts with `=` is a formula (one that does not parse is the cell's #ERROR!, and the text is kept to be corrected); one that is a number is a number; anything else is text.
    member _.Set(col: int, row: int, text: string) : Change[] =
        let at = CellRef.create col row
        if not (CellRef.valid at) then [||]
        else
            let k = CellRef.key at
            let before = Dictionary<int, Value>()
            // what is read after the change may differ from what was before: the cells that read this one are found before and after
            let existing = match slots.TryGetValue k with | true, s -> Some s | _ -> None
            let old = match existing with Some s -> s.Value | None -> Blank
            before.[k] <- old
            (match existing with Some s -> unlink k s | None -> ())
            let typed = text.Trim()
            if typed = "" then
                slots.Remove k |> ignore
                recalculate (dependentsOf k) before |> fun changes ->
                    if sameValue old Blank then changes else Array.append [| { Cell = at; Before = old; After = Blank } |] changes
            else
                let slot =
                    if typed.StartsWith "=" then
                        match Parser.parse typed with
                        | Ok e ->
                            let cells = ResizeArray<CellRef>()
                            let ranges = ResizeArray<CellRef * CellRef>()
                            Parser.references e cells ranges
                            { Source = text
                              Formula = Some e
                              Value = Blank
                              Reads = [| for c in cells do if CellRef.valid c then yield CellRef.key c |]
                              Ranges = [| for (a, b) in ranges -> (min a.Col b.Col, min a.Row b.Row, max a.Col b.Col, max a.Row b.Row) |] }
                        | Error _ -> { Source = text; Formula = None; Value = Err Syntax; Reads = [||]; Ranges = [||] }
                    else
                        let v = match FSharp.Wasm.Data.Numbers.parseFloat typed with Some n -> Number n | None -> Text typed
                        { Source = text; Formula = None; Value = v; Reads = [||]; Ranges = [||] }
                slots.[k] <- slot
                link k slot
                recalculate [ k ] before

    /// Say that what the host's names or functions stand for changed for these cells (or for every cell with `Recalculate`): they and what reads them are calculated again.
    member _.Invalidate(cells: CellRef[]) : Change[] =
        let before = Dictionary<int, Value>()
        let starts = [ for c in cells do if slots.ContainsKey(CellRef.key c) then yield CellRef.key c ]
        for k in starts do before.[k] <- slots.[k].Value
        recalculate starts before

    member this.Recalculate() : Change[] = this.Invalidate(this.Cells)
