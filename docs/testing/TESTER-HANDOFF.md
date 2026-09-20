# Testing-facilitator handoff — systems testing

date: 2026-08-13 (Sail) · carried into Watis World 2026-09-02 with the retired systems removed
authority: `docs/CANON.md` > this file. Talon's live word outranks both.

## Your job

You facilitate testing of the implemented systems with Talon. Two loops, run both:

1. **Structured testing loop.** Run the suites (`tests/Run-AllTests.ps1` registers every scene
   suite; `dotnet test tests/unit/SailNet.Tests.csproj` is the other half), record
   pass / fail / flake with evidence (log paths, exact failure strings), and keep the known
   defects current as findings land.
2. **Feedback loop.** Talon plays the build and gives you notes in plain conversational text —
   impressions, bugs, feel complaints, half-thoughts. Every note becomes a tracked actionable
   item. Nothing he says is allowed to evaporate. `docs/playtest/INTAKE.md` is the protocol:
   verbatim first, then causes, one packet per cause, re-gate on the combined tip, report in
   his numbering.

## The feedback protocol (the part that must not degrade)

- Keep the live ledger under `docs/playtest/` — one verbatim file per play session, created
  when the first note arrives, never edited to match a later understanding.
- Each note becomes an item with one of three triage classes:
  - **bug** — behaviour contradicts code intent or a bible. Fix it: branch off master, fix,
    full suite (BOTH commands), push, park. Never fix a finding that has an owner elsewhere;
    link it instead.
  - **tuning/feel** — a value judgment. Implement only if the note states the direction
    ("too slow" → faster, state the new value); otherwise write the smallest concrete
    question and ask it.
  - **design fork** — a genuine direction choice. NEVER resolve by inference. Park under
    "Needs Talon" with options and tradeoffs, move on.
- Work items while he plays; never block on him. Review is async — park finished work cleanly
  and continue.
- Questions: plain conversational text, ONE at a time, wait for the plain-text answer.
  **Never use a multi-choice popup or option list** (standing hard rule, 2026-08-12). Define
  any jargon your question uses.
- Canon binds testing too: no gore (canon §0.7) and the clip test (canon §0.8).

## How to run things

- **Play build:** export with `deploy/Export-WindowsClient.ps1`, or run the editor build with
  `Godot_v4.7-stable_mono_win64_console.exe --path .`. Hosting is Steam-gated (Steam must be
  running); ENet join is menu-reachable via the Direct field, but ENet *hosting* is CLI-only
  (`-- --server --port 7777`). `--cycle-period` is the only night-length control.
- **Full suite = TWO commands, always both** — see `.claude/rules/test-suite.md`.
- **Single suites:** `tests/Run-<Name>Test.ps1 -SkipBuild` after one build.
- **Bot captures:** `--capture-cam` is MANDATORY or you get a false pass (the follow camera
  frames the body, not the subject).
- Never launch a headed Godot instance while another agent is mid-verification (GPU contention
  produces garbage evidence). Headless suites serialize on the machine-wide mutex.

## Known traps (each has cost a full session)

- The scene suite is NOT deterministically green under load. Before blaming a change: re-run
  the failing suite standalone on an idle machine, then on a clean base, and compare the raw
  measured quantity, not the verdict.
- Modified `.import` / `.uid` files after an engine run are `core.autocrlf` churn with zero
  content diff. Do not revert them (reverting CREATES damage); never commit them with a fix.
- Exports target the **csproj**, never the .sln (MSB4126).
- Use the harness Read/Write/Edit tools for files, never PowerShell Get-Content/Set-Content
  round-trips (UTF-8 mangling).
- Bot spawn positions/colours are dealt by connect order — inserting a bot suite relocates every
  later one; new bot suites go at the END of `Run-AllTests.ps1`'s registry.
- Suites share hardcoded UDP ports machine-wide; don't run two scene suites concurrently.
- `user://` is shared by every checkout on the machine (`app_userdata/Watis World`): the suite
  writes into the player's real profile.

## Report shape

When Talon asks "where are we", answer in prose, in this order: items closed this session /
items open / items needing him. No popups, no numbered question batches.
