# Changelog

All notable changes to this project are documented here.

## [Unreleased]

### Added
- The F# base library of fsharp-wasm as its own repository, moved from `fable-library-wasmgc/` of the compiler repository:
  `FSharp.Wasm.Core` (Interop, Option, Result, Map, Set, System.Collections.Generic, ListHelpers, MathHelpers) with one compile
  list (`FSharp.Wasm.Core.props`) that replaces the two the compiler repository kept (`Fable.LibraryWasmGc.fsproj`,
  `Collections.props`).
- Apache License 2.0.

### Removed
- Duplicates and leftovers of the old directory: a second `Interop.fs` (the `Core/` one is the superset), an int-only `Map.fs`
  next to the generic `Collections/Map.fs`, an unused `Seq.fs` (the backend handles `Seq` itself), the empty `WasmGcModule.fs`
  and a shell script that had been committed as `current_interop_location.txt`.
