# UX Principles

Usability, structure and behaviour — the half of the work that isn't visual. A beautiful screen
that fails here is a failed screen.

---

## 1. Start with the job, not the screen

Before any layout exists:

1. **Who is here, and what did they come to do?** One sentence. If you can't write it, you can't
   design the screen.
2. **What do they already know?** Expertise, context, whether they're in a hurry, whether they're
   stressed. A form filled once at leisure and a HUD read under pressure are different problems.
3. **What's the success state?** What does "done" look like, and how do they know?
4. **What can go wrong?** Every failure mode is a state you owe a design.

**Design the flow before the screens.** A sequence of individually beautiful screens that don't
connect is the most common expensive mistake in interface work. Map the path: entry point →
steps → decision points → exits → failure branches. Then design each screen knowing what came
before and what's next.

### Reduce steps, then reduce thinking

In order of value:
1. **Remove the step entirely** — can you infer it, default it, or defer it?
2. **Remove the decision** — a good default the user can change beats a question.
3. **Make the step trivial** — better input type, autocomplete, smart parsing.
4. Only then: make the step *look* nicer.

**Tesler's law (conservation of complexity):** every process has irreducible complexity. The only
question is who absorbs it — the system or the user. Take it into the system wherever you can;
one extra week of engineering beats a million users doing manual work.

---

## 2. Affordances and signifiers

- **Affordance** = what an object *can* do. **Signifier** = the visible cue that tells the user
  so. Interfaces fail when affordances exist with no signifiers — the invisible swipe, the
  clickable div that looks like text.
- **If it's clickable, it must look clickable.** Underline links, give buttons a boundary,
  change the cursor. "Clean" designs that strip these are trading discoverability for looks.
- **If it looks clickable, it must be clickable.** Bordered non-interactive boxes cause dead
  clicks and erode trust.
- **Perceived affordance is learned.** Users read your interface with expectations from every
  other interface they've used — *Jakob's law*. Diverging from convention costs you a teaching
  budget you probably don't have. Spend it on your differentiator, never on your login form.

---

## 3. Cognitive load

Three kinds, and only one is worth spending:

- **Intrinsic** — the inherent difficulty of the task. Irreducible; support it.
- **Extraneous** — effort spent decoding your interface. **Eliminate ruthlessly.** This is where
  clutter, inconsistency and jargon land.
- **Germane** — effort building a useful mental model. Worth spending.

### Practical reductions

- **Recognition over recall.** Show the options; don't make people remember them. This is why
  visible navigation beats a command-only interface for novices, and why a "recently used" list
  beats a search box for repeated tasks.
- **Chunk information.** Group into 3–5 item sets. (The "7±2" figure everyone quotes overstates
  it — Miller's 1956 paper was about a different measure, and later work puts working memory
  closer to **4±1** chunks. Design for four.)
- **Progressive disclosure.** Show what's needed now; reveal complexity on request. Advanced
  settings behind a disclosure, optional fields behind "add details", power features in a menu.
  This is how you serve novices and experts in one interface without either compromise.
- **Sensible defaults are design.** Every default is a decision you've made *for* the user.
  Choose the one that's right for the majority, make it obvious, make it changeable.
- **Consistency is a load reduction.** Same word for the same concept everywhere; same control
  for the same action; same layout for the same kind of screen. Synonyms cost the user a lookup
  every time.

### The laws worth knowing (and their real limits)

