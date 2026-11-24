namespace FSharp.Wasm.Design

open FSharp.Wasm.Ui

/// What a page or a widget reads, by meaning and not by value.  Every colour is hex, so that a page, a canvas and an email can all use it.
type Tokens =
    { /// the background of the page
      Page: Color
      /// a panel on the page
      Surface: Color
      /// a panel on a panel (a menu, a card in a panel)
      Raised: Color
      /// dividers and the edges of panels: decoration, no promise of contrast
      Line: Color
      /// the edge of a control and the track of a slider: 3 : 1 against every background (WCAG 1.4.11)
      Control: Color
      Text: Color
      Heading: Color
      /// secondary text: 4.5 : 1 all the same
      Muted: Color
      /// the fill of a button
      Primary: Color
      PrimaryHover: Color
      PrimaryPressed: Color
      /// the label on `Primary`
      OnPrimary: Color
      /// the primary colour as text or an icon on the page (a link)
      Link: Color
      Accent: Color
      Secondary: Color
      /// where you are and what is live: a measurement, a selection that moves, a connection
      Signal: Color
      /// the focus ring: 3 : 1 against every background (WCAG 2.4.7)
      Focus: Color
      Success: Color
      Warning: Color
      Danger: Color }

module Tokens =
    /// The roles, once, with the names the exports use (`--fw-<name>`, the key in JSON).  Everything that walks over the tokens walks over this.
    let roles : (string * (Tokens -> Color)) list =
        [ "page", (fun t -> t.Page); "surface", (fun t -> t.Surface); "raised", (fun t -> t.Raised); "line", (fun t -> t.Line); "control", (fun t -> t.Control)
          "text", (fun t -> t.Text); "heading", (fun t -> t.Heading); "muted", (fun t -> t.Muted)
          "primary", (fun t -> t.Primary); "primary-hover", (fun t -> t.PrimaryHover); "primary-pressed", (fun t -> t.PrimaryPressed); "on-primary", (fun t -> t.OnPrimary)
          "link", (fun t -> t.Link); "accent", (fun t -> t.Accent); "secondary", (fun t -> t.Secondary)
          "signal", (fun t -> t.Signal); "focus", (fun t -> t.Focus)
          "success", (fun t -> t.Success); "warning", (fun t -> t.Warning); "danger", (fun t -> t.Danger) ]

    let tryGet (name: string) (t: Tokens) : Color option = roles |> List.tryFind (fun (n, _) -> n = name) |> Option.map (fun (_, get) -> get t)

    let private p = Palette.colour
    let private white = Hex "#ffffff"

    /// The light theme: a white page, ink for text, the violet at its strongest (700) as the fill of a button.
    let light : Tokens =
        { Page = white; Surface = p Palette.ink 50; Raised = p Palette.ink 100; Line = p Palette.ink 200; Control = p Palette.ink 500
          Text = p Palette.ink 900; Heading = p Palette.ink 950; Muted = p Palette.ink 600
          Primary = p Palette.violet 700; PrimaryHover = p Palette.violet 800; PrimaryPressed = p Palette.violet 900; OnPrimary = white
          Link = p Palette.violet 700; Accent = p Palette.violet 500; Secondary = p Palette.indigo 600
          Signal = p Palette.signal 700; Focus = p Palette.signal 700
          Success = p Palette.success 700; Warning = p Palette.warning 800; Danger = p Palette.danger 700 }

    /// The dark theme: the page is the darkest ink.  The fill of a button is a step darker than the violet of the text (600 against 400): white on 500 is 4.2 : 1, which is
    /// not enough for a label, and hover and pressed go darker, not lighter, so that the label never loses contrast.
    let dark : Tokens =
        { Page = p Palette.ink 950; Surface = p Palette.ink 900; Raised = p Palette.ink 800; Line = p Palette.ink 700; Control = p Palette.ink 500
          Text = p Palette.ink 100; Heading = p Palette.ink 50; Muted = p Palette.ink 400
          Primary = p Palette.violet 600; PrimaryHover = p Palette.violet 700; PrimaryPressed = p Palette.violet 800; OnPrimary = white
          Link = p Palette.violet 400; Accent = p Palette.violet 400; Secondary = p Palette.indigo 400
          Signal = p Palette.signal 400; Focus = p Palette.signal 300
          Success = p Palette.success 400; Warning = p Palette.warning 400; Danger = p Palette.danger 400 }

    /// A pair of roles and the contrast it must keep (WCAG 2.2), in both themes.
    type Check = { Fore: string; Back: string; Minimum: float; Why: string }

    let checks : Check list =
        let pairs (fore: string) (backs: string list) (min: float) (why: string) =
            backs |> List.map (fun b -> { Fore = fore; Back = b; Minimum = min; Why = why })
        [ yield! pairs "text" [ "page"; "surface"; "raised" ] 4.5 "text (1.4.3)"
          yield! pairs "heading" [ "page"; "surface"; "raised" ] 4.5 "text (1.4.3)"
          yield! pairs "muted" [ "page"; "surface"; "raised" ] 4.5 "secondary text (1.4.3)"
          yield! pairs "link" [ "page"; "surface"; "raised" ] 4.5 "a link (1.4.3)"
          yield! pairs "on-primary" [ "primary"; "primary-hover"; "primary-pressed" ] 4.5 "a button label (1.4.3)"
          yield! pairs "control" [ "page"; "surface"; "raised" ] 3.0 "the edge of a control (1.4.11)"
          yield! pairs "focus" [ "page"; "surface"; "raised" ] 3.0 "the focus ring (2.4.7, 1.4.11)"
          yield! pairs "signal" [ "page"; "surface" ] 3.0 "a mark that is not text (1.4.11)"
          yield! pairs "success" [ "page"; "surface" ] 4.5 "a message in text (1.4.3)"
          yield! pairs "warning" [ "page"; "surface" ] 4.5 "a message in text (1.4.3)"
          yield! pairs "danger" [ "page"; "surface" ] 4.5 "a message in text (1.4.3)" ]

    /// The contrast of a check in a theme; none when a name is not a role.
    let ratio (t: Tokens) (c: Check) : float option =
        match tryGet c.Fore t, tryGet c.Back t with
        | Some f, Some b -> Color.contrast f b
        | _ -> None

