---
name: ui-design
description: Use for any UI/UX work — designing, building, restyling, reviewing or debugging any screen, menu, HUD, form, dashboard, dialog, overlay, page or component, in any framework. Covers hierarchy, spacing, typography, colour, layout, states, feedback, motion, error handling, accessibility, tokens and component APIs. Reach for it whenever something "looks flat", "feels cheap", "isn't modern", is hard to use, or when a colour, size, spacing value, radius, state or animation is about to be chosen.
---

# UI/UX Design

## What this is

A method for producing interfaces that read as deliberate, and for fixing ones that don't.

**The central claim, which everything here follows from:** the gap between amateur and
professional interface work is almost never talent, taste, or effects. It is **structure decided
before surface**. Amateur work picks a colour first and a layout last. Professional work settles
purpose, hierarchy, grouping and rhythm — at which point the colour choice is nearly forced and
mostly doesn't matter.

This is medium-agnostic. It applies to web, native, game UI, terminal, print. Where a platform
detail matters, it is in `references/implementation.md`, not here.

## The order of operations

**Work in this order. Most bad UI is the result of starting at step 6.**

| # | Step | The question it answers | Skip it and you get |
|---|---|---|---|
| 1 | **Purpose** | What is this screen *for*? What's the one thing the user came to do? | A screen that shows everything and helps with nothing |
| 2 | **Content inventory** | What must actually appear? What's genuinely optional? | Layout built around imagined content that never arrives |
| 3 | **Hierarchy** | Rank every element primary / secondary / tertiary | Everything shouting; the eye lands nowhere |
| 4 | **Grouping & layout** | What belongs with what? What's the reading order? | Related things scattered, unrelated things adjacent |
| 5 | **Spacing** | One scale, applied consistently | The single biggest "looks amateur" tell |
| 6 | **Typography** | Size, weight and colour expressing the step-3 ranking | Hierarchy by font size alone; bloated layouts |
| 7 | **Colour** | Neutral base, scarce accent, semantic meaning | Rainbow UI; no accent left for what matters |
| 8 | **Depth & surfaces** | Elevation encoding meaning, one radius language | Either hard flat borders or gratuitous shadows |
| 9 | **States & feedback** | Every interactive thing responds; every wait is visible | A mockup that happens to compile |
| 10 | **Motion** | Transitions that explain change | Snapping, or bouncing that reads as a toy |
| 11 | **Verify** | Contrast, keyboard, targets, real content, real sizes | Passing your own machine and failing everyone else's |

Steps 1–5 decide whether it looks professional. Steps 6–8 are polish on that foundation.
**When something "looks flat" or "feels cheap", the cause is nearly always 3, 5 or 9 — not 7.**
Diagnose there first, whatever the complaint sounds like.

## The five decisions that carry most of the quality

If you do nothing else, do these.

### 1. One spacing scale, no exceptions

Pick a base unit (8px is the industry default; 4px for fine intra-component gaps) and make
**every** margin, padding and gap a multiple of it: `4, 8, 12, 16, 24, 32, 48, 64`. Name the
steps. No 13px, no 25px, no "looked right in the inspector."

Then apply the **proximity rule**: space *inside* a group must be smaller than space *around* it.
That single relationship does the work of most divider lines — if your grouping needs a border to
be readable, the spacing is wrong, and the border is a patch.

### 2. Hierarchy by weight and colour before size

Three ranks is usually enough. Express them with **weight and colour first**, size last:

```
Primary    600–700 weight,  full-contrast text
Secondary  400–500 weight,  muted text
Tertiary   400–500 weight,  dim text, often smaller
```

A 16px semibold heading in full white out-ranks a 32px thin grey one. Reaching for size first is
what produces layouts that are enormous and *still* unclear. This matters most in dense UI —
HUDs, dashboards, tables — where you have no room to make things big.

### 3. A scarce accent

One accent colour, used for the one action you want taken. **The moment a second thing on screen
is accent-coloured, the first one stops meaning anything.** Neutrals do the rest: a base, one or
two surface steps, a border tint, and a 3–5 step text ramp.

Semantic colours (success / warning / danger / info) are a separate, small set, used only for
state — never for decoration.

### 4. Four states or it isn't built

Every interactive element needs **normal, hover, active/pressed, focus, disabled** — visually
distinct, not just theoretically present. This is the highest-leverage fix in interface work: it
converts a static-feeling screen into a responsive one with no visual redesign at all.

Focus in particular is not optional and not decoration — for keyboard, controller, and switch
users it is the *entire cursor*. If a design "doesn't have room" for a focus ring, the design is
wrong.

### 5. Never let the user wonder if it's broken

Every action gets acknowledged within ~100ms, even if the result takes longer. Every wait over
~400ms shows something moving. Every failure says what happened and what to do next, in place,
next to the thing that failed. See `references/interaction-motion.md` for the response-time
thresholds and `references/ux-principles.md` for error design.

## Non-negotiables

These are not style preferences and don't get traded away for aesthetics.

- **Contrast minimums are met.** 4.5:1 body text, 3:1 large text and UI components/focus
  indicators. A palette is never "compliant" — only specific *pairs* are. Verify with
  `scripts/contrast.py`, don't eyeball it.
