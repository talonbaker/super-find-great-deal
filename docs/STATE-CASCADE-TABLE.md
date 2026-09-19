# State Cascade Table — the standing row list

**Applies to:** any change to what the player's body *is* — death, downed, stunned,
ragdolled, spectating, frozen. `MECHANICS-BIBLE.md` §2 says every dependent system must
update in the same commit. This document is the list of what "every dependent system"
actually means in Sail, so nobody has to remember it.

**A second cascade lives at the bottom of this file** (see *The dimensions cascade*): what
must update when the player's body changes **size** rather than state. It was added
2026-08-08 after campers replaced the -ufflings as the player character and eight systems
kept the creature's numbers. Rows 1–22 below do not cover it — a swap that leaves every
state machine untouched can still leave a head sticking out of a collider.

**How to use:** copy the table into the spec for the state you are adding. Fill every cell
with `CHANGED (how)` or `UNCHANGED (why safe)`. **A blank cell is the finding.** Do not
delete a row because it feels irrelevant — write `UNCHANGED` and say why. The rows that
look irrelevant are the ones that bite.

## Why this document exists

The failure mode this prevents is always the same shape: *the player's state changed, and
some system was never told.* The controllable-ragdoll bug was five instances of that shape
at once — input not disabled, camera still orbiting nothing, no reset, no defined fate for
the body, and no way for the player to tell what had happened.

You cannot enumerate the *failures* — that space is unbounded. You can enumerate the
*systems*, which is finite and derivable from the repo. Failures are the product of
(state change × system not told), so if the system list is complete, the failures cannot
hide. **Do not brainstorm this list per feature.** It is standing. Add to it when a new
system starts reading player state; never trim it to fit a spec.

## Derivation

Mechanically derived, not recalled: every file under `scripts/` referencing
`SandboxAvatar` (16 files as of 2026-07-20), then traced to what each reads or writes.

**Independently re-derived once.** A second session swept the same tree without seeing this
file and produced a table that agreed on every substantive finding — no liveness bit in
`MoveState`, ragdoll unpredictable, no camera detach, `ActorEvent` 4/5 burned, and a
reconnect that resumes standing. Two independent derivations converging is the strongest
evidence available that the row list is complete. That sweep found two systems this one
missed (rows 21–22) and two constraints (5 and 6 below); they were folded in rather than
shipped as a competing table.

## The rows

| # | System | Where it lives | What it reads/writes | The question your state must answer |
|---|---|---|---|---|
| 1 | **Player input** | `SandboxAvatar.IntentSource` (`:129`), `IIntentSource` | Steering intent every tick | Is input sampled, dropped, or redirected? |
| 2 | **Physics authority** | `AvatarMotor.Step`, `CharacterBody3D` vs. a ragdoll's `PhysicalBone3D` | Who writes the transform | **Exactly one writer per tick.** Entering the state must disable the previous writer in the same commit. |
| 3 | **Client prediction / reconciliation** | `InputRing`, `NetCodec.Snapshot`, `MoveState` | Rewind-and-replay of unacked inputs | Is prediction still valid? Ragdoll physics is *not* deterministic and cannot be replayed. |
| 4 | **Network replication** | `MoveState` (`scripts/net/MoveState.cs:12`) | Position, Velocity, Yaw, Coyote, JumpBuffer, Grounded | Which field on which channel carries liveness? **There is no liveness bit today — this is a wire-format change.** |
| 5 | **Camera** | `SandboxCamera` (`Attach`/`SetOrbit` only) | Follow target + orbit | Target and mode. **No detach/spectate/freeze API exists** — it must be built. |
| 6 | **Nameplate** | `SandboxAvatar.UpdateNameplate` (`:512`) | `GlobalPosition + (0, 1.15f, 0)` | Anchor must move to a rig node. That offset is tuned to a *standing* player crown; over a collapsed body it is empty air. |
| 7 | **Other world UI** | `WorldUi.Suppressed`, `InteractPrompt` | Global suppression flag | Does the interact chip still show for a body that cannot interact? |
| 8 | **Carry / held props** | `CarryController`, `PropManager.HeldPropIdsFor` | Single-slot hold | Is the prop dropped, kept, or frozen? Dropped *where* — at the body, or recovered home? |
| 9 | **Prop interaction reach** | `SandboxAvatar.PickupRadius` (`:45`), `PropManager.TestGrabRange` | Grab arbitration | Can this body still grab? Server and client must agree (INTERACTION §4). |
| 10 | **Voice** | `VoiceRangePublisher` (`:132`), `VoiceSpeaker.VoiceRoute` | Proximity vs. PA routing, re-resolved per tick | Which route? Can they speak, and who hears? *(Adding a route costs zero networking — it reads replicated state.)* |
| 11 | **Avatar visual** | `AvatarVisual`, `PresentationProfile` | Model, squash/stretch, color | What does the body look like, and does the visual-error smoothing still apply? |
| 12 | **Presentation events** | `ActorEvent` (`scripts/game/presentation/ActorEvent.cs:10`) | `.tres`-serialized ordinals | Which event fires? **Ordinals 4/5 are reserved ex-`Death`/`Recovered` — never reused, never reordered.** A third state needs a new ordinal. |
| 13 | **Blob shadow** | `BlobShadow` (`:34`, `:74`) | Attached to the `PhysicsBody3D`, reads avatar render position | Does a collapsed body still cast a standing blob? |
| 14 | **Interact highlighter** | `InteractHighlighter` (`Gameplay.cs:135`, ticked `:428`) | Nearest carryable for the local viewer | Does a downed viewer still shimmer props they cannot reach? |
| 15 | **Spawn / reset** | `SandboxAvatar.ResetToSpawn` (`:300`), `Gameplay.SpawnPositionFor` | Spawn transform | What is the exit path, and is it reachable and tested? |
| 16 | **Reconnect / grace** | `ReconnectRegistry.ResumeData` (`:31`) | `(Position, HeldPropIds, ColorIndex)` | **No liveness is captured.** What does a player who disconnects while downed resume *as*? |
| 17 | **Round loop** | `RunDriver` (`scripts/game/world/RunDriver.cs`) — see note below | `RunCycles`, `RunEnded`, phase-crossing `History` | Does the state survive a round reset, and what happens at round end? |
| 18 | **Telemetry** | `scripts/telemetry/` | Session counters | Is this state counted, and does it distort any existing metric? |
| 19 | **Bots / harness** | `BotHarness` | Drives avatars headlessly | Can the headless suite reach and assert this state? If not, it is untestable. |
| 20 | **Player legibility** | *(nothing today)* | — | How does the player know, within one second and without text, which state they are in? |
| 21 | **Collision layers** | `SandboxAvatar._Ready` (`:347-352`); precedent `sandbox/Carryable.cs:158-159` | Layer/mask membership | Does this body still block, bump, and get raycast? The avatar **never sets a layer** — everything sits on Godot default layer 1, and `project.godot` names none — so there is no layer for a body that should stop blocking teammates. `Carryable` going to layer 0 / mask 0 while held (restored at `:200-201`) is the working precedent. |
| 22 | **Player as interaction target** | `InteractTargeting.Pick` (`:41`), `SandboxAvatar.cs:1021` | Candidate set | Can a teammate act *on* this body? Targeting only ever considers the `Carryable` group — **a player is not addressable as an interaction target today**, so any assist, revive, or carry-a-teammate verb needs a new target path before it can exist. |

