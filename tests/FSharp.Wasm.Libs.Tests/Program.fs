/// The .NET run of the suites: TAP on standard output, exit code = number of failures.  (The WasmGC run is WasmTests.fs.)
module FSharp.Wasm.Libs.Program

[<EntryPoint>]
let main _ =
    if Wasmecto.Runner.runAll (fun line -> printfn "%s" line) (FSharp.Wasm.Libs.DataTests.suites @ FSharp.Wasm.Libs.UiTests.suites @ FSharp.Wasm.Libs.EditTests.suites @ FSharp.Wasm.Libs.IconsTests.suites @ FSharp.Wasm.Libs.FrameTests.suites @ FSharp.Wasm.Libs.FormulaTests.suites @ FSharp.Wasm.Libs.GraphTests.suites @ FSharp.Wasm.Libs.LiveTests.suites @ FSharp.Wasm.Libs.DesignTests.suites @ FSharp.Wasm.Libs.ActorsTests.suites) = 0 then 0 else 1
