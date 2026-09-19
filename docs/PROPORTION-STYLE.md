# Sail — Proportion & Stylization Style

> **Sibling to `docs/ART-BIBLE.md`.** `ART-BIBLE.md` governs **color and shading**.
> This doc governs **exaggeration** — how far proportions push from anatomical.
> They do not overlap; neither one is the other's authority.

**Status: NOT YET LOCKED.** Every dial below reads `TBD`. This document exists so that
`/spec-creature`'s §9 Style Profile block has somewhere to point, and so the discovery
process is written down before it runs rather than after.

---

## Why this is a separate axis

`/spec-creature` de-biases **structure** — stopping a generator from snapping a concept
back to the nearest real species. That is per-creature work.

Stylization is different. Whether Sail's creatures have big heads on small bodies, or
huge eyes, or stubby limbs, is **one decision for the whole game**. Making it
per-creature produces a bestiary that looks assembled rather than authored. It stays
`TBD` until deliberately discovered, and once locked, every creature inherits it unless
a specific creature deliberately overrides.

`ART-BIBLE.md` §6 already gestures at this — "chunky, stylized, silhouette-first; big
head/feature, small limbs" — but as prose direction, not as numbers a spec can carry.
This doc's job is to turn that prose into dials.

## The dials

| Dial | Status | Value | Notes |
|---|---|---|---|
| Head-to-body ratio | `TBD` | — | ART-BIBLE §6 leans "big head/feature" |
| Limb length — arms | `TBD` | — | independent of legs |
| Limb length — legs | `TBD` | — | independent of arms |
| Limb thickness | `TBD` | — | |
| Torso-to-limb ratio | `TBD` | — | |
| Eye size relative to head | `TBD` | — | ART-BIBLE §6: expressiveness is body language, not facial rigging |

## Discovery process (run this to move dials from TBD to locked)

Not a design meeting — a generation bake-off. It rides along on the free-tier/local
validation phase of `/generate-asset`, since it exercises the same pipeline mechanics.

1. Pick **one placeholder creature concept** and run it through `/spec-creature`.
2. Produce **N copies of that spec**, each with a *different explicit proportion
   profile* filled into §9 — small legs/big arms; big head/small torso; big eyes/small
   eyes; and so on. These are **deliberate dials, not accidental variation**. Write the
   numbers down before generating.
3. Run each through `/compile-spec` → `/generate-asset`.
4. Put the low-poly results **side by side** and pick what reads best at gameplay
   camera distance, in solid black, against Sail's actual checker floor.
5. Whatever wins becomes the locked table above.
6. Every future creature's §9 defaults to it. Overrides are allowed but must be written
   down as overrides.

## Scope note

**Current bite:** none — this doc is a placeholder with a written procedure, and no
creature is currently governed by it. **First real test:** the bake-off in the
discovery process above. **What passing looks like:** the table has locked numbers, at
least two creatures were built against them, and they read as siblings.
