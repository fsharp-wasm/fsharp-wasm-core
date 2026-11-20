/// The WasmGC run of the suites.  A compiled module exports the functions of its last file, so this file exports `wasmecto_run`;
/// the module's initialiser has registered the suites by the time the host calls it.
module FSharp.Wasm.Libs.WasmTestsMain

open Wasmecto.Runner

do register (FSharp.Wasm.Libs.DataTests.suites @ FSharp.Wasm.Libs.UiTests.suites)

let wasmecto_run () : int = runRegistered ()
