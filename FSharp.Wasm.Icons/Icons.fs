/// Icons as data: a list of shapes on a 24 x 24 grid, drawn with a stroke of 2, round ends and round joins, in the colour of the text around them
/// (`currentColor`).  One description, drawn the same way as SVG markup, as the elements of a DOM view (`element`) and as paths on a canvas.
namespace FSharp.Wasm.Icons

/// What an icon is drawn with, in the units of the grid.  A `Path` is SVG path data.
type Shape =
    | Path of d: string
    | Circle of cx: float * cy: float * r: float
    | Ellipse of cx: float * cy: float * rx: float * ry: float
    | Rect of x: float * y: float * width: float * height: float * radius: float
    | Line of x1: float * y1: float * x2: float * y2: float
    | Polyline of points: (float * float) list
    | Polygon of points: (float * float) list

type Icon = { Name: string; Shapes: Shape list }

[<AutoOpen>]
module IconBuilder =
    let icon (name: string) (shapes: Shape list) : Icon = { Name = name; Shapes = shapes }

module Icons =
    /// Every icon is drawn on a square of this many units ...
    let grid = 24.0
    /// ... with a stroke of this width.
    let strokeWidth = 2.0

    /// A number as an SVG attribute carries it.
    let number (x: float) : string = string x

    let private points (ps: (float * float) list) : string =
        ps |> List.map (fun (x, y) -> number x + "," + number y) |> String.concat " "

    /// The SVG element of a shape: its tag and its attributes, in the order SVG documents write them.  A DOM view builds its elements from this, a text renderer prints it.
    let element (shape: Shape) : string * (string * string) list =
        match shape with
        | Path d -> "path", [ "d", d ]
        | Circle (cx, cy, r) -> "circle", [ "cx", number cx; "cy", number cy; "r", number r ]
        | Ellipse (cx, cy, rx, ry) -> "ellipse", [ "cx", number cx; "cy", number cy; "rx", number rx; "ry", number ry ]
        | Rect (x, y, w, h, radius) ->
            "rect", [ "x", number x; "y", number y; "width", number w; "height", number h ] @ (if radius > 0.0 then [ "rx", number radius ] else [])
        | Line (x1, y1, x2, y2) -> "line", [ "x1", number x1; "y1", number y1; "x2", number x2; "y2", number y2 ]
        | Polyline ps -> "polyline", [ "points", points ps ]
        | Polygon ps -> "polygon", [ "points", points ps ]

    /// The attributes of the `svg` element that draws an icon at `size` pixels: the grid as its view box, and how it is stroked.  Decorative by default (`aria-hidden`):
    /// the control around an icon carries the name.  No `xmlns`: an element of a DOM view is in the SVG namespace already; `svg`, the markup, adds it.
    let svgAttributes (size: int) : (string * string) list =
        [ "width", string size
          "height", string size
          "viewBox", "0 0 " + number grid + " " + number grid
          "fill", "none"
          "stroke", "currentColor"
          "stroke-width", number strokeWidth
          "stroke-linecap", "round"
          "stroke-linejoin", "round"
          "aria-hidden", "true"
          "focusable", "false" ]

    let private markup (tag: string) (attrs: (string * string) list) (inner: string) : string =
        let a = attrs |> List.map (fun (k, v) -> " " + k + "=\"" + v + "\"") |> String.concat ""
        if inner = "" then "<" + tag + a + "/>" else "<" + tag + a + ">" + inner + "</" + tag + ">"

    /// The icon as SVG markup at `size` pixels.
    let svg (size: int) (icon: Icon) : string =
        icon.Shapes
        |> List.map (fun s -> let tag, attrs = element s in markup tag attrs "")
        |> String.concat ""
        |> markup "svg" (("xmlns", "http://www.w3.org/2000/svg") :: svgAttributes size)
