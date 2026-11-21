# Merging what exists twice

A platform of many repositories collects copies: a helper written twice because two repositories could not see each other, a format two layers both print.
Finding one is the easy part. This is how a copy is merged **without breaking anyone and without losing anything one of the copies did**. It has been used
once already (JSON: the backend and the newsletter each had a parser; `FSharp.Wasm.Data` has the union of both, and both build on it).

## The steps

1. **Inventory.** Name the copy, where each lives, its public surface, its tests and its consumers (`grep` over all repositories, including the pins).
   Decide what is a duplicate and what is deliberate: a second implementation that exists to check the first (the .NET reference of GGBP against the portable
   layer, which an equality test compares byte for byte) stays, with its test.
2. **Characterise before touching.** Pin what each copy does with tests that run on both: the same cases through both implementations (a linked test source,
   or generated inputs through both and a comparison, as the compiler's differential suite does). A difference found now is a decision; found after the merge
   it is a regression.
3. **Merge by union, not by picking a winner.** The target does everything either copy did (the merged JSON has the nesting limit and the strict strings of one,
   the error positions and the pretty printer of the other). The tests of both copies move to the target and must pass there.
4. **Put it in the lowest layer all its consumers can reach**, and never let a dependency point upward: data and operations in `fsharp-wasm-core`, geometry and
   gestures in the graphics layer, wire formats in the backend. A copy that cannot move down because it needs something above it is two things; split it.
5. **Keep the old surface alive for a while.** The old module stays as thin forwarding definitions with the same names and signatures, marked obsolete with a
   pointer to the new one. A wire format never changes in place: a new field or operation gets a version byte, and the old reader keeps reading the old form.
6. **Move the consumers one repository at a time.** Each on `develop`, CI green, the bump of the pin in a commit of its own. The order follows the layers:
   the library first, then its consumers, then the compiler's pins of them (the repositories pin each other; the compiler's dogfood gate compiles the
   consumers with the new library, which is the check that nothing broke on the wasm side).
7. **Remove the forwarding definitions** only when every consumer has moved, a tagged release has carried them, and the changelog said they were going.
8. **Everything stays revertable.** Each step is a commit of its own; nothing is rewritten, so a step that turns out wrong is one `git revert`.
9. **Write it down**: the changelog of the target, and an issue per copy that is not merged yet.

## What has been found

| copy | where | state |
|---|---|---|
| JSON parser and writer | fsharp-wasm-backend (`FSharp.Wasm.Server`) and fsharp-wasm-newsletter | merged into `FSharp.Wasm.Data`, both build on it |
| typed CSS and HTML for email | newsletter | moved to `FSharp.Wasm.Ui` |
| the undo log (snapshots of document and profile, typing folded) | `Newsletter.Studio` | to become `FSharp.Wasm.Edit.History`: its 53 checks are the characterisation (step 2); the widget editor has no undo at all and gets it from the same code |
| colour maths and WCAG contrast | `FSharp.Math` / `Theme` of fsharp-wasm-graphics, and `FSharp.Wasm.Ui.Color` | **suspected**, not measured: inventory first (step 1), then the tests that run on both |
| typed CSS values | `Browser.Css` of fsharp-wasm-runtime and `FSharp.Wasm.Ui.Style` | **suspected**, runtime issue 2 |
| GGBP encoding | `FSharp.Wasm.Protocol` (.NET) and `FSharp.Wasm.Server` (portable) | deliberate: one checks the other (2000 random patches, byte for byte) |
