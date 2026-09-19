---
name: vfx-audio-sync
description: Use when a Sail world-audio cue must be aligned with, withheld from, or deliberately decoupled from a visual event — spending the ambient bed as wrong silence, a sound with no visible source, a synced audio-plus-visual peak two players perceive in the same instant. Which mode and when, never how the bed is built — that is the sound-* family, starting at sound-integration-guide.
---

# vfx-audio-sync

## Overview

World audio and its relationship to the visual layer. Research section 7. Three modes, and the
skill's real job is knowing which one a moment wants: **reinforce** (a synced peak),
**withhold** (wrong silence), or **decouple** (a sound with no visible source).

**Source:** `docs/superpowers/research/atmospheric-vfx-RESEARCH-RESULT.md` §7, plus its
*Known collisions with the shipped repo* section, which is binding. Every code sample in that
file is unverified; this skill states per API whether it checked.

This skill owns the game's **highest-value unbuilt device**, and that device is blocked. Read
the next section before anything else.

## The bed does not exist, and that blocks the whole device

`THRILL-BIBLE.md` §6.3: *"Withdrawing a sound the player has stopped noticing is stronger than
adding one. It requires an established ambient bed to withdraw — the bed is the setup and its
absence is the payoff, which makes this one device that must be **invested in before it can be
spent**."*

**TIDE has no ambient bed.** Not a thin one, not a placeholder — nothing. `SfxLab` synthesises
one-shot cartoon SFX; `scripts/voice/` carries player voice; `DayNightSky` renders a sky in
silence. There is no continuous world sound anywhere on this branch. §10's device register
records **Wrong silence — `unbuilt` — no ambient bed exists** for exactly this reason, and
§6.3's own scope note says the device is *"currently impossible to execute — it is an
investment, not a technique."*

**Consequences, in order of how often they get ignored:**

1. **The bed is the work.** Building `WrongSilenceController` before the bed exists is building
   a subtraction with nothing to subtract from. It will pass its tests, replicate correctly,
   cut on schedule, and produce no feeling whatsoever, because -80 dB of nothing is
   indistinguishable from 0 dB of nothing.
2. **The bed has to be spent-worthy before it is spent.** A bed the player never stopped
   noticing has not been established. The device works on a sound that has faded below
   attention — which takes minutes of continuous, unremarkable, non-looping-sounding presence,
   not a stinger loop.
3. **A bed is a real investment with its own failure modes** — a loop the ear catches, a layer
   that reads as music, a mix that competes with proximity voice — and none of those are
   solved by the withdrawal machinery.
4. **Do not build the machinery "so it's ready."** Untested replication for a device with no
   content is the worst of both: it will drift, and its first real exercise will be the moment
   it matters.

**So the honest order of work is: bed first, then withdrawal, then the ratio that governs how
often withdrawal is spent.** If this skill is invoked for wrong silence and the bed still does
not exist, the correct output is that finding, not an implementation.

## Directed, not decided

`/direct` and `docs/THRILL-BIBLE.md` decide **which** feeling, **why there**, and whether the
device is already spent. This skill decides **how it sounds and how it replicates**. It
executes a direction; it never originates one.

What it serves, specifically:

| Section | What it asks for | This skill's part |
|---|---|---|
| §6.3 | wrong silence — withdrawal beats addition | the bed, the cut, the return, the replication |
| §4.2 | the Phasmophobia test — a beat must be legible to someone who was not there | the sharper audible trigger at the moment of dread, which §4.2 names as the fix and explicitly *not* more ambience |
| §4.6 | the strongest spikes align an audio event with a visual one in the same instant | frame-accurate alignment when a peak is directed, and only then |
| §5.5 | the subtraction of a *voice* — a teammate mid-sentence, then nothing | **not this skill's system.** See *The voice boundary*. |

Governed by two prohibitions that constrain everything above:

- **§8.2 — startle is release, never build.** A sting is the end of a build, not the engine of
  one. A skill that can fire audio spikes on demand is the easiest place in the codebase to
  violate this, so: a peak needs a build behind it that `/direct` authored. No build, no peak.
