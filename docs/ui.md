# FSharp.Wasm.Ui: typed CSS and HTML for the web and for email

`FSharp.Wasm.Ui` has no dependencies, runs under .NET and as WebAssembly GC, and is tested both ways (the Wasmecto suites of
`tests/FSharp.Wasm.Libs.Tests`). The first program built on it is [fsharp-wasm-newsletter](https://github.com/fsharp-wasm/fsharp-wasm-newsletter).

| module | what it is |
|---|---|
| `Color` | `Hex`, `Rgb`, `Hsl`, `Oklch`, `Var`; WCAG luminance and contrast; `adjustLightness` / `scaleLightness` (Sass' `color.adjust` and `color.scale`); `css Email` lowers every colour to `#rrggbb` |
| `Style` | typed values (`Px`, `Pct`, `Calc`, …), typed constructors (`Style.padding`, `Style.textAlign TextAlign.Center`, `Style.border`, …) and `Style.raw` as the escape hatch; selectors (parsed, printed, specificity); rules (`Styled`, `Media`, `Layer`, `Container`); `merge` (later wins, `!important` stays); `render Web \| Email` |
| `Html` | `Node` = element (tag, attributes, **typed style**, children) \| text \| raw \| fragment; a string renderer that escapes |
| `Inline` | applies a stylesheet to a tree: matching by tag, class, id, attribute, `:not`, descendant and child combinators; specificity then source order; the element's own style last; media queries and `:hover` stay behind for a `<style>` block |
| `Ink` | **Foundation for Emails as F#**: settings (a record), the stylesheet (a function of them), the markup (container, row, column, grid, button, callout, spacer), and `page`, which inlines and prints a whole email |

## Are we better than Sass templates?

For this job, yes, and measurably: Foundation's pipeline is Sass sources → a Sass compiler → a CSS inliner (a Node tool) → the HTML.
Here it is one F# program with no build step that runs in the browser (the live preview of the designer) and in a Worker.

* **Values have types.** `Style.padding (Px 16.0)` cannot be misspelled, `Style.textAlign TextAlign.Centre` does not compile.
* **Tokens are data, and they are checked.** The stylesheet is a function of a settings record; the contrast of every button and text colour is
  computed from it (the designer's checks do), instead of being discovered in a mail client.
* **One definition, two targets.** A colour written in `Oklch` is `oklch(…)` for the web and `#rrggbb` for an email. `Var` is a custom
  property on the web and its fallback in an email.
* **The inliner is part of the library**, so there is no second tool, and an unused rule costs nothing (nothing is inlined that nothing matches).
* **It is a library, not a template language:** loops, conditions and composition are F#.

### Fidelity to Foundation, measured

The same markup was inlined twice: by `Inline.apply` with `Ink.stylesheet`, and by [juice](https://github.com/Automattic/juice) with
Foundation's own compiled `foundation-emails.css`. Of 92 elements of a page with a grid, columns, buttons, a callout and a spacer,
**90 get identical effective inline styles**. The other two are an artefact of the comparison script (a shorthand against its longhands)
and `margin: default`, an invalid value in Foundation's compiled CSS that is not ported. Colours match to the digit
(`color.scale($primary, $lightness: 85%)` is `#def0fc`, `color.adjust(…, -20%)` is `#0f5f94`, as in Foundation's CSS).
The comparison is `tools/fidelity` in fsharp-wasm-newsletter (run it with a checkout of Foundation for Emails); the unit tests pin the numbers (`th.large-6` is 274 px wide at 580 px, the breakpoint is 596 px).

### What is not there yet (honestly)

* Selectors are strings that are parsed when the stylesheet is built (a mistake fails at once, and the tests build every stylesheet).
  Typed selector builders are the next step; the parser stays for literals.
* Foundation's menu, thumbnail, block grid, visibility helpers, `hr` and the `valign` rules are not ported; the button is Foundation's table
  button (no VML, so Outlook shows square corners).
* No scoped or atomic class extraction and no hashed class names for the web side; no emission tests for `@container` and `@layer` yet (the types exist).
* The email is not tested in real mail clients.

## Where this is going

```text
Color · Style   typed values, selectors, rules                       ← done
Html · Inline   a document tree that is data; the inliner            ← done
Ink             Foundation for Emails                                ← done (grid, button, callout, spacer, alignment, typography)
Web kit         scoped and atomic CSS, container queries, tokens → custom properties, a component set for pages      (next, beside Ui)
Tokens          one design-token model — colour (oklch), spacing, type scale — that the stylesheets here and FSharp.Graphics.Widgets.Theme both read:
                a theme is data that lowers to a canvas palette or to CSS
Roles           the semantic vocabulary the canvas widgets already speak (Scene.Role: button, heading, …) ↔ HTML elements and ARIA.
                The accessibility overlay of the widget toolkit is a lowering of `SemanticTree` to `Html` nodes: the one place a scene meets the DOM
Editor kit      undo/redo with typed steps, selection, block operations, field schemas, JSON — shared by the newsletter designer and the widget editor
SaaS            a Worker renders templates (Ui and the newsletter core are pure, so the same code runs there); accounts; sending
```

### Why there is no "scene graph → HTML" bridge

The scene graph (`FSharp.Graphics.Scene`, in fsharp-wasm-graphics, with the widget library `FSharp.Graphics.Widgets` beside it) is a *drawing*
model: shapes, positioned glyphs, transforms, clips, blur, masks, blend modes. Most of that has no HTML form, and an email has far less. What a
scene does carry that a document needs is *meaning* — `Semantic` nodes with a role, a name and a state. So the two worlds are **not** joined by
lowering scenes to markup; they share what is really common: tokens, roles, the editor kit and JSON. The canvas widgets stay on the scene graph;
the web and email components are built on this library; both speak the same vocabulary, and neither is a rendering of the other.
