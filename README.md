# fsharp-wasm-core

<!-- platform -->
> **Part of the [fsharp-wasm platform](https://fsharp-wasm.github.io/projects/)**: base libraries. Builds on nothing of ours (the lowest layer). Used by [graphics](https://github.com/fsharp-wasm/fsharp-wasm-graphics), [runtime](https://github.com/fsharp-wasm/fsharp-wasm-runtime), [ide](https://github.com/fsharp-wasm/fsharp-wasm-ide), [newsletter](https://github.com/fsharp-wasm/fsharp-wasm-newsletter), [backend](https://github.com/fsharp-wasm/fsharp-wasm-backend).
> [All projects](https://fsharp-wasm.github.io/projects/) · [Roadmap](https://fsharp-wasm.github.io/docs/roadmap/) · [Tests](https://fsharp-wasm.github.io/tests/) · [Benchmarks](https://fsharp-wasm.github.io/benchmarks/) · [Live demos](https://fsharp-wasm.github.io/demos/)

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

## The standard libraries beside it

The base library is the `System.*` surface. Next to it are the libraries that every program of the platform needs and that were
written twice, or would be: each is a project of its own, **without dependencies**, runs under .NET and as WasmGC, and is tested both ways
(`tests/FSharp.Wasm.Libs.Tests`, written with [Wasmecto](https://github.com/fsharp-wasm/fsharp-wasmecto)).

| project | what it is |
|---|---|
| `FSharp.Wasm.Data` | `Json` (a value type, a writer, a pretty printer, a parser that reports the position of the first problem, bounded nesting, no exceptions) and `Numbers` (integer and float parsing by the JSON grammar, no `TryParse`) — what the backend and the newsletter each had; both build on it now (`FSharp.Wasm.Server`, `Newsletter.Core`) |
| `FSharp.Wasm.Ui` | typed CSS and HTML for the web and for email: `Color` (hex, hsl, oklch, WCAG contrast), `Style` (typed values, selectors, rules, merging, rendering), `Html` (a document tree that is data), `Inline` (an inliner), `Ink` (the grid and components of Foundation for Emails, MIT, as F#) — see [docs/ui.md](docs/ui.md) |
| `FSharp.Wasm.Edit` | the edits of a document as data: operations on a tree that have an inverse (insert, remove, move, replace, many), `drop` (where a drop lands, with the shift of indices a move needs), a history that folds typing into one step, and an edit in place. No geometry, so the DOM and the canvas share it — see [docs/edit.md](docs/edit.md) |
| `FSharp.Wasm.Frame` | tables of facts summed along hierarchies, for pivot tables, ledger reports and cost-centre views: hierarchies of ordered leaves (every group is a contiguous range), a cube of prefix sums over two of them (the total of any group of rows over any group of columns is four reads, whatever the size of the groups; a group's total is the sum, the average, the first or the last of its leaves), pivoting by building the cube again, drill-down to the facts under a cell, formulas over measures (a variance, a ratio), and `Format.amount`. No dependencies. |
| `FSharp.Wasm.Formula` | the formulas of a spreadsheet: a parser with positions (`A1`, `$A$1`, ranges, names, numbers, text, the operators with the precedence of a spreadsheet, calls), an evaluator with spreadsheet errors (`#DIV/0!`, `#NAME?`, `#VALUE!`, `#REF!`, `#NUM!`, `#CYCLE!`) and `IF` that takes one branch only, and a `Book` of cells that keeps what depends on what and recalculates only what a change reaches, in dependency order, naming the cells that changed (what a view repaints and what travels as a patch). Written for the editing of the accounting grid ([docs/formula.md](docs/formula.md)) |
| `FSharp.Wasm.Graph` | an incremental data graph: an engine that keeps what depends on what (sources, and calcs that read them and run again once each and in order, only when something they read came out different, with cycles named and a chain a hundred thousand long a walk, not a stack), and a document of nodes with fields, maps, lists and columns that is changed by patches: data, stamped, invertible, the same graph in any order, with a history (`Timeline`) and a tape of what each step reached. The accounting grid's plan is edited on it ([docs/graph.md](docs/graph.md)) |
| `FSharp.Wasm.Live` | the operations of a table as calcs of the graph engine: filter and group by over the columns of a document (a table that only grew costs its new rows), the pivot of facts for a pair of dimensions, a plan written over one measure of a cube by the cells that changed, and the lines that are shown when groups are opened and folded. What the accounting grid and the libraries for science share ([docs/graph.md](docs/graph.md)) |
| `FSharp.Wasm.Telemetry` | the readings of instruments: a sample (time, value, quality as in OPC), the alarm of a tag with its limits, deadband and delay and the life ISA-18.2 gives it (raised, acknowledged, returned, cleared, escalated, eased; shelving), and the historian's deadband compression of a history ([docs/telemetry.md](docs/telemetry.md)) |
| `FSharp.Wasm.Icons` | icons as typed shapes on a 24 x 24 grid (stroke only, round ends and joins, in the colour of the text), the same everywhere: as SVG markup, as the elements a DOM view builds (`Icons.element`), as paths on a canvas. The set is 51 icons of [Lucide](https://lucide.dev) (ISC, some MIT), generated by `tools/icons/generate.mjs`; no dependencies, [documentation](docs/icons.md). |
| `FSharp.Wasm.Design` | the look of the platform as data, in one place: seven colour ramps (violet, indigo, ink, signal, success, warning, danger) of eleven OKLCH steps, the roles a page and a widget read (page, surface, text, primary, focus, …) for a light and a dark theme, the scales of space, radius, type, weight, leading and motion, the contrast every pair of roles must keep (WCAG 2.2; a test fails below it), and the exports: CSS custom properties `--fw-*` (`design/tokens.css`) and JSON (`design/tokens.json`). Typed CSS and colour come from `Ui`, JSON from `Data`. |
| `FSharp.Wasm.Actors` | processes the way Erlang/OTP has them, as pure state machines: a supervisor (one_for_one, one_for_all, restart intensity, give up and escalate) and a pool of workers (a mailbox per worker, at-least-once delivery with an idempotent end, the jobs of a worker that died go back to the queue) are functions of an event and a state that give a new state and commands; the world carries the commands out. Tested under .NET and as WasmGC with a simulated world that crashes workers at random. See docs/actors.md. |

```bash
git submodule update --init lib/wasmecto
bash build/test.sh dotnet            # under .NET
FSHARP_WASM_DIR=/path/to/fsharp-wasm bash build/test.sh wasm   # compiled to WasmGC and run in Node (also a gate of the compiler's CI)
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
| merging what exists twice | [docs/consolidation.md](docs/consolidation.md) |
| BCL coverage | `docs/bcl-surface.md`, and `docs/bcl-coverage.md` of the compiler |

## Contributing

Welcome. [CONTRIBUTING](https://github.com/fsharp-wasm/.github/blob/develop/CONTRIBUTING.md) is the organisation's guide and [AGENTS.md](AGENTS.md) says how the platform is worked on (the compiler comes first: a fault found here
is fixed in the compiler, not worked around here).

- **Run the tests:** `bash build/test.sh dotnet` (under .NET); `FSHARP_WASM_DIR=… bash build/test.sh wasm` (compiled to WasmGC and run in Node).
- **A good first contribution:** A function that `FSharp.Core` has and this library lacks, written the natural way with a test that runs both ways. Issues labelled [`good first issue`](https://github.com/fsharp-wasm/fsharp-wasm-core/labels/good%20first%20issue) are the places to start.
- **A change** goes to `develop` as a pull request, with a test that fails without it. Open an issue first for anything larger than a fix.

## License

[Apache License 2.0](LICENSE). The sources moved here from fsharp-wasm (MIT) keep that notice in
[`LICENSES/MIT-fsharp-wasm.txt`](LICENSES/MIT-fsharp-wasm.txt); see [NOTICE](NOTICE).
