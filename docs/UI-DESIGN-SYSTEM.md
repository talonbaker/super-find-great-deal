# The UI design system

**Status:** live, 2026-08-15. Implements the PAPER & FIRELIGHT direction
(`docs/agents/2026-08-13-ui-direction-paper-and-firelight.md`) as a substrate.
Supersedes the mechanism described in `docs/superpowers/audits/2026-08-08-ui-token-audit.md`
(kept as the baseline measurement) and retires `tools/BuildTheme.gd`.

---

## The one thing to know

**Nothing in the interface holds its own appearance.** Every colour, gap, corner, weight and
state resolves through `scripts/ui/design/`, and there is no second place to put one. Change a
value there and the whole game follows on the next frame — menus, HUD, dialogs, flow screens,
both temperatures, all five states.

That is not tidiness for its own sake. It is the answer to a specific question Talon asked:

> *"What if I want to explore a button which is not square and solid in color but round and
> opaque? I would like to quickly see this change reflected and I don't want a complete overhaul
> of the UI system to do this for one change I want to see."*

So:

```csharp
UiRecipes.Current = UiRecipes.Current with
{
    Action = UiRecipes.Current.Action with
    {
        Shape = SurfaceShape.Pill,
        Fill  = SurfaceFill.Translucent,
    },
};
UiThemeService.Instance?.Rebuild();
```

Every button in the game is now round and translucent. No screen was edited, because no screen
holds a shape.

To re-cut *everything* — buttons, cards, fields, chips, HUD scraps — in one move:

```csharp
UiRecipes.Current = UiRecipes.Current.WithShape(SurfaceShape.Pill);
```

---

## Seeing a change

```
godot --path . -- --ui-theme-demo
```

Every component, in all five states, at whichever temperature is lit, on one screen.

| Key | Does |
|---|---|
| `SPACE` | cross to the other temperature — the real ~2s crossfade, not a cut. **Renders as no visible change while light mode is retired** (below); the machinery is exercised, the two ends are the same colours |
| `1` `2` `3` `4` | square / soft / rounded / pill, applied to every surface in the game |
| `5` | cycle the fill treatment: solid → translucent → outline |
| `R` | restore the shipped recipes |

If a change is not visible on that board, it is not in the system yet.

---

## The layers

Each one is pure data until the last, which is why the Godot-free xUnit suite can measure the
real shipped appearance rather than a copy of it.

| File | Holds | Edit it when |
|---|---|---|
| `UiTokens.cs` | The closed palette, and the two semantic token sets (Day, Night — **Day is retired, see below**) | Changing what a colour *is* |
| `UiScale.cs` | Five spacing steps, three radii, six type sizes, the motion budget | Changing how big or how fast |
| `UiRecipes.cs` | Shape / fill / edge / padding / press-feel, per component | Changing what a component *looks like* |
| `UiStyle.cs` | recipe + state + temperature → a resolved spec, for all five states | Changing how a state behaves everywhere |
| `UiThemeFactory.cs` | tokens + recipes → a complete Godot `Theme` | Adding a control type |
| `UiThemeService.cs` | Builds it, applies it to the root window, crosses it at dusk | Almost never |
| `UiLayers.cs` | The whole CanvasLayer ladder | Adding a full-screen surface |

### Tokens

Two sets, `UiTokens.Day` and `UiTokens.Night`. **They are the only two.** Read the live one via
`UiThemeService.Tokens`, which includes mid-crossfade blends.

> **Light mode is retired (Talon, 2026-08-15: "theme light mode stinks").** `UiTokens.For`
> returns `Night` for either temperature, so the whole game — front-of-house, HUD, flow cards,
> and the in-round *day* phase — ships the one dark look. `Day` is still defined, still
> contrast-tested, and still the far end of the dusk interpolation; it is simply never selected.
> **Reverting is one line** (`temperature == UiTemperature.Day ? Day : Night`), which is the
> point: retired, not deleted. `UiSystemTests.LightModeIsRetired` fails if that line comes back
> without the doc changing with it.

