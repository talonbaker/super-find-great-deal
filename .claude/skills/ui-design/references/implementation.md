# Implementation

Translating the design decisions into a specific platform, and the traps in each. The principles
are in the other references; this is the mechanical layer.

---

## Universal rules

Whatever the platform:

1. **Define tokens in one place**, reference them everywhere. If a colour appears as a literal in
   a component, it's a bug.
2. **Let the layout system own position and size.** Manual coordinates survive exactly until the
   first resize, font change, or translated string.
3. **Separate the three spacing concepts** and don't conflate them:
   - *Padding* — inside a component's own boundary
   - *Gap/separation* — between siblings, owned by the container
   - *Margin/inset* — around a group or against a screen edge
4. **State styling belongs in the shared layer; transitions belong on the element.** Most
   platforms swap state styles instantly — smooth transitions are a separate mechanism.
5. **Semantic elements before custom ones.** You inherit focus, keyboard, and accessibility for
   free, and rebuilding them by hand is where bugs live.

---

## Web / CSS

### Tokens as custom properties

```css
:root {
  /* primitives */
  --gray-950: #0a0a0b;  --gray-900: #121214;  --gray-100: #e8e8ec;
  --blue-400: #748ffc;
  --space-1: 4px; --space-2: 8px; --space-4: 16px; --space-6: 24px;
  --radius-md: 8px;
  --duration-fast: 150ms;  --ease-out: cubic-bezier(0.2, 0, 0, 1);

  /* semantics — the layer components use */
  --bg-canvas: var(--gray-100);
  --text-primary: var(--gray-950);
  --accent: #4c6ef5;
  --border-subtle: rgb(0 0 0 / 0.08);
}

/* dark: redefine ONLY the semantics */
@media (prefers-color-scheme: dark) {
  :root:not([data-theme="light"]) {
    --bg-canvas: var(--gray-950);
    --text-primary: var(--gray-100);
    --accent: var(--blue-400);
    --border-subtle: rgb(255 255 255 / 0.08);
  }
}
:root[data-theme="dark"] { /* same overrides, so the manual toggle wins both ways */ }
```

**The trap:** a token whose *only* definition lives inside a media query or theme block is
undefined in the other theme. Always define the complete set on the base selector.

### Traps

- **`outline: none` without a replacement** is the most common accessibility bug on the web. Use
  `:focus-visible` to style focus for keyboard users without showing rings on mouse click.
- **Animate `transform` and `opacity`.** Animating `width`, `height`, `top` or `margin` forces
  layout every frame. Where you must change layout, animate a transform that looks equivalent.
- **`gap` over margins** in flex/grid — it doesn't produce edge margins you then have to strip.
- **Logical properties** (`padding-inline`, `margin-block`) survive right-to-left languages;
  physical ones don't.
- **`clamp()` for fluid type and spacing** — one declaration instead of breakpoint steps.
- **Reserve image dimensions** (`aspect-ratio` or width/height attributes) or you get layout
  shift on load.
- **`prefers-reduced-motion`** — wrap non-essential motion, don't remove feedback.
- **Container queries** where a component's layout depends on *its* space, not the viewport's —
  this is usually what you actually wanted from media queries.
- **`:has()`** for parent-state styling, replacing a lot of former JavaScript.

---

## Godot (4.x)

Godot's `Theme` resource *is* a design-token system: colours, fonts, font sizes, integer
constants, `StyleBox` presets, and type variations map one-to-one onto tokens.

### Structure

- **One `Theme` as the single source of truth.** Assign it as the **project default**
  (`Project Settings → GUI → Theme → Custom`) rather than per scene — then every `Control`
  inherits it by existing, and no screen can be built having forgotten it. Theme-variation names
  also autocomplete in the inspector only in this configuration.
- **Generate the theme in code** (a tool script) rather than hand-authoring the `.tres`. Tokens
  become named constants in one file, the resource is always valid, and features like
  `FontVariation` weight axes and glyph spacing are far easier to set programmatically.
  **If the resource is generated, it's build output — never hand-edit it, or the next
  regeneration silently destroys the change.**
- **Type variations are your semantic component set.** Register a variation, style it once, then
  a node declares its role with one property.

```csharp
// setters are consistently (itemName, themeType, value)
theme.SetTypeVariation("PrimaryAction", "Button");
theme.SetColor("font_color", "PrimaryAction", textPrimary);
theme.SetFontSize("font_size", "PrimaryAction", 14);
theme.SetStylebox("normal",  "PrimaryAction", normalBox);   // note: lowercase "b" in SetStylebox
theme.SetStylebox("hover",   "PrimaryAction", hoverBox);
theme.SetStylebox("pressed", "PrimaryAction", pressedBox);
theme.SetStylebox("focus",   "PrimaryAction", focusBox);
theme.SetStylebox("disabled","PrimaryAction", disabledBox);

// on a node:
myButton.ThemeTypeVariation = "PrimaryAction";
```

