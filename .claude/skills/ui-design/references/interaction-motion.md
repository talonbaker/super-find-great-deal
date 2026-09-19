# Interaction, Feedback & Motion

What turns a static composition into something that feels alive and responsive. This is where
"looks fine but feels cheap" is fixed.

---

## 1. Interactive states

**Every interactive element owes five visually distinct states.** This is the single
highest-leverage change available in most interfaces — it costs no redesign and transforms how
the product feels.

| State | Signals | Typical treatment |
|---|---|---|
| **Default** | It exists and is actionable | Base surface, clear boundary or affordance |
| **Hover** | The pointer will act here | Lighten/darken surface, strengthen border, raise elevation |
| **Active / pressed** | The press registered | *Darker or inset*, often a 1–2px downward shift or 0.97 scale |
| **Focus** | Keyboard/controller is here | High-contrast ring, 2px, offset from the element |
| **Disabled** | Not available now | Reduced contrast, no hover response, cursor change, **and a reason** |
| **Loading** | Working on it | In-place spinner, label change, element stays same size |
| **Selected / on** | Persistent state | Distinct from hover — this one *stays* |

### Make the deltas obvious

The most common failure is states that technically differ but aren't perceptible. **If you have
to A/B two screenshots to see the hover state, users won't see it at all.** Err large: a hover
that changes surface opacity from 10% to 22% is not subtle, and users consistently report
subtler versions as "unresponsive" or "cheap."

### Press should feel physical

Pressed should read as *depressed*, not just tinted:
- Go **darker than default**, not lighter (light-on-press reads as hover, which is confusing)
- Shift content down 1–2px, **compensating the opposite side** so the element's total size
  doesn't change — otherwise the whole layout reflows on every click
- Or scale to ~0.97 with the transform origin centred

### Focus is not hover

They're different inputs and should look different. A common good pattern: hover changes the
*surface*, focus draws a *ring*. Then a keyboard user can tell what's focused even while the
mouse sits over something else.

**Focus rules:**
- Never remove focus indication without replacing it with something better
- 2px minimum, 3:1 contrast against *both* the component and the page behind it
- Offset it slightly from the element so it doesn't get lost in a border
- Use focus-visible semantics where available so mouse users don't see rings on click, but
  keyboard users always do
- Move focus deliberately: into a dialog on open, back to the trigger on close, to the first
  error on failed submit

### Non-standard controls

Anything custom-drawn must re-implement all of this by hand — pointer enter/leave, focus
enter/leave, press, and the disabled case. Half-implemented custom controls are why bespoke
widgets feel worse than native ones.

---

## 2. Response time — the thresholds that matter

These are perceptual constants, not preferences:

| Budget | What it means | What you must do |
|---|---|---|
| **<100ms** | Perceived as instantaneous | Acknowledge *every* input within this, even if the work takes longer |
| **~400ms** | The Doherty threshold — flow is maintained | Target for common operations |
| **<1s** | Thought flow uninterrupted, but noticed | No indicator needed; keep it snappy |
| **1–10s** | Attention wanders | Show a determinate progress indicator |
| **>10s** | User leaves or multitasks | Progress + estimate + let them do something else; notify on completion |

**Acknowledge instantly, complete eventually.** A button that visually depresses at 20ms and
shows a spinner while a 3s request runs feels responsive. The same button that does nothing for
3s feels broken, even though it's the same request.

### Perceived performance beats actual performance

- **Optimistic UI** — show the result immediately, reconcile with the server after, roll back
  visibly on failure. The single biggest perceived-speed win available.
- **Skeleton screens** over spinners — they show the shape of what's coming, avoid layout shift,
  and read as faster for the same duration.
- **Progressive loading** — render what you have; don't block the whole view on the slowest part.
- **Prefetch on intent** — hover or focus is a strong signal the click is coming.
- **Never show a spinner for <300ms.** A flash of loading state feels *worse* than a brief pause.
  Delay showing it; if the work finishes first, nobody sees a flicker.
- **Determinate beats indeterminate.** A progress bar that moves — even approximately — reads as
  faster and more trustworthy than an endless spinner.
- **Never let a progress indicator stall silently.** A frozen bar is worse than none. If you
  can't measure progress, animate something that clearly means "working."

---

## 3. Motion

### What motion is for

Motion has three legitimate jobs. Anything else is decoration and should be cut:

1. **Continuity** — showing that a thing moved or transformed rather than being replaced. Where
   did the panel come from? What turned into what?
2. **Attention** — directing the eye to a change it would otherwise miss.
3. **Feedback** — confirming input landed.

If an animation doesn't do one of those, remove it. Motion that exists to be admired gets
annoying by the twentieth viewing — and users see your interface far more than twenty times.