The counts are deliberate and are the overhaul's acceptance bar — seven ink tokens against a
measured baseline of 27 distinct text colours, twelve fills against 46, **one** scrim against
five. `UiSystemTests` fails if they creep.

### Day and night are not two skins

They are one component set under two lighting conditions. Dusk is
`UiTokens.Lerp(day, night, k)` — an interpolation — fired off the same exactly-once
`RunDriver.PhaseCrossed` event every other day/night consumer uses.

Every metric that affects layout is identical at both ends, so nothing reflows as the light
goes. `NothingReflowsAtDusk` asserts it. **A night idea that is not expressible as a token
change on the day object is wrong by definition** — redesign the day object.

With light mode retired the crossfade still fires, still interpolates, and still costs what it
costs; it just resolves to the same colours at both ends, so it reads as no change on screen.
That is deliberate — the seam stays exercised, so bringing a light temperature back is a token
edit rather than a re-integration.

### Recipes are the dials

A recipe is a component's look described entirely in choices — "round", "translucent", "loose
padding", "presses down" — with no pixel values and no colours in it. `UiStyle` resolves those
choices against the current tokens.

The kit is closed: a screen composes from `UiRecipeSet` and nothing else. A new visual element
enters deliberately — one PR, one review — or not at all.

### States are derived, never authored

Nobody writes a pressed state, so nobody can forget one. `UiStyle.Resolve` switches
exhaustively over all five, which is the mechanism behind the state-coverage target.

**Focus is the loudest state in the system, always.** This game is controller-first: the focus
border is wider than every other state's, and it cannot be the accent (an ember ring on an ember
button measured 1.00:1 — the first cut of this system shipped exactly that, and the tests caught
it). By day focus is the fastener's dark metal; at night it is a torch beam with an ember halo —
and with light mode retired, the torch beam is the only one anyone sees.

---

## Working in it

### Building a screen

Take a theme variation, never a value.

```csharp
new Button { Text = "Start Hosting", ThemeTypeVariation = "PrimaryAction" }
new Label  { Text = "YOUR NAME",     ThemeTypeVariation = "FieldLabel" }
new PanelContainer { ThemeTypeVariation = "HudScrap" }
column.AddThemeConstantOverride("separation", UiScale.SpaceNormal);
```

Variations: `PrimaryAction` `SecondaryAction` `GhostAction` `TertiaryAction` `DangerAction`
`MenuAction` `MenuDanger` · `Hero` `Display` `Title` `Body` `Caption` `SectionLabel`
`FieldLabel` `Crumb` `Tagline` `Micro` `Danger` `OnScrim` `DisplayOnScrim` `Mono` `MonoHero`
`MonoTimer` · `HudScrap` `Chip`.

> The `Accent` colour slots (`teal`, `orange`, `mint`, …) are **legacy Meridian names** kept on
> purpose — sixteen scene references and eleven call sites read them, and renaming is a separate
> deliberate pass under the Issue #62 protocol. Read the token they map to, never the name:
> `teal` is now ember, `orange` is now danger. New code uses the `Token` slots.

### Drawing your own pixels

Controls that take a stylebox need nothing. Controls that cache one or paint themselves must
follow the light:

```csharp
UiThemeService.Bind(node, tokens => node.Color = tokens.SurfaceCard);  // re-apply on change
UiThemeService.BindRedraw(control);                                    // just queue a redraw
HudTheme.BindPanel(panel);                                             // the HUD-scrap case
```

Without this, a widget built at noon is still lit at noon after dark — the bug that quietly
turns a two-temperature system into a one-temperature one.

### Adding a full-screen surface

Add a rung to `UiLayers` in the right band. **Never write a bare integer into a CanvasLayer** —
a number chosen at the call site can only be checked against the neighbours that author
remembered, which is how the project ended up with two collisions and a pause overlay beneath
the HUD it covers.

