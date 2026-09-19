# PROGRAM — Super Find Great Deal: three-room hide-and-startle MVP

Date: 2026-09-19. Orchestrator: FABLE_SUPERMARKET (wrangles, reviews, verifies — never implements).
Lanes: Opus `general-purpose` only. Target repo: `C:/repos/super-find-great-deal`
(github.com/talonbaker/super-find-great-deal, `main`). Godot 4.7 C#/mono, .NET 8, Windows.
Rules that carry over: max two Godot-launching lanes at once; light R&D gates per lane
(xUnit + one smoke + captures); the final gate is Talon's own launcher with a human at the keyboard;
when the program hits its definition of done, stop cutting packets and hand him one directory + one command.

Brief: Talon's "Super Find Great Deal MVP — Playtest Brief" (2026-09-19). This document is the plan
against it. Prior directions (explosion-jump, bike, moped, third-person movement) are paused and are
not referenced or reused here except where a subsystem is named below as a copy source.

---

## 0. Repo survey — what exists and what we reuse

Three repos were surveyed in full, including all-branch history (mp-foundation, Watis_Game, Sail).
Lineage: `mp-foundation@8d07fed` → extracted into `Sail@65931fde` → MVP-pruned into
`Watis_Game@40980dc`. Watis_Game `origin/main @ 93bef3d` is the newest, best-factored foundation.