*Rows 21–22 added 2026-07-20 from a second, independent derivation (see the note under
Derivation). Row numbering above them is stable — `BEHAVIOR-BIBLE.md` §10.2 cites row 8, and
`MECHANICS-BIBLE.md` §2.5 cites row 20. **Append; never renumber.***

## Hard constraints found while deriving this

These are not opinions; they are properties of the current code.

1. **`MoveState` is the reconciliation contract.** Its doc says *every field that influences
   movement must live here, not in node-private fields*, because reconciliation rewinds and
   replays from it. So if a state changes how the body moves, it **must** be in `MoveState`
   or replay diverges. Adding liveness is a wire-format change, not a flag on a node.
2. **Ragdoll physics cannot be client-predicted.** `AvatarMotor.Step` is deterministic and
   shared by prediction, authority, and replay. `PhysicalBone3D` simulation is not. Any
   ragdoll-driven state is server-authoritative with full round-trip latency.
   `PropManager`'s loose-prop path is the working precedent: server ticks physics, streams
   the transform, latches to rest after 18 slow ticks, recovers out-of-bounds bodies home.
3. **`SandboxCamera` has no detach.** Its public surface is `Attach`, `SetOrbit`, `Yaw`,
   `CameraNode`, `HandlesPauseToggle`. Any state that changes what the camera does requires
   new API, not configuration.
4. **`ActorEvent` ordinals are a serialization contract.** 4 and 5 are reserved former
   `Death`/`Recovered` slots and must keep those meanings if reused at all.
5. **No damage source exists.** Nothing calls `ServerApplyKnockback`
   (`SandboxAvatar.cs:745`) in production code — it is an orphaned hook. There are no
   hazards, hitboxes or enemies, and the only kill volume is the `KillPlaneY = -30`
   out-of-bounds recovery (`sandbox/Carryable.cs:27`, checked at `SandboxAvatar.cs:720`).
   The first player-failure package therefore has to build its own **cause** as well as its
   effect, which is roughly half the work and is not scoped anywhere yet.
6. **The never-lose-control invariant is written in the source, and Dead reverses it.**
   `SandboxAvatar.cs:24-28` states it as a design rule attributed to Talon by name — *"the
   player NEVER loses control. There is no reaction state… nothing ever locks input or
   interrupts an action"* — and commit `9d1d183` removed the previous death path on exactly
   that ground. **Downed is compatible** (control is impaired, never taken); **Dead is not.**
   So those comments are rows that must be *edited*, not prose that merely drifted. Leaving
   them in place while shipping Dead is how the next agent reads them as law and quietly
   removes the state again — which is what happened to the last one.