- **Spacing lives in theme constants**, not in node offsets:
  `SetConstant("separation", "BoxContainer", 16)` covers both `HBoxContainer` and
  `VBoxContainer` by inheritance; `h_separation`/`v_separation` on `GridContainer` and
  `FlowContainer`; `margin_left`/`margin_top`/`margin_right`/`margin_bottom` on
  `MarginContainer`. Constants are strictly integers.
- **Component padding is `StyleBox.ContentMargin*`**; spacing *between* components is the
  container's separation constant. Keep them distinct.
- **Elevation on `StyleBoxFlat`:** `SetCornerRadiusAll`, `BorderWidth*`/`BorderColor`,
  `ShadowSize`/`ShadowColor`/`ShadowOffset` (shadow does nothing if `ShadowSize < 1`),
  `AntiAliasing` for clean rounded edges. On dark UI prefer a lighter `BgColor` step plus a
  low-alpha border over a heavy shadow.

### Traps

- **Theme lookup order:** node's `ThemeTypeVariation` → node class → ancestor themes → project
  default. A per-node `AddTheme*Override` beats all of them — which is why "my colour isn't
  applying" is nearly always a stray override.
- **Overrides are legitimate in exactly one case:** a custom-drawn control suppressing inherited
  chrome. When you do it, cover *every* state, or you get a mystery border in the one state you
  missed.
- **Custom `_Draw` controls get no states for free.** Connect `MouseEntered`/`MouseExited`/
  `FocusEntered`/`FocusExited` to `QueueRedraw` yourself, or the widget feels painted on.
- **Containers overwrite child size and position** on every sort. Don't fight them with anchors.
- **State styleboxes swap instantly** — there is no theme-level transition. Smooth motion is a
  `Tween` on the node (`CreateTween().TweenProperty(...)` over `modulate`, `scale`, `position`).
- **A pressed-state content shift must compensate**: `+N` top and `−N` bottom keeps total height
  constant so the layout doesn't reflow on click.
- **Multi-frame async UI animation can outlive its node.** If you await frames, re-check the
  node is still valid after *each* await — a scene change inside that window otherwise throws
  where nothing can catch it.
- **Overlapping tweens on one property fight each other**, and capturing a "rest" position from
  a mid-animation node drifts it permanently. Store rest once; kill the in-flight tween before
  starting a new one.
- **Game UI needs distance testing.** What's readable at a desk is unreadable on a couch. Also
  test both target resolutions — game UI scales differently from web.
- **Controller navigation makes focus mandatory.** With no pointer, the focus ring is the entire
  cursor. Set initial focus deferred on screen open so it survives the same-frame layout pass.

---

## React / React Native

- **Tokens as a typed theme object**; consume via context. Type the keys so a typo fails the
  build rather than silently rendering nothing.
- **Never inline style objects in render** — they allocate every frame and defeat memoisation.
- **Style-prop APIs invite drift.** Constrain component props to variants and sizes; don't accept
  arbitrary style overrides in a design-system component.
- **React Native:** no cascade, no percentage-of-content sizing, different shadow APIs per
  platform (`shadow*` on iOS, `elevation` on Android). Test both — a shadow tuned on iOS
  frequently doesn't exist on Android.
- **Respect the OS reduced-motion and text-scale settings** — `AccessibilityInfo` on RN,
  `prefers-reduced-motion` on web.

---

## Native mobile

- **Follow the platform's own conventions.** iOS and Android users have different expectations
  for navigation, back behaviour, sheets, and destructive-action placement. A cross-platform
  design that ignores both is worse than two native-feeling ones.
- **Support Dynamic Type / font scale.** Users set large text for a reason; fixed-height
  containers around text break at large sizes.
- **Safe areas and insets** — notches, home indicators, gesture zones, keyboards.
- **Touch targets ≥44pt** for anything primary.
- **Thumb reach matters** on large phones: primary actions belong in the lower two-thirds; the
  top corners are the hardest region to reach one-handed.

---

## Terminal / TUI

- **You have ~16 reliable colours** plus 256/true-colour where supported. Detect; degrade
  gracefully.
- **Never rely on colour alone** — a lot of output is piped, logged, or read in a colour-blind
  terminal. Symbols and text carry meaning; colour reinforces.
- **Respect `NO_COLOR`** and detect whether output is a TTY before emitting escape codes.
- **Alignment is your only layout tool** — columns, consistent indentation, and whitespace.
- **Progressive disclosure = verbosity flags.** Default output is the summary; detail on request.
- **Errors still need the three parts** (what, why, what next) — a stack trace is not an error
  message.