| Need (from the brief) | Found | Where | Verdict |
|---|---|---|---|
| Networked pickup / put-down | `CarryController` + `Carryable` + `NetworkedProp` + `PropManager` (server-authoritative grab arbitration, Held/Loose/Resting replication, late-join dump, disconnect release, reconnect restore). Drop + throw only; **no rotate, no place**. | Watis_Game main, `scripts/game/props/`, `scripts/game/sandbox/` | **Reuse as the authority layer.** |
| R.E.P.O.-style carry (hand-held physics feel, place by hand) | `scripts/game/sandbox/feel/` — `Interactor` (critically-damped spring driving a frozen kinematic body, heft dials, swing, droop, throw inherits item velocity), `Interactable` component, `InteractionSlot` (the **place** verb, snap slots with tag/verdict), hover outline shader, headless self-test `Run-FeelTest.ps1`. **Entirely local, zero netcode.** Built after Talon's 2026-08-30 notes that cubes "aren't actually being held". | Watis_Game `origin/main` only (not in the checked-out worktree), also on disk at `C:/repos/Watis-feel2` | **Reuse; wrap in PropManager's authority (CARRY-1).** |
| Proximity voice → intercom | Opus over Concentus, blind server relay, `VoiceRoute { Proximity, Pa }`; the **PA route is already an intercom**: no falloff + a 3-effect bus (overdrive → 2.2 kHz lowpass → boxy reverb). Routed per talker by `VoiceManager.PaResolver`; the server-side `VoiceProximityGate` must read the same resolver or it silently mutes PA. | Watis_Game `scripts/voice/`, `scripts/net/VoiceProximityGate.cs` | **Reuse directly; swap the resolver predicate to "talker is in a different room than me" (VOICE-1).** |
| Buttons that actually work | `IPressable` (client asks, server adjudicates), `InteractTargeting.Pick` (aim cone beats type), `InteractHighlighter`, `InteractPrompt` + `ControlGlyphs` (live key glyph). The one shipped in-world control, `BubbleResetLever` (792 lines), failed **three** playtest rounds: (1) debounce mistaken for confirmation; (2) world-space sign hidden behind the player's own third-person body; (3) warning never said which key. `INTERACTION-BIBLE.md` §2/§3/§5/§7 record the laws. | Watis_Game `scripts/game/sandbox/IPressable.cs`, `InteractHighlighter.cs`, `scripts/ui/InteractPrompt.cs`, `docs/INTERACTION-BIBLE.md`, `docs/playtest/2026-08-29-*.md`, `2026-08-30-*.md` | **Reuse the interfaces and the lessons; build one small `RoundButton` on them (BTN-1). Failure (2) evaporates in first person.** |
| Round / state machine | `scripts/game/round/` — engine-free `RoundLoop` (`Gathering → Countdown → Round → Tally`), one-tick reset edge, absolute-value wire so a late joiner is complete from one message, int identities (a byte-clamp bug already fixed), reserved channel 21. Never merged. | Watis_Game `origin/feat/2026-09-15-session-2-round-loop` | **Reuse the shape; rewrite the phases as the hide-seek loop (ROUND-1).** |
| Moving players between rooms | `TvPortal` + `TvPortalHost`: server-authoritative teleport with a prediction-epoch bump so the owner snaps clean; Sail `HouseDoor` adds a landing allowlist. | Watis_Game `scripts/game/world/TvPortal.cs`, `scripts/game/bubble/TvPortalHost.cs` | **Reuse the epoch-bump teleport as the room-transition primitive, driven by round phase, not by walking into a screen (ROUND-1).** |
| Doors | `TinyDoor` (hinged leaf, tween 108°, blocker collider) — **local only**, ported with `StartOpen` forced true because making it authoritative "means a new networked interactable". | Watis_Game `scripts/game/world/puffinlab/TinyDoor.cs` | **Reference only. DOOR-1 builds the burst door as round state, not as a node with its own RPCs.** |
| First person | **None, anywhere, in any history.** Sail 2026-07-25 explicitly excluded it. But the seam is clean: `AvatarMotor.Step` is camera-agnostic; `MoveIntent` already carries world-space `MoveDir` + `AimYaw/AimPitch`; `AvatarProportions.EyeHeightM` / `AimAnchorLocal` is a ready eye mount (voice already emits from it). | Watis_Game `scripts/net/AvatarMotor.cs`, `scripts/game/sandbox/MoveIntent.cs`, `SandboxCamera.cs` (1040 lines, third-person only) | **Build new: one `FirstPersonCamera` + one `IIntentSource` (FP-1). Motor, netcode, prediction, bots untouched.** |
| Knockable shelf/bin props | Only two RigidBody prop scenes (`Crate.tscn`, `Sphere.tscn`); `PropManager.AdoptAuthoredProps` adopts authored props deterministically; a nested-scene `[Export]` reads back as default on this Mono build, so prop kind is inferred from the collider. No bins, no shelves. | Watis_Game `scenes/game/props/`, `PropManager.cs:542` | **Build new content on the existing scaffolding (SHELF-1).** |
| Two-client local playtest + bots | ENet server + N `--bot` clients; `Run-MultiplayerTest.ps1`; `BotHarness` JSONL; machine-wide suite mutex; `ViewportCapture` + F9 `DevScreenshot`; `--capture-cam` for identical framing per peer. | Watis_Game `tests/`, `scripts/game/BotHarness.cs`, `scripts/dev/` | **Reuse wholesale.** |
| Steam relay + room codes + child-process host | `LocalServerHost`, `SteamLobby`, `SteamPeer`, `RoomCode`, handshake with room-code capability | Watis_Game `scripts/net/hosting/`, `scripts/net/steam/` | **Reuse wholesale; untouched.** |
| Spoons (prior hidden-tell direction) | **No artifact anywhere** — no doc, branch, commit or memory. | — | Nothing to carry over. |
| Usernames / score display | `NetworkManager.LocalDisplayName` replicated to `SandboxAvatar.DisplayName`; `VoiceManager.GetRemotePlayers()` is the only player list; `RoundEndTallyPanel`, `RoundHud` exist on the session branches. | Watis_Game | Reuse names; build a tiny board (HOLD-1). |

Not reused (out of scope or the wrong shape): `scripts/game/bubble/`, `water/`, `honk/`, `watcher/`,
`sight/`, `achievements/`, `failure/`, `run/` (the quota playthrough spine), `dev/playground/`
(moped/bike/city/desert/dog), `dev/session/` lab, `world/bubbletest/`, all explosion-jump branches.

