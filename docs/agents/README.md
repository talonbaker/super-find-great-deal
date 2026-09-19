# docs/agents/ — the file-based agent workflow

Authority: `docs/superpowers/specs/2026-08-10-agentic-workflow-structure-design.md`
(ratified by Talon, 2026-08-10). This README is the system map; the spec's numbered
sections are the detail behind every rule here.

Everything in this directory is plain markdown, git-versioned, and Obsidian-compatible.
Role identity is **dispatch-loaded** (a dispatched agent's prompt starts by reading its
`ROLE.md`), hard invariants are **path-scoped** in `.claude/rules/`, and all cross-role
context moves as **handoff files** through inboxes and outboxes.

## Layout

```
docs/agents/
  README.md                 # this file — the system map
  ORCHESTRATOR.md           # orchestrator preamble (the sixth role)
  SKILLS.md                 # index of this repo's skills
  inbox/                    # the orchestrator's inbox: failure reports, routed outbox items
  templates/
    dispatch.md             # THE dispatch template (supersedes both predecessors)
    handoff.md              # handoff/report template with frontmatter schema
  roles/
    systems-design/     ROLE.md  inbox/  outbox/
    programming/        ROLE.md  inbox/  outbox/
    environment/        ROLE.md  inbox/  outbox/
    art-atmosphere/     ROLE.md  inbox/  outbox/
    verification/       ROLE.md  inbox/  outbox/
  archive/                  # consumed packets + the consolidation audit
```

Bibles stay in `docs/` — role files **cite** them by relative link, never copy them.
One source of truth.

## Modes: just talking, exploring, tasking

Three modes, narrowest first. Only Talon moves between them; nothing promotes a mode by
inference.

**"Just talking"** (`~/.claude/CLAUDE.md` §10, added 2026-08-11). Entered when Talon says
**"just talking."** Conversation only: **no skills invoked, no personas loaded, no files
written, nothing dispatched, nothing tracked.** Think with him, answer, push back, riff.
This is an explicit standing instruction and it **overrides any skill-invocation mandate**,
including a skill whose own description claims it must run before any response. It ends
only when he explicitly asks for action — "task it out," "do it," "write that down." **A
question from Talon is never automatically a task**, and in this mode it is never one at
all.

**Exploring — the default.** Conversation, brainstorming, and direction-testing produce
no dispatches and no files beyond notes Talon asks for. The difference from just-talking
is narrow but real: exploring may read the repo, invoke a skill, and write a note when
asked; just-talking may not.

**Tasking.** The explicit flip is Talon saying **"task it out."** Breakdown → packets →
dispatched roles → verification, per [`ORCHESTRATOR.md`](ORCHESTRATOR.md). Ambiguous
phrasing is treated as exploring; the orchestrator may ask "task it out?" but never
assumes.

## Human gates

The pipeline never cascades a bad result past one of these:

1. **After breakdown** — the packet list goes to Talon (as a packet-list file, or an
   Issue set where a project uses GitHub) before any dispatch.
2. **After merge** — verification's pass is required before the result is queued for
   Talon's review; interactive-feel items are flagged for his next hands-on session.
   Review is **unscheduled** (`~/.claude/CLAUDE.md` §5, revised 2026-08-10): flag it,
   park it cleanly, never block on him.
3. **Always** — destructive operations, scope changes, genuine design forks (per the
   ripeness law in `../DESIGN-BIBLE.md`: surfaced with the trigger, never unripe).

## Handoff routing

One artifact type, one template: [`templates/handoff.md`](templates/handoff.md).
Filenames: `YYYY-MM-DD-<packet>-<slug>.md`, kebab-case.

- A role writes to **its own `outbox/` only**. No side channels — a role never writes
  into another role's inbox.
- The **orchestrator routes**: `git mv` from the sender's outbox into the recipient's
  `inbox/`, flipping `status: routed`. Every hop has one owner and a git history.