| Law | The useful version | The caveat |
|---|---|---|
| **Fitts's** | Time to hit a target grows with distance and shrinks with size. Big, close targets; screen edges are effectively infinite in depth. | It's a model of pointing, not of finding. A huge button nobody can locate is still slow. |
| **Hick's** | Decision time grows with the log of the number of choices. | Applies to *unordered* equivalent choices. A well-sorted 50-item list isn't 50 choices — it's a search. Don't use this to justify hiding things. |
| **Miller's** | Chunk information; working memory is small. | The famous "7±2" is misapplied. Use ~4. And it doesn't limit menu length — that's a search problem, not a memory one. |
| **Doherty threshold** | Below ~400ms system response, users stay in flow and productivity jumps. | Perceived time is what matters; see `interaction-motion.md`. |
| **Postel's** | Be liberal in what you accept. Parse the phone number with spaces, the date in any format. | Never be liberal about what you *do* — confirm destructive interpretations. |
| **Peak-end** | Experiences are remembered by their most intense moment and their ending. | Invest in the peak and the finish — a great success state pays back disproportionately. |
| **Serial position** | First and last items in a list are best remembered. | Put the most important navigation at the ends. |
| **Zeigarnik** | Incomplete tasks stay in mind; progress indicators exploit this. | Also why abandoned multi-step flows nag — use for good (progress), not for pressure. |
| **Aesthetic-usability** | Attractive interfaces are *perceived* as more usable and are forgiven more. | Dangerous: it masks real usability problems in testing. Never let it substitute for the real thing. |

---

## 4. Nielsen's heuristics, as a working checklist

Still the best general-purpose review tool. Run through them on any screen:

1. **Visibility of system status** — the user always knows what's happening. Loading, saved,
   syncing, offline, how many steps remain.
2. **Match to the real world** — their vocabulary, not your database schema's.
3. **User control and freedom** — a clearly marked exit from anywhere. Undo and cancel.
4. **Consistency and standards** — internal consistency and platform convention.
5. **Error prevention** — better than good error messages. Constrain input, confirm destructive
   acts, disable what can't work (and say why).
6. **Recognition over recall** — options visible; no memory tests between screens.
7. **Flexibility and efficiency** — accelerators for experts that don't burden novices:
   shortcuts, bulk actions, saved views.
8. **Aesthetic and minimalist design** — every extra element competes with the essential ones.
   This means *remove noise*, not *remove function*.
9. **Help users recognise, diagnose and recover from errors** — plain language, specific cause,
   concrete next step.
10. **Help and documentation** — findable, task-oriented, in context.

---

## 5. Error handling

The most-skipped and most-revealing part of an interface.

### Prevent first

- Constrain input so invalid states are unreachable — pickers over free text, correct input
  types, sensible ranges.
- **Disable actions that can't succeed, and always say why.** A disabled button with no
  explanation is a dead end; a tooltip or inline note turns it into guidance.
