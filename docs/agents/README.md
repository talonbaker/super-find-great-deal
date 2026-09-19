# docs/agents/ — packets, handoffs, and where they live

This repo's agent workflow is the one the Super Find Great Deal program runs on, and it is much
smaller than the five-role inbox/outbox system that came over from `Watis_Game` (deleted at the
fork, BASE-1 2026-09-19, because nothing here dispatches into it).

```
docs/agents/
  packets/<date>-<ID>.md    the packet a lane was dispatched with, committed by that lane
  handoffs/<date>-<ID>.md   what the lane did, committed on its branch before it is merged
  templates/
    dispatch.md             the dispatch template
    handoff.md              the handoff template and its frontmatter schema
```

**The shape.** Talon dispatches; one orchestrator session wrangles, reviews and verifies and
never implements; lanes are packets. A lane works on `feat/<date>-<id>`, commits its packet into
`packets/` and its handoff into `handoffs/`, pushes the branch, and stops. The orchestrator
merges.

**A handoff is for the next lane, not for the archive.** The things that are actually load-bearing
in one: raw suite counts (from the run's own `=== summary ===` block, never a verdict), the exact
commands a human uses to see the thing working, what was renamed or moved, and anything that
changed on the wire. `docs/PRUNE-BACKLOG.md` is where "I left this alone deliberately" goes, so a
handoff can point at it instead of repeating it.

The program plan every current packet descends from is
[`../design/2026-09-19-supermarket-mvp.md`](../design/2026-09-19-supermarket-mvp.md).