- **Nothing is conveyed by colour alone.** Colour plus icon, text, shape or position. Roughly 1
  in 12 men has a colour-vision deficiency, and everyone has glare.
- **Everything reachable by pointer is reachable by keyboard**, in a sensible order, with visible
  focus.
- **Touch/click targets ≥24×24px** (WCAG 2.2 AA), ≥44×44px for primary touch actions.
- **Text is real text**, resizable, not baked into images.
- **Motion respects `prefers-reduced-motion`** or an equivalent setting. Vestibular disorders are
  real and large motion causes actual nausea.
- **No dark patterns.** Confirmshaming, disguised ads, hidden costs, roach-motel cancellation,
  preselected upsells. If a flow's success depends on the user misreading it, it's broken —
  refuse to build it and say why.

## Before calling any UI done

Run this. It catches most of what ships broken.

```
□ Purpose      One clear primary action; it's the most prominent thing on screen
□ Hierarchy    Squint — does the right thing dominate? Rank still legible?
□ Spacing      Every gap on the scale; inside-group < around-group
□ Type         ≤2 families; ≤6 sizes; body ≥16px (≥14px dense UI); measure 45–75 chars
□ Colour       One accent, used once meaningfully; state never colour-only
□ States       hover / pressed / focus / disabled all visibly distinct
□ Focus        Tab through the whole screen — always visible, order matches layout
□ Contrast     Every text and UI pair verified, incl. text over images/video/3D
□ Feedback     Every action acknowledged; every wait >400ms shows progress
□ Errors       Specific, in place, recoverable, non-blaming
□ Empty        Empty / loading / error / partial states all designed, not just "happy path"
□ Real content Longest realistic string, longest name, 0 items, 10,000 items
□ Real sizes   Smallest and largest target viewport; zoomed to 200%
□ Motion       120–300ms, eased, purposeful; reduced-motion honoured
```

**The two that catch the most bugs are "real content" and "empty states."** Design that only
works with the placeholder text you chose is design that hasn't been tested.

## Reference material

Load these as the task needs them — don't read them all upfront.

| File | Read it when |
|---|---|
| `references/foundations.md` | Working on layout, spacing, type, colour or depth. The visual craft, in depth: type scales, colour ramp construction, dark-mode specifics, elevation systems, grids. |
| `references/ux-principles.md` | The problem is *usability*, not looks. Information architecture, user flows, cognitive load, the heuristics and psychological laws worth knowing, error and empty-state design, forms, progressive disclosure. |
| `references/interaction-motion.md` | Adding states, feedback, animation, or fixing "feels sluggish"/"feels cheap". Response-time thresholds, easing, choreography, micro-interactions, perceived performance. |
| `references/accessibility.md` | Any accessibility question, or before shipping. WCAG thresholds with the exact criteria, keyboard patterns, screen readers, motion sensitivity, cognitive accessibility. |
| `references/design-systems.md` | Building something reused, or a codebase with style drift. Token architecture, naming, component APIs, theming, governance, migration. **The advanced tier.** |
| `references/implementation.md` | Translating tokens into a specific platform. CSS/web, Godot Theme, React Native, native mobile — plus the traps in each. |
| `scripts/contrast.py` | Any contrast question. Checks pairs, audits a whole palette against backgrounds, and suggests the nearest passing colour. |

## Working method

**When designing something new:** walk the order of operations. Don't skip ahead to colour
because it's the fun part — you'll redo it once the structure lands.

**When fixing something that "looks bad":** the complaint is rarely the cause. Inventory the
actual values first — every distinct colour, size, spacing value and radius in use. Sprawl in
that inventory *is* the diagnosis, and the count tells you which step to fix. Then fix in the
order of operations, one screen at a time. **A sweeping change across every screen at once makes
improvements and regressions indistinguishable.**

**When reviewing:** run the checklist above. Report what fails and why it matters to a user, not
which rule it violates.

**When the design brief is genuinely ambiguous** — two readings lead to materially different
work, and neither is obviously right — ask. Ambiguity about a *value* (exact px, exact ms) is not
that: pick a sensible one from the scales here, state it, move on.

## Caveats

- **These are heuristics, not laws.** The named "laws" (Fitts, Hick, Miller) are useful models
  from real research that get flattened in secondary sources. `references/ux-principles.md` notes
  where the popular version overstates the finding — Miller's "7±2" especially, which is widely
  misapplied and closer to 4±1 for working memory.
- **Convention usually beats cleverness.** Users spend nearly all their time in *other*
  interfaces and arrive with those expectations. Novel interaction patterns need a reason and a
  test, not just a preference.
- **Accessibility minimums are a floor, not a target.** Passing 4.5:1 doesn't mean it's
  comfortable. For sustained reading on dark backgrounds, aim higher (~6:1) — light-on-dark
  reading performance is measurably worse than the ratio alone suggests.
- **Automated checks catch maybe a third of accessibility problems.** Contrast and markup are
  checkable; focus order that's technically valid but nonsensical, or an error message that's
  announced but useless, need a human.
- **Aesthetics do affect perceived usability** (the aesthetic-usability effect is real and
  replicated), which is precisely why it's dangerous: a good-looking prototype tests better than
  it works, and it hides usability problems in user testing. Never treat "people liked how it
  looked" as evidence it works.
