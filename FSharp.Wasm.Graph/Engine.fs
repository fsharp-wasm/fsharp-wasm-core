namespace FSharp.Wasm.Graph

open System.Collections.Generic

/// What the engine keeps of a source or a calc.  A source has a value and reads nothing; a calc reads cells while it runs, and runs again when one of them changed.
[<NoEquality; NoComparison>]
type Cell =
    { Id: int
      IsCalc: bool
      /// what it read the last time it ran
      mutable Reads: int[]
      /// 0 fresh, 1 stale (reached by this flush, not settled yet), 2 settling (waiting for what it reads), 3 running
      mutable State: int
      /// it changed in this flush
      mutable Changed: bool
      /// a source it reads changed, or it was told to run
      mutable Direct: bool
      /// it was found to be part of a cycle while it ran
      mutable InCycle: bool
      /// what it reads, while it runs
      mutable Reading: ResizeArray<int>
      /// compute the next value (into a place of its own: nothing is kept yet)
      mutable Compute: unit -> unit
      /// keep the next value, or the one a cycle gives; true when it is not the one it had
      mutable Commit: bool -> bool }

/// What one flush did: the calcs a change reaches, how many of them ran, and how many came out with another value.  `Reached - Ran` is what the early cut-off saved.
type Flushed = { Reached: int; Ran: int; Changed: int }

/// The incremental engine.  Sources are the inputs; a calc is a function of what it reads (sources and other calcs), found by running it, so the reads may differ from run to run (`IF` reads the branch
/// it takes).  A flush runs the calcs that a change reaches, **once each and after the cells they read**, in a walk with a stack of its own (a chain a hundred thousand long is no call stack), and a
/// calc none of whose inputs came out different does not run: that is the early cut-off.  Cells that read each other are named by the run that finds it, and take the value of a cycle.
type Engine() =
    let cells = Dictionary<int, Cell>()
    /// the calcs that read a cell
    let readers = Dictionary<int, HashSet<int>>()
    let mutable nextId = 0
    /// sources that changed, and calcs that were told to run, since the last flush
    let pending = ResizeArray<int>()
    /// the calcs that run now, the innermost last
    let running = ResizeArray<Cell>()
    /// the runs since the last flush started
    let mutable runs = 0

    let link (reader: int) (read: int) =
        match readers.TryGetValue read with
        | true, set -> set.Add reader |> ignore
        | _ ->
            let set = HashSet<int>()
            set.Add reader |> ignore
            readers.[read] <- set

    let unlink (reader: int) (read: int) =
        match readers.TryGetValue read with
        | true, set ->
            set.Remove reader |> ignore
            if set.Count = 0 then readers.Remove read |> ignore
        | _ -> ()

    /// Replace what a calc reads by what it read this time (each once, in the order they were first read).
    let reads (c: Cell) =
        let seen = HashSet<int>()
        let now = ResizeArray<int>()
        for r in c.Reading do
            if seen.Add r then now.Add r
        for r in c.Reads do
            if not (seen.Contains r) then unlink c.Id r
        for r in now do link c.Id r
        c.Reads <- now.ToArray()
        c.Reading <- null

    /// Run a calc: its reads are the ones this run makes, and a cycle found on the way (a read of a calc that is running) gives it the value of a cycle.
    let rec run (c: Cell) : bool =
        runs <- runs + 1
        c.State <- 3
        c.InCycle <- false
        c.Reading <- ResizeArray<int>()
        running.Add c
        try
            c.Compute()
        finally
            running.RemoveAt(running.Count - 1)
            c.State <- 0
        reads c
        let changed = c.Commit c.InCycle
        c.InCycle <- false
        c.Changed <- changed
        changed

    /// Make a calc fresh: first what it reads that is not fresh, then itself when something it reads changed.
    and settle (c: Cell) : unit =
        if c.State = 1 then
            c.State <- 2
            for r in c.Reads do
                let rc = cells.[r]
                if rc.IsCalc && rc.State = 1 then settle rc
            if c.State = 2 then
                // a read that is still being settled or run has no answer yet: it cannot be cut off (it is a cycle, or a read that the order did not foresee)
                let mutable moved = c.Direct
                for r in c.Reads do
                    let rc = cells.[r]
                    if rc.Changed || rc.State >= 2 then moved <- true
                if moved then run c |> ignore else c.State <- 0

    member _.NewCell(isCalc: bool) : Cell =
        nextId <- nextId + 1
        let c =
            { Id = nextId; IsCalc = isCalc; Reads = [||]; State = 0; Changed = false; Direct = false; InCycle = false; Reading = null
              Compute = (fun () -> ()); Commit = (fun _ -> false) }
        cells.[c.Id] <- c
        c

    /// Forget a calc (or a source): it reads nothing and nothing reads it that is still alive.
    member _.Remove(c: Cell) : unit =
        for r in c.Reads do unlink c.Id r
        c.Reads <- [||]
        readers.Remove c.Id |> ignore
        cells.Remove c.Id |> ignore

    /// A calc or a source is read: it is made fresh when a flush has reached it and not settled it yet (a read that the order did not foresee), a read of a calc that is running is a cycle,
    /// and the reader (the calc that runs) takes it into what it reads.
    member _.Read(c: Cell) : unit =
        if c.State = 1 then settle c
        elif c.State = 2 then run c |> ignore
        elif c.State = 3 then
            let mutable i = running.Count - 1
            while i >= 0 do
                running.[i].InCycle <- true
                i <- if running.[i].Id = c.Id then -1 else i - 1
        if running.Count > 0 then running.[running.Count - 1].Reading.Add c.Id

    /// Run a calc now, outside a flush: when it is made, to give it its first value and its reads.
    member _.First(c: Cell) : unit =
        run c |> ignore
        c.Changed <- false

    /// A source has another value: what reads it runs at the next flush.
    member _.Changed(c: Cell) : unit =
        c.Changed <- true
        pending.Add c.Id

    /// Say that something the calc reads without the engine knowing (a name of the host) changed: it runs at the next flush, and what reads it if its value is another.
    member _.Invalidate(c: Cell) : unit = pending.Add c.Id

    /// Run what the changes since the last flush reach.
    member _.Flush() : Flushed =
        let reached = HashSet<int>()
        let frontier = Stack<int>()
        let started = ResizeArray<int>(pending)
        pending.Clear()
        for id in started do
            match cells.TryGetValue id with
            | true, c ->
                if c.IsCalc then
                    c.Direct <- true
                    if reached.Add id then frontier.Push id
                else
                    match readers.TryGetValue id with
                    | true, set ->
                        for r in set do
                            cells.[r].Direct <- true
                            if reached.Add r then frontier.Push r
                    | _ -> ()
            | _ -> ()
        while frontier.Count > 0 do
            let k = frontier.Pop()
            match readers.TryGetValue k with
            | true, set ->
                for r in set do
                    if reached.Add r then frontier.Push r
            | _ -> ()
        for id in reached do cells.[id].State <- 1
        // the order of the walk: a depth-first post order along what the calcs read (each cell once, a read that is on the way is skipped: it is a stale edge or a cycle, and the run finds out which)
        let colour = Dictionary<int, int>()
        let order = ResizeArray<int>()
        let ids = ResizeArray<int>()
        let nexts = ResizeArray<int>()
        for root in reached do
            if not (colour.ContainsKey root) then
                colour.[root] <- 1
                ids.Add root
                nexts.Add 0
                while ids.Count > 0 do
                    let top = ids.Count - 1
                    let rs = cells.[ids.[top]].Reads
                    if nexts.[top] < rs.Length then
                        let r = rs.[nexts.[top]]
                        nexts.[top] <- nexts.[top] + 1
                        if reached.Contains r && not (colour.ContainsKey r) then
                            colour.[r] <- 1
                            ids.Add r
                            nexts.Add 0
                    else
                        order.Add ids.[top]
                        ids.RemoveAt top
                        nexts.RemoveAt top
        runs <- 0
        for id in order do settle cells.[id]
        let mutable changed = 0
        for id in reached do
            if cells.[id].Changed then changed <- changed + 1
        let result = { Reached = reached.Count; Ran = runs; Changed = changed }
        for id in reached do
            let c = cells.[id]
            c.Changed <- false
            c.Direct <- false
            c.State <- 0
        for id in started do
            match cells.TryGetValue id with
            | true, c -> c.Changed <- false
            | _ -> ()
        result

