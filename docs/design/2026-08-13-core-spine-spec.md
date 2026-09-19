# Core spine — playthrough state machine, event contract, persistence taxonomy

```yaml
packet: CORE-SD-1
role: systems-design
date: 2026-08-13
status: ready
consumers: CORE-PROG-A1 (machine), CORE-PROG-A2 (persistence), CORE-PROG-B1 (screens), CORE-VER-1
canon: docs/CANON.md — §1.5 (the Pot as the quota) and §1.11 (the round)
       fact-15 amendment (open-ended run, no win-line, quota-missed is the only ending)
```

This is the contract the CORE program builds against. Every claim about existing code
below was verified against the worktree on 2026-08-13 (file:line cited); where this spec
corrects the packet or the breakdown, the correction is also listed in the SD-1 report.
**No code in this document is implementation** — paper signatures are contracts for
PROG-A1/A2/B1, and the values marked *(value call)* are placeholders a balance or feel
pass may retune freely.

Vocabulary, used consistently below:

- **Playthrough** — Start Game through Loss. The brief's "session". One escalating
  sequence of rounds; open-ended; ends only on a missed quota (canon fact 15, amended).
- **Round N** — one Day + one Night, 1-based. Maps to `CycleDriver.CyclesElapsed = N−1`
  while the round is being played.
- **Band** — `CycleBands.Band` (Day / DuskSweep / Night / DawnSweep), derived from the
  clock. Bands are *time*; states are *game*. They are related but never conflated.
- **The clock** — `CycleDriver` (`scripts/game/world/CycleDriver.cs`), free-running,
  server-authoritative, unpausable (verified: nothing in `scripts/` sets `GetTree().Paused`
  or any `ProcessMode`; the only origin-mover is `CycleDriver.Setup`).
- **The crossing layer** — `RunDriver` (`scripts/game/world/RunDriver.cs`), the locked
  exactly-once contract: `PhaseCrossed`/`RunEndedSignal`/`RunReset` (:101/:105/:111),
  reliable + ordered + `CallLocal` on `NetCodec.RunChannel`. **This spec subscribes to
  it and never modifies it.**

---

## 1. The state graph

### 1.1 Two tiers, and why the split is principled

The brief's nine states do not all live in one machine, because four of them exist when
no server exists. The host is a **separate spawned server process joined as a client**
(`scripts/net/hosting/LocalServerHost.cs` — spawn at `:62`, host joins its own child as
`SessionRole.Client` from `HostMenu.cs:230–244`). Menu and Start Game happen *before*
that process is alive; a networked state machine cannot own states that predate its own
authority. So:

- **Tier 1 — AppFlow (local, per-client, scene-switch-as-state).** Splash → CampfireMenu
  → Host/Join menus → Gameplay scene → back to CampfireMenu. This is exactly today's
  `ScenePaths` + `ChangeSceneToFile` flow (`Boot.cs:279/:374`, `HostMenu.cs:244`,
  `Gameplay.cs:2070/:2082`) and it **stays scene-switch-as-state** — no new enum, no
  new machinery. AppFlow covers brief states 1 (Start Game) and 9 (Menu).
- **Tier 2 — PlaythroughState (networked, server-authoritative).** A new manager,
  **`PlaythroughDriver`**, owns brief states 2 and 6–8 plus the loss verdict, and
  *derives* states 3–5 from the crossing layer it subscribes to. It covers everything
  that must be one fact shared by up to six peers.

**Placement of `PlaythroughDriver` (argued, per the packet):** a **`Gameplay`-child
manager node** — `new PlaythroughDriver { Name = ... }; AddChild(...)` in
`Gameplay._Ready`, constructed **immediately after `RunDriver`** (`Gameplay.cs:391`) and
**before every manager that will gate on it**, with `static Instance`, `_isServer`,
`Synced`, reliable `CallLocal` RPCs, and a `SendFlowStateTo(peer)` entry in the
late-join funnel. Reasons, in order:

1. It is the codebase's stated law: "a plain `Node` child of `Gameplay`, added
   identically on every peer, holding every `[Rpc]` the system owns"
   (`CampfireManager.cs:10–17`). World state is never an autoload — the autoload roster
   is transport/telemetry only (`NetworkManager`, `VoiceManager`, `Telemetry`,
   `ActiveDevice`, `DevScreenshot`), and a scene change is a deliberate total
   world-state wipe that only `NetworkManager` survives. An autoload PlaythroughDriver
   would outlive the world it describes — exactly the bleed the brief forbids.
2. The host-as-separate-process constraint means every flow verb already takes a network
   hop; putting the machine anywhere client-side would give the host client an authority
   it does not have. `PlaythroughDriver` is server-detected, client-applied — the
   `RunDriver` shape exactly, one layer up: `CycleDriver` → `RunDriver` →
   `PlaythroughDriver`.
3. A split (part autoload, part child) was considered and rejected: the only state that
   must survive scene switches is "which scene to load next," and AppFlow already
   carries that implicitly.

**What "lobby" means here:** there is **no pre-game room** in this codebase and this
spec does not create one. "Upgrade Lobby" (brief state 7) is an **in-session
PlaythroughState between rounds** — the session keeps running, nobody rejoins anything.
The only pre-game flow remains Host/Join menus → Gameplay. (The word "Lobby" elsewhere
in the code means the Steam matchmaking phonebook, `scripts/net/steam/SteamLobby.cs` —
unrelated; and `SessionSummaryPanel.cs:98`'s "Return to Lobby" button actually calls
`Gameplay.LeaveToMenu()`. B1 should rename that label; see §7.)

### 1.2 The enum and the (state × band) product

```csharp
public enum PlaythroughState : byte
{
    Boot         = 0, // server constructing / client connecting; pre-round-1
    RoundIntro   = 1, // brief state 2 — "Round N" card, quota announced
    InRound      = 2, // brief states 3–5; fine structure is the Band
    RoundEnd     = 3, // brief state 6 — tally screen
    UpgradeLobby = 4, // brief state 7 — ~30 s social interlude
    Loss         = 5, // brief state 8 — run over
}
```

States 3–5 of the brief (Day, Day→Night, Night) are **not** new machine states: they
are `InRound × Band`, read from the existing crossing layer. Making them duplicate
outer states would create two copies of one fact — the exact drift `CycleBands`'s own
doc warns about. The player-facing "state" is always the pair:

| PlaythroughState | Nominal band(s) | Band is gameplay-live? |
|---|---|---|
| Boot | any (clock already running server-side) | no |
| RoundIntro | Day (phase 0 after anchor; test hooks like `--cycle-start-phase` can put it elsewhere — legal, degenerate) | yes |
| InRound | Day → DuskSweep → Night (DawnSweep only for the verdict instant, §1.4) | yes |
| RoundEnd | DawnSweep, then Day of the next cycle (clock free-runs; §1.5) | **no** |
| UpgradeLobby | DawnSweep or Day (same) | **no** |
| Loss | any (clock free-runs, inert) | **no** |

**The gating rule (binding on every downstream packet):** any system whose behavior
keys on the band for *gameplay* effect — creature activity, night pressure, chill —
must additionally gate on `PlaythroughDriver.State` being band-live per the table
above. The clock never pauses, so during RoundEnd/UpgradeLobby/Loss the sky does
whatever the clock says (dawn rising behind the tally screen is a feature), but nothing
*hunts* by it. This is a `STATE-CASCADE-TABLE.md`-class cascade: PROG-A1 lists every
band consumer it gates, and CORE-VER-1 checks the list against a grep, not against A1's
memory. (Night-scoped *cutoffs* — the glow-stick dawn cutoff at
`GlowStickManager.cs:417` — are unaffected: `NightToDawn` only ever fires while
InRound, see §1.5.)