## 1. Decisions and assumptions (state them, don't relitigate them)

1. **Base: seed the new repo from `Watis_Game@93bef3d` (origin/main) as one squashed commit** with a
   provenance line, then prune to the foundation in the same packet. Rationale: it is the only tree that
   has the R.E.P.O. carry, the newest net stack (`NetProfile`, reconnect, desync monitor), the
   interaction interfaces and their scar tissue, and the two-client test harness. History stays in
   Watis_Game; `git log -L` archaeology is done there.
2. **Godot project renamed** to "Super Find Great Deal" in BASE-1 so `user://` does not share Watis
   World's profile (a known hazard: a suite bot once spent an achievement into Talon's live profile).
3. **Carry authority stays in `PropManager`** (server arbitrates grab, streams loose physics, latches
   rest). The feel-system spring runs **on the holder only** as the visual of a Held prop; other peers
   keep deriving the held transform from the holder's hand anchor. "Place" = release with zero
   velocity and the held orientation, sent to the server as the intended transform, validated by range.
   "Rotate" = hold a modifier and move the mouse / scroll to spin the held basis (local until placed).
   Slots (`InteractionSlot`) are used only where the task room needs snap.
4. **Room transitions are authoritative teleports on phase change** (the TvPortal epoch-bump), not
   walking through doors. The three rooms are three boxy authored sections of one world scene, spaced
   apart with solid walls. The only door that moves is the burst door between task room and the
   seeker's entry vestibule.
5. **Roles alternate each round**; first round the host hides, the second joiner seeks. Exactly one
   hider and one seeker. `Protocol.MaxPlayers` stays 6 at the transport; the round loop refuses Start
   unless exactly two humans are present (message on the button, not silence).
6. **Theme for the MVP search room: shapes.** Dressing = many identical primitives of one shape and
   colour (cubes, red). The rack offers three near-miss objects (sphere, cylinder, wedge — same red,
   same size class). Colour theming is a second aisle later, not now.
7. **Task room mechanic default: block towers** (option A below) — it is the example the brief itself
   uses and it makes the interruption cost physical. Talon's pick is still open; TASK-1 is not
   dispatched until he answers or tells me to proceed with the default.
8. **Startle staging ships as knobs**, not as a single fixed choice: tell delay (default 0.4 s), fake
   knock rate (default 0), burst impulse radius/force, camera kick, bang volume. All in one tuning
   file so a playtest can A/B without a rebuild.
9. **Voice: always on.** Cross-room = PA/intercom route (filtered, no falloff); same room = proximity.
   No mute-by-phase. Taunting and bluffing are the design; the intercom is the channel for it.
10. **Gates:** per lane, `dotnet test` + the lane's own smoke suite + `Run-MultiplayerTest.ps1` +
    captures sent to Talon as they land. Full scene suite runs at INT-1 only. The final gate is a
    human at the keyboard on Talon's launcher, two windowed clients, the whole loop once.
11. **Scenes are authored, not code-built** (the standing rule from `.claude/rules/godot-scenes.md`).
    Ugly is fine; procedural is not — the self-test that counts packed vs live nodes stays.
12. **The hidden object is never clipped, out of bounds, or unreachable by accident** (Talon,
    2026-09-19). Hiding it inside a movable container on purpose is encouraged; ending up inside a
    wall, a shelf back, another prop's volume, or under the floor is a defect. Enforced in three
    layers, §5b, all server-side; CARRY-1 owns the place-time layer, REACH-1 owns the rest audit and
    the Confirm-time retrievability check, ROUND-1 folds the fact.

## 5b. Placement integrity — the hidden object stays findable

Known failure mode from every prior playtest: props clip into walls and each other. In this game a
seeker who cannot find the object because of a physics glitch is the worst outcome the design has, so
the checks are deliberate, layered, and every rejection says why.

