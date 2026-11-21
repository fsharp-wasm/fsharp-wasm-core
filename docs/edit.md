# FSharp.Wasm.Edit

The edits of a document, as data. A document is a **tree of items** (a flat list is a tree whose items have no children); an edit is one **operation** on it,
and every operation has an **inverse**, so that undo and redo are the same function and a history is a list of operations. Gestures (a drag, typing in
place, a paste) end in an operation; nothing in this library knows about pixels, the DOM or a canvas, so the newsletter designer (DOM) and the widget editor
(canvas) use the same code. No dependencies; it runs under .NET and as WasmGC, and is tested both ways (`tests/FSharp.Wasm.Libs.Tests/EditTests.fs`).

## The model

```fsharp
type Path = int list                       // the child indices from the top-level list: [2; 0] is the first child of the third item
type Shape<'item> = { Kids: 'item -> 'item list; WithKids: 'item -> 'item list -> 'item }   // how a document is a tree
type Place = Before of Path | After of Path | Into of Path                                   // a position, relative to an item
type Op<'item> =
    | Insert  of at: Path * item: 'item
    | Remove  of at: Path * item: 'item                    // carries what it removes: that is what makes it invertible
    | Move    of source: Path * target: Path
    | Replace of at: Path * before: 'item * after: 'item   // an edit in place: the item as it was and as it is
    | Many    of Op<'item> list                            // one step of a history: a paste, a drop that also renames
```

`Ops.apply shape op roots` gives the new document or an error that says which part found no place (the document is not changed then).
`Ops.invert op` gives the operation that undoes it; `invert (invert op) = op`, and applying one after the other gives the document back
(a property test runs 3000 random operations on a tree and checks it).

### Move, and where a drop lands

`Move (source, target)` takes the item at `source` out and puts it at `target` **as that path reads in the document without the item**. That makes
the inverse exact (`Move (target, source)`), but it is not what a view knows: a view knows where the pointer is in the document as it is.
`Ops.drop shape source place roots` is the conversion, and the place where everybody gets the off-by-one wrong:

- `Before p` puts the item at `p`, `After p` behind it, `Into p` as the last child of the item at `p`;
- an index behind the source in the same list shifts down by one once the source is out;
- a place inside the item itself (`Into` itself, next to its own grandchild) is **none**, and so is a drop that changes nothing
  (`Before` the next sibling): a view shows no drop mark for a place that gives `None`.

A test tries every source against every target with every kind of place on a tree and checks that each drop that is possible ends where it said.

## History

`History<'item>` holds the operations done and undone, up to a limit. `History.commit shape key op roots h` does an operation and notes it; `undo` and `redo`
apply the inverse and the operation. **Typing is many small edits of one thing, which one undo takes back**: an edit with the same non-empty `key` as the one
before (`"name:3"`: the field of the block that is being typed in), on the same path, is folded into it. Undo and redo end the open edit, so typing again
after an undo is a step of its own. A new edit ends the redo list.

## Editing in place

`Inline.start path item`, `Inline.change draft`, `Inline.commit` (a `Replace` of what was there, or none when nothing changed: typed and typed back).
Escape is dropping the session. The caret and the selection belong to the host: the DOM has its own (an input, `contenteditable`); on a canvas the caret is
placed with GlyphAura's `TextNode.hitTest` (glyph index under a pixel), which is why no hit test is written again here.

## Where the rest lives (no second copy of anything)

| need | where | status |
|---|---|---|
| areas, the drag session (threshold, capture, Escape, auto-scroll), where a pointer is in a list (before, after, inside) | `FSharp.Graphics.Interact` in fsharp-wasm-graphics, depending on `FSharp.Math` only; the scene adapter reads the bounds that `Hit` and `Layout` already compute | planned |
| picking, hover, press capture, bubbling on the canvas | `FSharp.Graphics.Scene` (`Hit.pick`, `Hit.Router`) | exists |
| the caret under a pointer in text | GlyphAura (`TextNode.hitTest`) | exists |
| the keyboard way to drag (Alt+arrows, announced in a live region: WCAG 2.5.7) | `Interact`, on the same operations | planned |
| the operations on the wire: sync, a second window, a collaborator | **Ghost** (GGBP) in fsharp-wasm-backend, carried by fsharp-wasm-edge | below |

## The operations as GGBP patches (Ghost, with Edge)

GGBP is the patch format of the shadow graph (`UpdateLeaf`, `ReplaceBranch`, `ListMutation` with `Insert`/`Remove`/`Update`/`Clear`, `DeleteNode`) that a server
sends to a client; it has no move and no inverse. The mapping, to be built in the backend with the byte-equality test of its other patches:

| `Op` | GGBP |
|---|---|
| `Replace` | `UpdateLeaf` per field that differs (a diff of the two items), `ReplaceBranch` for a changed subtree |
| `Insert`, `Remove` | `ListMutation` `Insert`, `Remove` |
| `Move` | **a new operation** (`MoveNode`), with a version byte: the protocol has none, and a remove plus an insert would lose the identity of the item |
| `Many` | one patch (a transaction) |

The transport (WebSocket, SSE, fetch) and the client's shadow are fsharp-wasm-edge's. A history, undo included, stays on the client; what goes over the wire
is the operation, so that a second client can apply it. Nothing of this is built yet.

## What compiling it found

The library is also a test of the compiler: it is compiled as a referenced project of the test module (the Dogfood suite `core-libs` of fsharp-wasm). Two faults
showed up, and both were fixed in the compiler rather than written around here (fsharp-wasm `docs/STATUS.md`, 2026-11-21): a lambda in an argument whose type
variable only the callee fixes (the `put` steps of `Ops`) was compiled with an `int` for it, and a private `show` of a test file was taken for the generic `show`
of Wasmecto. The code reads the way one writes it, and that gate keeps both from coming back.
