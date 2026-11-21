# FSharp.Wasm.Icons

Icons as data. One description of an icon — a list of shapes on a 24 x 24 grid — drawn the same way everywhere: as SVG markup, as the elements a DOM view builds, as paths on a canvas.
No dependencies, no drawing of its own beyond the SVG text, so it runs under .NET and as WasmGC.

## What an icon is

```fsharp
type Shape =
    | Path of d: string                                  // SVG path data
    | Circle of cx: float * cy: float * r: float
    | Ellipse of cx: float * cy: float * rx: float * ry: float
    | Rect of x: float * y: float * width: float * height: float * radius: float
    | Line of x1: float * y1: float * x2: float * y2: float
    | Polyline of points: (float * float) list
    | Polygon of points: (float * float) list

type Icon = { Name: string; Shapes: Shape list }
```

Every icon is drawn on a grid of `Icons.grid` (24) units with a stroke of `Icons.strokeWidth` (2), round ends and round joins, no fill, in the colour of the text around it (`currentColor`).
That is the whole style: an icon is the shapes, nothing else, so a renderer that strokes them this way draws the set.

## Drawing one

| where | how |
|---|---|
| SVG markup | `Icons.svg 16 Lucide.plus` — a string, `aria-hidden` (the control around an icon carries the name) |
| a DOM view | `Icons.svgAttributes size` for the `svg` element and `Icons.element shape` (`"circle", [ "cx", "9"; … ]`) for each shape: build the elements with the view's own DSL, so a morph keeps them |
| a canvas | the shapes as paths: `Path` is SVG path data, the others are one-liners (a circle is an arc) — in the graphics layer, which already has a path |

`Icons.element` is the one place that says which attribute a shape has; a view and the text renderer both use it.

## The set

`Lucide` is the set the platform uses, 51 icons of [Lucide](https://lucide.dev) (ISC; the icons that Lucide took over from Feather are MIT; both are compatible with Apache-2.0, their text is in
`LICENSES/ISC-MIT-lucide.txt` and the notice in `NOTICE`): the kinds of block of a newsletter, acting (plus, close, delete, copy, undo, drag), looking (preview, code, device), saying (check, warning, info)
and drawing. `Lucide.all` lists them, `Lucide.tryFind "grip-vertical"` finds one by the name Lucide gives it.

The file `FSharp.Wasm.Icons/Lucide.fs` is **generated**: edit `tools/icons/icons.txt` (a name per line) and run `tools/icons/generate.mjs` against an unpacked `lucide-static` (the header of the script says how).
The generator takes only the shapes Lucide draws with and the attributes that say where they are; anything else stops it, so a new kind of icon is noticed and not dropped. (One thing it ignores on purpose:
the dots of `palette` are filled and have a radius of 0.5, which the stroke of 2 covers completely.)

## Tests

`tests/FSharp.Wasm.Libs.Tests/IconsTests.fs`, under .NET and as WasmGC: the set is named as Lucide names it and each name is there once; the SVG of `plus` is exactly what is written above; each icon is well formed (one closed
element per shape, no coordinate outside the grid); and every number of every shape survives its text (`Numbers.parseFloat (Icons.number n) = Some n`).
