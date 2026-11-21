/// The tests of FSharp.Wasm.Icons: the shapes of the set, how they are written as SVG, and that every number survives the text.
/// Written with Wasmecto, run under .NET and as WebAssembly GC.
module FSharp.Wasm.Libs.IconsTests

open Wasmecto
open FSharp.Wasm.Data
open FSharp.Wasm.Icons

/// The numbers a shape is placed with.
let private numbers (s: Shape) : float list =
    match s with
    | Path _ -> []
    | Circle (cx, cy, r) -> [ cx; cy; r ]
    | Ellipse (cx, cy, rx, ry) -> [ cx; cy; rx; ry ]
    | Rect (x, y, w, h, r) -> [ x; y; w; h; r ]
    | Line (x1, y1, x2, y2) -> [ x1; y1; x2; y2 ]
    | Polyline ps | Polygon ps -> ps |> List.collect (fun (x, y) -> [ x; y ])

let private isKebab (name: string) : bool =
    name.Length > 0 && name |> Seq.forall (fun c -> (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c = '-')

/// How many elements are closed in a text: "/>" occurs.
let private closed (text: string) : int =
    let mutable n = 0
    for i in 0 .. text.Length - 2 do
        if text.[i] = '/' && text.[i + 1] = '>' then n <- n + 1
    n

let suites : Test list = [
    testList "Icons" [
        testCase "the set: named as Lucide names them, each once, each with something to draw" <| fun () ->
            let all = Lucide.all
            Expect.equal all.Length 51 "the icons of tools/icons/icons.txt"
            Expect.isTrue (all |> List.forall (fun i -> isKebab i.Name)) "kebab-case names"
            Expect.equal (all |> List.map (fun i -> i.Name) |> List.distinct |> List.length) all.Length "each name once"
            Expect.isTrue (all |> List.forall (fun i -> not i.Shapes.IsEmpty)) "every icon has shapes"
            Expect.equal (Lucide.tryFind "grip-vertical" |> Option.map (fun i -> i.Shapes.Length)) (Some 6) "the grip is six dots"
            Expect.isNone (Lucide.tryFind "no-such-icon") "an unknown name is none"
        testCase "an icon as SVG: the grid as the view box, stroked in the colour of the text, decorative" <| fun () ->
            Expect.equal
                (Icons.svg 16 Lucide.plus)
                "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\" focusable=\"false\"><path d=\"M5 12h14\"/><path d=\"M12 5v14\"/></svg>"
                "plus"
            Expect.equal (Icons.element (Circle (9.0, 12.5, 1.0))) ("circle", [ "cx", "9"; "cy", "12.5"; "r", "1" ]) "a circle"
            Expect.equal (Icons.element (Rect (3.0, 4.0, 18.0, 16.0, 0.0))) ("rect", [ "x", "3"; "y", "4"; "width", "18"; "height", "16" ]) "a rect without corners has no rx"
            Expect.equal (Icons.element (Rect (3.0, 4.0, 18.0, 16.0, 2.0)) |> snd |> List.last) ("rx", "2") "a rounded one has"
            Expect.equal (Icons.element (Polyline [ (1.0, 2.0); (3.5, 4.0) ])) ("polyline", [ "points", "1,2 3.5,4" ]) "points"
        testCase "every icon is well formed: one element per shape, each closed, nothing outside the grid" <| fun () ->
            let mutable outside = 0
            for i in Lucide.all do
                let text = Icons.svg 24 i
                Expect.isTrue (text.StartsWith "<svg " && text.EndsWith "</svg>") (i.Name + ": one svg element")
                Expect.equal (closed text) i.Shapes.Length (i.Name + ": one closed element per shape")
                for s in i.Shapes do
                    for n in numbers s do
                        if n < 0.0 || n > Icons.grid then outside <- outside + 1
            Expect.equal outside 0 "no coordinate outside 0 .. 24"
        testCase "every number is written so that it reads back as itself" <| fun () ->
            let mutable wrong = 0
            for i in Lucide.all do
                for s in i.Shapes do
                    for n in numbers s do
                        if Numbers.parseFloat (Icons.number n) <> Some n then wrong <- wrong + 1
            Expect.equal wrong 0 "every coordinate of every icon survives its text"
            Expect.equal (Icons.number 4.0) "4" "a whole number has no point"
            Expect.equal (Icons.number 0.5) "0.5" "a fraction keeps it"
    ]
]