### Duration

| Motion | Duration |
|---|---|
| Micro-feedback (hover, press, toggle) | 100–150ms |
| Standard transition (panel, dropdown, tab) | 200–300ms |
| Large or full-screen transition | 300–500ms |
| Entrance of a set (staggered) | 200–300ms each, 30–80ms apart |

**Above ~400ms, motion starts to feel slow** on a repeated interaction. Below ~100ms it may as
well be instant. Larger objects and larger distances need proportionally more time — a modal
crossing the screen is not a checkbox.

### Easing

| Curve | Use for |
|---|---|
| **Ease-out** (fast start, slow finish) | **The default.** Entrances, anything responding to input — it feels immediately responsive |
| **Ease-in-out** | Movement between two on-screen positions |
| **Ease-in** (slow start, fast finish) | Exits only — things leaving should accelerate away |
| **Linear** | Only for continuous loops (spinners, progress) |
| **Spring** | Direct-manipulation feel; needs care — easy to overshoot into toy territory |

**Never `ease-in` on an entrance** — it delays the visible response and reads as lag.

**Overshoot/bounce is a tone decision, not a default.** It reads as playful and consumer.
Professional tools, dense UI, and anything with a serious register should not bounce.

### Choreography

- **Related things move together; unrelated things don't.** Motion is a grouping signal.
- **Stagger sets of items** by 30–80ms so they read as a sequence, not a blob. More than ~6 items
  and the last ones feel slow — cap the stagger or animate the container instead.
- **Transform and fade, don't move far.** 8–24px of travel is plenty; large translations are
  distracting and expose more of the layout to jitter.
- **Preserve spatial logic.** A panel from the right returns to the right. A row that expands
  should push its neighbours, not overlay them, if the model is "this grew."
- **Animate the cheap properties.** Transform and opacity are compositor-friendly nearly
  everywhere; animating layout properties (width, height, top, margin) forces re-layout every
  frame and stutters. Where you must change layout, animate a transform that *looks* like it.

### Micro-interactions

The small, complete moments: a toggle sliding, a like animating, a field turning valid, a copy
button confirming. They follow a consistent structure — **trigger → feedback → result → mode
change** — and they're where an interface's personality lives.

Keep them short, keep them consistent, and make sure the *result* is legible without the
animation, because it might be disabled.

---

## 4. Feedback beyond visuals

- **Confirm the outcome, in place.** After copying, the button says "Copied". After saving, the
  state changes. A toast in the corner is the weakest form of confirmation — it's far from the
  user's gaze and it disappears.
- **Toasts** are for transient, non-critical, non-actionable messages. Anything the user must act
  on or must not miss belongs inline or in a dialog. Never put an error that loses data in a
  toast that auto-dismisses.
- **Sound and haptics** are powerful and easily overdone. Both need an off switch. Never make
  sound the *only* channel for information — the same redundancy rule as colour.
- **Announce dynamic changes to assistive tech.** A visual-only status change doesn't exist for a
  screen-reader user; live regions (or the platform equivalent) are what carry it. See
  `accessibility.md`.

---

## 5. Reduced motion

Vestibular disorders are common, and large-motion interfaces cause genuine nausea and dizziness
— not mild annoyance.

Honour the system setting (`prefers-reduced-motion` on web, equivalents elsewhere). Reduced
motion does **not** mean no feedback:

- Replace large translations, scaling, parallax and rotation with **cross-fades**
- Keep short opacity and colour transitions — they're rarely a problem
- Keep loading indicators, but prefer non-spinning ones
- Never remove the *information* the motion carried; deliver it another way

The most problematic effects are parallax, large-scale zooming, spinning, and anything that
moves continuously in the background.

---

## 6. Common failure modes

- **"Feels cheap"** → hover/press deltas too subtle, or no press state at all. Increase the delta;
  add a physical press.
- **"Feels sluggish"** → animation duration too long, or `ease-in` on entrances, or no
  sub-100ms acknowledgement of input.
- **"Feels janky"** → animating layout properties instead of transforms, or work on the main
  thread during the animation.
- **"Layout jumps on click"** → press state changed the element's total size. Compensate the
  opposite side.
- **"Something drifts out of position after repeated use"** → overlapping animations on the same
  property, or capturing a "rest" position from an element that's mid-animation. Store the rest
  value once, cancel the in-flight animation before starting a new one.
- **"Flickers on load"** → a loading state shown for less than ~300ms. Delay it.
- **"Nothing looks selected with a keyboard/controller"** → no focus ring, or focus never
  initialised on the screen. Set initial focus on open.
- **"Animation plays when the element is destroyed"** → asynchronous animation outliving its
  target. Validate the target still exists across every frame boundary the animation waits on.