/// The scales: space on a grid of 4 px, radius, type, weight, leading and motion.  Numbers, so that CSS, a canvas and an email take what they need of them.
module Scale =
    /// `--fw-space-<n>` is 4 px times n.
    let space : int list = [ 0; 1; 2; 3; 4; 6; 8; 12; 16; 24 ]
    let radius : (string * int) list = [ "sm", 4; "md", 8; "lg", 14; "pill", 9999 ]
    /// px; CSS gets them in rem (16 px).  A step is about a quarter larger than the one before, rounded to whole pixels.
    let size : (string * int) list =
        [ "xs", 12; "sm", 14; "base", 16; "lg", 18; "xl", 22; "2xl", 28; "3xl", 36; "4xl", 48; "5xl", 64 ]
    let weight : (string * int) list = [ "regular", 400; "medium", 500; "semibold", 600; "bold", 700; "heavy", 800 ]
    let leading : (string * float) list = [ "tight", 1.15; "snug", 1.3; "normal", 1.55 ]
    /// milliseconds: feedback (a press), a change of state, an entrance
    let duration : (string * int) list = [ "fast", 90; "base", 160; "slow", 280 ]
    let easeOut = "cubic-bezier(0.2, 0, 0, 1)"
    let easeIn = "cubic-bezier(0.4, 0, 1, 1)"
    let sans = "\"Inter Variable\", Inter, ui-sans-serif, system-ui, -apple-system, \"Segoe UI\", Roboto, \"Helvetica Neue\", Arial, sans-serif"
    let mono = "ui-monospace, SFMono-Regular, Menlo, Consolas, monospace"
    /// light theme only: the dark theme separates panels by colour
    let shadow = "0 0.375rem 1.5rem 0 rgb(140 152 164 / 13%)"
    /// the height of the bar of the platform, in rem
    let barHeight = 4.0