**Row 17 update (2026-08-06, L1 / Issue #104).** A round loop now exists: `RunDriver`
(`scripts/game/world/RunDriver.cs`) answers the row's own question directly — a
server-authoritative `ResetRun()` rewinds `CycleDriver.CyclesElapsed` to 0 (re-invoking
`CycleDriver.Setup`, its own existing public entry point, never a new hook into it) and
broadcasts a reliable, ordered `RunReset` event every peer applies identically
(`BroadcastRunReset`, `CallLocal = true`); "what happens at round end" is the `RunEnded` edge
(fires once, at the DawnToDay crossing that closes the final configured cycle) and its
`RunEndedSignal` event. **This table's own declared scope is player-BODY state** (death,
downed, ragdoll, spectate) — L1's reset touches none of it: no avatar is moved, no input is
disabled, no camera detaches, nothing in rows 1–16 or 21–22 changes at all
(`UNCHANGED — RunDriver never reads or writes SandboxAvatar/player state`). Only two other
rows are actually live here:
- **Row 18 (Telemetry):** `UNCHANGED (deferred)` — L1 adds no telemetry counters; a future
  Story (L7's payout, L11's summary) may want a "run completed"/"run reset" event and should
  hang it off `RunEndedSignal`/`RunReset` rather than re-deriving it.
- **Row 19 (Bots/harness):** `CHANGED` — `BotHarness` now polls `RunDriver.Instance` every
  sample (`runCycles`, `runEnded`, `runSynced`, cumulative `runHistory`) the same way it
  already polls `CycleDriver.Instance`, and `Run-RunDriverTest.ps1` is the headless proof this
  row asks for: exactly-once-in-order crossings, late-join sync, run-end, and reset —
  see that suite for how each is anchored on the JSONL stream.

L7 (wallets) and L11 (summary UI) are the systems this row was written for and are exactly
the ones L1 does **not** reset — `RunReset` is the hook they subscribe to and reset
themselves; a Story that adds a new subscriber to `RunReset`/`RunEndedSignal` without actually
resetting its own state is the "we forgot system X" failure this table exists to catch, same
as any other row.

## Row 20 is a gap in the bibles, not just in a feature

No bible currently requires that **a state change be legible to the player it happened
to.** `INTERACTION-BIBLE.md` §2 covers feedback, but is scoped to player-facing
interactables — things the player acts *on*, not things that happen *to* them.
"It was unclear what even happened" was a real defect in the original ragdoll and no
existing rule catches it. Proposed addition, to `MECHANICS-BIBLE.md` §2:

> **2.5 A state change must be legible to the player it happened to.** Within roughly one
> second, without reading text, the player must be able to tell that their state changed
> and *which* state they are now in. Two states that a player can be in for more than a
> moment must be distinguishable from each other, not merely from normal play.

**Status: already adopted on the contracts branch — you do not need to answer this twice.**
It landed verbatim as `MECHANICS-BIBLE.md` §2.5 in PR #23
(`feat/mechanics-family-terrain-contracts`), together with its multiplayer generalisation
`INTERACTION-BIBLE.md` §8 — §2.5 asks whether the *actor* can perceive the change, §8 asks
who **else** must and through which channel. That branch's §2.5 cites this file and this row
by number, which is why rows must be appended and never renumbered. If PR #23 merges first,
this section is history rather than a proposal; if this PR merges first, it is the record of
where the rule came from.

## Filled: the water states (2026-08-08, W2 — lake water contract)

