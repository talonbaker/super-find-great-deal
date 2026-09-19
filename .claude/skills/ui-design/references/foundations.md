# Visual Foundations

Layout, spacing, typography, colour, depth. The craft that makes an interface read as
deliberate.

---

## 1. Hierarchy

Hierarchy is the structure that tells the eye where to look first, second, third. It comes from
*relationships* between elements, never from any element's absolute value.

### Rank before you style

Sort every element on the screen into three buckets. Three is almost always enough; four means
you probably haven't decided.

- **Primary** — the one thing the user came for, plus the one action you want taken
- **Secondary** — supporting information they'll want once oriented
- **Tertiary** — metadata, timestamps, help text, legal, attribution

Then express the ranking with the four channels, roughly in this order of power:

| Channel | Strength | Cost |
|---|---|---|
| **Position** | Very high | Free — but constrained by layout |
| **Contrast/colour** | Very high | Free — but spends your accent budget |
| **Weight** | High | Free |
| **Size** | Moderate | Expensive — consumes space fast |
| **Whitespace around** | High | Expensive in dense UI, cheap otherwise |

**The common mistake is reaching for size first.** It's the weakest channel per pixel spent. A
16px semibold in full-contrast white beats a 32px thin grey every time, and leaves you room for
content.

### Isolation is the strongest single tool

The Von Restorff effect: one thing that differs from its neighbours is remembered and noticed
far out of proportion to the difference's size. This is why *scarcity* matters more than
intensity — one accent button on a neutral screen is a beacon; four accent buttons are just a
colourful screen.

### Where the eye actually goes

Useful heuristics, not laws — they come from eye-tracking research that secondary sources
routinely over-simplify:

- **Reading order dominates.** In left-to-right scripts, top-left first, then rightward and down.
  Everything else is a modifier on this.
- **F-pattern** in text-heavy content: users scan the first lines fully, then progressively less
  of each subsequent line, hugging the left edge. Front-load information; put key words at the
  start of lines and headings.
- **Z-pattern** in sparse layouts with few elements: top-left → top-right → diagonal →
  bottom-right. Primary action bottom-right works with it.
- **Layer-cake pattern** in well-structured content: users jump heading to heading, skipping
  body. Good headings are therefore load-bearing, not decoration.
- **Faces and motion hijack attention** regardless of layout. Use deliberately; an animated
  element near your primary action *steals* from it.

Design for scanning, not reading. Users don't read screens; they hunt.

### Advanced: hierarchy under density

In dashboards, HUDs, tables and pro tools, you can't afford whitespace or size:

- Lean almost entirely on **weight + colour**, in a tight ramp
- Use **alignment** as grouping (a shared left edge reads as a set)
- Use **tabular/lining numerals** for any column of numbers so digits align optically
- Right-align numbers, left-align text, and never centre either in a table
- Reserve a single accent for the one value that needs attention — a threshold breach, an alert

---

## 2. Spacing & layout

### The scale

Pick a base unit and never leave it. 8px is the default across Material, Apple HIG, Carbon,
Fluent and Ant Design — most screen sizes divide evenly by it, so it stays crisp across density
ratios. Use 4px as a half-step for tight intra-component gaps (icon-to-label, chip padding).

```
4    xs    icon↔label, chip padding, tight inline gaps
8    sm    related items inside a group, compact list rows
12   md-   dense form rows, secondary gaps
16   md    default gap between items; standard component padding
24   lg    between subgroups within a section
32   xl    between major sections
48   2xl   page-level separation
64   3xl   hero / major landmark separation
```

Anything not on the scale needs a reason you'd defend out loud. A screen with 10, 14, 18 and 22
in it does not have a spacing system — it has ten separate decisions that nobody made
deliberately.

### The proximity rule

**Space inside a group must be less than space around it.** This is the Gestalt proximity
principle and it is the highest-value layout rule there is:

```
✗  Label                    ✓  Label
   [16px]                      [4px]
   Input                       Input
   [16px]                      [24px]
   Label                       Label
   [16px]                      [4px]
   Input                       Input
```

The left column is one undifferentiated list. The right column is two obvious fields — with no
borders, no boxes, no dividers. **If your grouping needs a divider line to be readable, fix the
spacing instead.** Dividers are for when spacing alone can't do it (long lists, tables), not a
default.

### The other Gestalt principles worth knowing

- **Common region** — a shared background or border groups items even against proximity. Useful
  when spacing can't be changed. Cheaper than it looks: a 4% background tint is enough.
- **Similarity** — items sharing colour/shape/size read as the same kind of thing. Corollary: two
  things that behave differently must *look* different, or users will expect them to match.
- **Continuity** — the eye follows lines and alignment. A consistent left edge is a rail.
- **Closure** — people complete implied shapes; you don't need to draw the whole box.
- **Figure/ground** — one layer must clearly read as "on top". Ambiguity here is what makes
  glassy/translucent UI feel muddy.

### Grids and alignment

- **Fewer alignment edges is better.** Every distinct left edge is a line the eye has to track.
  Most good screens have two or three.
