# The data graph

`FSharp.Wasm.Graph` and what is built on it, as of 2026-11-26. The design and the reasons are in [data-graph.md](data-graph.md); this page says what exists, how it is cut into modules, and what is not
built yet. Everything here runs under .NET and as WebAssembly GC and is tested both ways (`tests/FSharp.Wasm.Libs.Tests`, 159 tests each).

## The modules

```
FSharp.Wasm.Graph      Engine    sources and calcs: what depends on what, run once each, in order, cut off when the value is the same
                       Doc       nodes with registers, maps, lists (sequence CRDT) and columns; edits are patches of ops (data, stamped, invertible)
                       Timeline  the tape of steps, undo and redo
FSharp.Wasm.Formula    Sheet     the cells of a spreadsheet as calcs (the text can live in a Doc); Book is a sheet with its own text
FSharp.Wasm.Frame      Cube, Hierarchy, Facts: tables summed along hierarchies (no engine in it)
FSharp.Wasm.Live       Rows      filter, group by: calcs over the columns of a Doc, a table that only grew costs its new rows
                       Cubes     pivot, a plan written over a cube (only changed cells), the lines that are shown
applications           fsharp-wasm-accounting (the plan of the budget), fsharp-wasm-sci (tables, tensors: not written yet)
```

A layer knows the ones above it in this list and none below it: `Graph` depends on the base library only, `Live` on `Graph` and `Frame`, and the applications on all of them. A library for
science needs `Graph` (and `Live` for tables); it does not need `Formula`.

## The engine

```fsharp
let e = Engine()
let price = Source<float>(e, 10.0, Same.float)
let qty   = Source<float>(e, 3.0, Same.float)
let total = Calc<float>(e, Same.float, nan, (fun before after -> printfn "%g -> %g" before after), fun () -> price.Value * qty.Value)
qty.Set 4.0 |> ignore
let flushed = e.Flush()        // { Reached = 1; Ran = 1; Changed = 1 } and "30 -> 40" was printed
```

A calc is run when it is made, and again at a flush when something it read came out different. What it reads is what it read when it ran, so `if a.Value then b.Value else c.Value` depends on `a` and on
one of the others. A flush runs what a change reaches **once each, after the cells they read**, in a walk with a stack of its own (a chain of a hundred thousand calcs is no call stack; there is a test); a calc none of whose inputs
came out different does not run (`Reached - Ran` is what the early cut-off saved). Cells that read each other have the value of a cycle, all of them, and are themselves again when it is broken.

## The document

```fsharp
let doc = Doc(1)                                   // 1 is this actor: ids and stamps are made from it, so two actors never clash
let tl = Timeline(doc)
let mutable plan = 0
tl.Do("create the plan", fun tx -> plan <- tx.Create "Plan") |> ignore
let step = tl.Do("B2 = A1*2", fun tx -> tx.Put(plan, "cells", "16385", Text "=A1*2"))
step.Flushed                                       // what the step reached, ran and left alone
tl.Undo() |> ignore                                // an undo is a new step that restores what the step replaced
tl.Tape                                            // every step with its patch, its inverse and its flush
```

A field is a register (`Set`), a map from text to atoms (`Put`), a list (`Insert`, `Remove`) or a column of numbers (`Append`, `Write`, `Truncate`); an atom is nothing, a flag, a number, text or the id of
another node (that is what makes it a graph). An edit is an `Op`, a step is a `Patch` (the ops and a word), and applying a patch gives the patch that undoes it. `Set` and `Put` are last-writer-wins by stamp, a list is
addressed by the item before the new one (so two inserts that did not see each other end in the same order), and a patch that arrives for a node before its `Create` is kept for it. Reads inside a calc
(`doc.Get`, `Entry`, `Keys`, `Items`, `Count`, `Data`) are dependencies, each on exactly what it read: a calc that reads one key of a map does not run for another.

## Tables

`Live.filter`, `Live.groupBy` and `Cubes.overlay` keep what they made. A column that only grew (an `Append`) costs the new rows; a new parameter, or a number that was there and changed (`Write`, `Truncate`, which
change the column's epoch), is a scan of the whole. The `Rows` a filter makes carry the epoch, so a group over a filter knows whether it may add or has to count again. `Cubes.pivot` builds the cube for a pair
of dimensions once per turn (`Cubes.pivotOver` also when its `revision` reads another number: facts that are written into in place, a line of the ledger corrected, are told to the calc that way); `Cubes.overlay` writes a plan over one measure of it by `Cube.withLeaves`, only for the cells that changed.

## Not built yet

- Json and Markdown as node types, the scene graph as nodes with the style apart, and events as data instead of closures (steps 3 and 4 of data-graph.md).
- The wire form of a patch (JSON text first, binary later) and the transport over the edge host: a patch is data and a `Patch` can be sent as it is, but there is no writer and no reader yet.
- A column has one writer: two actors that append at the same time are not merged. A list finds an item by walking it (linear): fine for a few thousand items, not for a million.
- A range in a formula depends on each cell of it (a region dependency in the engine would make it one), and a node, a key or an item is never forgotten.
- Sorting (`Live.sortBy`) and joins; the pivot is rebuilt when the facts grow, not extended.