### The one accent

Ember = fire = the thing that matters, **once per screen**. Where a screen has no primary verb
to carry it, use `UiKit.Keyline()`. Exactly one recipe may claim the accent, and a test enforces
that.

Two documented exceptions, both information surfaces where colour encodes *which* or *whether* —
minimap markers and wall-map pins. They take decoration primaries, separated in value as well as
hue so they survive a colour-blind read (ART-BIBLE §3).

---

## The theme is built at runtime

`UiThemeService` builds the `Theme` from tokens and hangs it on the root window, where every
Control inherits it and a stale committed `.tres` cannot leak in.

**There is no generator step, and that is the point.** `tools/BuildTheme.gd` had drifted from
the `UITheme.tres` it produced on at least six values, so regenerating would have silently
restyled the game — which is why the codebase carried a standing "never regenerate" caution,
which is why screens grew their own constants, which is how there came to be three styling
systems. A generator that cannot be run is not a source of truth. Building from the same tokens
the widgets read deletes the artefact that could drift.

`resources/UITheme.tres` still exists for **editor preview only**, and is regenerated from the
same factory:

```
godot --headless --path . -- --build-ui-theme
```

Running it is always safe. Not running it is also safe.

---

## What the tests hold

| Test file | Holds |
|---|---|
| `UiContrastTests` | Every ink on every surface, every recipe in every state, both temperatures. 0 pairs below 4.5:1; night body ≥ 6:1 |
| `UiSystemTests` | The target table — token counts, scale conformance, five distinct states, focus loudest, one accent, nothing reflows at dusk |
| `UiLayerLadderTests` | No two surfaces share a rung; pause covers what it must; diagnostics on top |
| `UiNoBespokeStylingTests` | Source scan: no hand-typed colour, spacing or font size in the shipped UI. Positive-controlled |

A contrast floor is not a guideline here. **A night screen that fails one is not moody, it is
broken.**

---

## Known gaps

- ~~**Not seen running.**~~ **Closed 2026-08-15.** The system has now been rendered headed at
  both temperatures across every surface — four capture sets under `artifacts/ui-captures/`
  (`01-baseline`, `02-propagation-fixed`, `03-paper-kit*`, `04-standard-dark`), 52–56 shots each,
  produced by `--ui-capture`. Rendering it is what found the headline defect (the theme reached
  nothing) and the paper-vs-flat decision below.
- ~~**The paper is still flat colour.**~~ **Built, then made opt-in.** B2-ART shipped the art
  (`assets/ui/paper/`: three edge treatments × two corner classes, grain stock, tape/clothespin
  chrome, paper-campfire splash pieces) and the hook (`UiThemeFactory.BoxStyled`) that makes
  `SurfaceEdge` select it. Talon then chose flat: **`SurfaceEdge.Flat` is the default on every
  recipe** (2026-08-15, "closer to standard polish"), so the shipped chrome is standard
  flat-colour. The kit is not dead — it is one dial away, per recipe or wholesale:
  `UiRecipes.Current = UiRecipes.Current.WithEdge(SurfaceEdge.Scissor)`. Two Godot API gaps are
  permanent and documented in `UiThemeFactory`: `StyleBoxTexture` has no border colour/width and
  no shadow, so Focus always stays flat and paper elevation is baked into the art.
- **The campfire menu keeps its burned planks.** The direction calls for paper cards on the
  overlay panels and a paper banner for the wordmark. The planks' colours are tokens now, so
  that swap is a one-line change — but discarding a shipped identity element is Talon's call,
  not a defect fix.
- **`/direct` has not run.** Program law 7 gates the affect-bearing final forms — the nightfall
  beat, the loss tone, the tally voice, the splash identity — behind a `/direct` pass and
  Talon's veto. This work restyled those screens; it did not author or change their copy, and
  their affect devices remain unbuilt.
- **The wordmark slot stays blank.** The game is untitled; nothing here bakes a name in.
