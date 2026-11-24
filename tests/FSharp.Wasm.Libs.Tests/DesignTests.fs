/// The tests of FSharp.Wasm.Design: the ramps, that every pair of roles keeps its contrast in both themes, and that the exports say what the tokens say.  Run under .NET and as WasmGC.
module FSharp.Wasm.Libs.DesignTests

open Wasmecto
open FSharp.Wasm.Ui
open FSharp.Wasm.Data
open FSharp.Wasm.Design

let private has (haystack: string) (needle: string) = Expect.stringContains haystack needle ("contains " + needle)

let private lum (c: Color) : float = match Color.toRgb c with Some rgb -> Color.luminance rgb | None -> -1.0

let private hexOf (c: Color) : string = match c with Hex h -> h | other -> defaultArg (Color.toHex other) "?"

let ramps =
    testList "Design.Palette" [
        testCase "every ramp has the eleven steps, each darker than the one before" <| fun () ->
            for r in Palette.all do
                Expect.equal r.Points.Length Palette.steps.Length (r.Name + " has a point for every step")
                let l = Palette.steps |> List.map (fun s -> lum (Palette.colour r s))
                Expect.isTrue (l |> List.pairwise |> List.forall (fun (a, b) -> a > b)) (r.Name + " gets darker with every step")
        testCase "the violet and the indigo of the site are on the ramps" <| fun () ->
            // the values the site has used since the start (its primary, its accent and its second colour): the ramps run through them
            Expect.equal (Palette.colour Palette.violet 700) (Hex "#6d28d9") "primary"
            Expect.equal (Palette.colour Palette.violet 500) (Hex "#8b5cf6") "accent"
            Expect.equal (Palette.colour Palette.indigo 600) (Hex "#4f46e5") "secondary"
        testCase "a step that sRGB cannot show is the colour of that lightness and hue with less chroma" <| fun () ->
            for r in Palette.all do
                for s in Palette.steps do
                    let l, c = List.item (List.findIndex ((=) s) Palette.steps) r.Points
                    let rgb = (Color.toRgb (Palette.colour r s)).Value
                    // the hex is the colour fitted by chroma: the same as converting the fitted point
                    let l', c', h' = Color.fitOklch (l, c, r.Hue)
                    Expect.equal (Color.ofOklch (l', c', h')) rgb (sprintf "%s %d" r.Name s)
                    Expect.isTrue (c' <= c) (sprintf "%s %d does not gain chroma" r.Name s)
        testCase "a step that is not a step is refused" <| fun () ->
            Expect.throws (fun () -> Palette.colour Palette.violet 650 |> ignore) "650"
    ]

let tokens =
    testList "Design.Tokens" [
        testCase "every check names roles that exist" <| fun () ->
            for c in Tokens.checks do
                Expect.isTrue (Tokens.ratio Tokens.light c).IsSome (c.Fore + " on " + c.Back)
        testCase "every pair of roles keeps its contrast in the light theme and in the dark" <| fun () ->
            let failing (name: string) (t: Tokens) =
                Tokens.checks
                |> List.choose (fun c ->
                    match Tokens.ratio t c with
                    | Some r when r < c.Minimum -> Some(sprintf "%s: %s on %s is %.2f, needs %.1f" name c.Fore c.Back r c.Minimum)
                    | _ -> None)
            Expect.equal (failing "light" Tokens.light @ failing "dark" Tokens.dark) [] "no pair falls below its minimum"
        testCase "white on the violet of the text is not enough for a label, which is why a button is a step darker in the dark theme" <| fun () ->
            let onFive = Color.contrast (Hex "#ffffff") (Palette.colour Palette.violet 500) |> Option.get
            Expect.isTrue (onFive < 4.5) (sprintf "violet 500 under white is %.2f" onFive)
            Expect.isTrue (Tokens.dark.Primary <> Palette.colour Palette.violet 500) "so the button is not 500"
        testCase "hover and pressed are darker than the button, in both themes" <| fun () ->
            for t in [ Tokens.light; Tokens.dark ] do
                Expect.isTrue (lum t.PrimaryHover < lum t.Primary && lum t.PrimaryPressed < lum t.PrimaryHover) "darker, so the label keeps its contrast"
        testCase "the page is the lightest surface in the light theme and the darkest in the dark" <| fun () ->
            Expect.isTrue (lum Tokens.light.Page > lum Tokens.light.Surface && lum Tokens.light.Surface > lum Tokens.light.Raised) "light"
            Expect.isTrue (lum Tokens.dark.Page < lum Tokens.dark.Surface && lum Tokens.dark.Surface < lum Tokens.dark.Raised) "dark"
        testCase "every colour of a theme is hex" <| fun () ->
            for t in [ Tokens.light; Tokens.dark ] do
                for name, get in Tokens.roles do
                    Expect.isTrue (Color.isHex (hexOf (get t))) name
    ]

let export =
    testList "Design.Export" [
        testCase "the stylesheet has the light values, and the dark ones for the system and for the page that asks" <| fun () ->
            let css = Export.css
            has css ":root {"
            has css "color-scheme: light;"
            has css ("--fw-primary: " + hexOf Tokens.light.Primary + ";")
            has css "@media (prefers-color-scheme: dark) {"
            has css ":root:not([data-theme=\"light\"]) {"
            has css "[data-theme=\"dark\"]:root {"      // the renderer writes the attribute before the pseudo-class: the same selector, the same specificity
            has css ("--fw-primary: " + hexOf Tokens.dark.Primary + ";")
            has css "--fw-shadow: none;"
        testCase "every role, every step of every ramp and every scale is a custom property" <| fun () ->
            let css = Export.css
            for name, _ in Tokens.roles do has css ("--fw-" + name + ": ")
            for r in Palette.all do
                for s in Palette.steps do has css ("--fw-" + r.Name + "-" + string s + ": " + hexOf (Palette.colour r s) + ";")
            for n in Scale.space do has css ("--fw-space-" + string n + ": " + string (4 * n) + "px;")
            has css "--fw-size-base: 1rem;"
            has css "--fw-radius: 8px;"
            has css "--fw-primary-rgb: 109, 40, 217;"
        testCase "the dark block has a value for every role" <| fun () ->
            let css = Export.css
            let dark = css.Substring(css.IndexOf "@media (prefers-color-scheme: dark)")
            for name, _ in Tokens.roles do has dark ("--fw-" + name + ": ")
        testCase "the JSON says the same: themes, ramps, and the ratio of every check" <| fun () ->
            match Json.parse (Json.stringify Export.json) with
            | Error e -> Expect.isTrue false ("parses: " + string e)
            | Ok j ->
                let count (key: string) = match Json.field key j with Some(JObj o) -> o.Length | _ -> -1
                Expect.equal (count "light") Tokens.roles.Length "light roles"
                Expect.equal (count "dark") Tokens.roles.Length "dark roles"
                Expect.equal (count "ramps") Palette.all.Length "ramps"
                match Json.field "checks" j with
                | Some(JArr cs) ->
                    Expect.equal cs.Length Tokens.checks.Length "checks"
                    Expect.isTrue (cs |> List.forall (fun c -> match Json.field "light" c, Json.field "dark" c with Some(JNum a), Some(JNum b) -> a >= 3.0 && b >= 3.0 | _ -> false)) "every ratio is there"
                | _ -> Expect.isTrue false "checks"
    ]

let suites : Test list = [ ramps; tokens; export ]