/// How two values are told apart: a source or a calc whose new value is the same as its old one is no change.
module Same =
    let float (a: float) (b: float) : bool = a = b || (a - a <> 0.0 && b - b <> 0.0)
    let int (a: int) (b: int) : bool = a = b
    let text (a: string) (b: string) : bool = a = b
    /// The same object: for what is rebuilt as a whole (a cube, a list of rows) and is another one when it was rebuilt.
    let reference (a: 'a) (b: 'a) : bool = obj.ReferenceEquals(a, b)
    /// Never the same: it ran, it changed.
    let never (_: 'a) (_: 'a) : bool = false

/// An input: a value that is set from outside.  Reading `Value` inside a calc makes the calc depend on it.
type Source<'a>(engine: Engine, initial: 'a, same: 'a -> 'a -> bool) =
    let cell = engine.NewCell false
    let mutable value = initial
    member _.Cell : Cell = cell
    /// The value; inside a calc it is a dependency.
    member _.Value : 'a =
        engine.Read cell
        value
    /// The value, without being a dependency.
    member _.Peek : 'a = value
    /// Put another value; false when it is the same.  What reads it runs at the next flush.
    member _.Set(v: 'a) : bool =
        if same value v then false
        else
            value <- v
            engine.Changed cell
            true

/// A value that is calculated from what it reads: it runs when it is made, and again when something it read came out with another value, once each flush.  `onCycle` is what it is when
/// it is part of a cycle, `onChange` is told the old and the new value whenever it came out with another one (in the order of the flush).
type Calc<'a>(engine: Engine, same: 'a -> 'a -> bool, onCycle: 'a, onChange: 'a -> 'a -> unit, f: unit -> 'a) =
    let cell = engine.NewCell true
    // until its first run a calc has the value of a cycle: there is no other value to start from (and no null: a calc of text or of a class must not start as one)
    let mutable value = onCycle
    let mutable next = onCycle
    let mutable made = false
    do
        cell.Compute <- fun () -> next <- f ()
        cell.Commit <- fun cycle ->
            let v = if cycle then onCycle else next
            if made && same value v then false
            else
                let before = value
                let first = not made
                value <- v
                made <- true
                if not first then onChange before v
                true
        engine.First cell
    member _.Cell : Cell = cell
    member _.Value : 'a =
        engine.Read cell
        value
    member _.Peek : 'a = value
    /// Run it at the next flush whatever it read (what it depends on lies outside the engine).
    member _.Invalidate() : unit = engine.Invalidate cell
    member _.Dispose() : unit = engine.Remove cell
