# Handoff template

The one artifact type for all cross-role communication (spec §6). Copy this file,
fill every frontmatter field, write the six body sections. Filename:
`YYYY-MM-DD-<packet>-<slug>.md`, kebab-case, no `#`, `^`, `|`, `[`, `]`.

Write it to **your own role's `outbox/`** — the orchestrator routes it. The one
exception: `type: failure` goes straight to the orchestrator's inbox
(`docs/agents/inbox/`).

Status lifecycle: `draft` (you are still writing) → `ready` (in your outbox, complete)
→ `routed` (the orchestrator has `git mv`'d it to the recipient's inbox) → `consumed`
(the recipient has read and acted on it; the orchestrator archives it next pass).

Two states sit outside that line and are set by the **orchestrator only**:

- **`parked`** — authored, correct, and deliberately not running. A packet whose `env:`
  this session cannot satisfy, or that is blocked on an upstream artifact or on Talon.
  A parked packet must be cold-start-ready: someone picking it up months later gets
  everything from the file. It must also carry `blocked-by:` saying what would unpark it.
  `parked` is not a lie the prose corrects — never stamp a parked packet `routed`.
- **`superseded`** — the artifact was correct when written and has since been invalidated
  by a later one. It keeps `superseded-by:` pointing at what replaced it. See
  "Supersession" below; this is the state that prevents wrong builds.

---

```yaml
---
type: handoff            # handoff | report | failure
from: programming        # your role
to: verification         # a role, or "orchestrator"
packet: WP-<id>
status: ready            # draft | ready | routed | consumed | parked | superseded
env: cloud               # cloud | local-gpu | local-talon — see "The env field" below
branch: feat/<...>
date: 2026-08-11
# --- conditional fields: include only when they apply ---
superseded-by: docs/agents/…/<file>.md   # REQUIRED when status: superseded
blocked-by:                              # REQUIRED when status: parked
  - "SD-1's spec landing"
  - "Talon: canon fact 8 persistence fork"
issue: "#NNN"            # OPTIONAL — omit entirely when the work has no Issue
---
```

## The `env` field — one definition, no second reading

**`env:` describes the execution requirement of the WORK, and it is set on the dispatch
packet by the orchestrator.** It answers "what does a session need in order to *do* this?"
— not "what did the last session happen to have," and not "what will someone downstream
need."

- **A report inherits its packet's `env:` or omits the field entirely.** A worker never
  invents one. If your work's real requirement turned out to differ from your packet's
  `env:`, that is a finding for your report body and a correction for the orchestrator to
  make — not a frontmatter edit you make on your own authority.
- `env:` on a *packet* is binding: a `cloud` session must not execute a `local-gpu` packet.

*Why this is spelled out:* on 2026-08-11 a programming report was stamped `env: local-gpu`
with no justification for entirely headless work, because the field's referent was
undefined and "the environment the next step might need" is a defensible reading of an
undefined field. The orchestrator caught it at the routing hop. Defining the referent is
cheaper than catching it every time.

## Supersession — the rule that prevents wrong builds

When a role's handoff **corrects** the packet that produced it — a value overturned, a
model replaced, an assumption refuted — every downstream artifact still asserting the old
answer is now wrong, and nothing about it *looks* wrong. Two documents sitting in one
inbox contradicting each other, with no stated precedence, is how a fresh-context worker
builds the wrong thing while following instructions perfectly.

So, as a hard rule (README, "Supersession"):

1. The **orchestrator** re-stamps every downstream packet a routed correction invalidates,
   **before dispatching anything further**. Never the worker; never after the fact.
2. Re-stamping = `status: superseded` + `superseded-by: <path>` + a one-line
   `> **SUPERSEDED:** …` banner at the top of the body saying what changed and why.
3. **Written precedence: the newest orchestrator-issued artifact wins.** When a packet and
   a routed handoff disagree, the later one is right. State it in the re-issued packet
   rather than leaving a reader to infer it.
4. A superseded packet is never deleted. It is the record of what was believed and when.

## Current state

What is true right now: what exists on the branch, what is built, what is not.
Written so the recipient needs no other document to orient.

## Corrections to the packet

**Anything your packet asserted that turned out to be wrong** — a value, a model, an
assumption about the codebase — with the measurement that refutes it. Empty is a fine
answer; write "None." rather than dropping the heading.

This section exists so the orchestrator can detect a correction mechanically instead of
inferring it from prose. Everything listed here triggers the supersession rule above, so a
correction buried in Decisions instead of here is a correction that will not be routed.

## Decisions made (with why)

Every call you made that was not spelled out in the packet, each with its reasoning.
A value you picked ("ambiguity about a value is yours") is listed here; a direction
you were tempted to pick is not — that belongs in Open questions. A packet assertion you
*overturned* is not here either — that is Corrections, above.

## Open questions

Forks and blanks you did NOT resolve, stated ripe: the trigger that makes each one
answerable, the options, and their tradeoffs. Never resolve a `[BLANK — Talon]` here.

## Verification evidence

What was run and what it showed — suite results (raw counts, not just verdicts),
captures, JSONL, before/after measurements. State explicitly what is UNVERIFIED:
reasoned rather than measured.

## Read first

The ordered list of files the next agent must read before acting, and why each one
is on the list.