- **§8.3 — reliable telegraphs.** If a cue always precedes a threat, the cue has taught players
  when they are safe. This is why the ratio below matters more than any single trigger.

**Blanks this skill carries and does not resolve.** `THRILL-BIBLE.md` §9 (tone position) had its
**axis decided 2026-07-26**; what is still open is the **ratio** — how often a dread build cashes
out absurd versus stays sincere, and whether any beat may land with no absurd release at all. It
bears directly here: **how comic or how sincere a sting is allowed to be depends on a calibration
that is not made.** A slide-whistle sting and a low sub-drop are both defensible under different
ratios, and picking one silently forecloses the fork. §9's own ripeness trigger is *the first
playtest in which anyone is actually frightened* — a ratio cannot be tuned against a feeling the
game has never produced. Until then, build sincere dread first and **flag** rather than resolve
any beat whose absurd payoff would have to be invented. §6.2's night-floor conflict is likewise a
shipped decision in live opposition to a doctrine section, and no `vfx-*` skill may resolve it.

§9 also promotes this skill's dependency explicitly: *"§6.3's ambient bed is no longer a
nice-to-have investment; it is on the critical path, because wrong silence is unavailable without
it."*

## Delegates the how — the `sound-*` family

**Added 2026-07-28.** This skill decides *which mode a moment wants, when to withhold, and the
ratio*. It does **not** decide how any of it is built. That substrate is a separate family,
standing in the same relation to this skill that `vfx-particles` stands in to the rest of `vfx-*`:

| Question | Skill |
|---|---|
| How is a bed layered so it does not turn to mud — frequency lanes, depth, staggered entry, ducking | `sound-soundscape-construction` |
| How is a threat's voice synthesised, and how is proximity expressed in sound | `sound-threat-audio` |
| How is 3D positioning, attenuation, air absorption and reverb zoning configured | `sound-spatial-audio` |
| How does a tension value become an audible build — sweeps, crescendo tiers, pulse, distortion | `sound-anticipation-escalation` |
| How is a withdrawal actually executed — envelope shapes, duration tiers, selective-lane silence, the recovery beat | `sound-silence-negative-space` |
| How does a cue get triggered and aligned, and why a local event bus is not cross-client simultaneity | `sound-event-wiring` |
| Which `AudioEffect*` classes exist, their real parameters, chain ordering and cost | `sound-real-time-effects` |
| What the concurrency budget actually permits, pooling, voice stealing, LOD | `sound-optimization` |
| How variety and continuous state-response are generated from no assets at all | `sound-procedural-reactive` |
| The map of all of the above and the honest build order | `sound-integration-guide` |

**The direction still starts here.** A `sound-*` skill invoked with no direction behind it has
nothing to build — the same rule the repo `CLAUDE.md` states as *"never invoke one bare — a VFX
skill with no direction behind it has nothing to build."*
Two consequences worth stating: the bed dependency in the section above is
`sound-soundscape-construction`'s work to execute but this skill's call to make, and the ratio
below is enforced here and merely *reported on* by anything downstream.

## When to Use

- A level needs its ambient bed's sync mode, timing, or withdrawal ratio decided (the bed's
  own construction — layering, frequency separation, mixing — is `sound-soundscape-construction`)
- Wrong silence is being scheduled or replicated (the withdrawal mechanics themselves are
  `sound-silence-negative-space`)
- A directed peak needs its audio half landed in the same instant as its visual half
- A sound with no visible source is wanted, and the question is what *not* to fire alongside it
- The mix between world audio, `SfxLab` one-shots, and proximity voice needs arbitrating
- An audio cue is suspected of having become predictable