- The recipient flips a packet it has read to `status: consumed`; the orchestrator
  archives consumed packets (`git mv` to `archive/`) on its next pass, keeping inboxes
  readable at a glance.
- **Failure reports** (`type: failure`) are the one exception: they go straight to the
  orchestrator's inbox — `docs/agents/inbox/`, the one top-level inbox. A failure is
  never self-corrected across role boundaries: the orchestrator decides block / route
  around / escalate to Talon.
- Sub-delegation is allowed (a role agent may spawn its own subagents), but all
  cross-role communication still goes through handoff files and the orchestrator.

## Supersession — the rule that prevents wrong builds

A worker doing its job well will sometimes prove its own packet wrong. When it does, every
downstream packet still asserting the old answer is now wrong **and nothing about it looks
wrong** — it is a well-formed document in the right inbox. Two documents in one inbox
contradicting each other, with no stated precedence, is how a fresh-context worker builds
the wrong thing while following instructions perfectly.

So:

1. Workers declare corrections in their handoff's `## Corrections to the packet` section
   (never buried in Decisions — the orchestrator reads that heading specifically).
2. **The orchestrator MUST re-stamp every downstream packet a routed correction
   invalidates, BEFORE dispatching anything further.** Re-stamping is
   `status: superseded` + `superseded-by: <path>` + a `> **SUPERSEDED:** …` banner saying
   what changed. Never the worker's job; never done after the next dispatch.
3. **Written precedence: the newest orchestrator-issued artifact wins.** A packet and a
   routed handoff that disagree resolve in favour of the later one. Say so in the re-issued
   packet rather than leaving a reader to infer it.
4. Superseded artifacts are never deleted — they are the record of what was believed, and
   when.

*Earned 2026-08-11:* SD-1 overturned two values from its packet; PROG-1's inbox then held a
packet saying "carried prop, 480 s" and a spec saying "per-peer bank, 300 s," with nothing
stating which won. Only a hand-written paragraph in the dispatch prompt resolved it. A loop
re-dispatching from files alone would have had no way through.

## Environment routing

**`env:` describes the execution requirement of the WORK.** It answers "what does a session
need in order to *do* this?" — not "what did the last session have," and not "what will
someone downstream need." The orchestrator sets it on the **dispatch packet**, at breakdown.
**Reports inherit their packet's value or omit the field**; a worker never invents one, and
a mismatch between the packet's `env:` and the work's real requirement is a finding for the
report body, not a frontmatter edit made on the worker's own authority.

- **`cloud`** — verifiable by build/test/logic assertions alone: compile, headless
  suites, simulation, spec/review/doc work. Default when no pixels need judging.
- **`local-gpu`** — someone must see rendered output: headed captures, lightmaps,
  atmosphere/VFX judgment. Never concurrent with another headed run (GPU-contention
  gotcha — see `.claude/rules/test-suite.md`).
- **`local-talon`** — needs Talon's hands or eyes for *feel*: queued with repro steps
  for his next hands-on session, whenever that is.

Rule of thumb: if it can be fully verified by logic/test assertions with no one
inspecting rendered output, it is `cloud`; if judging it requires seeing pixels, it is
`local-*`.

A packet whose `env:` this session cannot satisfy is **parked**, not skipped: `status:
parked`, a `blocked-by:` list, and enough context in the file that someone picks it up cold
months later. See the handoff template's status lifecycle.

## Load mechanism and the silent-load check

Role identity loads explicitly: every dispatched agent's prompt begins "read
`docs/agents/roles/<role>/ROLE.md`, then the named inbox packet." Deterministic,
identical across worktrees, machines, cloud sandboxes, and non-Claude harnesses.

**Mandatory first-line check:** every role agent's first output line is
`ROLE: <name> — PACKET: <id> — TOKEN: <the role's token>`. A missing or wrong line means
the preamble did not load: kill and redispatch. The cheap habit for interactive sessions:
open with "confirm your role and current task."

