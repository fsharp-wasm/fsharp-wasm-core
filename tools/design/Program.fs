/// `dotnet run --project tools/design` writes design/tokens.css and design/tokens.json; `-- --check` compares them with what the tokens give and exits 1 when they differ.
module Design.Generate

open System.IO
open FSharp.Wasm.Data
open FSharp.Wasm.Design

let private root = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", ".."))

[<EntryPoint>]
let main argv =
    let check = argv |> Array.contains "--check"
    let outputs = [ "design/tokens.css", Export.css; "design/tokens.json", Json.pretty Export.json + "\n" ]
    let stale =
        outputs
        |> List.filter (fun (rel, text) ->
            let path = Path.Combine(root, rel)
            if check then not (File.Exists path) || File.ReadAllText path <> text
            else
                Directory.CreateDirectory(Path.GetDirectoryName path) |> ignore
                File.WriteAllText(path, text)
                printfn "wrote %s (%d bytes)" rel text.Length
                false)
    for rel, _ in stale do eprintfn "%s is not what the tokens give: run `dotnet run --project tools/design`" rel
    if stale.IsEmpty then 0 else 1
