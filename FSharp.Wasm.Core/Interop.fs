// The Fable.Core attributes a program compiled by fsharp-wasm needs: [<Import>], [<Export>] and `nativeOnly`.
//
// The front end of the compiler identifies them by their QUALIFIED names ("Fable.Core.ImportAttribute"), so the module has
// to be called `Fable.Core` — the name is fixed by that front end.  Compiling this file replaces the NuGet package
// Fable.Core; every project that declares host imports includes it once.
module Fable.Core

open System

// ─────────────────────────────────────────────────────────────────
// Import attribute
// ─────────────────────────────────────────────────────────────────

/// Declares a Wasm import: [<Import("funcName", "moduleName")>].
/// Applied to a binding whose body is `nativeOnly` — the body is never compiled;
/// our WasmGC backend emits a `(import "moduleName" "funcName" ...)` declaration.
[<AttributeUsage(AttributeTargets.Method ||| AttributeTargets.Property)>]
type ImportAttribute(selector: string, from: string) =
    inherit Attribute()
    /// The name of the imported symbol in the host module (Wasm function name).
    member _.Selector = selector
    /// The host module name (the first string in the Wasm import pair).
    member _.From = from

// ─────────────────────────────────────────────────────────────────
// Export attribute — mark a function as exported from the Wasm module
// ─────────────────────────────────────────────────────────────────

/// Marks a function as exported from the compiled Wasm module.
/// If `name` is omitted, the F# member name (camelCase) is used.
///
/// Example:
///   [<Export("add")>]
///   let add (a: int) (b: int) : int = a + b
///
/// Produces: (export "add" (func $add))
[<AttributeUsage(AttributeTargets.Method ||| AttributeTargets.Property, AllowMultiple = false)>]
type ExportAttribute(?name: string) =
    inherit Attribute()
    member _.Name = name

// ─────────────────────────────────────────────────────────────────
// nativeOnly sentinel
// ─────────────────────────────────────────────────────────────────

/// Sentinel body for [<Import>] and [<Emit>] bindings.
/// The WasmGC backend replaces the entire binding at compile time;
/// this expression is never actually evaluated.
/// At .NET runtime (e.g., in tests) it throws to make misuse obvious.
let inline nativeOnly<'T> : 'T =
    failwith "nativeOnly: this binding is replaced by the WasmGC compiler backend. \
              If you see this at runtime, the function was called outside a compiled Wasm context."