### 1.3 Every state: entry, exit, in-state behavior

Brief-state numbers in parentheses. "Server detects" always means: detected in the
server process's single-threaded physics tick, committed, then broadcast reliable +
ordered + `CallLocal` on `NetCodec.RunChannel` (§3 has the wire contract).

**Menu (9) — AppFlow, local.**
- *Entry:* Splash hand-off at app start; `Gameplay.LeaveToMenu()` after a Loss (or any
  mid-run leave — unchanged); `Fail` path lands on JoinMenu (`Gameplay.cs:2070`).
- *In-state:* today's CampfireMenu behavior, untouched. No server exists (a host
  arriving here disposed its hosted server: `NetworkManager.cs:221` →
  `HostedServer.Dispose()` — verified; there is no leave-and-keep-session path today,
  and this spec does not add one).
- *Exit:* Host pressed → Start Game. Join pressed → join flow (lands in whatever
  PlaythroughState the session is in; §6 case 1).

**Start Game (1) — AppFlow, local + process spawn.**
- *Entry:* Host flow confirmed in HostMenu.
- *In-state:* `LocalServerHost.StartAsync` spawns the headless server child, waits for
  its readiness line, host switches to the Gameplay scene as a client
  (`HostMenu.cs:186–244`). Splash/loading treatment over all of it is B1 surface 6.
- *Exit:* client's Gameplay scene is up and `PlaythroughDriver.Synced` is true — the
  player is now inside Tier 2, in whatever state the server broadcast (for the host,
  Boot or RoundIntro(1)). Start Game has no server-side existence at all; the *server's*
  first state is Boot.

**Boot — PlaythroughState, the anchor state the brief implies but does not name.**
- *Entry:* `PlaythroughDriver` constructed in `Gameplay._Ready`. Server and client
  alike start here; a client additionally stays here until its first
  `SyncFlowStateTo` lands (`Synced` gate, same contract as `RunDriver.Synced`).
- *In-state:* server: `Gameplay.StartAsServer` seeding runs to completion
  (`ServerBuildWorldFires` at `:815` before `SpawnInitialProps` at `:819` — existing
  documented order, untouched). Client: "Connecting…" gate (B1 surface 6).
- *Exit:* server only — the first physics tick where `CycleDriver.Synced` and
  `RunDriver.Synced` are true and world seeding is complete: run the **playthrough
  boundary reset** (§5.4, unconditional and idempotent), then commit
  `RoundIntro(1)`.

**RoundIntro (2) — "Round Start."**
- *Entry:* from Boot (round 1) or from UpgradeLobby (round N ≥ 2, after the clock
  re-anchor, §1.5). Broadcast carries `(round, demand)` — demand is the server's
  authoritative number from the quota schedule (§2), never client-derived.
- *In-state:* play is **live** — control is not frozen (freezing six players for a
  banner invites every input-buffering edge case and buys nothing). B1 shows the "Round N — the demand is X" card; the diegetic demand display (per canon §1.5) re-anchors to the new demand (surface 5). Banking is accepted (§2.4).
- *Exit:* server timer `T_intro` elapses *(value call: 4 s; clamp ≥ 1 s)* → InRound.

**Day (3) = InRound × Day.**
- *Entry:* `RoundIntro → InRound` commit; the band is already Day.
- *In-state:* the existing day game, untouched: gather, findables, vending, banking at
  the camp drop-off (slot, §2.4). All existing `PhaseCrossed` consumers behave as today.
- *Exit:* the clock crosses into DuskSweep — `PhaseCrossed(DayToDusk)`, existing,
  locked. Not a PlaythroughState transition; the state stays InRound.

**Day → Night Transition (4) = InRound × DuskSweep.**
- *Entry:* `PhaseCrossed(DayToDusk)`.
- *In-state:* the telegraph window. ~70 % built (PhaseToastLayer, DayPhaseWidget flash,
  wristwatch, NightDome/sky). The brief's requirement that "whatever actually changes
  mechanically at night should be spelled out to the player here" is B1 surface 1;
  *what it should feel like* is **needs `/direct`** — not decided here.
- *Exit:* `PhaseCrossed(DuskToNight)`, existing, locked.

**Night (5) = InRound × Night.**
- *Entry:* `PhaseCrossed(DuskToNight)`.
- *In-state:* quota pressure is live — this is where banking races the dawn. Creatures,
  light, chill: all existing/parallel systems, none owned by this program.
- *Exit:* `PhaseCrossed(NightToDawn)` — **the verdict instant** (§1.4). The server
  evaluates the loss-predicate chain in the same tick it applies the crossing and
  commits exactly one of RoundEnd or Loss. InRound therefore never meaningfully
  contains DawnSweep.

**RoundEnd (6).**
- *Entry:* verdict tick, predicate chain returned no loss. Broadcast carries the
  `RoundSummary` (§3.2).
- *In-state:* non-diegetic tally over a live dawn. Gameplay-inert per §1.2's gating
  rule; mutation requests are denied with feedback (§5.6). B1 surface 2 — today's
  `SessionSummaryPanel` adapts (it currently fires on `RunEndedSignal` only —
  end-of-run — verified `SessionSummaryPanel.cs:111–135`; the per-round adaptation is
  B1's, the data seam stays `ISessionSummarySource`-shaped).
- *Exit:* server timer `T_tally` elapses *(value call: 15 s)* **or** every connected
  peer has sent `RequestReadyAdvance` (§3.3) — whichever first. The timer is the
  guarantee that an AFK player cannot hold five others hostage; the ready-skip is the
  courtesy. → UpgradeLobby.

**UpgradeLobby (7).**
- *Entry:* from RoundEnd. Broadcast carries `(roundJustSurvived, durationSec)`.
- *In-state:* the ~30 s low-stakes social interlude. **Content-agnostic by design:**
  Q3 ("what is an upgrade, and is its currency the wallet?") is open with Talon, so the
  machine guarantees only *time, safety, and an extension point* — a state window in
  which some future spend-verb can be enabled. Nothing in the machine assumes purchases
  exist; when Q3 resolves, its content plugs into this window without touching a
  transition. Players are **not teleported** — a player deep in the woods at dawn stays
  there, protected by the gating rule, and walks back; relocating them would fight
  canon fact 14's walk-home. "Living menu" presentation and tone: **needs `/direct`**.
- *Exit:* server timer `T_lobby` elapses *(value call: 30 s, per the brief)* or
  all-ready skip (same mechanism as RoundEnd). On exit the server **re-anchors the
  clock** (§1.5) and commits `RoundIntro(N+1)`.

**Loss (8).**
- *Entry:* verdict tick, a predicate returned a `RunOutcome` (§1.6). Broadcast carries
  the outcome. The world is left **intact** behind the screen — the post-mortem view is
  B's to use or ignore; teardown is *not* run at Loss entry (see §5.4 for why the
  boundary is an entry guard on the *next* playthrough, which is strictly stronger).
- *In-state:* gameplay-inert; unambiguous full-screen failure feedback (B1 surface 4;
  tone — the brief's "cheeky/self-aware" lean — is **needs `/direct`**, and THRILL §9
  tone is a live blank nobody downstream resolves). No auto-timeout: players sit with
  it as long as they like. Two verbs:
  - **Continue** → local, per-client: `Gameplay.LeaveToMenu()` (host's dispose kills
    the server child for everyone — existing, unchanged, and *no longer load-bearing
    for cleanliness*, §5.4).
  - **Play Again** → `RequestPlayAgain` (§3.3), server-validated (only in Loss).
- *Exit:* Play Again → boundary reset → `RoundIntro(1)` of a **new playthrough** in the
  same process (§1.7). Or per-client Continue → Menu (AppFlow).

**Menu after Loss (9):** identical to Menu above; the loop closes.