**Layer 1 — at place time (CARRY-1, inside the place RPC).** The server tests the prop's own
collision shape at the intended transform: (a) inside the room's authored bounds volume; (b) shape
overlap against static geometry and every other prop, penetration depth ≤ `PlaceOverlapToleranceM`
(default 0.02 m; small overlaps are what the physics settle resolves). Deeper overlap → refuse with
the reason "Doesn't fit there"; the prop stays in hand. Each prop remembers its **last good
transform**: the last place or rest transform that passed.

**Layer 2 — at rest, continuously (REACH-1, server).** Every time a prop latches Resting, and on
the kill plane, the server re-runs (a) and (b) on the rest transform — physics after release can
roll a prop under a shelf back or push it into a wall. Failure → depenetrate along the minimum
translation up to `DepenetrateMaxM` (0.15 m) and re-test; still failing → teleport to the last good
transform (never to spawn). Cheap: one shape query per settle event.

**Layer 3 — at Confirm (REACH-1 provides `TargetRetrievable`; ROUND-1 refuses with
`NobodyCouldReachThat`).** Before Hiding → Seeking commits, the target must be Resting, must pass
layer 2, and must be **grab-reachable**: sample standing points on a ring of radius `GrabReachM`
around the target on the walkable floor (a downward ray finds floor, a capsule cast confirms a body
fits there), and from each point's eye height cast a ray at the target. The target is retrievable if
from at least one point the first hit is the target itself **or a movable prop** (a bin, a box, a lid
— things the seeker can pick up or knock aside). If every ray hits static geometry first, or no
standing point exists within reach (the top shelf at 2.4 m), Confirm is refused with the reason and
the hider must move it. Buried under ten movable items is legal; that is the game.

**During Seeking** layer 2 keeps running, so a seeker who knocks a shelf onto the target cannot
bury it inside static geometry either. Timeout still ends the round; there is no rescue beyond the
audit in the MVP (the hider-only intercom heat knob is the candidate if playtests want one).

A planted self-test covers each case: inside a wall (refused), inside a static shelf back (refused),
3 m up (refused), under an overturned bin (allowed), inside a closed movable box (allowed), pushed
through the floor by physics (returned to last good).

## 2. The loop, as the server sees it

```
Holding ──Start(host, 2 humans, hider holds a rack object)──▶ Hiding(30 s)
   ▲                                                              │ Confirm (hider, not holding the target) or timeout
   │                                                              ▼
 Tally(6 s) ◀── End button (anyone, Together) ── Together ◀── Found ◀── Seeking(180 s cap)
   │ roles swap, world reset edge                     ▲ burst door         │ timeout → Tally (hiders keep towers)
   └──────────────────────────────────────────────────┘
```

Phase → who is where (teleports fire on the transition tick, server-side, epoch bump per moved player):

| Phase | Hider | Seeker | Timers / facts folded |
|---|---|---|---|
| Holding | holding room (practice props, rack, Start button, board) | holding room | `HumansPresent`, `HostPressedStart`, `HiderHeldRackProp` |
| Hiding | search room, carrying the target | holding room (cut off) | `RemainingSec`, `HiderPressedConfirm`, `HiderHoldsTarget` |
| Seeking | task room (towers, End button inert) | search room (bins, shelves, drop-off bin) | `RemainingSec`, `TargetInDropOff`, `TowersCompleted` |
| Found → Together | task room | teleported to the vestibule behind the burst door, door bursts, walks in | `StartleFiredTick`, towers frozen at this tick |
| Tally | both see the card | both | `LastTally` computed once; `ResetRequested` true for one tick |