**Not for:** the proximity voice pipeline (`scripts/voice/` — see below); which feeling a moment
should produce (`/direct`); the urgency cue that warns of rising pressure (`/spec-urgency-cue`,
governed by `LEVEL-BIBLE.md` §8.2 — and see *The two-channel rule*, because conflating these is
the named failure); particle and instancing cost (`vfx-particles`); lighting and fog
(`vfx-lighting`); the session-monotonic escalation curve (`vfx-escalation`); UI and menu sound
(`SfxLab.PlayUi`); and **how any of the above is actually built** — see the delegation table
above, and start at `sound-integration-guide` if you do not know which member owns a question.

## The voice boundary

**`scripts/voice/` is shipped, works, and is not this skill's.** What exists:

- `VoiceManager` (autoload) — clients capture and Opus-encode, the server relays *without
  decoding*, tagging the sender id server-side so a client cannot spoof another player.
  Dedicated unreliable ENet channel 2, per-client rate limiting, bounded packet size.
- `VoiceSpeaker` — per-speaker decode, jitter prebuffer, and an `AudioStreamPlayer3D` parked on
  the stable Players root and repositioned each tick. Godot's built-in 3D attenuation, not
  hand-rolled distance math.
- `VoiceConfig` — `ProximityUnitSize = 6.0f`, `ProximityMaxDistance = 24.0f`, marked *"Do not
  fork these."* Buses: `Voice`, plus a PA bus built from overdrive + lowpass + boxy reverb.
- `VoiceRoute` — `Proximity` (positional falloff) or `Pa` (attenuation disabled, whole building
  hears it), re-resolved every tick from replicated state.
- `MuteRegistry` — local-only per-player mute, keyed by both peer id and SteamID64.

**The boundary:** this skill owns *world* audio and its relationship to VFX. It does not own the
voice pipeline, does not fork `VoiceConfig`'s attenuation values, and does not add DSP to the
`Voice` bus. Where world audio and voice interact, the interaction is a **mix** question —
ducking the bed under speech, keeping the bed out of the frequency range voice occupies — and
it belongs on the world side of the boundary, never inside `VoiceSpeaker`.

