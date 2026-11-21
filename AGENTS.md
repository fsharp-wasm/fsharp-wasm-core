# AGENTS.md

How we work is written down once, in the compiler repository: **https://github.com/fsharp-wasm/fsharp-wasm/blob/develop/AGENTS.md**. Read it first; it applies here. What matters most:

- **The compiler comes first**: a fault found while building on the stack is fixed in the compiler (fsharp-wasm), never worked around here; this repository is written the natural way.
- Work on `develop`; `main` only by fast-forward after green CI; never rewrite pushed history.
- Author and committer **Sascha Dewald <sascha.dewald@gmail.com>**, with the `Co-Authored-By` trailer.
- Do not write it twice: look for what exists in a lower layer first.
- Claims are measured and dated; answers to the maintainer are in German; code, comments and commits in English.
- Nothing outward-facing (deploy, release, pull request, issue) without his go.

This repository: the base library and the standard libraries: `FSharp.Wasm.Core`, `Data` (JSON, numbers), `Ui` (typed CSS and HTML, email), `Edit` (operations on a document, history). The lowest layer: everything above uses it, it uses nothing of ours. `docs/consolidation.md` is the strategy for merging what exists twice.