**The token is what makes the check mean anything.** Each `ROLE.md` carries a **Role
token** that appears in that file and nowhere else in the repo, and the orchestrator
**never writes it into a dispatch prompt** — it verifies the returned value by reading the
role file. Without that, the check proved only that the prompt arrived: an agent could echo
the required line straight out of its instructions having never opened its preamble. (In
the 2026-08-11 shakedown all three workers demonstrably *had* read their ROLE.md, but the
evidence for that came from their behaviour, not from the check.)

## Guardrail rules — `.claude/rules/`

Hard invariants that must hold regardless of role or dispatch quality live in
`.claude/rules/*.md` with `paths:` frontmatter: `godot-scenes.md`,
`test-suite.md`, `imports-and-encoding.md`. **All four stay.** They are the repo's
most expensive lessons and they bind whether or not anyone read them.

**The auto-load is UNVERIFIED on this harness.** These files were designed to inject
themselves into any agent touching the paths they guard. Two probes have failed to observe
that happening — one during the 2026-08-11 shakedown, one independent — and
`.claude/settings.json` contains only a `permissions` block, with no hook implementing it.
**Both probes were confounded on the same condition:** the probing session's project-root
checkout was not the one containing `.claude/rules/`. So the mechanism is not disproven —
it is unobserved, and the confound is now known and reproducible.

Treat it this way until someone observes it firing in a session whose **project-root
checkout contains `.claude/rules/`**:

- **The mechanism you rely on is citation by path.** Dispatch prompts name the relevant
  rule files by path and tell the agent to open them. That demonstrably works.
- **Do not paste rule contents into a packet.** A pasted rule is a second copy that goes
  stale — the 2026-08-11 run shipped packets carrying a three-item flake list while the rule
  file carried four, and *neither list covered either failure that actually occurred*.
- **Do not assume an agent has the rules in context.** If a packet touches guarded files,
  say which rules apply and let the agent read them.

If you do observe the auto-load firing, record it here with the session conditions and
promote this section back.

## Multi-machine claim protocol — PROPOSAL, not implemented

Documented convention awaiting Talon's greenlight; there is **no `claims/` directory
yet** and nothing enforces this. (Spec §11.)

- Claiming a headed build = committing `docs/agents/claims/headed-<machine>.md`
  (machine name, branch, purpose, ISO timestamp, TTL — default 2 h). Pushing the commit
  *is* the claim; releasing = deleting the file and pushing.
- Another machine wanting the resource pulls first; a live claim means wait or route
  the packet elsewhere; a claim past TTL may be overridden by committing the
  replacement (the override commit records it).
- Race window: two machines claiming within one pull cycle both think they won locally —
  push rejection (non-fast-forward) is the arbiter; the loser rebases, sees the claim,
  backs off. Single-digit-seconds window, acceptable for a one-person fleet.
- The same file convention would later cover other exclusive resources (Steam uploads,
  Blender MCP) if needed. YAGNI until then.

## Obsidian conventions

The future vault root is the repo root; `.obsidian/` is already gitignored. Until the
vault exists, these keep every file compatible (spec §12):

- Kebab-case filenames; no `#`, `^`, `|`, `[`, `]` in names.
- YAML frontmatter (becomes Obsidian Properties).
- **Standard relative markdown links**, not `[[wikilinks]]` — valid on GitHub, in
  editors, and in Obsidian. When the vault is created, set *"Use [[Wikilinks]]" off*.

## Legacy handoff piles

Pre-existing handoff documents stay where they are; all **new** handoffs use the
inbox/outbox flow above. The legacy locations:

- `docs/superpowers/status/` — per-packet status/report directories.
- `docs/superpowers/handoffs/` — the short-lived dedicated handoff directory.
- `docs/superpowers/` (top level) — loose dated dispatch and handoff files.

The content-stale surfaces among them are catalogued in
[`archive/2026-08-10-consolidation-audit.md`](archive/2026-08-10-consolidation-audit.md)
— every disposition there is **PENDING TALON**; none is executed without his sign-off.
