# CANON — what this repository is

**This repository is an MVP about movement, flow, feel and mechanics. It has no premise, and
none is to be inferred.** (Talon, 2026-09-02.)

---

## Read this before citing this file at anyone

This file used to carry a setting — cavemen, fire, a camp, a Pot, mushroom pockets. **All of it
is gone.** It was always out of date, because the premise churns faster than any document can
track, and a stale premise in a file marked "source of truth" is worse than no premise at all:
agents quoted it back at Talon and treated his own newer direction as the thing that was wrong.

So the rule is now simple, and it is the whole reason this file still exists:

- **There is no setting, no player noun, no story and no threat in this repo.** Do not
  invent one, do not carry one over from `Sail`, and do not treat a leftover word in an old
  document, an identifier or a test fixture as evidence of one.
- **The level has exactly one mechanical objective: collect all the bubbles** (Talon,
  2026-09-04). It is stated once when the player enters the level and restated in HOW TO PLAY;
  the string lives in `PhaseToastText.BubbleGoalToast` and nowhere else. **It is an objective, not
  a premise** — there is nothing it means, nobody it is for, no timer, no fail state and no
  consequence for ignoring it, and no story may be inferred from it. This bullet read "and no
  goal" until that date, which is the rule above working exactly as written: his direction moved
  and the document was what needed fixing.
- **Never tell Talon that something "contradicts canon."** If his direction and a document
  disagree, the document is what is wrong. Update it, or say nothing.
- **If a task genuinely cannot proceed without a premise decision, stop and ask him** — one plain
  question. Do not guess and then build on the guess.

A premise will exist again one day. When it does, it lands here and nowhere else — that much of
the old one-place rule was right, and is retained.

## What the MVP is about

Movement, flow, feel, and the mechanics underneath them. Concretely, what this build actually
does: a networked avatar you move, jump, sprint and throw with; carrying and props; a level to
move through; water, night and light as things movement happens *in*; and the netcode, voice and
app shell that let several people do it together.

**Judge work against that, not against a story.** "Does this feel good to move through" and "is
this mechanically sound" are the questions. If a proposal only makes sense once you assume a
setting, it is out of scope for this repo right now.

## §0 — Durable. Survives every premise pivot.

Engineering and process tenets, not setting. That is why they survived the cut.

**0.1 — Follow modern industry standards.** Not novelty, not cleverness, not a bespoke solution
where a standard one exists.

**0.2 — Performance and security beat anything flashy or unstable.** Every time. A feature that
costs frame time or introduces instability loses to one that does not.

**0.3 — The cheap or simple option wins** over anything needing lots of wheels and context menus.
Mechanisms with many knobs lose to mechanisms with few.

**0.4 — Simplicity as a tiebreaker only.** The full order is **performance → reliability/safety →
modern industry standards → simplicity**. Simplicity breaks ties when everything above it is
genuinely equal. *"No more no less."* (Talon, 2026-08-21.)

**0.5 — The premise will change.** This is the only certainty, and the corpus is built to absorb
it rather than resist it. As of 2026-09-02 there is no premise at all, which is the cleanest
possible expression of this rule.

**0.6 — No rule is beyond Talon's arbitrary change, and he needs no reason.** *"Nothing should be
so firm a rule that I can't arbitrarily change it at will for any reason even if it breaks the
established rule."* (2026-08-21.) Two clarifications, both load-bearing:
- **It licenses Talon, never an agent.** An agent never self-authorises a break, and never argues
  the rule back at him — it updates the document.
- **It applies to §0 itself.**

**0.7 — No gore, ever.** Standing law, not repealed. Death exists and is fine; viscera is not.

**0.8 — The clip test.** Every mechanic must survive a streamer's out-of-context clip. A beat that
fails this is not tuned down; it is not built.

**0.9 — The parity law.** Gameplay-relevant world state is server data, identical on every client.
Rendered presentation is presentation only. This one is a live code invariant as well as a
principle — see `.claude/rules/` and `AvatarMotor.Step`.

---

## Retired — do not build toward any of these

The whole of the former §1 direction, deleted 2026-09-02: the far-future post-collapse setting,
cavemen and every other player noun, the mushroom hook, the Pot and its quota loop *as story*,
anomaly pockets, the seeing stone, fire and the dark as premise, and the day/night rehearsal law
that depended on all of them. Earlier retirements went before it — the camp, the sleepover, the
house, the hand, the camcorder, Bigfoot, prey-animal species identity, the `-uffling` roster,
"quarters" as a currency name, and children/campers as player characters.

`Sail` remains the historical record for all of it. Read it there; never build from it here.
