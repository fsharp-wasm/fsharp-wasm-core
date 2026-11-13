# Changelog

All notable changes to this project are documented here.

## [Unreleased]

### Added
- The F# base library of fsharp-wasm as its own repository, moved from `fable-library-wasmgc/` of the compiler repository:
  `FSharp.Wasm.Core` (Interop, Option, Result, Map, Set, System.Collections.Generic, ListHelpers, MathHelpers) with one compile
  list (`FSharp.Wasm.Core.props`) that replaces the two the compiler repository kept (`Fable.LibraryWasmGc.fsproj`,
  `Collections.props`).
- Apache License 2.0.

### Changed
- Merged with `develop`, which already carried a library of the same purpose: the System.* surface in F# (`Interfaces`, `Exception`,
  `Math`, `Task`, `RuntimeHelper`) and its Expecto tests are part of `FSharp.Wasm.Core` now, as the group `Bcl`. Where both
  libraries defined the same types (`Dictionary`, `Stack`, `StringBuilder`, a placeholder `ResizeArray`) the one the compiler builds
  and tests is kept — `Collections/System.Collections.Generic.fs` — and `StringBuilder` is the compiler's own (`src/Bcl` of
  fsharp-wasm). `Stack` gained `TryPop`, `TryPeek` and a constructor from a sequence, which the old tests asked for.
- The project is `FSharp.Wasm.Core.fsproj` (it was `FSharp.WasmCore.fsproj` with the assembly name `FSharp.Wasm.Core`).
- The nested submodules `vendor/fsharp` and `vendor/fsharp-wasm` are gone: nothing here used them, and `vendor/fsharp` pinned a commit that
  does not exist in the organization, so a recursive checkout of any repository that contains this one failed.

### Removed
- Duplicates and leftovers of the old directory: a second `Interop.fs` (the `Core/` one is the superset), an int-only `Map.fs`
  next to the generic `Collections/Map.fs`, an unused `Seq.fs` (the backend handles `Seq` itself), the empty `WasmGcModule.fs`
  and a shell script that had been committed as `current_interop_location.txt`.
