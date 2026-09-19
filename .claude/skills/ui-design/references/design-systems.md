# Design Systems & Tokens

The advanced tier. This is what keeps an interface coherent as it grows past what one person can
hold in their head — and what stops style drift from being inevitable.

**The core idea:** a hardcoded value is a decision with no name. Nobody can find it, nobody knows
why it's that number, and the next person makes the same decision slightly differently. Every
literal you type is future inconsistency. Tokens are how you give decisions names.

---

## 1. Token architecture

Use **two tiers**. This is the single most important structural decision in a design system.

### Tier 1 — primitives

Raw values. Named for *what they are*. No opinion about usage.

```
color-gray-950   #0A0A0B      space-1    4px       radius-sm   4px
color-gray-900   #121214      space-2    8px       radius-md   8px
color-gray-800   #1C1C20      space-3    12px      radius-lg   16px
color-gray-100   #E8E8EC      space-4    16px      radius-full 9999px
color-blue-500   #4C6EF5      space-6    24px
color-blue-400   #748FFC      space-8    32px      font-size-1 12px  …
```

### Tier 2 — semantics

Jobs. Named for *what they're for*. These reference primitives, and **components reference only
these**.

```
bg-canvas        → gray-950        text-primary     → gray-100
bg-surface       → gray-900        text-secondary   → gray-400
bg-elevated      → gray-800        text-muted       → gray-500
border-subtle    → white @ 8%      text-disabled    → gray-600
border-default   → white @ 15%     accent           → blue-400
                                   accent-hover     → blue-300
                                   danger           → red-400
```

### Why the two tiers matter

Because `bg-surface` can be remapped to a different primitive and every component follows. A
component that references `gray-900` directly has hardcoded a *decision* about dark mode into a
place that shouldn't know about it.

**The test:** if switching to light mode requires editing components, your components are
referencing primitives. Fix the layer, not the components.

A third tier — **component tokens** (`button-primary-bg` → `accent`) — is worth adding only once
you have many components and real theming needs. Below that scale it's ceremony.

### Naming

- **Semantics name the job, never the appearance.** `danger`, not `red`. `accent`, not `blue`.
  The day the brand colour changes, `color-brand-blue: #E5484D` is a permanent wound.
- **Be consistent about direction.** Either `text-primary`/`text-secondary` or
  `text-strong`/`text-weak` — mixing scales is how you end up with `text-muted-secondary-2`.
- **Encode state in the name**: `accent`, `accent-hover`, `accent-active`, `accent-subtle`.
- **Beware ordinal names that stop scaling.** `gray-1…gray-5` breaks the day you need one
  between 2 and 3. Numeric ramps with gaps (50, 100, 200 … 950) leave room.

---

## 2. What to tokenise

| Category | Tokens |
|---|---|
| **Colour** | Neutral ramp, accent + variants, semantics, borders, overlays/scrims |
| **Spacing** | The scale (§ foundations), plus component padding presets |
| **Typography** | Families, sizes, weights, line-heights, letter-spacing — and *composites* (a "body" token bundling all five) |
| **Radius** | One value or a small role-assigned set |
| **Elevation** | Shadow definitions and/or surface steps, one per level |
| **Motion** | Durations and easing curves — `duration-fast`, `ease-out` |
| **Borders** | Widths, and the focus-ring definition as one token |
| **Layout** | Container max-widths, breakpoints, grid columns, z-index layers |

**Z-index especially.** An un-tokenised stacking order becomes `z-index: 99999` within a year.
Name the layers once: `z-base`, `z-dropdown`, `z-sticky`, `z-modal`, `z-toast`.

**Typography composites are underrated.** A `text-body` token carrying family + size + weight +
line-height together prevents the very common bug where someone gets the size right and the
line-height wrong.

---

## 3. Component design

### The API is the design

A well-designed component exposes **variants** (`primary`/`secondary`/`ghost`), **sizes**, and
**states** — and nothing else. Every additional prop is a decision moved from the system to the
caller, which is exactly what a system exists to prevent.

**Never expose raw styling props.** A `color` prop that takes any value defeats the whole
exercise; you have re-invented the hardcoded literal with extra steps.

### Build the right amount

- **Rule of three** — abstract on the third occurrence, not the first. Two similar things might
  be coincidence.
- **Composition over configuration.** A component with 14 boolean props is several components
  wearing a trenchcoat. Split it, or let callers compose smaller pieces.
- **Design the states with the component**, not after. Every component ships with hover, active,
  focus, disabled, loading, error and empty where applicable — and its documentation shows them.
