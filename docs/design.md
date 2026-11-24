# FSharp.Wasm.Design

The look of the platform, once. Until this library the same colours were written down in four places (the site's `CustomStyles.astro`, the runtime's `theme.css`, `FSharp.Graphics.Widgets.Theme`, the
newsletter), and they had drifted: the site is violet on white or on a near-black navy, the canvas widgets are blue on slate. A page, a canvas and an email now read the same tokens.

Dated 2026-11-24. Everything below was run that day (`bash build/test.sh dotnet`, and compiled to WasmGC: 76 tests each).

## What is in it

- **Ramps** (`Palette`): seven hues of eleven steps each, 50 (lightest) to 950 (darkest). A step is a point of OKLCH, so equal steps look equally far apart. The violet runs through the colours the
  site has always used (700 is its primary `#6d28d9`, 500 its accent `#8b5cf6`; the indigo's 600 is its second colour `#4f46e5`; a test says so). *Ink* is the neutral, tinted toward the violet so
  that greys and brand belong together. *Signal* (cyan) is where you are and what is live: the focus ring, a measurement, a selection that moves. *Success*, *warning* and *danger* say something.
  A step sRGB cannot show gives up chroma, never lightness or hue (`Color.fitOklch` in `FSharp.Wasm.Ui`); clipping a channel would turn the hue.
- **Roles** (`Tokens`): what a page or a widget reads, by meaning: `page`, `surface`, `raised`, `line`, `control`, `text`, `heading`, `muted`, `primary` (the fill of a button) with `primary-hover`,
  `primary-pressed` and `on-primary` (its label), `link`, `accent`, `secondary`, `signal`, `focus`, `success`, `warning`, `danger`. `Tokens.light` and `Tokens.dark` give each a value. Every colour is hex:
  a page, a canvas and an email can all use it.
- **Scales** (`Scale`): space on a grid of 4 px, radius, type sizes (about a quarter larger each step), weights, line heights, motion (90, 160 and 280 ms and two easings), the font stacks.
- **Checks** (`Tokens.checks`): the pairs of roles and the contrast each keeps (WCAG 2.2): text 4.5 : 1 on page, surface and raised; the label of a button 4.5 : 1 on the button in all three states; the edge of
  a control and the focus ring 3 : 1 on every background; and so on. 29 pairs, in both themes; the tests fail below the minimum. On 2026-11-24 the tightest margins were a success message on a surface in the light theme (4.92 : 1 against a minimum of 4.5) and the edge of a control on a raised panel in the light theme (3.59 : 1 against 3); `design/tokens.json` has all of them.
- **Exports** (`Export`): `Export.css` is the stylesheet of custom properties `--fw-*` (light by default, dark when the system asks for it or the page says `data-theme="dark"`; `data-theme="light"` keeps a
  page light on a dark system), `Export.json` the same values for whatever draws them. `dotnet run --project tools/design` writes `design/tokens.css` and `design/tokens.json`; with `-- --check` it
  fails when they are not what the tokens give (CI does this).

## A decision the tokens record

White on the violet of the text (500) is 4.2 : 1: not enough for a label. The site's dark theme used exactly that for its buttons. In the tokens the fill of a button is a step darker than the violet of
the text (600 against 400 in the dark theme, 5.7 : 1), and hover and pressed go darker still, so the label never loses contrast. `link` is the violet as text; `primary` the violet as a fill: two roles
where there was one value.

## Using it

- **CSS**: link `design/tokens.css` and write `color: var(--fw-text); background: var(--fw-surface); border-radius: var(--fw-radius-md); padding: var(--fw-space-4);`. A ramp step is
  `var(--fw-violet-500)`.
- **F#**: `Tokens.light.Primary` is `Hex "#6d28d9"`; `Color.parseHex` gives the channels a canvas wants.
- **Email**: the same hex values; `Ui` lowers everything else.

## Not here yet

- The canvas theme (`FSharp.Graphics.Widgets.Theme`) and the runtime's `theme.css` still carry their own values; moving them onto these tokens changes how every canvas application looks (the widgets are blue
  today), so it is a decision of its own.
- A brand typeface for code (the monospace is the system's); the display type beyond Inter.
- Elevation as tokens (the shadow is one value, light theme only).
- Motion is values only; no component uses the scale yet.
