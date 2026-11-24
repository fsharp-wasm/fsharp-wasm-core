namespace FSharp.Wasm.Design

open FSharp.Wasm.Ui

/// A ramp is one hue at eleven steps, 50 the lightest to 950 the darkest.  Each step is a point of OKLCH, so that equal steps look equally far apart: the lightness
/// is what the design says, the chroma is what the hue would like at that lightness, and a step sRGB cannot show gives up chroma (`Color.fitOklch`), never lightness or hue.
type Ramp =
    { Name: string
      Hue: float
      /// lightness and wanted chroma, per step of `Palette.steps`
      Points: (float * float) list }

module Palette =
    let steps : int list = [ 50; 100; 200; 300; 400; 500; 600; 700; 800; 900; 950 ]

    /// The colour of a step as hex: what a page, a canvas and an email all can show.
    let colour (r: Ramp) (step: int) : Color =
        match List.tryFindIndex ((=) step) steps with
        | Some i ->
            let l, c = List.item i r.Points
            let l', c', h' = Color.fitOklch (l, c, r.Hue)
            (match Color.toHex (Oklch(l', c', h')) with Some hex -> Hex hex | None -> Hex "#000000")
        | None -> invalidArg "step" (sprintf "%s has no step %d" r.Name step)

    // The violet of the platform (the site's primary, 700, and its accent, 500, sit on this line at hue 293), an indigo beside it for a second voice, an ink for everything
    // neutral (tinted toward the violet, so greys and brand belong together), and the colours that say something: signal (where you are, what is live), success, warning, danger.
    let violet : Ramp =
        { Name = "violet"; Hue = 292.7
          Points = [ 0.975, 0.012; 0.945, 0.030; 0.895, 0.062; 0.82, 0.108; 0.709, 0.159; 0.606, 0.219; 0.541, 0.241; 0.491, 0.241; 0.432, 0.205; 0.38, 0.155; 0.285, 0.115 ] }
    let indigo : Ramp =
        { Name = "indigo"; Hue = 277.0
          Points = [ 0.97, 0.012; 0.94, 0.03; 0.89, 0.06; 0.81, 0.10; 0.70, 0.16; 0.60, 0.21; 0.511, 0.23; 0.45, 0.22; 0.40, 0.19; 0.35, 0.15; 0.27, 0.10 ] }
    let ink : Ramp =
        { Name = "ink"; Hue = 280.0
          Points = [ 0.985, 0.004; 0.965, 0.008; 0.93, 0.014; 0.87, 0.02; 0.72, 0.03; 0.60, 0.035; 0.50, 0.04; 0.40, 0.045; 0.245, 0.048; 0.172, 0.048; 0.122, 0.04 ] }
    let signal : Ramp =
        { Name = "signal"; Hue = 205.0
          Points = [ 0.97, 0.02; 0.94, 0.04; 0.90, 0.07; 0.85, 0.10; 0.79, 0.12; 0.72, 0.13; 0.62, 0.12; 0.52, 0.105; 0.45, 0.09; 0.38, 0.07; 0.28, 0.05 ] }
    let success : Ramp =
        { Name = "success"; Hue = 150.0
          Points = [ 0.97, 0.02; 0.94, 0.04; 0.90, 0.08; 0.85, 0.12; 0.78, 0.16; 0.70, 0.18; 0.60, 0.17; 0.52, 0.145; 0.45, 0.12; 0.39, 0.10; 0.28, 0.07 ] }
    let warning : Ramp =
        { Name = "warning"; Hue = 80.0
          Points = [ 0.98, 0.02; 0.95, 0.05; 0.91, 0.10; 0.87, 0.14; 0.82, 0.16; 0.77, 0.16; 0.68, 0.15; 0.58, 0.13; 0.50, 0.11; 0.43, 0.09; 0.28, 0.06 ] }
    let danger : Ramp =
        { Name = "danger"; Hue = 27.0
          Points = [ 0.97, 0.015; 0.94, 0.03; 0.89, 0.06; 0.82, 0.11; 0.72, 0.17; 0.64, 0.22; 0.58, 0.23; 0.52, 0.22; 0.45, 0.19; 0.40, 0.15; 0.28, 0.10 ] }

    let all : Ramp list = [ violet; indigo; ink; signal; success; warning; danger ]
