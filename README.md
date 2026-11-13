# fsharp-wasm-core

The F# base library of programs compiled by [fsharp-wasm](https://github.com/fsharp-wasm/fsharp-wasm) to WebAssembly GC.

It is **source-only**: a program compiles these files together with its own — they are F#, not a binary dependency — and the
compiler turns them into WasmGC like any other code. Under .NET the same project builds with the BCL's own types.

```text
FSharp.Wasm.Core/
  Interop.fs                          [<Import>], [<Export>], nativeOnly — module Fable.Core (the name the front end looks for)
  Interfaces.fs  Exception.fs         the System.* surface in F#: IList / IDictionary, the exception hierarchy
  Math.fs  Task.fs  RuntimeHelper.fs  System.Math, System.Threading.Tasks, runtime helpers
  Option.fs  Result.fs                combinators
  Collections/
    Map.fs  Set.fs                    persistent collections (comparer-injected, as the front end calls them)
    System.Collections.Generic.fs     Dictionary, HashSet, Queue, Stack (the BCL types, written in F#)
    ListHelpers.fs                    pairwise, distinct, countBy, groupBy …
  Math/MathHelpers.fs                 powi/powf/clamp and the functions without a wasm opcode: exp, atan, atan2, asin, acos, sinh, cosh, tanh
  FSharp.Wasm.Core.props              the compile list, in order, for a program to import
tests/FSharp.WasmCore.Tests/          Expecto tests, run under .NET (dotnet run --project tests/FSharp.WasmCore.Tests)
```

## Use it

```xml
<PropertyGroup>
  <FSharpWasmCoreDir>$(MSBuildThisFileDirectory)../fsharp-wasm-core/FSharp.Wasm.Core</FSharpWasmCoreDir>
</PropertyGroup>
<!-- fsharp-wasm sets FABLE_COMPILER_WASMGC: under .NET the BCL types are used instead -->
<Import Project="$(FSharpWasmCoreDir)/FSharp.Wasm.Core.props" Condition="'$(FABLE_COMPILER_WASMGC)' == 'True'" />
```

Import it from the root of the dependency graph (the files must precede everything that uses them). `FSharpWasmCoreOnly`
selects groups (`Interop;Bcl;Option;Result;Collections;Lists;Math`; default `Collections`). The compiler finds the library by
itself — `FSHARP_WASM_CORE_DIR`, else the `core/` checkout inside its repository, else a sibling `fsharp-wasm-core/`.

## Where things are

| | |
|---|---|
| the compiler | [fsharp-wasm](https://github.com/fsharp-wasm/fsharp-wasm) — `src/Bcl` holds the helpers that are built *by* the compiler (strings, StringBuilder, formatting), this repository the ones written *in F#* |
| BCL coverage | `docs/bcl-surface.md`, and `docs/bcl-coverage.md` of the compiler |

## License

[Apache License 2.0](LICENSE). The sources moved here from fsharp-wasm (MIT) keep that notice in
[`LICENSES/MIT-fsharp-wasm.txt`](LICENSES/MIT-fsharp-wasm.txt); see [NOTICE](NOTICE).