### 1.4 The verdict instant — one tick, one order

At the server tick that applies `PhaseCrossed(NightToDawn, N)` while `State == InRound`:

1. The crossing applies (RunDriver's own broadcast has already been queued on
   RunChannel — reliable + ordered, so every client sees the crossing *before* the
   verdict, always).
2. `PlaythroughDriver` runs the **loss-predicate chain** (§1.6) — in registration
   order, first non-null outcome wins (MECHANICS-BIBLE §7: explicit priority, never
   code-order accident).
3. Exactly one commit: `Loss(outcome)` or `RoundEnd(summary)`. Both are broadcast on
   RunChannel *after* the crossing — same channel ⇒ same arrival order on every peer.
4. Store hooks fan out (§5.3), in registration order.

Nothing else may end a run. Predicates evaluate **only** at this point — there is no
mid-round surprise loss, which is also what makes "quota-fail races X" cases
unrepresentable (§6 case 7).

### 1.5 The clock seam — re-anchor, never pause

Verified facts the design must live with: the clock cannot be stopped (§ vocabulary);
its only origin-mover is `CycleDriver.Setup(isServer, periodSec, startPhase)`, and that
call **zeroes `CyclesElapsed`** (`_elapsedSec = clamp(startPhase) * _periodSec`,
`CycleDriver.cs:98`) — which is exactly right for `ResetRun` (rewind to day 1) and
exactly wrong for "start round N+1's day fresh."

**Design: re-anchor forward, with one additive parameter.** PROG-A1 extends
`CycleDriver.Setup` with an optional `int startCycles = 0`:
`_elapsedSec = (startCycles + clamp(startPhase)) * _periodSec`. Every existing caller
is untouched (default 0 reproduces today exactly); `RunDriver` is untouched. This is an
additive extension to CycleDriver's public surface, not a change to the locked
RunDriver contract, and it follows the precedent that `ResetRun` already re-anchors
through `Setup` + immediate `SendPhaseTo` fan-out to every peer (`RunDriver.cs:230–239`)
— PROG-A1 copies that fan-out verbatim.

**Who owns it:** `PlaythroughDriver`, and only it (plus the existing `ResetRun` path).
At `UpgradeLobby(N) → RoundIntro(N+1)` the server calls
`CycleDriver.Setup(true, periodSec, 0f, startCycles: N)` then fans `SendPhaseTo` to all
peers. The clock lands at (cycles = N, phase 0) — round N+1's day, full length.

**Why this is safe against the crossing layer (the forward-only invariant):**
RunDriver's tracker is ordinal arithmetic, `ordinal = cyclesElapsed*4 + bandIndex`
(`RunPhaseTracker`, `RunDriver.cs:317+`). During RoundEnd + lobby the clock free-runs
from the verdict instant (ordinal 4N−1, DawnSweep of cycle N−1) toward Day of cycle N
(ordinal 4N):

- If the lobby outlasts the dawn sweep, `DawnToDay` fired naturally during
  RoundEnd/lobby (gameplay-inert per the gating rule; harmless). Re-anchor target
  ordinal = 4N = current → no crossing emitted, no stall.
- If re-anchor happens mid-sweep, the jump is ordinal 4N−1 → 4N: RunDriver emits
  exactly the one `DawnToDay(N)` crossing via its own catch-up walk. Exactly-once
  holds.

**The invariant, stated for VER:** `PlaythroughDriver` only ever re-anchors to an
ordinal ≥ the tracker's current one. A backward re-anchor through this path would stall
the tracker (`ordinal <= _lastOrdinal` suppresses events until real time catches up) —
backward jumps are the exclusive property of `ResetRun`, which resets the tracker
explicitly. PROG-A1 asserts this in its headless tests.

**`RunEndedSignal` under the open-ended run:** today run-end is timer exhaustion —
`IsRunEndCrossing: kind == DawnToDay && cyclesElapsedAfter >= runCycles`
(`RunDriver.cs:369`), default `runCycles = 2`. Canon retires the very concept of a
configured run length. Design: **neutralize, don't modify** — `Gameplay._Ready` passes
`runCycles = int.MaxValue` by default (a `LaunchOptions` default change in PROG-A1);
`--run-cycles` survives as a test hook with its legacy meaning so existing selftests
keep passing. `RunEndedSignal` therefore never fires in real play; **no new consumer
may subscribe to it** — `PlaythroughDriver.RunConcluded` is the only run-end. The one
existing subscriber (`SessionSummaryPanel`) migrates to the new contract in B1.
`SendRunStateTo`'s `(RunCycles, RunEnded)` payload keeps working unmodified.

### 1.6 The verdict layer — `RunOutcome` and the predicate chain

```csharp
public enum RunOutcomeKind : byte
{
    QuotaMissed = 0,
    // Append-only; ordinals cross the wire (VendDenial precedent, VendingManager.cs:86).
}

/// One per playthrough, loss-only under the open-ended model.
public readonly record struct RunOutcome(
    RunOutcomeKind Kind,
    int Round,          // the round whose night ended the run
    int Demand,         // cumulative demand that was due (for the loss screen's honesty)
    int Banked);        // what was actually banked

public interface ILossPredicate
{
    string Id { get; }                    // stable, for logs and tests
    RunOutcome? Evaluate(int round);      // server-side, at the verdict instant only
}
```

`PlaythroughDriver` holds an ordered predicate list; registration order is evaluation
order is priority order (MECHANICS-BIBLE §7). **Exactly one predicate ships:**
`QuotaMissedPredicate` — `QuotaLedger.CumulativeBanked < QuotaLedger.CumulativeDemand(round)`
(§2). The shape lets a future predicate register without rework; per the packet and the
gate record, **no second predicate is invented** — there is no incapacitation-loss slot
to keep warm (breakdown gate record: instant respawn at hearths means a group wipe
needs no scripted rule).

