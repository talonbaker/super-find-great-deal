# Dispatch template

THE dispatch template — supersedes `docs/dispatch-prompt-template.md` and
`docs/superpowers/dispatch/AGENT-DISPATCH-PROMPT.md` (both kept, both bannered).
The orchestrator fills the `«BRACKETED»` fields per packet and sends the block below
as the dispatched agent's prompt. Environment gotchas are **not** pasted here: they live
in `.claude/rules/*.md` and the template **cites them by path**, once. (The auto-load
those files were meant to get is unverified on this harness — see
[`../README.md`](../README.md), "Guardrail rules". Citation by path is the mechanism you
can rely on, so cite them; do not paste their contents, or the packet becomes a second
stale copy of a rule.)

## The role token — never write it into the prompt

Each `ROLE.md` carries a **Role token**: a short string that appears in that file and
nowhere else in the repo. The dispatched agent must quote it in its first output line.

**The orchestrator does not paste the token into the dispatch prompt.** That is the whole
point — a token the prompt supplies is one the agent can echo without ever opening
`ROLE.md`, which is exactly the hole the old first-line check had (it could not distinguish
a loaded preamble from an echoed prompt line). Write
`TOKEN: «the token printed in your ROLE.md»` and leave it unfilled; verify the returned
value yourself by reading the role file.

A wrong or missing token means the preamble did not load: kill and redispatch.

## Packet freshness — check before you send

Before dispatching, confirm this packet has not been invalidated by a routed correction
(handoff template, "Supersession"). If an upstream role overturned something this packet
asserts, **re-stamp the packet first** — `status: superseded`, `superseded-by:`, a banner —
and dispatch the re-issued one. Never send a packet whose inbox already holds a document
contradicting it: that is a wrong build waiting to happen, and it will not look like a bug.

Two contradictions between the predecessors, resolved (spec §13.1):

- **PR or no PR is a per-packet field.** `pr: open` means push the branch and open a
  PR per the End state section; `pr: none` means commit and stop — the orchestrator
  integrates. Neither is the default; the orchestrator sets it every time.
- **Agents CAN write `.md` files.** The old "your harness blocks report files" claim
  is false and dead. Reports are written to the role's `outbox/` as real files using
  the [handoff template](handoff.md).

---

