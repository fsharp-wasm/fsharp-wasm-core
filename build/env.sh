# source build/env.sh — the environment of a build in this repository.
#
#   git submodule update --init            # lib/wasmecto: the test framework
#
# FSHARP_WASM_CORE_DIR: where Wasmecto finds FSharp.Wasm.Core (it names the folder that holds FSharp.Wasm.Core/FSharp.Wasm.Core.fsproj):
# here, this repository.  The compiler is not a submodule: build/test.sh takes a release binary (FSHARP_WASM_BIN) or a checkout (FSHARP_WASM_DIR).
_core_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export FSHARP_WASM_CORE_DIR="${FSHARP_WASM_CORE_DIR:-$_core_root}"
unset _core_root
