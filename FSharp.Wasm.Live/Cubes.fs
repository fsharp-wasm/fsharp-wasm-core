namespace FSharp.Wasm.Live

open System.Collections.Generic
open FSharp.Wasm.Graph
open FSharp.Wasm.Frame

/// The operations of a pivot table as calcs: the cube of the facts for the two dimensions that are on the axes, the cube with a plan written over one measure, and the lines that are shown.
module Cubes =

    let private samePair (a: int * int) (b: int * int) : bool = fst a = fst b && snd a = snd b

    /// The cube of the facts with dimension `rows` on the rows and `cols` on the columns, for facts that are written into in place (a line of the ledger is corrected: the owner of the
    /// facts writes the number into the array and raises `revision`, and that is what tells this calc that what it read has changed).  Turning the table is a new pair, and building it again
    /// is one pass over the facts; nothing else reaches it.
    let pivotOver (engine: Engine) (facts: Facts) (axes: Source<int * int>) (revision: Source<int>) (formulas: (string * Expr)[]) : Calc<Cube> =
        // the first value of a calc is the one it has before it runs: the cube of the first pair
        let (r0, c0) = axes.Peek
        let first = Facts.pivot facts r0 c0 formulas
        Calc<Cube>(engine, Same.reference, first, (fun _ _ -> ()), fun () ->
            revision.Value |> ignore
            let (r, c) = axes.Value
            Facts.pivot facts r c formulas)

    /// The cube of facts that do not change under it: `pivotOver` with a revision that nobody raises.
    let pivot (engine: Engine) (facts: Facts) (axes: Source<int * int>) (formulas: (string * Expr)[]) : Calc<Cube> =
        pivotOver engine facts axes (Source<int>(engine, 0, Same.int)) formulas

    /// What a plan puts into a measure of the cube: for each leaf cell it gives a number for, `(row leaf, column leaf, value)`.  `cells` is read inside the calc, so what it reads is what the
    /// cube depends on.  Only the cells that came out different since the last run are written (`Cube.withLeaves`: the difference is added to the prefix sums below and to the right of the
    /// cell, so every total above it moves by exactly that and the other measures are shared); a cell that is not in the plan any more is the number of the facts again.  A cube that was
    /// built again from the facts (the table was turned) gets the whole plan.
    let overlay (engine: Engine) (raw: Calc<Cube>) (measure: int) (cells: unit -> (int * int * float)[]) : Calc<Cube> =
        let applied = Dictionary<int, float>()
        let mutable base' = raw.Peek
        let mutable last = raw.Peek
        Calc<Cube>(engine, Same.reference, raw.Peek, (fun _ _ -> ()), fun () ->
            let source = raw.Value
            let width = source.ColTree.Leaves
            let plan = cells ()
            if not (obj.ReferenceEquals(source, base')) then
                // another cube of the facts: nothing of the plan is written into it yet
                base' <- source
                last <- source
                applied.Clear()
            let now = Dictionary<int, float>()
            let writes = ResizeArray<int * int * float>()
            for (r, c, v) in plan do
                let k = r * width + c
                now.[k] <- v
                match applied.TryGetValue k with
                | true, old when old = v -> ()
                | _ -> writes.Add((r, c, v))
            // a cell that left the plan is the number of the facts again
            for kv in applied do
                if not (now.ContainsKey kv.Key) then writes.Add((kv.Key / width, kv.Key % width, Cube.leaf source measure (kv.Key / width) (kv.Key % width)))
            applied.Clear()
            for kv in now do applied.[kv.Key] <- kv.Value
            if writes.Count > 0 then last <- Cube.withLeaves last measure (writes.ToArray())
            last)

    /// The nodes of a hierarchy that are shown: what is open, in order.  The `open'` flags are the table's own to change (a click on a caret is a new array).
    let visible (engine: Engine) (tree: Hierarchy) (isOpen: Source<bool[]>) : Calc<int[]> =
        Calc<int[]>(engine, Same.reference, [||], (fun _ _ -> ()), fun () ->
            let open' = isOpen.Value
            if open'.[0] then Array.append [| 0 |] (Hierarchy.visible tree (fun i -> open'.[i])) else [| 0 |])