**§5.5's "teammate voice cuts out" is a voice-system device, not a world-audio one.** It would
live next to `VoiceManager`, and it is `unbuilt` in §10 for a specific reason worth flagging
here: **there is no afterlife or spectator channel.** A dead player currently has no route to be
heard at all. §5.5 (a voice subtracted mid-sentence) and §5.6 (failure is content — *"the dead
stay audible and stay in the room"*) both depend on that channel existing. It does not. That is
a dependency to report, not a thing to build sideways into a world-audio system.

Budget note that does cross the boundary: `SfxLab` documents a concurrency budget of **≤24
concurrent 3D players including the 5 voice speakers and ambience**, with a 14-slot one-shot
pool sized to stay inside it. An ambient bed lands in that same budget and must be counted
against it, not added beside it.

## Core patterns

### 1. Build the bed

The prerequisite, and the actual first packet. What a bed has to be:

- **Continuous and unremarkable.** It has to fall below attention. Anything with a hook, a
  melody, or a recognisable loop point stays in attention and can never be withdrawn to effect.
- **Layered, so it can thin rather than only stop.** A partial withdrawal (the insects stop, the
  wind stays) is a different and more deniable cue than total silence, and it is the version
  that survives repetition better.
- **Mixed under voice, not against it.** Proximity voice is §5.1's highest-leverage instrument.
  A bed that makes players harder to hear has cost more than it bought.
- **Its own bus**, so it can be ducked, withdrawn, and volume-controlled as one group — the
  `SfxLab.EnsureBus` / `VoiceManager.EnsureOutputBus` pattern, lazily created and routed to
  Master.

Positional or non-positional is a real fork with consequences: a non-positional bed is one
`AudioStreamPlayer`, trivially withdrawn, and identical everywhere on the map — which flattens
§5.4's information asymmetry. A set of positional emitters gives the map an audio topology and
makes zones distinguishable, at the cost of more nodes in the ≤24 budget and a much more
complicated withdrawal. **State which was chosen and why; do not drift into one.**

### 2. Wrong silence, server-authored and replicated

**The research says this must be server-triggered and replicated, and then its sample does the
opposite** — each client calls `GD.RandRange` and starts its own `SceneTreeTimer`. That is the
whole device broken: every client loses ambience at a different second, nobody looks up at the
same moment, and the shared-stake quality that makes it a group beat is gone. §4.1's
**simultaneity** condition is not a nice-to-have here; it is the difference between a moment and
four separate moments.

**Follow `CycleDriver`'s pattern.** `scripts/game/world/CycleDriver.cs` is the house form for
server-authoritative replication and it earned every piece of it:

- **The server picks the interval and owns the schedule.** Clients never roll for it.
- **Advance from the sim tick, never a wall-clock `Timer`** — `CycleDriver` is explicit that a
  wall-clock timer drifts from physics and desyncs whatever derives from it on the same cadence.
- **The house RPC attribute form is**
  `[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = ..., TransferChannel = ...)]`.
  **Not** `[Rpc(CallLocal = false)]`, which is what the research writes. Authority mode is what
  makes "server → everyone" a checked property rather than a convention.
- **Transfer mode differs from `CycleDriver`'s and the reason matters.** `CycleDriver` broadcasts
  `Unreliable` because a dropped phase sample is superseded within 0.5 s by the next one. **A
  silence event has no successor.** Drop it and that client simply never goes quiet, which is
  the desync the device cannot survive. Use `Reliable` — the same reasoning `CycleDriver` itself
  applies to `SyncPhaseTo`.
- **Its own transfer channel.** Voice is 2, move 3, prop 4, cycle 5 (`NetCodec`); an atmosphere
  channel gets its own constant next to those rather than sharing one. The next free index today
  is 6 — confirm against `NetCodec` at build time rather than trusting this line.
- **Latest-wins staleness guard** on a sequence number, per `CycleDriver.Apply`.
- **Late-join is a first-class case, not an edge case.** `CycleDriver` gives every joining or
  resuming peer a targeted `SendPhaseTo` from `Gameplay.OnPeerConnected` — the same funnel
  `PropManager.SendDumpTo` uses — and that targeted sync **always wins**, even against a
  numerically higher seq already applied from a prior connection, because a reconnected peer's
  `_lastAppliedSeq` is stale history rather than an ordering claim.

  **A peer that connects mid-silence needs exactly that treatment.** Without it, it starts its
  bed at full volume during a silence everyone else is inside — it hears a bed that should be
  absent, and the one player who joined late is the one player who does not experience the
  device. The targeted sync must carry both *whether a silence is active* and *how much of it
  remains*, so the joiner's return fade lands with everyone else's rather than starting a fresh
  full-length silence of its own.

- **Clients apply, never decide.** A client's only job is to run the cut and the return from the
  authoritative event.

**The envelope asymmetry is the craft part and it is not arbitrary:** cut fast, return slow. The
abruptness of the loss is what registers as *wrong*; the slowness of the return is what keeps the
player unsure whether it is over. A symmetric fade in both directions reads as a mix change.

**`Tween` verification.** The research chains `TweenProperty` → `TweenInterval` → `TweenProperty`
and hangs `ScheduleNext` off `Tween.Finished`. **Verified against Godot 4.7** (`GodotSharp`
4.7.0 docs): `Tween.Finished` is *"emitted when the Tween has finished all tweening"* — the whole
chain including the interval, so the callback does fire where the research expects. Two caveats
it omits: `Finished` is *"never emitted when the Tween is set to infinite looping"*, and a tween
from `CreateTween()` is bound to its node and dies with it — so a scheduler that re-arms itself
from `Finished` stops silently if the tween is killed. In a server-authored design this matters
less, because the *schedule* lives on the server and the tween is only the client-side envelope.

### 3. The three modes and the ratio

**The ratio is the load-bearing idea in this skill, and it is a directorial call, not an
audio-engineering one.**

The research's figures are roughly **60% visual-only or audio-only with no companion, 30%
wrong-silence, 10% full synced peaks**, shifting toward more peaks as the clock advances. Treat
those as `/direct`'s to set — **per level, recorded in the `THRILL-BIBLE.md` §10 register
alongside the devices they govern** — with this skill enforcing whatever it is given and
reporting what actually fired.

**Why the ratio matters more than any single trigger.** §8.3: a cue that always fires teaches
players when they are safe. Every individual trigger in this skill can be perfectly designed and
the system can still fail, because the failure is *distributional*. If every visual event gets an
audio companion, players learn that unaccompanied visuals are harmless. If wrong silence always
precedes something, silence becomes a warning and its absence becomes an all-clear — which is
§2.1's fuel spent in the least useful way possible. The ratio is the only control that operates
on the distribution rather than on an instance, which is why it is the thing to get right and
the thing to instrument.

Corollaries worth enforcing mechanically:

- **Never let a synced peak and a wrong silence fire back-to-back.** Each needs room; stacked,
  they dilute both.
- **Vary the interval.** A fixed cadence becomes learnable. 40–110 s is the research's range and
  the *irregularity* is the load-bearing part, not the average — a playtest call either way.
- **Wrong silence must not always co-occur with a visible event.** If players can predict the cut
  from a visual tell, it has become a telegraph and §8.3 has been violated by construction.
- **Measure, don't assume.** Log what fired and in what proportion. Sail has a live Firebase
  telemetry path (`scripts/telemetry/`), so the ratio is checkable against reality rather than
  against intent.

### 4. The two-channel rule

**A dread cue and an urgency cue must never be the same channel.** `THRILL-BIBLE.md` §8.3 records
the tension explicitly and it is the easiest thing in an audio system to get wrong, because both
are "a sound that means something is coming."

| | Urgency cue | Dread cue |
|---|---|---|
| Owner | `LEVEL-BIBLE.md` §8.2, `/spec-urgency-cue` | `THRILL-BIBLE.md` §6.3, `/direct` |
| Contract | **honest, always** — it is a fairness contract | **must sometimes lie** — a reliable one is decoded and dies |
| Failure if broken | players die to an unsignalled state change; `MECHANICS-BIBLE.md` §10.4 territory | players learn when they are safe; §8.3 violated |
| Redundancy | audio alone is never sufficient (`INTERACTION-BIBLE.md` §8.2 — the tether severs audio on purpose) | free to be audio-only; that is often the point |

**The named rule: an urgency cue and a dread cue may not share a bus, a sound family, a
frequency band, or a spatial signature.** If the tide's rising hiss and the dread-layer's
withdrawal-and-return live in the same sonic space, then either the dread cue becomes a fairness
promise the design cannot keep, or the urgency cue inherits the dread cue's licence to lie — and
the second is a bug, not a style choice. Keep them audibly separable to a player hearing both for
the first time.

Note also that the urgency cue is subject to the redundant-channel rule and **audio alone never
satisfies it**. Anything this skill builds for urgency is a supporting channel, never the whole
cue.

### 5. Audio without visual confirmation

A distant snap, an indistinct call, spatialised and given no companion visual. This is the mode
that preserves §2.1's ambiguity most cheaply, and the discipline is entirely in what you *do not*
also fire.

**Verified against Godot 4.7:** `AudioStreamPlayer3D.MaxDistance` is *"the distance past which the
sound can no longer be heard at all,"* only effective above `0.0`, always linear regardless of
attenuation model, and working in tandem with `UnitSize` (which is the actual falloff factor).
Units are world units; Sail's convention treats one unit as roughly one metre — `VoiceConfig`
documents its 24.0 as *"gone past ~24 m"* against a 5 m/s walk speed. So "24 m" is a repo
convention layered on a unitless engine value, not an engine guarantee.

**Reject the research's playback code outright.** It constructs a fresh `AudioStreamPlayer3D` per
sound, sets `GlobalPosition` in the object initialiser *before* the node is in the tree, and
`QueueFree`s on `Finished`. That is spawn-per-sound — the exact anti-pattern `SfxLab` was written
to prevent, with the position-before-tree bug `JuiceFx` documents in its own comments layered on
top. **Use `SfxLab.PlayStream3D`**, which is the single compliant positional one-shot path: a
bounded pool, prefer-idle then steal-oldest, per-shot pitch jitter so repeats do not sound
machine-gunned, and lazy re-anchoring across scene changes.

### 6. The synced peak

Reserved, rare, and only where `/direct` has authored a build for it to release. §8.2: a startle
is a release, never an engine. §4.4 puts the ceiling at roughly one true spike per session
(`[research default — pending Talon confirmation]`, and not this skill's to confirm).

When one is directed, the alignment has to be exact — §4.6 is about the *same instant*, and audio
that trails its visual by a few frames reads as two events. Fire both from one call site rather
than from two systems that happen to be listening to the same signal, and gate the audio half on
the visual half's actual presentation rather than on the trigger.

## Tuning guide

Reasoning attached, none of it a gate.

- **Cut fast, return slow.** The research's 0.4 s / 1.2 s is a sane starting shape. The ratio
  between them is the part doing the work.
- **Silence duration** long enough to be noticed, short enough that players do not conclude the
  audio broke. This is the value most likely to need a headed playtest rather than a number.
- **40–110 s between silences** — a range, deliberately wide, because irregularity beats
  frequency. Re-roll per event from the server.
- **Ratio drift with the clock** — more synced peaks as the session runs is the research's
  recommendation and it is coherent with §3's arc. Whether TIDE's *cyclic* clock justifies it is
  a different question; see *Integration points*.
- **Bed level** set relative to proximity voice at conversational distance, not in isolation. If
  the bed makes a teammate at 10 m harder to understand, it is too loud regardless of how it
  sounds alone.

## Integration points

- **`CycleDriver.Instance.Phase`** is the shipped clock. Consume it; author nothing new. Gate
  every read on **`CycleDriver.Instance.Synced`** — a client's `Phase` sits at its
  zero-initialised default until the first authoritative update lands, and rendering that as
  "phase 0" is the bug `CycleDriver` names as the most likely one in the feature. `DayNightSky`
  shows the guard.
- **`Phase` is cyclic, not monotonic.** It wraps in `[0,1)` over a 120 s default period, and
  `CyclesElapsed` counts the wraps. It is **not** the research's one-way 0→1 `DayProgress` across
  a session. A sync ratio bound directly to `Phase` resets every two minutes and will re-spend
  its peak allocation on every cycle. Any session-monotonic quantity must be **derived, with the
  derivation stated** — that derivation belongs to `vfx-escalation`, not here.
- **`NetCodec`** for the transfer-channel constant, alongside `MoveChannel` / `PropChannel` /
  `CycleChannel`.
- **`Gameplay.OnPeerConnected`** is the late-join funnel — the same call site that fires
  `CycleDriver.SendPhaseTo` and `PropManager.SendDumpTo`.
- **`SfxLab`** for every positional one-shot, and for the ≤24 concurrent-3D-player budget the bed
  must be counted inside.
- **`scripts/telemetry/`** — the live Firebase path makes the ratio and the event log measurable.
  The research's "correlate the event log with voice-chat activity spikes" is buildable on what
  ships, not hypothetical: `VoiceManager` already tracks per-sender received-packet counts for
  its headless harness.
- **There is no event bus, and `docs/ATMOSPHERIC-VFX-INTEGRATION.md` §3.4 rules it should not be
  built yet.** The research routes everything through `AtmosphereEventBus` and assumes it
  throughout; the map declines it — a bus with one listener (this skill) is exactly the
  YAGNI case decision D2 already covers. Route firing decisions through `vfx-escalation`'s
  `ShouldFireEvent` / `RegisterEventSpend` pair instead.

## Precedent

- **Phasmophobia** — Corey Dixon on deliberately omitting an expected ambient hum in the asylum.
  The direct model for wrong silence, and the reason §6.3 exists.
- **Alien: Isolation** — the tracker emits a sound the alien can hear. §2.3's cost-to-know, and
  the model for any future audio-with-consequence. `unbuilt` in §10.
- **`scripts/game/world/CycleDriver.cs`** — the replication contract this skill copies:
  server-owns-the-input, sim-tick advance, authority-mode RPC, staleness guard, targeted
  late-join sync, `Synced` gate.
- **`scripts/game/sandbox/SfxLab.cs`** — the pooled-playback contract and the concurrency budget.
- **`scripts/voice/`** — the boundary, and proof that Sail can already do positional audio
  properly.

## Troubleshooting

- **Wrong silence lands on nobody** — check the bed exists and has been running long enough to
  fall below attention. This is the first thing to check and it will be the answer more often
  than anything else in this list.
- **Some players hear the cut and others do not** — the trigger is being randomised per client
  instead of server-authored. Pattern 2.
- **A player who joined mid-round hears the bed during a silence** — no targeted late-join sync.
  `SendPhaseTo`'s treatment, applied to the silence state.
- **Players predict the silence** — it is co-occurring with a visible tell, or the interval is
  effectively fixed. §8.3 has been violated by construction, not by tuning.
- **The cut reads as a bug rather than a beat** — the fade is too slow (reads as a mix change) or
  the bed was too prominent to have been forgotten in the first place.
- **A beat feels frightening in play and flat on a recording** — that is §4.2, and the fix is a
  sharper audible or visible trigger at the moment of dread. **Not more ambience.** Proposing
  more atmosphere against a legibility failure is the single most likely wrong answer this skill
  can give.
- **Playtesters report sting fatigue** — check the ratio before touching any individual trigger.
  Peaks firing too often relative to silence and unexplained events is the usual cause.
- **A sound reveals what it was supposed to leave ambiguous** — a companion visual fired that
  should not have. Mode 5's discipline is in the omission.

## Caveats

- **No numbers as law.** 60/30/10, 40–110 s, 0.4 s / 1.2 s, 24 m — every one is a starting point
  with a reason, and the ratio in particular is `/direct`'s to set per level.
- **Unverified is stated, not implied.** `Tween.Finished`, `AudioStreamPlayer3D.MaxDistance` and
  `UnitSize` were checked against `GodotSharp` 4.7.0. Anything not checked is marked
  `unverified against Godot 4.7` inline. Research code is not evidence.
- **Not the voice pipeline.** `scripts/voice/` is shipped, tuned, and out of scope. §5.5 and §5.6
  need an afterlife channel that does not exist — report it, do not route around it.
- **Not a resolver.** §9's tone **ratio** bears directly on how a sting is allowed to sound and
  stays open (the axis was decided 2026-07-26; the calibration was not); §6.2's night-floor
  conflict is a shipped decision in live opposition to doctrine and stays open. Flag what a call
  would foreclose; never infer a house tone from the avatars or from what the references did.
- **The bed is not a formality.** Every technique in this file is downstream of it. A plan that
  schedules withdrawal work before bed work has the dependency backwards.
- **Headless cannot hear.** `tests/Run-*.ps1` can prove that a silence event replicated to every
  peer with the right timing — that part is genuinely CI-testable and should be tested, the same
  way `CycleSelfTest` tests phase arithmetic without a scene tree. It cannot tell you whether the
  silence landed. That needs a headed session with more than one human in it.

*Scope note: written 2026-07-25 against a branch with no ambient bed of any kind, no atmosphere
audio system, no event bus, no afterlife or spectator voice channel, and a §10 register recording
wrong silence as `unbuilt` for exactly that reason. What does exist: proximity voice with 3D
attenuation and a PA route, `SfxLab`'s pooled positional one-shots and synthesised palette,
`CycleDriver`'s replication pattern, and a live telemetry path. **Current bite: none — the
device this skill is named for cannot be executed today.** First real test: an ambient bed
running in a level for a full session, then one server-authored withdrawal inside it, in a
recorded multiplayer session with a mid-round joiner. Passing looks like two players reacting in
the same second to a sound that stopped, the late joiner reacting with them, and nobody able to
say afterwards what the bed had been.*