Scores: hider = towers completed at the Found tick (progress frozen — the cost of being found late is
the tower you didn't finish); seeker = seconds remaining at the find (0 on timeout). Both shown on the
holding-room board and the tally card. Score dicts are per peer id, ints, absolute on the wire.

## 3. Lanes

Dependency graph. Godot-launching lanes are marked (G); at most two run at once.

```
BASE-1 (G) ─┬─▶ FP-1 (G) ───────────────┐
            ├─▶ ROUND-1 (xUnit + 1 smoke)┼─▶ BTN-1 (G) ──┐
            └─▶ CARRY-1 (G) ─────────────┤               ├─▶ INT-1 (G) ─▶ Talon rides
                                         ├─▶ REACH-1 (G) ┤
                                         ├─▶ SHELF-1 (G) ┤
                                         ├─▶ DOOR-1 (G) ─┤
                                         ├─▶ TASK-1 (G) ─┤   (after Talon's pick)
                                         ├─▶ VOICE-1 ────┤
                                         └─▶ HOLD-1 ─────┘
```

| Packet | Scope (one sentence) | Depends on | Gate |
|---|---|---|---|
| **BASE-1** | Seed from Watis main, prune to the foundation, rename the project, three boxy rooms authored with named spawn points, `--world supermarket` default, suites trimmed to green, CLAUDE.md rewritten for this game, this doc committed. | — | build green; xUnit green; `Run-MultiplayerTest.ps1` 2 bots green; two windowed clients stand in the holding room (capture). |
| **FP-1** | First-person camera at eye height + mouse look + `FirstPersonIntentSource`; own body hidden for the local player (hands optional); FOV/no-bob defaults tuned against motion sickness; bots unaffected. | BASE-1 | xUnit (prediction match unchanged); 2-bot suite; capture from each client. |
| **ROUND-1** | `HideSeekLoop` (engine-free: phases, timers, facts, one-tick reset, tally) + wire (absolute, channel 21) + server driver + phase-driven authoritative teleports + role assignment (alternating) + HUD phase/timer strip. | BASE-1 | xUnit for every transition and the refusal cases; one scene smoke where two bots are walked through the whole loop by scripted inputs. |
| **CARRY-1** | Wrap `feel/Interactor` in `PropManager` authority: pick, carry with the spring on the holder, rotate held (modifier + mouse/scroll), place gently at the held transform, throw; other peers derive from the anchor; late-join and disconnect paths unchanged. | BASE-1 (aim comes from `MoveIntent.AimYaw/AimPitch`, not the camera node, so FP-1 is not a dependency) | `Run-FeelTest`, `Run-CarryTest`, `Run-CarryDriftTest`, 2-bot place-and-observe smoke. |
| **BTN-1** | `RoundButton` (pressable, server-adjudicated per peer, physically depresses + lamp, refusal = shake + click + reason text, live key glyph) ×3 (Start, Confirm, End) + `DropOffBin` (Area3D, accepts only the target prop id, wrong prop = buzz) + `ObjectRack` (three near-miss props, records the hider's choice). | ROUND-1, CARRY-1 | xUnit on the adjudication; smoke: bot presses Start with 1 human → refused with reason; 2 humans → Hiding. |
| **REACH-1** | Placement integrity layers 2 and 3 (§5b): rest-time overlap/bounds audit with depenetration and last-good fallback for every prop; Confirm-time grab-reachability for the target as a `TargetRetrievable` fact with a reason; planted self-test of the six cases. | CARRY-1, ROUND-1 | xUnit on the reachability rule (engine-free geometry); planted scene self-test; 2-bot smoke where a bot places the target in a wall and Confirm is refused. |
| **SHELF-1** | Supermarket dressing for the search room: aisles of shelves and bins, ~100–150 authored primitive props (cubes, red) adopted by `PropManager`, knockable; the rack's three near-miss objects; prop count and server physics cost measured. | BASE-1, CARRY-1 | authored-vs-live node count self-test; server frame time p50/p95 with all props disturbed; captures. |
| **DOOR-1** | The burst door: round state (not a node RPC) opens it on Found; leaf slams with overshoot, impulse to task-room props in radius, camera kick on hiders, bang; seeker enters through it; staging knobs (tell delay, fake knock rate, impulse, kick) in one tuning file. | ROUND-1 | smoke: Found tick → door open on every peer within one snapshot; captures of the hider's POV at the burst. |
| **TASK-1** | The task-room mechanic (default: block towers on pads, tower = N blocks stacked stable for 1 s, score accrues; startle freezes the count and the impulse knocks the in-progress tower). | CARRY-1, ROUND-1, Talon's pick | xUnit on the tower predicate; smoke; captures. |
| **VOICE-1** | Intercom-by-room: `PaResolver` = "talker's room ≠ listener's room", wired on client and server; proximity in the same room. | ROUND-1 (room membership) | `Run-VoiceTest`, `Run-VoiceGateTest`; routing verdict JSON asserted for cross-room and same-room. |
| **HOLD-1** | Holding room: names of everyone present + current scores on a board, a few practice props, the rack and Start button placed. | ROUND-1, SHELF-1 (props) | capture; late-joiner sees the board complete. |
| **INT-1** | Merge all lanes, full scene suite + xUnit, human-path gate on Talon's launcher, ride card at `C:/repos/RIDE-CARD-2026-09-19-SUPERMARKET.md`, push. | all | full suites, raw counts; one directory + one command. |

Worktrees: `C:/repos/sfgd-<lane>` off `super-find-great-deal`, branches `feat/2026-09-19-<lane>`.
Packets: `C:/repos/PACKET-2026-09-19-<ID>.md` (root copies authoritative), committed into
`docs/agents/packets/` by the lane. Handoffs to `docs/agents/handoffs/<date>-<ID>.md` on the branch.

## 4. Task room — options (Talon's pick)

**A. Block towers (default).** Four pads; a tower = 5 blocks stacked and stable for one second; the
score is towers completed. The in-progress tower is exactly what the startle costs: the door's impulse
knocks it over, and the count freezes on the Found tick. *Supports absorbed-then-interrupted well:*
stacking with a spring-carry demands eyes and hands; the cost is physical, visible to both players,
and legible on the board ("3 towers, fourth was at 4/5"). *Risk:* if the carry spring is too loose,
stacking is frustrating rather than absorbing; CARRY-1's heft dials are the fix.

**B. Shape sort chute.** Blocks of three shapes slide down a chute; sort each into its bin; +1 per
correct, streak bonus. *Supports absorption* through a constant stream (you cannot look away), and the
interruption cost is the broken streak. *Weaker:* the cost is a number, not a thing falling; the
hands-on demand is lower (grab, turn, drop), so the "caught mid-motion" moment is less physical.

**C. Fragile carry across a gap.** Carry a stack of plates across the room on a tray-like held object
and set it on a shelf; each delivered stack scores. *Strongest physical interruption*: a flinch
literally drops the stack. *Risk:* it stresses the carry system hardest, scoring is lumpy (one big
score per delivery), and it is the hardest to make read on a board.

Recommendation: **A**, with C's "forced flinch" borrowed as a knob in DOOR-1 (the startle can force
the hider to drop what they hold). B is the fallback if stacking feels bad in the first ride.

## 5. Startle staging — proposal

Trigger is fixed: the target lands in the drop-off bin. Staging, in order, on every peer from one
server tick:

1. **t = 0 (Found tick, server):** towers frozen; seeker teleported to the vestibule behind the door,
   already facing in; the door's blocker is still solid.
2. **t = 0 → tell (default 0.4 s):** one small tell, sub-second so it reads as a startle and not a
   warning: the task-room lights dip and the intercom clicks live with a breath of room tone. Knob
   `TellSec` 0..1.5. At 0 there is no tell at all.
3. **Burst:** the leaf slams open with overshoot (fast tween with a bounce, not physics — a hinge
   joint can tunnel), the blocker drops on the same frame, an impulse is applied to every loose prop
   in `BurstRadiusM` of the doorway (so the in-progress tower and anything on the floor jumps), the
   hiders get a camera kick, a bang plays positional at the door and a second time flat on the
   hiders' bus (a positional-only bang is too quiet if the hider faces away). Knob: `ForceDropOnBurst`
   (the flinch — the hider's held object is released).
4. **Seeker walks in** under their own control; nothing is a cutscene, no one loses input.
5. **End button** lights in the task room; anyone presses; tally.

What makes it a jack-in-the-box rather than a script: the abstract expectation is total (both players
know the door is the only way this ends) and the instant is unknowable (the seeker's progress is
invisible except through what they say on the intercom, which they can lie about). Two knobs push
anticipation harder, default off for the first ride: `FakeKnockRate` (the door rattles at random
intervals during Seeking — the seeker can also press a "knock" on their side of the wall as a taunt),
and `SeekerHeatOnIntercom` (a faint crackle on the hiders' intercom that rises as the seeker nears
the target; hider-only, so it feeds dread without helping the seeker).

## 6. Beyond the MVP — what the core opens up

The core is anticipation with an unknowable instant, paid off physically. Everything below keeps
that and adds pressure, never dilutes it.

- **N hiders, one seeker, one shared task room.** Each hider has a different near-miss object; each
  find bursts the door once. Hiders who have been found stay in the task room, so the second burst
  startles everyone again, and the found hider becomes a liar on the intercom ("they're nowhere near
  yours"). Last hider standing gets the multiplier.
- **Two search rooms, two doors on different walls.** Two seekers hunt in parallel; the hiders never
  know which wall goes. This is the cheapest way to multiply the instant without adding rooms.
- **Caught hiders join the seek.** Infection rules: a found hider is teleported into the search room
  as a second seeker. The task room empties toward the end and the last hider is stacking alone with
  everyone else on the other side of the door.
- **Hider-side sabotage with a budget.** The hider gets 30 seconds and three "mess" actions: tip a
  bin, pull a shelf, plant a decoy near-miss. The seeker's search becomes archaeology; the hider's
  choices become the story afterward.
- **Object types as pressure.** A noisy object (rattles when moved, audible on the intercom) tells the
  hiders the seeker is close. A fragile object breaks if the seeker knocks it — penalty to the seeker,
  relief for the hider. A heavy object slows the hider's hiding, so they hide it worse.
- **The search room does not reset between rounds.** Mess accumulates across a session; round five is
  played in the wreckage of rounds one to four. Free content, escalating comedy.
- **Spectators are participants.** Anyone not in the round watches both rooms and has the intercom.
  They can lie. Dead-time becomes the social layer.
- **The startle photo.** On the Found tick the game captures the hider's POV (the tower mid-fall, the
  door leaf, the seeker's face in the gap) and shows it on the tally card, saved to disk. This is the
  shareable object; the game photographs its own best moment. Cheap: `ViewportCapture` already exists.
- **Themes via the near-miss principle.** Tomatoes and one orange; cereal boxes and one that is a
  book; shape aisle, colour aisle, brand aisle. The rule stays "close enough to need searching,
  distinct enough to be findable"; only the dressing changes.
- **Asymmetric information on the intercom.** Give the seeker a "knock" and the hiders a "peek" (a
  0.5-second keyhole view of the search room, once). Both are bluffing tools, both cost something.

## 7. Open questions for Talon (one at a time, in chat)

1. Task room mechanic: A towers (default) / B sort chute / C fragile carry — or something else?
2. After the first ride: tell delay 0 vs 0.4 s, and whether the forced drop on burst feels like a
   payoff or a punishment.

## 8. Definition of done

Two windowed clients from Talon's launcher, direct connect over ENet: host and joiner stand in the
holding room with names and scores on the board; the hider picks a rack object and presses Start;
30 seconds to hide; Confirm; the seeker searches the shelves and bins, drops the right object in the
bin; the door bursts on the hiders mid-stack; both press End; the tally shows towers and find time;
roles swap; round two starts. One directory, one command, a ride card with captures. Then stop.
