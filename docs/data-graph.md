# The data graph: an incremental Elmish layer for data, with a patch protocol

Status: **under construction**, 2026-11-26. The maintainer gave the go the same day ("implement the data graph, use it for the editing of the accounting grid, keep fsharp-wasm-sci in mind, fold, group by, filter and
pivot are needed, extend the architecture, modular if need be") and the recommended answers of the five decisions below were taken. What is built, and how it is cut into modules, is in [graph.md](graph.md); this
document keeps the design and the reasons.

## What he asked for

1. A data layer like the TEA loop of the scene graph, but for data: **incremental**, a graph (or a tree where that is enough).
2. The events of widgets travel as a **Ghost-like protocol** (patches).
3. Data is **always a graph**, except primitives (arrays, lists, numbers, text); every node can hold a graph, a tree or a list again, and the elements of a list can be nodes again.
4. **JSON and a Markdown AST as native datatypes.**
5. Perhaps the **scene graph as an AST**, with the style kept apart from the layout.
6. Something that has not been seen, modern, the best there is. The accounting grid (editing, formulas) is the first customer.

## The idea in one paragraph

One persistent graph of **nodes** with typed fields, in which an edit is a **patch**: a short, versioned, invertible list of operations (`Set`, `Insert`, `Remove`, `Move`, `Append`, `Link`). The loop is the one
people know, `update : Msg -> Model -> Model * Patch`, but the model is the graph and the answer says *what changed*, so nothing downstream starts again from the top: **derived nodes** (a cell's value, a pyramid of
a column, the pixels of a chart, a Markdown preview, a scene) are functions of other nodes that remember what they read, run again only when it changed, and stop the propagation when their value comes out the same
(`Book` does exactly this for formulas). A view is a derived node; a patch is what travels to a view, to a worker, to another machine, to the history, to a test. The scene graph, the editor's undo, the Ghost
protocol of the edge host and the accounting grid's recalculation become one mechanism instead of four.

## Nodes, fields, edges

```
Node       = id (stable, never reused) + type + fields
Field      = a name and one of
  Prim     number | text | bool                      a value, compared by value
  Column   float32[] | int32[] | bytes               a primitive array that is *opaque*: patches append or overwrite ranges, a million samples are one field
  Ref      the id of a node        (owned: the node is its child, a tree edge;   or shared: a graph edge)
  Seq      an ordered list of Prim or Ref            (elements may be nodes: a list of graphs)
  Set/Map  an unordered set, a map from Prim to Prim or Ref
Derived    a node whose fields are computed: reads (tracked) -> value, with early cut-off
```

Primitive arrays and lists stay primitives (a column of ten million floats is not ten million nodes), but a field that holds *nodes* is a `Seq` or a `Map` of `Ref`s: a list whose elements are graphs, a graph
node that is a list of lists. A type is declared once, with typed handles (no reflection: the compiler has none, and handles are what makes a field typed):

```fsharp
let account = node "Account" { text "name"; ref "parent" "Account"; seqOf "children" "Account"; column "monthly" }
let name = Field.text account "name"                  // Field<string>
Graph.set g id name "Revenue"                          // a typed access, and a patch op in the answer
```

## Patches: the Ghost-like protocol

`GGBP` (fsharp-wasm-backend: `Protocol`, `Ghost`) already says how a server and a client exchange small edits. The patch of the data graph is the same idea at the level of nodes:

| op | meaning | inverse |
|---|---|---|
| `Create id type` / `Delete id` | a node comes and goes | each other |
| `Set id field value` | a primitive or a ref | `Set` with the old value |
| `Insert seq after-id item` / `Remove seq item` / `Move seq item after-id` | a list of graphs | each other / `Move` back |
| `Append column values` / `Write column at values` | a primitive array grows or is overwritten | `Truncate` / `Write` the old values |
| `Batch [ ops ]` | one step of the user | the batch reversed |

Every op carries a version `(actor, counter)`; a list is addressed by the **id of the item before** and not by an index, as a sequence CRDT (RGA) does it, so two patches that were made without seeing each other
can be applied in either order and give the same graph (conflict-free for lists, last writer wins per field with the actor as the tie-break). That is the whole price of being able to **sync, collaborate and replay**; it
is paid once, in the store. `FSharp.Wasm.Edit` (operations on a tree with inverses, `drop`, `History`) becomes a client of this: its operations are patches of a tree, its history is the patch log.

The patch is data, so it is serialisable (JSON text today, a binary form later), and the same patch is what the edge host carries, what a Web Worker receives, what the history stores and what a test asserts.

## Events are data: the end of the closure in `Reactive`

A scene today holds `Reactive of handler: InputEvent -> bool` (a closure). A closure cannot travel, be recorded or compared. The scene graph as an AST needs `Reactive of binding: Binding`, where a binding
is data (`OnPress (Msg "toggle" nodeId)`, `OnDrag (Msg "move" nodeId)`), and the loop turns the event into the message and the message into a patch. Consequences, all of them wanted: a session can be **recorded
and replayed**, a widget can live in another process, a UI test is a list of messages, an accessibility action is the same message as a click. This is the one **breaking change** of the proposal (the scene's
handlers); everything else is added next to what exists.

## Derived nodes: incremental, with early cut-off

A derived node is `reads -> value`. The engine records what it read (the way `Book` records the cells a formula reads, and a ranges as boxes), and when a patch touches something it read, the node is run
again in dependency order, **once**; if its value is equal to the old one the nodes that depend on it are left alone (early cut-off). Derived nodes are what the platform already has, without the name:

| today | as a derived node |
|---|---|
| a formula cell | reads its cells; the formula parser of `Formula` builds it; `Book` is the special case |
| the pivot's totals (`Frame.Cube`) | reads the facts; a patch that changes a budget cell rebuilds the prefix sums of that measure only |
| the min-max pyramid of a chart | reads a `Column`; an `Append` patch is O(1) amortised, as `Pyramid.Append` already is: a **stream** is a column that grows |
| a Markdown preview, a JSON tree view | reads the document nodes; a keystroke changes one text node |
| the scene of a widget | reads the model; its value is a patch of the scene AST, which the canvas host already knows how to turn into the damage of a frame (`Damage`) |

## JSON and Markdown as native node types

`FSharp.Wasm.Data.Json` is a value type with a parser and a writer; as nodes, a JSON document is a graph of `Object` (a `Map`), `Array` (a `Seq`) and primitive nodes, with the same writer and parser at the edge,
so a JSON patch (RFC 6902 style: add, remove, replace, move) is a patch of the graph, and a `Json` node can be a field of anything. A **Markdown AST** is a new type: block nodes (heading, paragraph, list, item,
quote, code, table, rule) and inline nodes (emphasis, strong, code, link, image, text) as a tree, text as a `Seq` of characters runs (a sequence CRDT, so two people typing into one paragraph merge),
a CommonMark subset parser and a printer that gives the text back, and the renderings as derived nodes: HTML (`FSharp.Wasm.Ui.Html`), email, plain text, the scene (rich text on the canvas, which GlyphAura shapes).

## The scene graph as an AST, and the style apart

The scene is already a tree of nodes. As graph nodes it gets identity (so a patch can say *this* rectangle), bindings instead of closures, and a clean split that is good for the people who write it:

- **Structure** (nodes): stacks, padding, text, shapes, clips: what is there and how it is arranged. Layout stays with the structure (a stack's direction and gap are structure).
- **Style** (a separate graph): design tokens (`FSharp.Wasm.Design`: colours, roles, ramps), and rules that match nodes by type, class or role and set colours, radii, type sizes, shadows. A node says `class "primary"`; the
  stylesheet says what that is, for light and for dark. Changing a token is **one `Set`** and every node that reads it is derived again: a theme switch is a patch of three fields.

For the people who write it this is the CSS/HTML separation they know, without the cascade's surprises (matching is by explicit class and role, specificity is the order of the rules), and the same stylesheet can style
the DOM renderer and the canvas renderer, which is what the platform needs (task: the canvas theme onto the design tokens).

## What it looks like when it works (the demo)

The accounting grid, editing with formulas, on one screen with: a **patch tape** that shows every edit as a card (what it set, who made it, which derived nodes it reached and how many it left alone because
the value came out the same); a **time slider** that scrubs through the history (the grid, the chart and the notes go back and forward together, because they are derived from one graph); two panes in two Web Workers that edit
the same budget at the same time, **offline and then merged**, with the merge shown as patches crossing; the chart of the plan against the actual drawn from the grid's column as it changes; and a Markdown note
beside it that quotes `={cell}`. Nothing in it is faked: the tape is the real patch log, and the cut-off counts are the engine's own.

## Order of work (each step useful alone, each with tests under .NET and as WebAssembly GC)

| step | what | state |
|---|---|---|
| 0 | `Formula`: parser, evaluator, `Book` (dependency order, early cut-off, changes as output, cycles) | **done** |
| 1 | `Graph`: the engine (tracking, cut-off, cycles), the document (typed fields, patches with versions and inverses), `Timeline`; `Book` rebuilt on it | **done** 2026-11-26 (`Edit` as a client and the JSON form of a patch are not) |
| 2 | Lists as sequences (RGA), `Column` with `Append`; filter, group by, pivot and the plan over a cube as calcs (`FSharp.Wasm.Live`) | **done** 2026-11-26 (the chart pyramid as a calc is not) |
| 3 | Json and Markdown node types, the Markdown parser and printer, the renderings | |
| 4 | Scene as nodes with bindings; the style graph; the canvas host takes scene patches | breaking change of `Reactive` |
| 5 | Transport: patches over the edge host (GGBP), two workers, merge; the accounting demo with tape and slider | |

## What the maintainer has to decide

1. **The name and the place.** The lowest layer (core) is where it belongs: `FSharp.Wasm.Graph` (store, patch, derived), with Json and Markdown in `Data` and the scene adaptation in graphics. A shorter name for the
   whole idea would help the site; none is chosen.
2. **Events as data.** Replacing the closure of `Reactive` by a binding is the one breaking change; it is what makes recording, remote widgets and one protocol possible. Recommended: yes, in step 4, with the old
   closures kept as a deprecated case for a release.
3. **Conflict rules.** Per field last writer wins (actor as the tie-break), lists by RGA; a different rule for a field (a counter that adds, a set that unions) is possible and costs a declaration per field. Recommended:
   start with the two rules.
4. **Style apart from structure** (as above). Recommended: yes; layout stays with the structure.
5. **How far the first version goes.** Steps 0 to 2 are the engine and are what the accounting editing needs; 3 to 5 are what makes it visible to others. Recommended: build 1 and 2 now, edit the accounting grid on them,
   then the demo (step 5) with a first, small Markdown note, and the scene (step 4) after that.

## Prior art we take from, and what is ours

Salsa and Jane Street's Incremental (demand-driven recomputation with early cut-off), Elm and TEA (the loop), Automerge and Yjs (patches, sequence CRDTs, history), RFC 6902 (JSON patch), and the spreadsheet's dependency
graph (`Book`). What is ours is the combination in one small portable core that runs as WebAssembly GC and under .NET: one store whose patches are the unit of update, of history, of sync, of a test and of
a repaint, and whose derived nodes are the formulas, the pyramids, the previews and the scenes of the platform.