```
ROLE FIRST: read `docs/agents/roles/«ROLE»/ROLE.md`, then the packet file
`docs/agents/roles/«ROLE»/inbox/«PACKET-FILE»`. Your first output line MUST be:
ROLE: «ROLE» — PACKET: «PACKET-ID» — TOKEN: «the token printed in your ROLE.md»
Read the token out of your ROLE.md — it is deliberately not written anywhere in this
prompt. A missing or wrong first line means your preamble did not load and your session
will be killed and redispatched.

You are working on the Sail repo (Godot 4.7, C#/mono, Windows). Read `/CLAUDE.md` in
full — the Canon block is the current direction and the bible check is part of "done".

If any document in your inbox is `status: superseded`, it is history: read it for context,
never build from it. **Where two documents disagree, the newest orchestrator-issued one
wins.** If you find a contradiction that this rule does not settle, STOP and file a failure
report — do not pick one and build on it.

## Branch — with the stale-base check

**Pick ONE of the two shapes below; the orchestrator deletes the other before sending.**

**(a) Own branch — the default for a standalone packet.**
git fetch origin, then create «NEW-BRANCH» from «BASE-BRANCH» (currently «SHA»).
**Verify your base is not stale before starting:** `git log --oneline -1` must show
«SHA» or later; «list any files that must exist right after checkout as a base sanity
check». If the base is stale or a listed file is missing, STOP and file a failure
report — wrong base, don't guess past it.

**(b) Shared branch — sequential packets on one feature, each needing the last one's
files.** (SD → PROG → VER is the standard case; a branch each would mean cross-branch
cherry-picking for no gain.)
**This packet does NOT create a branch.** Work in the existing worktree at «WORKTREE-PATH»,
already on «SHARED-BRANCH» (based on «BASE-BRANCH» at «SHA», with «WHICH UPSTREAM PACKETS»
committed on top). All relative paths in this prompt are relative to that worktree.
**Verify before starting:** `git -C «WORKTREE-PATH» branch --show-current` must print
«SHARED-BRANCH», and «list the upstream artifacts that must already exist — their absence
means the upstream packet did not land». If either check fails, STOP and file a failure
report.
Set `pr:` on the LAST packet in the chain only; every earlier one is `pr: none`.

**Two traps when writing that "must already exist" list — both hit VER-2 on 2026-08-11,
and both would have STOPPED a verifier who read the packet strictly.**

- **Never point a precondition at the sender's `outbox/`.** Routing is a `git mv` from
  outbox to inbox, so by the time the recipient runs, the outbox is *empty by design* — the
  precondition is falsified by the very act that made the packet ready. Point at the path
  the file will have **after** routing: the recipient's own `inbox/`.
- **Never require a fully clean tree in a worktree that has run the suite.** Godot rewrites
  `.import` files on every run and emits `.uid` files for new scripts, so `git status
  --porcelain` is never empty there. Scope the check to what actually matters — no
  uncommitted changes outside generated files — and name the generated extensions
  explicitly (`.import`, `.uid`), because a filter that names only one of them still fails.
  See `.claude/rules/imports-and-encoding.md`: that churn is not the recipient's to clean.

The general rule behind both: **a precondition must be satisfiable at the moment the
recipient reads it, not at the moment you wrote it.** Write it against the post-routing
world.

Either shape: you are in a worktree — never `git checkout` in `C:/repos/Sail`, and
re-verify `git branch --show-current` before every commit (sibling sessions share this
machine).

«Note any in-flight PRs that might land under this branch, and whether to wait or
proceed — don't leave it implicit.»

## The ask

«Talon's own words, verbatim and in full, as a quote — the adjectives carry the
design. Then context: what exists today, what is wrong with it, what "done" looks
like, with acceptance criteria explicit enough that you never have to guess or ask.»

## What you own

YOURS: «files and directories this packet may edit — must sit inside the role's
allowed write paths from its ROLE.md»
NOT YOURS: «files other packets own, or out of scope — name them»

Read anything freely. Edit only what is yours. Writing outside your ROLE.md's allowed
paths is never self-corrected: stop and file a failure report to the orchestrator's
inbox (`docs/agents/inbox/`).

## Scope — «N items»

1. «Item: precise instruction, citing the doc/spec that specifies WHAT. Expected
   before/after stated explicitly.»
2. «...»

### Explicitly out of scope

«The things a reasonable agent might be tempted to also fix while in this code — give
scope creep a clear line not to cross, and cite where each deferred item IS tracked so
it reads as sequenced-but-later, not silently dropped.»

## Acceptance criteria — «N», numbered

«The checkable list. This section is MANDATORY on every packet: `docs/agents/roles/verification/ROLE.md`
says outright that acceptance criteria "arrive in the packet," so a packet without them
hands the verifier nothing to verify against and it will improvise its own — which is the
verifier quietly becoming the designer.

Write each one so a third party who never saw this conversation can rule PASS / FAIL /
UNVERIFIABLE on evidence. "Works correctly" is not a criterion. "Burnout fires exactly
once per stick across a long tick stream — tested" is.

Mark any criterion that is an ABSENCE check ("no renderer was added", "no persistence
hook exists") — the verification role owes a positive control on each of those, and it can
only know to provide one if the packet says which they are.

Restate this list verbatim in the verification packet, so that packet is self-contained.»

1. «…»
2. «…»

## Rules

- Hard invariants live in `.claude/rules/`, cited here by path — read the ones that touch
  your work: `godot-scenes.md` (authored-scene and project-file traps),
  `test-suite.md` (suite discipline),
  `imports-and-encoding.md` (.import churn, UTF-8, staging). They bind whether or not you
  read them. **Do not assume they were injected into your context — open the ones that
  apply.**
- **Run long commands (the test suite especially) in the FOREGROUND and block on
  them.** Dispatched subagents never receive background-task notifications in this
  environment — an agent that launches the suite in the background and waits to be
  woken simply ends its session mid-task. If a foreground call times out, re-invoke
  it or poll synchronously; never stop and wait.
- **"The full suite" is TWO commands, and neither one alone is the full suite:**
  ```
  powershell -File tests/Run-AllTests.ps1        # the scene/integration suites. PowerShell
                                                 # 5.1 — `pwsh` is NOT installed.
  dotnet test tests/unit/SailNet.Tests.csproj    # the xUnit suite
  ```
  **No count is written here on purpose.** This template carried "37 scene/integration suites"
  and "362 as of 2026-08-11" until 2026-08-28, when the runner was printing **66** and xUnit
  **2787** — a template that pins a total is a second copy of `.claude/rules/test-suite.md` that
  goes stale exactly the way its own flake-list rule two bullets down warns about. **Count the
  result lines your own run emits and quote that**; the current baseline lives in
  `.claude/rules/test-suite.md`'s end-of-file block and nowhere else.
  `Run-AllTests.ps1` does **not** run `dotnet test` — its `Invoke-BuildAndImport` only calls
  `dotnet build`. A packet that says "full suite" and runs only the runner silently skips
  every xUnit test. Run both, before every commit, never filtered, and report RAW COUNTS
  from each rather than a verdict.
- **A red is not automatically a regression, and green is not automatically clean.** The
  scene suite is not deterministic on this machine. Discriminate per
  `.claude/rules/test-suite.md`: re-run standalone, compare the RAW measured quantity
  across runs, and check the base commit. Do not paste a flake list into this packet — the
  rule file is the single source and it has been wrong before; if you find it wrong again,
  that is a finding for your report.
- **The bible check is part of "done"** — state the three lines (`Bibles applied:` /
  `Items checked:` / `Result:`); if no bible applies, say so and say why.
- **Don't invent answers.** A fork about a VALUE is yours — pick sensibly, state it,
  move on. A fork about a DIRECTION goes in your report's Open questions, ripe
  (trigger, options, tradeoffs), and you do not build on a guess.
- «Packet-specific stop conditions — e.g. "any suite red that the base commit passes:
  STOP and report exactly what failed."»

## End state

pr: «open | none»   ← set by the orchestrator, every packet, no default

- **COMMIT YOUR WORK** on «NEW-BRANCH» — do not leave it uncommitted, even on
  failure; an unregistered worktree makes uncommitted work look lost.
- If `pr: open`: push the branch and open a PR to «BASE-BRANCH» titled «convention»;
  body = scope items with evidence, deferred items named, suite result.
- If `pr: none`: do not push or open anything — the orchestrator integrates.
- Write your report to `docs/agents/roles/«ROLE»/outbox/` using
  `docs/agents/templates/handoff.md` (type: report). Include verification evidence
  with raw numbers, and an explicit UNVERIFIED section for what you reasoned rather
  than measured.
- **Fill the report's `## Corrections to the packet` section, or write "None."** If this
  packet asserted something that turned out to be wrong — a value, a model, an assumption
  about the codebase — it goes there, with the measurement that refutes it. Not in
  Decisions: the orchestrator reads that heading specifically to decide what downstream
  packets must be re-stamped, and a correction filed elsewhere will not be routed.
- **State each acceptance criterion above and where its evidence is.** The verifier should
  not have to hunt.
- Final message: Outcome (Done / Ready for review / Blocked), the report path, the
  PR number if any, and anything not completed and why. **Hard cap 15 lines** — the
  detail lives in the report file; a long final message is paid for again in every
  later orchestrator tool call (ratified 2026-08-14, see
  `docs/agents/ORCHESTRATION-RUNTIME.md`).

## The standard

The recurring failure in this repo is features built exactly as literally requested
with the common-sense implications left unhandled. The best agents here checked
whether the thing they believed was actually true — and when the brief was wrong,
said so. When you state a number, have measured it. When you cannot measure, say so.
```
