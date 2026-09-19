# Accessibility

Not a compliance chore and not a separate mode — it is the part of design that decides whether
the interface works for people whose situation differs from yours. Most of it improves the
product for everyone.

**The framing that makes this stick:** every permanent disability has a temporary and a
situational equivalent. One-handed use covers an amputation, a broken arm, and holding a baby.
Captions cover deafness, a noisy bar, and a muted phone. High contrast covers low vision and
direct sunlight. You are designing for far more people than the disability statistics suggest.

---

## 1. The WCAG criteria that come up constantly

Level AA is the near-universal legal and contractual target (EN 301 549 in the EU, ADA
enforcement in the US, and most procurement rules).

### Contrast

| Criterion | Requirement |
|---|---|
| **1.4.3 Contrast (Minimum)** — AA | **4.5:1** for normal text; **3:1** for large text |
| **1.4.6 Contrast (Enhanced)** — AAA | 7:1 normal; 4.5:1 large |
| **1.4.11 Non-text Contrast** — AA | **3:1** for UI component boundaries, states, focus indicators, and graphics needed to understand content |

**"Large text" means ≥18pt (24px), or ≥14pt (18.66px) when bold.** Not "looks big."

**Exempt:** disabled/inactive controls, pure decoration, logos, and text in a picture of
something real. Exempt from the *rule* is not exempt from judgment — disabled text still has to
be perceivable as present.

**Not exempt, and routinely failed:** placeholder text, helper text, low-emphasis metadata,
icon-only buttons, chart lines and labels, focus rings, borders that carry state.

Verify with `scripts/contrast.py`. Never eyeball it — perceived contrast is unreliable,
especially on dark backgrounds where most people over-estimate it.

*(Forward-looking: APCA, the perceptual contrast algorithm proposed for WCAG 3, models
light-on-dark far better than the current ratio does. It is still a draft and not the compliance
target. Use WCAG 2 ratios for conformance; if a pair passes 2.x but clearly looks wrong on dark,
trust the observation and fix it anyway.)*

### Colour and other single channels

- **1.4.1 Use of Color (A)** — colour must never be the *only* way information is conveyed.
  Add an icon, text, pattern, or position. Applies to: status dots, chart series, required-field
  marks, validation state, links inside body text (underline them), diffs, and heat maps.
- The same logic extends to **sound alone** and **motion alone**, though the criteria differ.

### Text and layout

- **1.4.4 Resize Text (AA)** — usable at 200% zoom with no loss of content or function.
- **1.4.10 Reflow (AA)** — no two-dimensional scrolling at 320 CSS px equivalent width. Content
  reflows into one column.
- **1.4.12 Text Spacing (AA)** — no loss of content when users override line-height to 1.5,
  paragraph spacing to 2×, letter-spacing to 0.12em, word-spacing to 0.16em. Fixed-height
  containers around text are the usual failure.
- **1.4.13 Content on Hover or Focus (AA)** — hover/focus content must be dismissable, hoverable
  (you can move the pointer onto it), and persistent until dismissed. Kills the tooltip that
  vanishes before you can read it.

### Keyboard and focus

- **2.1.1 Keyboard (A)** — all functionality available from a keyboard.
- **2.1.2 No Keyboard Trap (A)** — you can always tab back out. Modals and embedded widgets are
  the usual offenders.
- **2.4.3 Focus Order (A)** — order preserves meaning. It should match the visual layout;
  reordering visually without reordering the DOM/tree breaks this.
- **2.4.7 Focus Visible (AA)** — a visible focus indicator, always.
- **2.4.11 Focus Not Obscured (AA, WCAG 2.2)** — the focused element isn't hidden behind sticky
  headers, footers or cookie banners. Very commonly failed.

### Targets and input

- **2.5.8 Target Size (Minimum) — AA (WCAG 2.2):** **24×24 CSS px**, or adequate spacing between
  smaller targets.
- **2.5.5 Target Size (Enhanced) — AAA:** 44×44. Use this as the practical target for primary
  touch actions.
- **2.5.3 Label in Name (A)** — an element's accessible name must contain its visible label, or
  voice control users can't say what they see.
- **3.3.7 Redundant Entry (A, WCAG 2.2)** — don't make people re-enter information they already
  gave you in the same process.
- **3.3.8 Accessible Authentication (AA, WCAG 2.2)** — no cognitive function test (puzzles,
  transcription, memorisation) without an alternative. Allow paste into one-time-code and
  password fields — blocking paste breaks password managers and fails this.

### Motion and flashing

- **2.3.1 Three Flashes (A)** — nothing flashes more than 3 times per second. This is a seizure
  risk, not a style issue.