- **Handle content extremes at design time.** The longest realistic label, the missing value, the
  wrapped line, the 200-character name.

### Document what matters

For each component: what it's for, **when to use something else**, the variants with real
examples, the states, the accessibility contract (roles, keyboard behaviour, required labels),
and the content guidelines (sentence case? how long can a label be?).

**"When *not* to use this"** is the highest-value line in component documentation and it's almost
always missing.

---

## 4. Theming

Themes are alternate **semantic** mappings over the same primitives. If tier 2 is clean, a theme
is a value swap and nothing else.

- **Support three states, not two:** light, dark, and *follow the system*. Follow-system should
  be the default.
- **Define the complete palette in the base**, then override only what changes per theme. A token
  whose only definition lives inside a dark-mode block is undefined in light mode — a very common
  bug that surfaces as one mystery-coloured element.
- **Theme via a data attribute or context**, not by swapping stylesheets, so it can change
  without a reload.
- **Test both themes at every step.** A component built and reviewed only in dark mode will have
  light-mode bugs; this is close to a law.
- **Contrast must be verified per theme.** Passing in dark says nothing about light.

---

## 5. Adopting a system in an existing codebase

The realistic scenario: an inconsistent codebase, no system, and a request to fix it.

### Audit first — the inventory is the argument

Catalogue every distinct value actually in use: colours, font sizes, spacing values, radii,
shadows, z-indexes. Count them. The result is both the diagnosis and the justification:

- **Dozens of near-duplicate greys** → colour sprawl; token the palette first.
- **Few colours but many spacing values** → the problem is rhythm and hierarchy, not colour.
  Fix spacing first; it'll look 80% better before you touch a hue.
- **Values that repeat exactly in several places** → those are tokens with no name. Easiest,
  highest-confidence wins; do them first.
- **A whole subsystem that ignores the system** (one screen, one layer, one team's area) → this
  is the most common real pattern and it's structural, not stylistic. Bring it in on its own
  schedule.

### Then migrate incrementally

- **One screen or one component at a time**, with before/after captures. A sweeping change across
  everything at once makes improvements and regressions indistinguishable, and it's unreviewable.
- **Stop the bleeding first** — make the system the path of least resistance for *new* code
  before rewriting old code.
- **Codemod the mechanical parts** (literal → token), review the judgment calls by hand.
- **Not everything should converge.** Deliberately distinct surfaces exist — a diegetic in-world
  element, a marketing page, an embedded third-party view. The rule isn't "everything looks the
  same"; it's **"every difference is deliberate and recorded."** Anything intentionally outside
  the system should say so in a comment, or the next person will "fix" it.

### Enforce mechanically

Documentation does not prevent drift; tooling does.

- Lint rules banning raw colour literals and off-scale spacing in component code
- Type-safe token references so a typo fails the build
- Visual regression tests on the component library
- Automated contrast checks in CI
- A generated single source of truth (one definition file producing every platform's output)
  rather than parallel hand-maintained copies

**If tokens live in more than one hand-edited place, they will diverge.** Generate; don't
duplicate.

---

## 6. Governance

- **Someone owns it.** An ownerless system decays into a folder of components.
- **Make contribution possible.** If the only path to a new component is a request queue, teams
  will fork and drift instead.
- **Version and communicate changes.** Semantic versioning, changelogs, and deprecation warnings
  before removal — a design system is an API with the same compatibility obligations.
- **Track adoption**, not component count. A system nobody uses is a hobby.
- **Prune.** Components that exist for one caller, variants nobody picks, tokens with no
  references. Deleting is maintenance.

---

## 7. Failure modes

- **Too abstract too early** — a system built for imagined future needs before three real users
  exist. Build from actual patterns.
- **Too rigid** — no escape hatch, so teams fork it. Provide a documented way to go outside, and
  watch what people use it for: that's your roadmap.
- **Design and code drift apart** — the design tool and the implementation become separate
  systems that lie about each other. Generate from one source where possible; audit where not.
- **Tokens without semantics** — a palette of primitives with no role mapping. Renaming
  `#4C6EF5` to `blue-500` and stopping there is not a design system; you've made a colour list.
- **The system as a style guide** — a document rather than working code. Nobody reads it and
  nothing enforces it.
- **Ignoring accessibility until "later"** — a system that bakes in failing contrast propagates
  that failure into every product built on it. This is the most expensive mistake available here,
  because fixing it means touching everything.