**Round-end vs run-end, distinct on the wire and in the API:** `RoundEnded(RoundSummary)`
fires once per *survived* round (new — today's summary is end-of-run only, verified);
`RunConcluded(RunOutcome)` fires at most once per playthrough. They are different
events with different payloads, never one event with a flag.

**Delivery: "RunDriver's existing exactly-once channel"** means the semantics and the
literal channel — every verdict broadcast is a reliable, ordered, `CallLocal` RPC on
`NetCodec.RunChannel`, owned by `PlaythroughDriver` (its own node, per the
CampfireManager law — constructed managers own their `[Rpc]`s). Sharing the channel
with RunDriver is deliberate: ENet channel ordering makes crossing-then-verdict arrive
in that order on every peer, with no seq gymnastics.

### 1.7 Transition table

Every transition, with trigger / guards / detecting authority / replication /
disconnect-reconnect story (acceptance criterion 2). "Broadcast" = reliable, ordered,
`CallLocal`, RunChannel, applied identically everywhere; a peer that was absent for a
broadcast is made whole by the late-join sync (§3.4) — the subscribe-and-poll idiom
means no surface ever depends on having *witnessed* the transition.

| # | Transition | Trigger | Guards | Authority | Replication | Disconnect/reconnect behavior |
|---|---|---|---|---|---|---|
| T1 | Menu → Start Game | Host pressed | Steam/transport ready (existing HostMenu checks) | local client | none (pre-network) | n/a — no session yet |
| T2 | Start Game → Boot | Gameplay scene loads | — | local; server process starts in Boot by construction | n/a | client that dies here simply relaunches |
| T3 | Boot → RoundIntro(1) | first server tick with CycleDriver+RunDriver synced and world seeded | boundary reset (§5.4) has run this commit | server | broadcast `RoundIntro(1, demand)` | joiner during Boot: funnel delivers Boot; poll flips them on the broadcast or the next sync |
| T4 | RoundIntro(N) → InRound(N) | `T_intro` elapsed | State == RoundIntro | server | broadcast `RoundLive(N)` | reconnector lands in whatever committed; sync carries it |
| T5 | InRound band crossings | clock | — | server (RunDriver, locked) | existing `PhaseCrossed` | existing: late joiner gets phase via `SendPhaseTo`, no crossing replay (RunDriver.cs:90–95) — consumers poll |
| T6 | InRound(N) → RoundEnd(N) | `PhaseCrossed(NightToDawn, ·)` | State == InRound; predicate chain null | server | broadcast `RoundEnded(summary)` after the crossing | joiner into RoundEnd gets state + `LastRoundSummary` in sync |
| T7 | InRound(N) → Loss | same crossing | State == InRound; predicate returned outcome | server | broadcast `RunConcluded(outcome)` after the crossing | joiner into Loss gets state + `LastOutcome` in sync |
| T8 | RoundEnd(N) → UpgradeLobby(N) | `T_tally` elapsed or all connected peers ready | State == RoundEnd | server | broadcast `UpgradeLobbyStarted(N, durationSec)` | ready-set counts *connected* peers only — a disconnect mid-tally shrinks the set (never strands the skip); reconnector is not-ready again |
| T9 | UpgradeLobby(N) → RoundIntro(N+1) | `T_lobby` elapsed or all-ready | State == UpgradeLobby | server | clock re-anchor + `SendPhaseTo` fan-out (§1.5), then broadcast `RoundIntro(N+1, demand)` | reconnector's `SyncPhaseTo` always wins over stale extrapolation (`CycleDriver.Apply` isSync path, existing) |
| T10 | Loss → RoundIntro(1) | `RequestPlayAgain` from any peer | State == Loss | server | boundary reset (§5.4) → `RunDriver.ResetRun()` (existing, incl. clock rewind + RunReset fan-out) → broadcast `RoundIntro(1, demand)` | a second Play Again request arrives with State == RoundIntro → rejected by guard; harmless (§6 case 5) |
| T11 | Loss → Menu | Continue pressed | — | local client | none (scene switch; host path disposes the server, existing) | n/a |

Timers T4/T8/T9 are server-side accumulations in `PlaythroughDriver._PhysicsProcess`
(never wall-clock `Timer` nodes — the CycleDriver reasoning). Config extremes
(MECHANICS-BIBLE §6): each clamps to ≥ 1 s; a non-positive configured value logs and
clamps, never zero-fires.

---

## 2. Quota and escalation as data

### 2.1 The model

Per canon §1.5 and §1.11: a per-cycle tribute demand, banked **only** at the camp drop-off (hearths are never banks), escalating cycle over cycle, open-ended, with the diegetic display owned by canon §1.5.

The ledger is **cumulative**: `CumulativeBanked` only ever grows within a playthrough;
the round-N verdict checks it against `CumulativeDemand(N) = Σ demand(1..N)`. Surplus
banked in round N automatically counts toward round N+1 — the winter *cache* is a
stockpile, not a nightly bucket (Decision D3 in the SD-1 report records the alternative
and the one-line flip). The schema carries `carry_surplus` so a balance pass can flip
the model without a code change: `false` makes the verdict
`bankedThisRound >= demand(N)` with per-round reset of the round counter.

### 2.2 The schema — the exact file a balance pass edits

**`assets/run/quota_schedule.tres`**, a custom `Resource`
(`QuotaSchedule : Resource`, class in `scripts/game/run/` — the RagdollProfile pattern:
custom Resource class + `.tres` under `assets/`). Loaded by the server at Boot; the
same file ships to clients in the pack but **only the server's numbers are
authoritative** — every demand a client displays arrived in a broadcast or sync payload
(version-skew immunity by construction).

```gdscript
# assets/run/quota_schedule.tres  (conceptual field list)
early_rounds_demand : int[]   # hand-authored opening curve.        placeholder [3, 5, 8, 12]
tail_growth_factor  : float   # demand(n) = ceil(demand(n-1) * f)   placeholder 1.35
carry_surplus       : bool    # §2.1.                               placeholder true
```

`demand(n)`: `early_rounds_demand[n-1]` while in range, then
`ceil(previous * tail_growth_factor)`, with a **strict-increase floor**
`demand(n) ≥ demand(n-1) + 1` (the brief: "each round's quota should escalate past the
previous round's" — enforced structurally, so no data mistake can flatten the ramp).
Extremes (MECHANICS-BIBLE §6): empty array → `[3]`; `tail_growth_factor ≤ 1` → the
strict-increase floor makes the tail linear (+1/round) rather than dividing or
flat-lining; `demand` clamps at 1,000,000,000 (int overflow guard for the open-ended
tail — §6 case 14). All placeholder values are playtest fodder; **balancing is out of
scope, and every number tweak above is a `.tres` edit with zero code changes**
(acceptance criterion 5).

Unit: integer **cache units**. Each bankable findable will declare its cache value
(default 1) as an item-side field — that field belongs to the item contract
(MECHANICS-BIBLE §8 / a future `/spec-interaction` pass), not to this schema.

### 2.3 `QuotaLedger` — where the state lives, how it replicates

A **`Gameplay`-child manager** (CampfireManager law), constructed after
`PlaythroughDriver` (it registers with the store and answers to the driver; order is a
correctness dependency — documented at the construction site like every other, per
`Gameplay.cs:396–448` precedent). Server-authoritative; replication:

- `BankedChanged` — discrete transition ⇒ reliable, ordered, `CallLocal`,
  **RunChannel** (deliberate: banked-changes and the verdict share a channel, so no
  client can ever display a banked count the verdict contradicts).
- Late-join: `SendQuotaTo(peerId)` in the `Gameplay.OnPeerConnected` funnel
  (`Gameplay.cs:879–1053`), added after `SendFlowStateTo` (§3.4).
- Registered as a run-scoped store slice (§5.2): `ResetForNewPlaythrough()` zeroes the
  ledger.

### 2.4 Banking — the declared slot

No drop-off code exists (verified: zero `quota` hits in `scripts/`). The ledger's API
is the slot the future drop-off interactable calls:

```csharp
/// Server-side only. The camp drop-off's deposit verb, and the test harness's.
/// Guard: accepted while State is RoundIntro or InRound; otherwise returns a denial
/// (QuotaDenial.NotAcceptingNow) delivered to the requester — the VendDenial idiom
/// (VendingManager.cs:86–98, ordinals append-only). The haul is NEVER consumed on a
/// denial — the items stay carried (§6 case 6).
public bool ServerBank(int peerId, int cacheUnits, out QuotaDenial denial);
```

The drop-off interactable itself (its prompt, range check, animation, noise tier) is
future `/spec-interaction` work — out of this packet's scope, slot declared.

The **hearth/light-line/rock-ring/ash objects** similarly do not exist; their
persistence slots are declared in §5.5. Nothing in the quota design assumes them.

---

## 3. The A↔B event contract

Paper C# for everything Workstream B consumes. Delivery legend: **R+O+CL** = reliable,
ordered, `CallLocal`, `NetCodec.RunChannel`, exactly-once per event occurrence (the
RunDriver idiom, `RunDriver.cs:30–37`). Every event has a queryable twin — the
**never-strand rule**: an event fired before a peer existed never reaches it, so every
surface initializes by *polling* the queryable state after `Synced`, then reacts to
events (the `SessionSummaryPanel.cs:129–135` idiom, stated as the required pattern for
all six surfaces).

### 3.1 Existing, locked (consumed as-is)

```csharp
// RunDriver — locked contract, subscribe only.
event Action<PhaseEventKind, int> PhaseCrossed;   // R+O+CL. Late-join: NO replay; poll CycleDriver.Phase/CyclesElapsed + CycleBands.GetBand.
event Action RunReset;                            // R+O+CL. Fires on Play Again; post-reset values readable inside handlers.
// CycleDriver — Phase/CyclesElapsed/Synced, polled every frame (existing consumers unchanged).
```

### 3.2 New — `PlaythroughDriver` (server-detected, one RPC per transition)

Each transition is **one** wire message; applying it locally sets the queryable state
*first*, then invokes `StateChanged`, then the typed event — so a handler for either
always reads post-transition state (the `RunReset` ordering contract, generalized).

```csharp
public sealed partial class PlaythroughDriver : Node
{
    public static PlaythroughDriver? Instance { get; }
    public bool Synced { get; }                    // server: true from Setup; client: true once SyncFlowStateTo lands
    public PlaythroughState State { get; }
    public int Round { get; }                      // 1-based; valid from RoundIntro(1) onward
    public double StateRemainingSec { get; }       // countdown for RoundIntro/RoundEnd/UpgradeLobby; -1 where no timer (client-local
                                                   //   extrapolation between syncs; cosmetic — the server's own timer is the authority)
    public RoundSummary? LastRoundSummary { get; } // latched at each RoundEnded; null before the first
    public RunOutcome? LastOutcome { get; }        // latched at RunConcluded; null unless State == Loss

    public event Action<PlaythroughState /*from*/, PlaythroughState /*to*/, int /*round*/>? StateChanged; // fires for EVERY transition
    public event Action<int /*round*/, int /*demandAuthoritative*/>? RoundIntroStarted;
    public event Action<int /*round*/>? RoundLive;
    public event Action<RoundSummary>? RoundEnded;
    public event Action<int /*roundJustSurvived*/, double /*durationSec*/>? UpgradeLobbyStarted;
    public event Action<RunOutcome>? RunConcluded;
}

public readonly record struct RoundSummary(int Round, int Demand, int Banked, int NextDemand);
// Demand/Banked are cumulative (§2.1). Per-player contribution lines are NOT in v0 —
// adding a per-peer breakdown later is an append to this record plus a broadcast field,
// no reshape. (ISessionSummarySource already carries per-player wallet totals for the parts of the tally that want wallet lines — B1 composes, this contract does not duplicate it.)
```

All six broadcasts: **R+O+CL**, server-authority RPCs owned by `PlaythroughDriver`.
Verdict broadcasts are ordered after the `NightToDawn` crossing by shared-channel
ordering (§1.6).

### 3.3 New — client → server requests

```csharp
// Both: [Rpc(AnyPeer, Reliable, TransferChannel = NetCodec.RunChannel)], server
// validates sender and state, silently drops out-of-state requests (they are stale
// echoes of a screen the server already left — not errors).
public void RequestReadyAdvance();  // valid in RoundEnd, UpgradeLobby; adds sender to the ready set
public void RequestPlayAgain();     // valid in Loss only; triggers T10
```

Play Again keeps today's "any peer may request" semantics
(`RunDriverResetRequest.cs:34` precedent) but moves the entry point: B1's Loss screen
calls `RequestPlayAgain`, not `RequestResetFromClient` — the driver, not the UI,
sequences reset-then-intro. `RunDriverResetRequest` stays untouched (it is part of the
locked file pair and remains valid for tests), but no new UI calls it directly.

### 3.4 Late-join / never-strand — per-event story

`Gameplay.OnPeerConnected` (verified funnel at `Gameplay.cs:879–1053`, currently 14
`Send*To` systems) gains, in order, **after** `_runDriver.SendRunStateTo(id)` (:967):

```csharp
_playthroughDriver.SendFlowStateTo(id);  // (state, round, remainingSec, lastSummary?, lastOutcome?) — reliable, targeted
_quotaLedger.SendQuotaTo(id);            // (cumulativeBanked, demandCurrentRound, cumulativeDemand) — reliable, targeted
```

| Event | A peer that missed it recovers by |
|---|---|
| `PhaseCrossed` | existing: `SendPhaseTo` snapshot; consumers poll band (locked behavior, unchanged) |
| `StateChanged` / all typed state events | `SendFlowStateTo` carries current state + latched payloads; surfaces poll on `Synced` |
| `RoundEnded` | `LastRoundSummary` in the sync — a joiner during RoundEnd shows the tally from the poll |
| `RunConcluded` | `LastOutcome` in the sync — a joiner during Loss shows the loss screen from the poll |
| `UpgradeLobbyStarted` | state + `remainingSec` in the sync |
| `BankedChanged` | `SendQuotaTo` snapshot |
| `RunReset` | joiner post-reset simply receives the current (post-reset) world via the whole funnel — nothing to replay |

A duplicate application (broadcast + targeted sync landing on the same peer around a
connect) is idempotent by construction: both set the same fields to the same values
(the `CycleDriver.Apply` reasoning, `CycleDriver.cs:177–205`).

### 3.5 Traceability — the six B surfaces

| B surface | Events consumed | Queryable state | Gaps |
|---|---|---|---|
| 1. Day→night telegraph | `PhaseCrossed(DayToDusk)`, `PhaseCrossed(DuskToNight)` (existing); `StateChanged` (to suppress the telegraph outside InRound) | `CycleDriver.Phase`, `CycleBands.GetBand` progress, `PlaythroughDriver.State` | none — ~70 % built today; audio channel absence is a B1 item; *feel* needs `/direct` |
| 2. Round-end tally | `RoundEnded(RoundSummary)` | `State`, `LastRoundSummary`, `StateRemainingSec`; wallet lines via `ISessionSummarySource` (existing seam, `SessionSummarySource.cs:26`) | none |
| 3. Upgrade lobby | `UpgradeLobbyStarted`, `StateChanged` | `State`, `StateRemainingSec` | content pending Q3 — the *surface* (timer, space, transition treatments) is fully fed |
| 4. Loss screen | `RunConcluded(RunOutcome)` | `State`, `LastOutcome` | none; tone needs `/direct` |
| 5. Quota display (diegetic) | `RoundIntroStarted(round, demand)`, `BankedChanged(banked, delta)` | `QuotaLedger.CumulativeBanked`, `.CumulativeDemand`, remaining-need = max(0, demand − banked) | none mechanical. **Register constraint honored:** the contract exposes integers only; no meter ships (`ChillCueOverlay.cs:18–20` — "the register does not carry meters"; quota resolution is diegetic at the camp per the 2026-07-25 decision). Canon §1.5 names the Pot as the diegetic demand display, fed by exactly these two numbers. Mapping numbers → presentation needs `/direct` (not this program). B1's v0 may use **worded stages** (`ChillReadout` precedent) as the placeholder until the babies exist. |
| 6. Splash/loading gates | `StateChanged`, `RoundIntroStarted`; AppFlow scene switches (local) | `PlaythroughDriver.Synced` ("Connecting…" gate — the established `Synced`-gating idiom), `State` on arrival (join-in-progress lands on the right screen from the poll) | none |

Every surface is fed entirely by events and state defined above or already shipped —
**no surface needs an event that does not exist** (acceptance criterion 3).

---

## 4. Workstream separation

B1's **only** dependency on Workstream A is §3 of this document — paper signatures, on
which B1 builds a headless fake driver (the `LoopUiTelemetry` pattern) and asserts its
six surfaces without A's code existing. A1/A2 implement §1/§2/§5 without touching any
screen. They integrate at VER-1. This is the brief's separate-ownership requirement,
made structural: if B1 finds it needs a field §3 does not define, that is a contract
change routed through the orchestrator — never a reach into A's internals.

---

## 5. Persistence — taxonomy and the store seam

### 5.1 The two scopes (plus one sub-scope)

- **run-scoped-forgets** — exists within one playthrough; reset at the playthrough
  boundary. *Sub-scope:* **night-scoped** — additionally cut off at each dawn (the
  glow-stick precedent, `GlowStickManager.cs:417`).
- **map-scoped-remembers** — persists across every round boundary within the
  playthrough (canon fact 8, "the map remembers"); reset only at the playthrough
  boundary. Within one server process this persistence is *free* — the state simply
  stays live in memory; what the scope buys is (a) the guarantee that no round-boundary
  hook may touch it and (b) a reserved snapshot write path (§5.5) so disk persistence
  can be added later without re-plumbing. **No disk write exists or is added by this
  program** — a playthrough lives and dies with its server process, and crash-loss is
  accepted (§6 case 4).

### 5.2 Every runtime mutator, assigned

From the verified inventory (SD-1 audit; this table supersedes the packet's nine-item
list — the audit found four more mutators and corrected several reset claims):

| Mutator | Owner | Scope | Today | Justification (one line) |
|---|---|---|---|---|
| Props / world items | `PropManager` | **map-scoped** | **no reset at all** (verified: zero RunDriver refs; consumed logs never respawn — the unwinnable-second-run bug) | dropped wood staying where it fell across nights IS the map remembering; playthrough reset restores the initial dump (the A2 fix) |
| Player loadouts / held | `PropManager` holder map | run-scoped | partial (`ClientResetHeldState` :759 is client-local only) | a new playthrough starts empty-handed |
| Campfire / fire nodes | `CampfireManager` + `FireNodeRegistry` | **map-scoped** | resets on RunReset (:142→:887) | fires and their built/consumed state persist across nights (canon 8); reset correctly only at playthrough boundary — *already true today* since RunReset only fires there |
| Glow sticks | `GlowStickManager` | **night-scoped** + run | dawn cutoff (:417) + RunReset (:146→:429) — both shipped | canon 6's "do not persist across nights" is already structural; migrates as the first `INightScopedSlice` |
| Photos (credited evidence) | `PhotoRegistry` | run-scoped | **dead reset** (`:45`, never called) | evidence belongs to the playthrough that shot it |
| Film | `PolaroidFilmBank` | run-scoped | **dead reset** (`:50`, never called) | purchased film must die with the wallet that bought it (today it survives the reset that zeroes that wallet — verified inconsistency) |
| Wallets (tickets) | `WalletManager` | run-scoped | resets (:123→:215) | currency is per-playthrough |
| Wall-map pins | `WallMapManager` | **map-scoped** | **no reset** | pins are the players' accumulated knowledge of THIS playthrough's world — keep across nights, wipe at boundary |
| Water / per-peer chill | `WaterService` | run-scoped | **no reset** (deliberate deferral, `STATE-CASCADE-TABLE.md:183`) | a new playthrough starts dry and warm; the deferral ends here |
| Arrows (embedded) | `ArrowManager` + pool | run-scoped | **no reset** (pool-cap eviction only) | stray arrows are clutter, not memory; cheap to clear |
| Torches | `TorchManager` | **map-scoped** (fuel state remembers across nights) | resets on RunReset (:93→:388, refuel+douse) | same reasoning as campfire; current behavior already correct at the boundary |
| Incapacitation / injuries | `IncapacitationService` | run-scoped | resets (:344→:402) | no injury outlives the playthrough (a future premise pivot may retire this system wholesale — its slice migrates as-is regardless) |
| Eel charges (flag-gated) | `EelManager` | run-scoped | **dead reset** (`ServerResetForNewRun` :199, never called — third instance of the defect class) | per-playthrough resource |
| Reconnect records | `ReconnectRegistry` (`Gameplay.cs:213`) | run-scoped | **no reset** (verified — the 60 s pre-reset-resume exploit is live) | a resume ticket into a world that no longer exists must die at the boundary |
| Sight table | `PlayerSightService` | run-scoped | reset only from `Setup` | per-playthrough perception state |
| Quota ledger (new, §2.3) | `QuotaLedger` | run-scoped | n/a | the cache is the playthrough's score |
| Vending dispense lock | `VendingManager` | (none needed) | self-expiring 0.4 s | shorter-lived than any boundary |
| UI latches (summary shown, toasts, telemetry) | `SessionSummaryPanel` / `BedSleepFade` / `LoopUiTelemetry` | run-scoped, client-local | reset via RunReset subs | migrate to the store only if trivially convenient — client-local UI latches may stay direct `RunReset` subscribers (they are presentation, not world state; the store's completeness guarantee is about the *world*) |

### 5.3 The store — `WorldStateStore`

A **`Gameplay`-child**, constructed **immediately after `RunDriver` and before
`PlaythroughDriver` and every stateful manager** — which makes registration itself
enforce the ordering the audit flagged as fragile ("a system constructed before
RunDriver silently never subscribes … there is no registry that would catch this").
With the store, there is one subscriber to migrate to and one list to check.

```csharp
public interface IWorldStateSlice
{
    string SliceId { get; }              // stable id, for logs, ordering audits, and VER's completeness check
    void ResetForNewPlaythrough();       // REQUIRED idempotent: running it twice == running it once.
                                         // Restores the slice to its pristine, session-start state.
}

public interface INightScopedSlice : IWorldStateSlice
{
    void OnNightEnded(int round);        // the dawn cutoff, fanned at PhaseCrossed(NightToDawn)
}

public interface IMapScopedSlice : IWorldStateSlice
{
    void CaptureNightSnapshot(int round); // RESERVED across-nights write path, fanned at
                                          // PhaseCrossed(NightToDawn). No-op bodies are legal and
                                          // expected today; when disk persistence is ever built,
                                          // it lands here without re-plumbing a single manager.
}

public sealed partial class WorldStateStore : Node
{
    public static WorldStateStore? Instance { get; }
    public void Register(IWorldStateSlice slice);        // from each manager's Setup; order = construction order
    public IReadOnlyList<string> RegisteredSliceIds { get; }  // VER's completeness probe
    public void ResetForNewPlaythrough();                // fan-out in registration order; idempotent because slices are
}
```

**Fan-out points (capture / restore / reset / teardown):**

| Point | Trigger | Fan-out |
|---|---|---|
| Dawn cutoff | `RunDriver.PhaseCrossed(NightToDawn)` — store is the single subscriber | `INightScopedSlice.OnNightEnded(round)`, registration order |
| Across-nights write path | same crossing, after the cutoffs | `IMapScopedSlice.CaptureNightSnapshot(round)` — reserved, no-ops today |
| Play Again | `RunDriver.RunReset` — store is the **single** subscriber; the eight current ad-hoc world-state subscribers migrate to slices (A2); client-local UI latches may remain direct subscribers (§5.2 last row) | `ResetForNewPlaythrough()`, registration order |
| Playthrough boundary (the intentional one) | `PlaythroughDriver` calls `store.ResetForNewPlaythrough()` **synchronously inside every commit into `RoundIntro(1)`** — Boot→RoundIntro(1) at session start and T10 Play Again alike | same |
| Process teardown | server exit (existing `LocalServerHost` dispose paths) | nothing to do — no disk state exists; this row exists to say so explicitly |

**Replicated-slice rule (the `_seq` trap):** a slice that owns replicated
sequence-guarded state must *not* rewind its sequence counters on reset —
`CampfireManager.cs:906–909` states why (clients would reject post-reset state as
stale). Every new resettable replicated system copies that pattern; VER checks it.

### 5.4 The playthrough boundary is an entry guard, not an exit hope

The brief demands the boundary "not depend on the server process dying" and not be
"happens not to carry over." Design: **a playthrough may not begin unless the store has
affirmatively reset every registered slice in that process instance, in that commit.**
`PlaythroughDriver` runs `store.ResetForNewPlaythrough()` synchronously inside both
transitions into `RoundIntro(1)` — unconditionally, including the very first at session
start (where it fans out over a pristine world; slices are idempotent, so this is a
cheap no-op pass that buys an unconditional invariant instead of a conditional one; A2
may optimize with an internal pristine flag, invisible to this contract).

Consequences, stated:

- Process death remains a belt-and-braces cleaner but is **not the mechanism**. If a
  future change ever reuses a server process across sessions, the invariant already
  holds.
- Loss leaves the world intact behind the screen (§1.3) — the guarantee needs no
  teardown-at-loss, because nothing can *start* without the reset.
- `RunDriver.ResetRun()` in T10 runs **after** the store reset (driver sequence:
  store reset → `ResetRun()` → commit RoundIntro). `ResetRun` is `CallLocal`, so its
  `RunReset` fan-out (which also reaches the store — a second, idempotent pass) and
  clock rewind complete synchronously before the RoundIntro broadcast is queued;
  channel ordering then guarantees every client applies reset-before-intro. The double
  pass is the migration-window safety net: during A2's migration, systems reached via
  either path reset exactly once each, and idempotency makes the overlap harmless.

### 5.5 Declared slots — born into the persistent slice

The canon objects that do not exist yet register as `IMapScopedSlice` **on the day they
are born**, not retrofitted:

| Future object | Scope | Slot note |
|---|---|---|
| Hearth nodes (canon 13/14) | map-scoped | founded state, connection-to-mother-flame topology; respawn-chain regression on line cuts is *its* contract, the store just persists it |
| String-light lines (canon 6/13) | map-scoped | strung endpoints + integrity |
| Rock rings (canon 2/8) | map-scoped | built rings survive nights "unless something eats them" |
| Campfire ashes (canon 8) | map-scoped | likely a `FireNodeRegistry` extension — same slice as campfire, not a new one |
| Diegetic demand display state (canon §1.5) | derived, **not stored** | display derives from `QuotaLedger` — no second copy of the quota fact |

### 5.6 Six players at a boundary — flush ordering and in-flight requests

The server is already authoritative for **every** mutation in this codebase (verified
per-system in §5.2's inventory; the CampfireManager law: "no client prediction … the
server re-checks against its own authoritative state"). All boundary work happens
inside one server physics tick, single-threaded. Therefore:

- **There are no concurrent writes** — only *in-flight client requests* that arrive
  after a boundary committed. Rule: every mutation handler guards on
  `PlaythroughDriver.State` (band-live per §1.2). A request arriving post-boundary is
  **denied with feedback** (the `VendDenial` idiom — a denial enum per system,
  ordinals append-only) or, for stale flow requests (`RequestReadyAdvance` after the
  state advanced), silently dropped as an echo. **A denial never consumes the
  requester's resources** — the carried haul survives, the wallet is untouched.
- **Flush order within the boundary tick** is fixed and deterministic (MECHANICS-BIBLE
  §3): apply crossing → evaluate predicates → commit state → queue broadcasts → fan out
  store hooks in registration order. No mutation is ever half-applied across a
  boundary, because nothing yields mid-tick.
- Requests racing the boundary in flight are resolved by arrival tick, not by
  timestamps — whichever tick processes them sees a consistent world (§6 cases 6, 8).

---

## 6. Edge-case catalog

Each entry: the case, then the **designed resolution**. The packet's mandatory ten are
cases 1–8 + 5 + 7 (late join per state counts per the packet as one item; it is six
entries here).

**1. Late join into every state.**
- *Boot:* funnel delivers state Boot; client sits at the Connecting gate until the
  RoundIntro broadcast or next sync flips it. Resolution: standard `Synced` gating.
- *RoundIntro:* sync carries `(RoundIntro, round, demand, remainingSec)` — B replays
  the intro card from the poll.
- *InRound (any band):* today's funnel already delivers the whole world (14 systems)
  plus phase; add flow + quota syncs (§3.4). No crossing replay needed — locked
  RunDriver behavior, consumers poll.
- *RoundEnd:* sync carries `LastRoundSummary` — the joiner sees the tally mid-count.
- *UpgradeLobby:* sync carries `remainingSec` — the joiner gets the tail of the lobby.
- *Loss:* sync carries `LastOutcome` — the joiner lands on the loss screen. Their
  avatar spawns normally (input-inert per the gating rule) — the playthrough is over;
  a Play Again or Continue picks them up like everyone else. Resolution: allowed,
  cheap, no special path.

**2. Disconnect and reconnect across a *round* boundary.** `ReconnectRegistry` holds
`(position, heldPropIds, colorIndex)` for 60 s (`ReconnectRegistry.cs:22`). A player
dropping in Night N and resuming during RoundEnd/lobby/Day N+1 resumes at their saved
position with their held props — *correct*, because the world is map-scoped and still
exists; deep-woods position at dawn is exactly the walk-back canon wants. One
validation added (A2): resume re-checks each `HeldPropId` still exists and silently
drops missing ones — a night-scoped item (glow stick) whose dawn cutoff destroyed it is
never resurrected by a resume ticket.

**3. Reconnect across a *playthrough* boundary.** Verified live bug: the registry is
not cleared on reset, so a pre-reset resume ticket (stale position, stale props, into a
freshly wiped world) is consumable for 60 s after Play Again. Resolution: the registry
becomes a run-scoped slice (§5.2) — cleared at the boundary; a reconnector after Play
Again joins as a fresh peer of the new playthrough. No id-stamping scheme needed;
clearing is sufficient and simpler.

**4. Server process death mid-transition (or mid-anything).** No disk state exists by
design (§5.1), so there is nothing to half-write; clients' clocks keep extrapolating
until the existing `OnServerDisconnected` → Fail path bounces them
(`CycleDriver.cs:133–146` documents this exact behavior). A playthrough that loses its
process is simply over, ungracefully; **no bleed into the next playthrough is
possible** — a new session is a new process, and even a reused process is covered by
the entry guard (§5.4). Orphan protection for the child process already exists
(`WindowsJobObject` kill-on-close, `LocalServerHost.cs:176`). Resolution: accepted and
documented; crash-*recovery* (rejoinable runs) would require the disk snapshot path in
§5.5's reserved seam and is explicitly not this program.

**5. Reset idempotency and double-fire.** `ResetRun` is already idempotent
(`RunDriver.cs:217–220`). Every slice's `ResetForNewPlaythrough` is *required*
idempotent (§5.3, tested by A2 per slice). Double Play Again: the first request commits
`RoundIntro(1)`; the second arrives with `State != Loss` and is dropped by guard. The
T10 sequence's deliberate double store pass is harmless for the same reason (§5.4).
Duplicate broadcast delivery: applying the same state twice sets the same fields —
idempotent by construction (the `CycleDriver.Apply` reasoning).

**6. Quota met (or banking attempted) in the final seconds, players mid-action.** The
verdict reads `CumulativeBanked` at the verdict tick — one number, one tick, no
ambiguity. A `ServerBank` request in flight when the boundary commits arrives a tick
later, `State == RoundEnd`, and is **denied without consuming the haul** (§2.4) — the
player banks the same items five seconds into the next day (carryover model §2.1 makes
them count fully). Nothing is lost, the verdict is deterministic, and the near-miss
story ("we were three steps from the drop-off") stays tellable. The inverse — quota
*met* mid-action — has no edge at all: banking is discrete and server-serialized, so
"met" is a fact the moment the bank lands.

**7. Quota-fail racing a Play-Again request.** Structurally unrepresentable: a
`RequestPlayAgain` is only valid while `State == Loss`, and Loss is only entered by the
verdict that would have "raced" it; predicates evaluate only at the verdict instant
(§1.4), so there is no window in which both a fail and a Play Again are in flight for
the *same* playthrough. A stale request from a previous Loss screen arriving after T10
committed finds `State == RoundIntro` and is dropped. Resolution: state guards + the
single evaluation point.

**8. Two players affecting the same object as night falls (the brief's example).** The
server serializes all mutation in tick order — last-arrived-wins within existing
per-system rules (e.g. the wallet's spend-gated-on-`TrySpend`-return idiom,
`VendingManager.cs:211`). A band crossing does not lock objects; only PlaythroughState
transitions gate mutation (§5.6). Two grabs of one log at dusk resolve exactly as they
do at noon. Resolution: no new mechanism — the existing authority model already answers
this; the spec's contribution is saying so and forbidding anyone to add
timestamp-based resolution.

**9. Disconnect mid-persistence-write (the brief's example).** Structurally excluded:
clients never write persistence — there is no client-side write of any kind (§5.2,
every system server-authoritative) and no disk write anywhere. A disconnect can strand
an in-flight *request*, which then simply never applies. Resolution: excluded by
architecture; documented so nobody builds a client-side cache "for performance" later.

**10. Every player disconnected when the verdict fires.** The clock ticks with zero
peers ("the island exists without observers", `CycleDriver.cs:111–117`); a night can
resolve to an empty room and Loss can commit unheard — a rejoiner (within the reconnect
window or as a fresh join) lands on the loss screen via the sync. Resolution: accepted
for this program (pausing an empty server is a new mechanism with its own edge
catalog); **surfaced to Talon** as a behavior note in the SD-1 report — a group-wide
wifi blip during a night can genuinely lose a run, mitigated by the 60 s reconnect
window.

**11. Multi-band catch-up in one server tick (hitch).** RunDriver's ordinal walk emits
every missed crossing in order in one tick (`CrossingsBetween`). `PlaythroughDriver`
processes them in delivery order; if the batch contains `NightToDawn`, the verdict runs
at that point in the sequence exactly as it would alone. Resolution: inherited
correctness from the locked layer; A1's tests include a hitch case.

**12. A peer connects during the verdict tick.** Funnel runs post-commit (it is called
from the connect notification, after tick processing) and sends post-verdict state; if
the peer also received the tail of the broadcast stream, duplicate application is
idempotent (§3.4). Resolution: no special case needed; VER exercises it.

**13. Player deep in the woods at RoundEnd/UpgradeLobby.** No teleport (§1.3,
UpgradeLobby). Safety comes from the gating rule, not from position; the walk home is
gameplay, not an error. Resolution: designed-in; B1's lobby treatment must not assume
everyone is at camp (surface note).

**14. Numeric extremes of the open-ended run.** Round number is unbounded: demand
clamps (§2.2); `CycleDriver.CyclesElapsed` is an int (68 years of 720 s cycles —
fine); `CycleBands.DayIndex` clamps at 4, so bands stop escalating at day 5 —
acceptable *mechanically*, but see the canon finding in the SD-1 report (the night-
length escalation table itself contradicts retired canon; not resolved here). Timer
configs clamp ≥ 1 s (§1.7). Resolution: every formula's extremes stated where defined.

**15. Host leaves mid-round / from any state.** `LeaveToMenu` disposes the hosted
server — the match ends for everyone (existing, `PauseOverlay.cs:185–187` already
warns). Unchanged by this program; noted because the Loss screen's Continue rides the
same path. A "host migration / keep session" feature is explicitly not this program.

**16. Legacy `RunEndedSignal` path.** Neutralized, not removed (§1.5): `--run-cycles`
keeps legacy semantics for existing selftests; no new subscriber. Migration item for
B1: `SessionSummaryPanel`'s subscription moves to `RoundEnded`/`RunConcluded`, and its
"Return to Lobby" button label (which actually leaves to menu) gets renamed in
passing.

---

## 7. Sequencing for the downstream packets

Chain: **A1 → A2**, with **B1 parallel to both** against §3 only; all meet at VER-1.

**CORE-PROG-A1 — the machine.** Builds: `PlaythroughDriver` (states §1.2–1.4,
transitions §1.7, timers, predicate chain §1.6, `RunOutcome`), the `CycleDriver.Setup`
additive `startCycles` param + re-anchor fan-out (§1.5), `LaunchOptions` no-cap default
(§1.5), `QuotaSchedule` resource + `QuotaLedger` (§2), funnel additions (§3.4),
client→server requests (§3.3). Construction-order insertion: store (A2 stub or
interface-only) → `PlaythroughDriver` after `RunDriver`, `QuotaLedger` after
`PlaythroughDriver` — each with a why-comment at the site, per house style. Headless
tests: transition table walk, verdict priority, re-anchor forward-only invariant,
hitch catch-up, timer extremes.

**CORE-PROG-A2 — the store.** Builds: `WorldStateStore` + slice interfaces (§5.3),
migration of the **eight** ad-hoc `RunReset` world-state subscribers (not five — see
the report's Corrections; client-local UI latches may stay direct), wiring the
**three** dead resets (`PhotoRegistry:45`, `PolaroidFilmBank:50`, `EelManager:199`),
the `PropManager` slice + **initial-dump restore** (the unwinnable-second-run fix —
regression test: two consecutive playthroughs, second one winnable), `ReconnectRegistry`
slice + held-prop validation (§6 cases 2–3), `WaterService`/`WallMapManager`/
`ArrowManager`/`PlayerSightService` slices, the entry-guard boundary (§5.4). Depends on
A1's `PlaythroughDriver` commit points. Headless tests incl. per-slice idempotency and
the `_seq`-not-rewound rule.

**CORE-PROG-B1 — the screens.** Builds its six surfaces against §3 on a headless fake
driver (`LoopUiTelemetry` pattern); zero dependency on A's code. Extends
`PhaseToastLayer` (never a parallel system); adapts `SessionSummaryPanel` per-round;
loss screen; lobby v0 (content-agnostic pending Q3); quota v0 as worded
stages/diegetic placeholder (§3.5 row 5, via `/direct`); splash/loading and transition
treatments. Feel items queue for `local-talon`.

**Base freshness (required note):** `integration/2026-08-13-night-program` and the
glow-stick sound branch are in flight and touch `Gameplay.cs` wiring — **every PROG
packet re-checks base freshness at its own dispatch time** (re-verify the funnel line
numbers and the `_Ready` insertion points against its actual base; this spec's line
refs are correct for `feat/2026-08-13-core-spine` at dispatch of SD-1 and are expected
to drift). The shared-worktree rule applies: re-check the branch immediately before
every commit.

**CORE-VER-1** restates the packets' acceptance criteria verbatim and adds, from this
spec: multi-peer late join into all six PlaythroughStates, the reset soak (repeated
Play Again), the two-consecutive-playthroughs regression, the gating-rule grep vs A1's
declared consumer list, `RegisteredSliceIds` completeness vs §5.2's table, and positive
controls for every absence check.

---

## 8. Decisions and open forks (summary — full ripeness in the SD-1 report)

**Value calls made here (Decisions):** T_intro 4 s; T_tally 15 s; T_lobby 30 s;
all-ready skip on RoundEnd and UpgradeLobby; timer floor 1 s; quota placeholders
`[3,5,8,12]` × 1.35; carryover model (`carry_surplus = true`, D3); no auto-timeout on
Loss; no teleport into the lobby; RoundIntro control not frozen; unconditional
boundary reset at entry.

**Not resolved here (open with Talon, per the packet):** Q3 (what an "upgrade" is —
the lobby is built content-agnostic); THRILL §9 tone and §6.2 night floor (untouched);
every "needs `/direct`" affect call in §1.3/§3.5; the night-length escalation table vs
retired canon (finding, report); headless-loss-to-an-empty-room (behavior note,
report).