- **2.2.1 Timing Adjustable (A)** — time limits can be turned off, adjusted, or extended.
- **2.3.3 Animation from Interactions (AAA)** — honour reduced-motion preferences. Treat this as
  mandatory regardless of its AAA level; see `interaction-motion.md`.

### Structure and status

- **1.3.1 Info and Relationships (A)** — visual structure (headings, lists, tables, groups) is
  expressed in the markup/accessibility tree, not just visually.
- **4.1.2 Name, Role, Value (A)** — every control exposes what it is, what it's called, and its
  current state. Custom controls must do this by hand.
- **4.1.3 Status Messages (AA)** — content that appears without focus moving (validation results,
  "saved", search result counts, toasts) is announced via live regions.

---

## 2. Keyboard patterns worth memorising

- **Tab** moves between components. **Arrow keys** move *within* a composite component (menu,
  tab list, radio group, grid, listbox). A 30-item menu should be one tab stop, not thirty.
- **Escape** closes and returns focus to what opened it. Always.
- **Enter** activates buttons and links; **Space** activates buttons and toggles checkboxes.
- **Dialogs:** focus moves in on open, is trapped while open, returns to the trigger on close.
  This is the one place a focus trap is correct.
- **Skip link** to main content as the first focusable element, for anyone navigating past a
  large header.
- **Never use positive tab-index values.** They create an ordering nightmare. Fix the source
  order instead.

**The ten-minute test that finds the most bugs:** unplug the mouse and complete your core flow.

---

## 3. Screen readers and semantics

- **Use the native/semantic element.** A real button gets focus, keyboard activation, and the
  right role for free. A styled `div` gets none of it and needs all of it rebuilt by hand.
- **Semantic HTML/native controls first; ARIA only to fill genuine gaps.** The first rule of ARIA
  is not to use ARIA. Bad ARIA is measurably worse than none — it overrides correct native
  semantics with wrong ones.
- **Headings describe structure, not size.** One h1; don't skip levels; don't pick a level for
  its font size.
- **Accessible names must be meaningful out of context.** Screen reader users often navigate by
  pulling up a list of links or buttons; twelve links called "Read more" are useless.
- **Icon-only controls need an accessible label**, and the visible tooltip is not it.
- **Images:** meaningful ones get descriptive alternative text; decorative ones get an explicitly
  empty one so they're skipped. Describe the *purpose*, not the picture — for a chart, the
  finding; for a button icon, the action.
- **Announce async changes.** Polite live regions for status; assertive only for genuinely urgent
  interruptions.

---

## 4. Beyond the checklist

### Low vision

Support zoom and OS text scaling. Don't fix container heights around text. Respect
high-contrast/forced-colours modes rather than fighting them. Avoid pure-black-on-pure-white and
its inverse (halation).

### Colour vision

~8% of men and ~0.5% of women have a colour-vision deficiency; red/green is the most common
confusion — exactly the pair conventionally used for error/success. Always pair with shape, icon
or text. Check designs in a deuteranopia simulation.

### Motor

Large targets, generous spacing, no precision dragging without an alternative, no time-limited
interactions, no hover-only functionality. Avoid double-click and complex gestures as the sole
path to anything.

### Cognitive

The most common set of disabilities and the least designed for:
- Plain language. Short sentences. No unnecessary jargon.
- Consistent, predictable layout and navigation.
- Break long processes into steps with visible progress.
- Reduce memory demands — show what's needed rather than requiring recall.
- Allow errors and make recovery easy; never punish a mistake with lost work.
- Avoid auto-playing media, unexpected movement, and time pressure.

### Deaf and hard of hearing

Captions on all speech; transcripts for audio; visual equivalents for every sound cue. If your
interface uses an alert sound, it needs a visible alert too.

---

## 5. Testing

**Automated tools catch roughly 30–40% of issues.** They're necessary and nowhere near
sufficient — they can verify a label exists, not that it's useful.

A practical order:
1. **Automated scan** (axe, Lighthouse, or the platform equivalent) — clears the mechanical stuff
2. **Keyboard-only pass** through every flow
3. **Contrast audit** of every text and UI pair, including states and text over images
4. **Zoom to 200%** and the narrowest supported width
5. **Screen reader pass** on core flows — NVDA/Firefox and VoiceOver/Safari are the common pairs;
   even clumsy testing catches a lot
6. **Reduced motion and forced-colours modes**
7. **Real users with disabilities**, if you can reach them. Nothing else substitutes.

**Fix the source, not the symptom.** An ARIA patch over a wrong element is technical debt that
breaks the next time the component changes.