- **12-column grids** are the web default because 12 divides by 2, 3, 4 and 6 — most useful
  layouts fall out of it. Don't cargo-cult it into non-web UI.
- **Optical alignment beats mathematical alignment.** Circles, triangles and punctuation need to
  overshoot their box to *look* aligned. Trust your eye over the number here — this is one of the
  few places where it's right.
- **Let the layout system own positioning.** Manual absolute coordinates are correct until the
  first resize, font change, or translated string.

### Responsive

- Design the **smallest** viewport first — it forces the priority decision that the large layout
  then relaxes. Designing large-first and cutting produces a small layout that's missing things.
- Break at **content**, not at device names. When the line length gets uncomfortable or the
  layout gets awkward, that's the breakpoint.
- **Fluid over stepped** where possible: constrained fluid sizing (`clamp()`-style) beats
  jumping between fixed values at arbitrary widths.

---

## 3. Typography

### Type scale

Choose ~5–6 sizes from a ratio and stop. Common ratios: 1.2 (minor third, dense/pro UI), 1.25
(major third — Material's choice), 1.333 (perfect fourth — more dramatic, good for marketing).

```
1.25 ratio, 16px base:
12  14  16  20  25  31
caption / small / body / lead / heading / display
```

**Round to whole pixels** and adjust for taste — a scale is a starting point, not a mandate.

**The failure mode to watch for:** sizes bunched within 2–3px of each other. If `caption`,
`small` and `body` are 12/13/14, they are indistinguishable at a glance and you have one size
pretending to be three. Either separate them or merge them.

### Sizes that matter

- **Body ≥16px** for comfortable sustained reading. This is a real threshold, not a preference.
- **≥14px** acceptable in dense professional UI where the user is engaged and close to the
  screen. Below 12px is decoration, not text.
- **Game/TV/couch UI needs more** — test at real viewing distance, not at your desk. What's
  comfortable at 60cm is unreadable at 3m.
- Never size text below the platform's minimum interactive text size for anything that must be
  read to operate the interface.

### Weight, line-height, measure

- **1–2 typefaces maximum.** Variety comes from weight, not from more fonts. A single good
  variable sans covers everything; add a display or mono face only for genuine contrast.
- **Weight steps of 200+** to read as distinct (400→600 works; 400→500 barely registers).
- **Line-height:** body 1.4–1.6 (1.5 default); headings 1.1–1.25; buttons/labels 1.0–1.2. Larger
  text needs proportionally *less* line-height.
- **Measure (line length): 45–75 characters**, ~66 optimal. Long lines lose the reader on the
  return sweep. This is the most-ignored typography rule and one of the most consequential — cap
  it with a max-width, not with luck.
- **Letter-spacing:** slightly negative on large headings, slightly positive on small caps and
  all-caps labels. Never letter-space lowercase body text.
- **All-caps** is fine for short labels, actively harmful for anything longer than ~3 words —
  word-shape recognition disappears.

### Advanced

- **Variable fonts** give you the whole weight axis in one file — cheaper than shipping 4 static
  weights, and you can hit intermediate values.
- **Tabular/lining numerals** (`font-variant-numeric: tabular-nums` or the equivalent) for any
  number that updates in place or sits in a column. Without it, digits jitter as values change —
  the classic wobbling timer bug.
- **Optical sizing** where available: type designed for 12px needs different proportions than
  type at 60px.
- **Bold reads heavier on dark backgrounds.** Drop one weight step when inverting a light design
  to dark, or headings look chunky.

---

## 4. Colour

### Structure the palette by role, not by hue

Two tiers. Primitives are raw values; semantics are jobs. **Components reference semantics
only** — that's what makes retheming a remap instead of a rewrite.

```
Primitive:  gray-950  gray-900  gray-800 … gray-50    blue-600  blue-500 …
Semantic:   bg-canvas  bg-surface  bg-elevated
            border-subtle  border-default  border-strong
            text-primary  text-secondary  text-muted  text-disabled
            accent  accent-hover  accent-subtle
            success  warning  danger  info  (+ their -subtle/-text variants)
```

A neutral ramp plus one accent plus four semantics covers the overwhelming majority of
interfaces. If you find yourself needing a fifteenth grey, you're solving a spacing or hierarchy
problem with colour.

### The 60/30/10 balance

~60% dominant neutral (background), ~30% secondary surfaces and structure, ~10% accent. It's a
crude rule and it works. If accent exceeds ~10% of the visual field, hierarchy is gone.

### Dark mode is not inverted light mode

This is where most palettes fail:

- **Never pure black (`#000`) with pure white (`#fff`).** The 21:1 ratio causes *halation* —
  light text visibly bleeds and smears on OLED and high-contrast panels, worst for astigmatic
  readers. Use a near-black base (`#121212` is Material's, `#18181B`/`#0F172A` are good) and
  off-white text (`#E8EAED`-class).
- **Desaturate and lighten accents.** A saturated accent tuned for white backgrounds reads as
  harsh neon on near-black. Shift ~10–20% lighter and drop saturation.
- **Shadows nearly vanish.** Depth has to come from surface-lightness steps and hairline borders
  instead — see §5.
- **Elevation inverts.** In light mode, higher = more shadow. In dark mode, higher = *lighter
  surface*. Going darker to elevate reads as a hole.
- **Reduce heading weight one step**, per the typography note above.
- **It's not universally better.** Light-on-dark reading performance is measurably worse for
  most sighted users in sustained reading; dark mode helps in low light and for some
  photophobic and migraine-prone users, and hurts some astigmatic ones. Offer both plus
  "follow system"; don't force either.

### Contrast

Verify **pairs**, always — a palette is never compliant, only pairings are. Thresholds and the
exact WCAG criteria are in `references/accessibility.md`. Run `scripts/contrast.py`.

The pairs people forget:
- Text over **images, video or 3D scenes** — there is no fixed pair; you need a guaranteed scrim
- **Translucent surfaces** over arbitrary content — same problem
- **Disabled** text (exempt from WCAG, but still needs to be perceivable as *present*)
- **Placeholder** text (not exempt — it's real content and routinely fails)
- **Focus rings** against *both* the component and the background behind it
- **Icons and borders** that carry meaning — 3:1 minimum, they're UI components

### Semantic colour

- Red/green for danger/success is a convention, not a language — and it's the single worst pair
  for the most common colour-vision deficiency. **Always pair with an icon or text.**
- Danger doesn't have to be red. Any colour reserved and used consistently works, as long as it
  contrasts with your accent and never appears decoratively.
- Reserve semantic colours *absolutely*. The moment "success green" is used because a chart
  needed a fourth series, it stops meaning success.

---

## 5. Depth & surfaces

Pure flat design (circa 2015) reads as dated because it stripped the cues that signal what's
interactive. The current idiom is flat-with-depth: restrained, systematic, meaningful.

### An elevation scale that encodes meaning

```
0  base       page background                     no shadow, no border
1  raised     cards, panels, list items           hairline border, or +1 surface step
2  overlay    dropdowns, popovers, tooltips       subtle shadow + surface step
3  modal      dialogs, sheets                      pronounced shadow + scrim behind
4  critical   toasts, alerts, command palettes    strongest shadow
```

**Elevation must mean something.** A card is raised because it's a discrete object you can act
on, not because raised looks nice. Two things at the same conceptual level get the same
elevation.

### Two approaches, pick one and commit

- **Shadow-led** (light UI): soft, large-radius, low-opacity shadows. Real shadows are diffuse —
  a tight dark drop shadow reads as 2008. Layer two shadows (one tight and faint for contact, one
  wide and very faint for ambience) for a convincing result.
- **Border-led** (dark UI, and the current fashion generally): hairline borders at 8–15% white,
  plus a slightly lighter surface for each elevation step. Cheaper, crisper, and it's what dark
  interfaces need since shadows disappear against near-black.

### Radius

Pick a radius language and hold it:

- **One value everywhere** (6–8px) is the safest and reads as calm and modern.
- **Role-assigned values** — e.g. pill for buttons, 12–16 for panels, 8 for inputs, 4 for chips
  — is also legitimate *if the assignment is by role and written down*. Four values chosen by
  role is a system; four values chosen by mood is drift.
- **Nested radii should be concentric**: inner radius = outer radius − padding. An 8px card with
  16px padding wants a 8−16 → 0 (square) inner element, not another 8px one, or the curves fight.
- Radius carries tone: **0px** reads technical/brutalist/retro; **4–8px** neutral and modern;
  **16px+** friendly and consumer; **pill** playful or high-emphasis.

### Effects worth using, and the ones that aren't

- **Subtle gradients** within a single hue, very low contrast — adds life without noise.
- **Noise/grain** at low opacity — excellent for killing banding on large gradients, and for
  atmosphere. Cheap.
- **Frosted glass / backdrop blur** — genuinely useful for overlays where context should stay
  visible. But it costs performance (a full-screen multi-sample pass), and text on top of it has
  no guaranteed contrast. Always put a solid-enough scrim behind the text.
- **Neumorphism** — avoid. Its entire premise (low-contrast extruded surfaces) is
  irreconcilable with contrast minimums.
- **Heavy glassmorphism as a default surface** — avoid. Fine as a rare accent, incoherent as a
  system.

---

## 6. Icons & imagery

- **Icons need labels.** Unlabelled icons are a memory test. The universally understood set is
  tiny (roughly: home, search, close, back, print, and arguably a hamburger). Everything else is
  learned, not intuited.
- **One icon family**, one stroke weight, one optical size. Mixing filled and outlined styles
  arbitrarily is a visible tell.
- **Align optically, not mathematically** — icon bounding boxes lie.
- **Images need intrinsic dimensions reserved** before they load, or the layout jumps (cumulative
  layout shift). Reserve the aspect ratio.
- **Every meaningful image needs a text alternative**; decorative images need an explicitly empty
  one so screen readers skip them.