- Confirm destructive and irreversible actions — and make the confirmation *specific* ("Delete 3
  projects and 47 files?"), because generic confirmations get reflex-clicked.
- **Prefer undo to confirmation.** Let the action happen instantly with a 5–10s undo. Faster for
  the 99% who meant it, and safer for the 1% who didn't.

### When it fails anyway

A good error message has three parts:

```
What happened   "Couldn't save your changes."
Why             "The connection dropped."
What to do      "We kept a local copy — [Retry] or [Download draft]."
```

Rules:
- **In place, next to the thing that failed.** A form error at the top of the page for a field at
  the bottom is a scavenger hunt. Do both: a summary that links to fields, *and* inline messages.
- **Plain language.** No codes as the primary message (include one for support, secondary).
- **Never blame the user.** "Invalid input" → "Enter a date in the future."
- **Preserve their work. Always.** Losing typed input to a validation failure is the single most
  enraging thing an interface can do.
- **Validate at the right moment** — on blur, not on every keystroke (which shouts at people
  mid-typing), and never only on submit. Re-validate a corrected field immediately so they see
  the fix land.

---

## 6. The states nobody designs

Every data-driven view has at least six states. Shipping only the third is the most common
production bug in interface work.

| State | What it must do |
|---|---|
| **Empty (first use)** | Explain what will appear here and how to create the first one. This is prime onboarding real estate, not a sad "No data." |
| **Empty (no results)** | Distinguish "nothing matches your filter" from "nothing exists". Offer to clear filters. |
| **Loading** | Skeletons that match the eventual layout beat spinners — no layout shift, and perceived as faster. |
| **Partial** | Some loaded, some pending, some failed. Common in real systems, rarely designed. |
| **Error** | What failed, why, retry. Keep whatever did load. |
| **Ideal / full** | The happy path everyone designs. |
| **Overflow** | 10,000 items, a 200-character name, a missing avatar, an untranslated string. |

---

## 7. Forms

Forms are where UX quality is most visible and most often bad.

- **One column.** Multi-column forms cause skipped fields and ambiguous reading order. Exception:
  genuinely paired short fields (city/postcode, expiry/CVC).
- **Labels above fields**, always visible. Placeholder-as-label is an accessibility failure and a
  memory test — it vanishes exactly when the user needs it.
- **Ask for less.** Every field costs completion rate. Justify each one; delete or defer the rest.
- **Mark optional fields, not required ones** — if most are required, the asterisks are noise.
- **Group related fields** with spacing (see the proximity rule) and give groups headings.
- **Right input type and autocomplete hints** — correct mobile keyboard, browser autofill,
  one-time-code fields.
- **Format tolerantly, display strictly.** Accept the card number with spaces; show it formatted.
- **Never split a value into multiple inputs** (three date boxes, four card-number boxes) — it
  breaks paste, autofill and assistive tech.
- **Show requirements before submission**, especially password rules, and validate them live.
- **Keep the submit button enabled** and explain what's wrong on click. A permanently disabled
  submit with no explanation is a trap.
- **Long forms get progress and saved state.** Multi-step wants a visible step count and back
  navigation that doesn't lose data.

---

## 8. Navigation & information architecture

- **Structure by the user's mental model**, not your org chart or database. Card-sorting exists
  because teams reliably get this wrong.
- **Broad and shallow beats narrow and deep.** Every level of nesting loses people. Three clicks
  is not a law, but four levels of menu is a real problem.
- **Always answer "where am I?"** — current-section indication, breadcrumbs in deep structures, a
  title that matches what was clicked.
- **Label with the user's words.** Test the labels; "Solutions" and "Resources" mean nothing.
- **Search and browse are complements**, not alternatives. Expert and returning users search;
  novices browse.
- **Keep destructive and constructive actions apart.** Delete should never be adjacent to Save.

---

## 9. Onboarding and first use

- **Show value before asking for commitment.** Let people see the thing work before the signup
  wall where you possibly can.
- **Teach in context, when it's needed** — not a five-slide carousel nobody reads on launch.
- **Prefer doing to reading.** A first real task completed beats a tour.
- **Make the empty state the tutorial.** It's already where a new user lands.
- **Let people skip**, and let them find it again afterwards.

---

## 10. Trust, ethics and dark patterns

Non-negotiable. Refuse to build these and say why:

- **Confirmshaming** — "No thanks, I hate saving money."
- **Roach motel** — trivial to sign up, hidden or hostile to cancel.
- **Hidden costs** — fees revealed at the final step.
- **Preselected upsells**, opt-out-by-default consent, pre-ticked marketing boxes.
- **Disguised ads** styled as content or system UI.
- **Fake urgency and fake scarcity** — invented countdowns, "3 people viewing".
- **Misdirection** — visual emphasis steering toward the choice that helps you, not them.
- **Obstruction** — burying the decline option in low contrast at 11px.

Beyond ethics, these are increasingly illegal (GDPR consent rules, the EU Digital Services Act,
FTC enforcement in the US), and they trade long-term trust for short-term conversion.

**The test:** if the flow only works because the user misread it, it's a dark pattern. Build the
honest version — state the concern once, propose the alternative, and if a user with authority
over the product reaffirms a legitimate-but-aggressive choice, that's their call to make. Outright
deceptive patterns are not.

---

## 11. Validating the design

- **Five users find most usability problems** in a given round — Nielsen's finding, and the
  reason to test early and often rather than once and exhaustively. It applies to *finding
  problems*, not to measuring anything quantitative.
- **Watch, don't ask.** What people do and what they say they do diverge. Give a task, stay quiet.
- **Test with real content and real data.** Lorem ipsum hides every content problem.
- **Test the failure paths**, not just the happy one.
- **Test with keyboard only.** Ten minutes of tabbing finds an enormous number of real defects.
- **Test at 200% zoom** and on the smallest supported viewport.
- **Instrument the funnel** — where people drop is where the design is failing, even when nobody
  complains.