The first package to fill this table against a real player-body state change. Three new
states ride `MoveState`: `Water` (`Dry`/`Wading`/`Swimming`, derived from submersion depth),
`Soaked`, and `ControlLocked` (the sputter-out's ~3.5 s lock). Authority:
`docs/superpowers/specs/2026-08-08-lake-water-contract-design.md`.

| # | System | Verdict |
|---|---|---|
| 1 | Player input | **CHANGED.** `AvatarMotor.Step` zeroes MoveDir/Jump/Sprint when `ControlLocked`. Input is still *sampled* and still streamed — only its effect is dropped, and dropped in the one deterministic place all three simulation paths share, so prediction cannot disagree with authority about it. |
| 2 | Physics authority | **UNCHANGED.** `CharacterBody3D` + `MoveAndSlide` remains the sole writer in every water state. No ragdoll, no second writer: swimming is a deterministic vertical `MoveToward` inside the same `Step`, and the go-under is the same thing at a fixed rate. Spec §1.4 forbids water physics outright. |
| 3 | Prediction / reconciliation | **CHANGED.** All three fields live in `MoveState` (hard constraint 1 below: hysteresis makes `Water` history-dependent, so a replay from a different water state re-diverges forever). `PredictionMatches` compares all three exactly — without that, a positionally-matching prediction would take the early-return and never adopt a server-set `ControlLocked`, handing the player ghost control through the reconciliation door. |
| 4 | Network replication | **CHANGED.** Two water bits, `Soaked` and `ControlLocked` packed into the snapshot's existing flags byte (`span[5]`, which used one of its eight bits). Packet length unchanged at 51 bytes; `NetProfile.ProtocolVersion` 8 → 9 because the byte's *meaning* changed and a v8 peer would parse a v9 snapshot with no error and simply never see anyone swim. |
| 5 | Camera | **CHANGED.** `SandboxCamera` drops the focus point to `WaterY + 0.15` while the target is `Swimming` — applied to the focus, not the lens, so the orbit rig, the occlusion clamp and the spring arm are untouched. No detach and no new mode, so constraint 3 below is not triggered. |
| 6 | Nameplate | **UNCHANGED (safe).** The body stays upright and standing-shaped in every water state — there is no collapse — so the `+1.15` crown offset still lands on a head. The sputter-out's prone moment is covered by a full-screen cut to black, during which nothing is rendered to hang a nameplate over. |
| 7 | Other world UI | **UNCHANGED (deferred).** `WorldUi.Suppressed` is untouched; a wading player can still see interact chips, which is correct — wading is ordinary play. A swimming player has nothing in reach to prompt for (the lake is deliberately empty, spec §1.1). Revisit when the lake gets content. |
| 8 | Carry / held props | **UNCHANGED, deliberately and by decision.** Spec §4: "carried items are NOT dropped". We chose "tape, not everything"; there is no underwater retrieval loop in this scope, so dropping a bow in the deep would create an unrecoverable loss with nowhere to recover it from. |
| 9 | Prop interaction reach | **UNCHANGED.** No radius changes. See row 7 for why nothing is in reach anyway. |
| 10 | Voice | **UNCHANGED (noted).** Water does not alter routing or range. Spec §2 lists "voice legibility" as one of the night lake's four denial channels, but that is an *audio* treatment and belongs to packet W4, not to a route change here. |
| 11 | Avatar visual | **CHANGED.** `AvatarVisual.SetSoaked` lays a shared glossy `MaterialOverlay` over every mesh part — reversible with one null assignment, and it never touches the palette work `BuildAppearance` owns. Driven from `MoveState.Soaked` inside `AnimateVisual`, so owner, remote proxy and offline sandbox all get it from the identical signal. Dripping and flat hair (spec §7) are art debt, named. |
| 12 | Presentation events | **UNCHANGED.** No `ActorEvent` ordinal is taken. Water raises its own five-event stream on `WaterService`, which W4 subscribes to — a new channel rather than a reuse of the reserved 4/5 slots. |
| 13 | Blob shadow | **UNCHANGED (noted).** A swimmer still casts a blob on the lakebed several metres below. Harmless (it is a soft dark ellipse under black water) and, at night, invisible. Flagged rather than fixed: switching it off is a one-line cosmetic call whenever it reads wrong in a headed pass. |
| 14 | Interact highlighter | **UNCHANGED.** See rows 7 and 9. |
| 15 | Spawn / reset | **CHANGED.** The sputter-out relocates through the existing `ServerTeleportTo` (epoch bump = the codebase's own "snap, never smooth"), so it inherits the whole tested teleport path. `SpawnStatePreservingWaterFlags` keeps `Soaked`/`ControlLocked` across it — wiping them would hand the player one tick of steering inside the cut to black. |
| 16 | Reconnect / grace | **CHANGED (explicitly, to "not resumed").** `ReconnectRegistry.ResumeData` is NOT extended; `Gameplay.OnPeerDisconnected` calls `WaterService.ForgetPeer`. A resuming player keeps their last authoritative position, so if they were in the lake they are still in the lake — but the cold clock restarts at zero. That is the forgiving reading, and it is a decision, not an omission. |
| 17 | Round loop | **UNCHANGED (deferred).** `WaterService` does not subscribe to `RunDriver.RunReset`. Nothing persists across a run today except `Soaked`, which any player can clear at the fire in 20 s. Worth wiring when the run reset starts mattering; not worth a subscription that does nothing yet. |
| 18 | Telemetry | **UNCHANGED (deferred).** No counters. "Sputter-outs per session" and "tapes lost to water" are obvious future metrics and should hang off `WaterService.Sputtered` / `TapeRuinRequested` rather than being re-derived. |
| 19 | Bots / harness | **CHANGED.** `tests/Run-WaterTest.ps1` + `scripts/game/water/WaterSelfTest.cs` reach every state headlessly and measure them off a real `CharacterBody3D`, and the whole arithmetic tier is in `dotnet test`. The state is testable, which row 19 says is the bar. |
| 20 | Player legibility | **CHANGED.** Frost closing in from the screen edge (client-local, no shader, no HUD bar), an irregular quickening controller rumble, and a hard cut to black for the sputter-out. No text anywhere. The three states are distinguishable *from each other*, which is §2.5's actual bar: Wading reads as slowed with the camera unchanged, Swimming drops the eye to the waterline, and a sputter-out is a screen going black. |
| 21 | Collision layers | **UNCHANGED.** No layer or mask change. A swimming player still blocks and is still raycast — there is no reason for water to make a body pass through anything, and changing layers here would be the "always-safe tile" of collision. |
| 22 | Player as interaction target | **UNCHANGED.** No teammate-assist verb. Rescuing a drowning friend is a real design idea and it is out of scope (spec §11); the cold collecting you is the only exit, and it is guaranteed to work from everywhere. |

**Against the hard constraints below:** (1) satisfied — all three fields are in `MoveState`,
not on the node. (2) not triggered — no ragdoll anywhere in this package. (3) not triggered
— the camera's focus point is configured, not detached. (4) not triggered — no `ActorEvent`
ordinal used. (5) not triggered — water is not damage and no death path exists or is added.
(6) **triggered, and edited rather than left standing** — the never-lose-control comment at
`SandboxAvatar.cs` now carries the one named exception and the four conditions that bound it.

> **Addendum, 2026-08-29 (WATER-3): constraint (5) above is now out of date, deliberately.**
> Talon ruled on 2026-08-29 that the water kills — *"the water should kill the player after about
> three seconds"* — so a death path through water now exists. **It changes nothing in the table
> above**, and that is why this is an addendum rather than a re-fill: the drowning authors no new
> state, no new `ActorEvent` ordinal, no new `MoveState` field and no new replication channel. It
> calls `RespawnService.ServerKill(peer, RespawnCause.Drowned)` — the single kill seam PLAYTEST-1
> built for exactly this — so every row a death touches was already filled by that package: the
> drop funnel, the `ServerTeleportTo` epoch bump (row 15), the `Died`/`Respawned` broadcast and
> the comic beat. The one thing the water side owns that a death must not leave running is the
> submersion clock, and `ServerKill` clears it for **every** cause rather than only for drowning,
> so a body that leaves the map while submerged cannot respawn already three seconds into a breath
> it is no longer holding.
>
> **Row 20 (player legibility) is the one open item, and it is named rather than claimed.** The
> three water states are still distinguishable from each other as this table records. A death *by
> drowning* and a death *off the edge* are not: `RespawnCause` rides the `Died` event and nothing
> consumes it, so both play the same loft-and-spin. That is a §2.5 gap owned by a presentation
> packet, stated here so it reads as unhandled rather than as handled.

## Filled: the failure states (2026-08-09, phase 1c — Knocked Out, Frozen, the impulse ragdoll)

The package this table was originally written for. Two states ride `MoveState`: `Incapacity`
(`Active`/`KnockedOut`/`Frozen`, two bits, **one of four values reserved** for a third state)
and `ImpulseRagdoll` (one bit). Authority:
`docs/superpowers/specs/2026-08-09-steam-beta-mvp-PLAN.md` §10, Issue #179,
`BEHAVIOR-BIBLE.md` §10.

**The four rows §10.2 named as *missing API, not configuration* are rows 2, 3, 5 and 8.** All
four are built here, and all four are applied through one ordered transition
(`IncapacityCascade.Apply`) whose revocable rows run *before* the replicated commit and whose
single irreversible row runs *after* it — so a transition that fails partway either did not
happen at all or left every state system agreeing. See that class's own doc for the argument
and `IncapacityCascadeTests` for the proof, including a negative control that drops each row
in turn and requires the verifier to catch it.

| # | System | Verdict |
|---|---|---|
| 1 | Player input | **CHANGED.** `AvatarMotor.Step` discards MoveDir/Jump/Sprint whenever `AvatarMotor.ControlLockedBy` is true — the union of the water lock, incapacitation and the impulse ragdoll, expressed **once**. Input is still sampled and still streamed; only its effect is dropped, in the one deterministic place all three simulation paths share, so prediction cannot disagree with authority about it. The three reasons deliberately share one seam: "is this body under player control" must have a single answer, not three that can drift. |
| 2 | Physics authority | **CHANGED — and answered by ADDING NO SECOND WRITER.** `CharacterBody3D` + `MoveAndSlide` remains the sole transform writer in every failure state. **This package ships no `PhysicalBone3D` ragdoll at all**: a comic ice block and a body flat on its back are *poses*, and buying them with physics would have bought hard constraint 2 (ragdoll physics cannot be predicted or replayed) for no gameplay gain and against the register. The drag verb is the only case where another player influences this body, and it rides `ServerSetDragVelocity` → the body's own `MoveState.Velocity` → the same `Step` — the dragger never writes another body's transform. |
| 3 | Prediction / reconciliation | **CHANGED.** Both fields live in `MoveState` (hard constraint 1 — they influence movement, so a replay from a different value re-diverges). `PredictionMatches` compares both **exactly**; without that, a positionally-matching prediction (which is *every* prediction the tick a stationary player is knocked out) would take the early return and the client would never adopt the state — ghost control through the reconciliation door. Beyond that, **owner prediction is suspended outright while incapacitated**: `Reconcile` hard-adopts the authority and clears the ring. §10.2's stated reason was ragdoll physics; the operative reason here is the drag, whose velocity depends on where *another* player is standing and which the owner therefore cannot derive. Server-authoritative at full RTT, exactly as the bible says. The impulse ragdoll is deliberately **not** suspended — it injects no external velocity, so ordinary rewind-and-replay resolves it. |
| 4 | Network replication | **CHANGED.** Two bits for the state (snapshot flags byte, bits 5–6) and one for the impulse (bit 7). Packet length unchanged at 51 bytes; `NetProfile.ProtocolVersion` 9 → 10 because the byte's *meaning* changed and a v9 peer would parse a v10 snapshot with no error and simply never see a teammate go down. **The snapshot flags byte is now FULL** — said in `NetCodec` and again in `NetProfile`. The reserved fourth state value folds to `Active` on unpack. `IncapacitationService` adds an event stream and a late-join dump on the new `IncapacityChannel` (11) for the *cause*, which the state alone does not carry and which selects the skin. |
| 5 | Camera | **CHANGED — the missing API is built.** `SandboxCamera` gains `Detach()`/`Reattach()` (a real severance, for a future spectate mode and for teardown) and `SetInputDetached()` (follows, ignores mouse *and* stick, locks the orbit). Incapacitation uses the latter, and the split is a register decision as much as a technical one: the player must stop **driving** the camera — the half the shipped controllable-ragdoll defect got wrong — but keeps **seeing**, because watching yourself slide across camp as an ice block is the comedy the register law wants and a black screen throws it away. The pause toggle stays live; a downed player can still reach the menu. |
| 6 | Nameplate | **CHANGED.** The row that names the repo's own defect — "the nameplate floating over a headless body". `NameplateHeightM` is a gap above a *standing* crown; over a body lying flat it is a metre of empty air with a name in it. A Knocked Out player's plate drops to `HalfWidthM + 0.25`, derived from the measured body rather than typed. Frozen keeps the standing height — it *is* standing, just frozen — which doubles as a distance cue telling the two states apart. |
| 7 | Other world UI | **CHANGED (derived).** `WorldUi.Suppressed` is untouched; the interact chip and every other prompt key off the same replicated `ControlDeniedNow` the gates use, so there is no second flag to forget to set. A body that cannot interact shows no prompt. |
| 8 | Carry / held props | **CHANGED — and it is gameplay, not cleanup.** `PropManager.ScatterHeldBy` empties the loadout, broadcasts it, then releases every held prop into **Loose** (not Resting) with each fanned by the golden angle off the holder's facing, so a full loadout lands as a spread rather than a stack. Loose is the thrown-prop mode, so everything lands re-pickup-able through the already-tested settle-and-latch loop with no new lifecycle. Beta plan §10 makes this feed the fire economy: wood you were carrying is now on the ground and somebody has to come for it. Distinct from `ReleaseHeldBy` (disconnect), which stays tidy-and-findable — that difference is deliberate. |
| 9 | Prop interaction reach | **CHANGED.** No radius change; the *gate* changes. `PropManager.ControlDenied` refuses grab, drop, throw and slot-select server-side, and `SandboxAvatar` gates the same three intent handlers client-side at their shared call site so the local player's keys feel dead the same instant their steering does. Both halves read the identical replicated field, so they cannot disagree, and a doctored client that skips its own gate is simply ignored. |
| 10 | Voice | **UNCHANGED (noted, and it is a live blank).** No routing or range change: a knocked-out player can still talk, which is almost certainly right — calling for help is the co-op verb this state exists to create. But **"what an incapacitated player may do (voice, spectate, nothing)" is `BEHAVIOR-BIBLE` §10.4's blank and it is Talon's**, carried into PR #187's draft. This packet ships the forgiving reading rather than resolving it. |
| 11 | Avatar visual | **CHANGED.** `AvatarVisual.SetIncapacity` pitches the rig flat for Knocked Out (on the root, never on `_body`, which `Animate` owns) and lays a frost overlay plus a translucent emissive block for Frozen. `Animate` early-returns while incapacitated — a knocked-out player whose legs keep waddling and whose chest keeps breathing is the "some system was never told" shape. The overlay restores the wet sheen rather than clearing to null, so a player who froze while Soaked does not thaw dry. |
| 12 | Presentation events | **UNCHANGED.** No `ActorEvent` ordinal is taken; the reserved 4/5 ex-`Death`/`Recovered` slots stay reserved. `IncapacitationService` raises its own event stream instead — a new channel rather than a reuse, exactly the precedent W2 set. |
| 13 | Blob shadow | **UNCHANGED (noted).** A fallen body still casts a standing-sized blob directly under itself. Harmless at the scale involved and invisible at night, which is when this state mostly happens. Flagged rather than fixed: resizing it is a one-line cosmetic call whenever a headed pass says it reads wrong. |
| 14 | Interact highlighter | **CHANGED (derived).** Same predicate as rows 7 and 9 — a downed viewer no longer shimmers props they cannot reach, because the gate is asked rather than pushed. |
| 15 | Spawn / reset | **CHANGED.** `SpawnStatePreservingWaterFlags` now preserves `Incapacity` and `ImpulseRagdoll` alongside the two water flags, for the same reason one notch stronger: wiping them would hand a knocked-out or frozen player one tick of steering across any teleport that reaches them — including the sputter-out's own hard cut, which is precisely the teleport that lands a player on the shore about to be frozen. One tick of a controllable ice block is the whole defect in miniature. |
| 16 | Reconnect / grace | **CHANGED (explicitly, to "not resumed").** `ReconnectRegistry.ResumeData` is NOT extended; `Gameplay.OnPeerDisconnected` calls `IncapacitationService.ForgetPeer`. A resuming player comes back Active. That is the forgiving reading and it is a decision — but here it is also **load-bearing**, not merely kind: a departed peer left in the machine dictionary still counts toward `AllConnectedIncapacitated`, so a player who disconnected while down would arm the run's only hard loss condition forever from outside the session. |
| 17 | Round loop | **CHANGED.** `IncapacitationService` subscribes to `RunDriver.RunReset` and wipes every machine *including the injury ledger* — a summary screen showing the previous run's frostbite is exactly the defect this row exists to catch. It also subscribes to `RunDriver.PhaseCrossed` for the dawn floor (row 20's note, and §10.5). |
| 18 | Telemetry | **UNCHANGED (deferred).** No counters. "Knockouts per night", "freezes per night", "rescues by teammate vs. by dawn" and "runs ended by all-incapacitated" are the obvious metrics and should hang off `Incapacitated`/`Recovered`/`AllIncapacitatedSignal` rather than being re-derived. `LastExit` already distinguishes a rescue from a sunrise. |
| 19 | Bots / harness | **CHANGED.** `tests/Run-IncapacityTest.ps1` + `scripts/game/failure/IncapacitationSelfTest.cs` reach every denial state headlessly and measure them off a real `CharacterBody3D` — a player pushing full stick, sprint and jump travels zero metres; a frozen body cannot self-propel but does slide when the server injects the drag velocity; a frozen body over a bottomless deep is held at the swim line where a teammate can reach it. Every denial is paired with a positive control. Registered last in `Run-AllTests.ps1`. The whole arithmetic tier is in `dotnet test`. **Named gap:** the service's own integration (the dawn floor firing off a live `RunDriver`, the night-water join, the prop scatter) needs a full networked session and is a bot-suite shape, not a self-test scene — owed, not skipped. |
| 20 | Player legibility | **CHANGED.** Three channels at once, so any one carries the read through night, fog and distance: **silhouette** (flat on the ground vs. standing rigid inside a box), **colour** (nothing added vs. a pale blue that appears nowhere else in the camp palette), and **motion** (birds orbiting vs. total stillness — a frozen player is the only thing in this game that does not move at all). No text anywhere. §10.3's actual bar is that the two are distinguishable **from each other**, and all three channels separate them. The rescuer's own feedback is the streamed assist progress (`KnownAssistProgress`), per INTERACTION-BIBLE §2 — a verb that takes five seconds must show it is working. **Owed:** the side-by-side headed capture Issue #179 asks for. This packet was dispatched headless-only because a sibling agent owns the GPU lane; that capture is a weekend item, not a claim. |
| 21 | Collision layers | **UNCHANGED.** No layer or mask change, and that is the answer rather than an omission: an ice block must still block, still bump and still be raycast — it is a solid object a teammate shoves across camp, and making it pass through things would be the "always-safe tile" of collision. A fallen body likewise still occupies its space. |
| 22 | Player as interaction target | **CHANGED — the row's own prerequisite is built.** This row said a player is not addressable as an interaction target, because `InteractTargeting.Pick` only ever considers the `Carryable` group, so *"any assist, revive, or carry-a-teammate verb needs a new target path before it can exist"*. The new path is server-side and proximity-based rather than an extension of `Pick`: the server latches a genuine Interact rising edge out of the authoritative input stream (`TakeServerInteractEdge`) and resolves the nearest incapacitated teammate within `AssistRadiusM`. The owning client runs the identical pure query over the identical replicated facts to suppress its own grab for the same press, so both sides agree about what one keypress meant. |

**Against the hard constraints:** (1) satisfied — both fields are in `MoveState`, not on the
node. (2) **not triggered, deliberately** — no ragdoll physics anywhere in this package; see
row 2. (3) **triggered and answered** — `SandboxCamera` now has the detach API it lacked. (4)
not triggered — no `ActorEvent` ordinal used. (5) **partly answered** — the cause interface is
built (`IncapacityCause`), but no creature exists to raise it yet, which the constraint itself
predicted ("the first player-failure package therefore has to build its own **cause** as well
as its effect"); the one live cause is the lake, wired through the shipped `ChillClock`. (6)
**triggered, and edited rather than left standing** — the never-lose-control comment at
`SandboxAvatar.cs` already carried the water contract's named exception with four bounding
conditions, and incapacitation joins it under the same four (bounded in time by the dawn floor,
replicated rather than inferred, taken wholly, and a named exception rather than a general
licence).

**"Dead" is not shipped and not decided.** Beta plan §3.2 leaves the category to Talon and
PR #187 drafts §10 to read correctly either way. The wire reserves a fourth state value, so
adding one later costs a doc line and a `StateForCause` row rather than a protocol bump.

## Scope note

Derived 2026-07-20 against `master` `b43f5d7`. Its first real test was expected to be the
Downed/Dead package; in the event, W2's water states got there first (see the filled table
above). **Passing looks like:** every row filled before
implementation starts, and the resulting states shipping without a "we forgot system X"
follow-up fix. If a defect lands in a system that has a row here, the row was filled
carelessly; if it lands in a system with no row, add the row.

This list covers *wiring*, not *feel*. No table can tell you whether a mechanic is fun —
that is what playtest is for, and keeping the wiring questions out of playtest is the
entire point.

---

# The dimensions cascade — when the player's body changes SIZE

**Applies to:** any change to how big the player's body is. Adding a character to the
roster, swapping which character a player spawns as, rescaling a model, or reshaping one in
Blender. This is a **different axis** from the state cascade above and the rows are
different: nothing about death, input ownership or replication moves when a body gets
taller, and every row below is untouched by a body going Downed.

**Why it exists.** On 2026-08-08 the player character changed from an -uffling (a squat
creature, 0.93 m crown, 1.01 m wide) to a player (a human kid, 1.17–1.27 m crown, 0.40–0.91 m
wide) and **eight independent numbers stayed behind**, because none of them recorded where
they had come from. The visible defect was a player's head sticking through its own 0.9 m
collision capsule — but the same swap had also put the aim ray 20 cm below its eyes, drawn
the nameplate across its face, and left a shadow sized for something twice as wide. Exactly
the failure shape the state table was written for, on an axis the state table does not cover.

**Derivation.** Mechanically derived, not recalled: every literal metre value in `scripts/`
positioned relative to an avatar, traced back to the body it was tuned against.

**How to use.** Same rule as above: fill every cell with `CHANGED (how)` or
`UNCHANGED (why safe)`; a blank cell is the finding. The strong default for every row is
**derive, do not retype** — `AvatarProportions` turns a measured body into each number
below, and adding a seventeenth character should require no code at all.

| # | System | Where it lives | Sized against | The question |
|---|---|---|---|---|
| D1 | **Collision capsule** | `SandboxAvatar.ApplyProportions` | crown + half-width | Does the collider contain the whole visible model, and is it no wider than the silhouette? A model outside its capsule passes through ceilings and takes no part in collision. |
| D2 | **Aim / shot origin** | `SandboxAvatar.AimOriginGlobalPosition`, consumed by `ArcheryController` and `PolaroidManager` | measured eye centre | Where does an arrow or a Polaroid frame leave from? This is gameplay, not cosmetics — the wrong height clips shots on cover the player can see over. |
| D3 | **Nameplate anchor** | `SandboxAvatar.UpdateNameplate` | crown + fixed gap | Does the plate clear the head, or is it drawn across the character's face? |
| D4 | **Blob shadow** | `BlobShadow.Radius`, set in `ApplyProportions` | half-width | Does the grounding cue match the footprint the player sees? |
| D5 | **Carry + stow anchors** | `AvatarProportions.CarryAnchorRestLocal` / `StowAnchorRestLocal` | crown (height) + half-width (reach) | Does a held item sit at the chest and clear the body, or float / clip? Vertical offsets follow height; horizontal offsets follow width — a taller, slimmer character needs both to move in opposite directions. |
| D6 | **Follow camera focus** | `SandboxCamera.FocusHeight` | crown | Is the camera framing the head or the belt? Also feeds the focus-clamp ray origin, which must stay inside the character's own collider. |
| D7 | **Proximity voice emitter** | `VoiceManager.HeadPositionOf` | measured eye centre | Does the voice come out of the speaker's face, or out of the air above them? Was an absolute 1.6 m — adult head height, in a game with no adults. |
| D8 | **Appearance rebuild** | `SandboxAvatar.AvatarKey` setter | — | A character's identity can change mid-session (the avatar-key synchronizer catching up to the authority's pick). Does every dimension above re-derive, or does the new mesh wear the old body's numbers? |
| D9 | **Determinism across peers** | `AvatarProportions.For` | the imported `.glb` | Do the server, the predicting owner and every proxy derive the *identical* capsule? The capsule participates in `AvatarMotor.Step`, so a per-peer body is a silent desync source. Measurement must be a pure function of the model. |
| D10 | **Spawn + level clearance** | `Gameplay.SpawnPositionFor`, the camp geometry | crown + half-width | Does the body still fit through the doors, gaps and stairs it is expected to use — and does a *narrower* body now fit through gaps that were never meant to be passable? |
| D11 | **Animation amplitudes** | `AvatarVisual` (`WaddleHop`, `MaxBodyTilt`, squash/stretch), `BodyLanguageProfile` | tuned by eye on the -uffling | Does a displacement authored for a 0.93 m blob still read on a 1.25 m figure? **Deliberately NOT derived** — these are feel constants that need a headed pass, and silently rescaling them would be a feel change wearing a bug fix's clothes. Answer the row; do not automate it. |
| D12 | **Rig topology** | `AvatarVisual.BuildWholeFigure` vs the harvest path | — | The -uffling keeps its feet outside the squash/hop node so they stay planted; a whole-figure player does not, so its feet ride the hop. Which parts are *supposed* to stay on the ground? |
| D13 | **Scale datums in labs and docs** | `BuildingLab.CamperVisibleM` / `CamperColliderM`, `CampCaptureLab`, `CamperFaceLab` | whatever was measured at the time | Every capture lab judges architecture against a stated human height. Does that datum still name the character it claims to? |

**Scope note (2026-08-08).** Derived on `fix/player-proportions` against `feat/face-wiring`.
D1–D9 are now derived from measured geometry and pinned by
`SandboxSelfTest.RunProportionsTestsAsync`, which asserts relationships (the capsule contains
the model, the eyeline is inside the head) across **every** roster entry rather than literals
against one. D10–D13 are open and are answered per change, not automatically. **Passing looks
like:** adding character seventeen requires no code, and no "we forgot system X" follow-up.
