---
name: sound-silence-negative-space
description: Use when a Sail audio beat is executed by taking sound away — shaping an ambient withdrawal's cut and return, how long a silence lasts, withdrawing one frequency lane instead of the whole bed, a recovery quiet that reads as relief rather than dread. The shape of the cut, never whether or when it is spent — that stays vfx-audio-sync's.
---

# sound-silence-negative-space

## Overview

**The craft of executing an absence once someone else has decided to spend one.** Envelope
shapes, duration tiers and their perceptual thresholds, partial versus total withdrawal,
selective frequency-lane silence, and the recovery beat as a thing distinct from the
anticipatory beat.

The premise this skill serves — that a soundscape which is always present has no contrast to
exploit, and that removing the bed forces active auditory attention, which is itself an
uncomfortable alert state — is not this skill's to argue. It is `THRILL-BIBLE.md` §6.3's, and
`vfx-audio-sync` already owns it as a device.

**Read the boundary twice, because everything below depends on it.**

| Question | Owner |
|---|---|
| Should this moment be quiet at all? Is the device already spent? | `/direct`, `THRILL-BIBLE.md` §6.3 / §10 |
| Which mode does the moment want — reinforce, withhold, decouple? How often may withhold fire? Must it stay unpredictable? | `vfx-audio-sync` |
| What tension state is this silence sitting inside? Is this the earned breather? | `vfx-escalation` |
| How is the bed built, in what lanes, on what buses? | `sound-soundscape-construction` |
| What does the cut cost, and what may be pooled or freed? | `sound-optimization` |
| **What shape does the cut have, how long does it last, how much comes back, and how fast?** | **this skill** |

`vfx-audio-sync` also already owns the replication contract for a silence event — server-owned
schedule, sim-tick advance, Authority-mode RPC, latest-wins staleness guard, targeted late-join
sync. **This skill does not restate that contract and does not own it.** Everything here is the
*client-side apply step* that runs inside it, plus the pure envelope math the server needs to
tell a late joiner where in the envelope to land.

**And the bed does not exist.** `vfx-audio-sync`'s opening section states the blocker in full and
it is not repeated here beyond the one line that governs this file: with no ambient bed, Sail is
in total permanent silence already, so nothing can cut out. Every technique below is
unexecutable today. See the scope note.

## Directed, not decided

This skill is HOW-layer substrate. It executes a directed absence; it never originates one.
There is no reading of `THRILL-BIBLE.md` in which "the silence should be six seconds" is a
directorial call — but there is also no reading in which "we should go quiet here" is an
engineering one. Keep the two apart.

Concretely: if a proposal from this skill would decide **whether** a silence happens, **what it
means**, or **how often** it fires, stop and route it to `/direct` or `vfx-audio-sync` by name.
A shorter envelope is a tuning answer to a directorial complaint only when `/direct` has already
said the beat should land and it is landing wrong.

**Forks carried, not resolved.** `THRILL-BIBLE.md` §9's tone **axis is decided** (2026-07-26,
dread-forward, atmosphere named first); the **ratio** — how often a dread build cashes out
absurd — is open, ripeness trigger *"the first playtest in which anyone is actually
frightened."* It bears here because the return from silence is where an absurd payoff would
land if there is one, and picking a return shape that forecloses that is a silent resolution.
§6.2's night ambient floor versus night reversal is a live conflict between a shipped decision
(`DayNightSky.MinAmbientEnergy = 0.30f`) and doctrine; no skill overrules it. §4.4's
one-spike-per-session ceiling is `[research default — pending Talon confirmation]` — carry that
marker verbatim into any comment or config. `LEVEL-BIBLE.md` §8.3 is `[BLANK — Talon]` on
whether Sail commits to a dedicated always-meaningful diegetic audio channel, and the bible
itself says it *"constrains sound design broadly, which is why it is a call rather than a
detail."* It constrains this skill hardest of all — see pattern 8.

## When to Use

- A withdrawal's cut-and-return envelope needs designing, or an existing one reads wrong
- A silence duration has to be picked, and the question is what the player concludes at that length
- One lane of the bed should drop rather than the whole thing
- A recovery quiet after a tension peak has to read as relief and is reading as more dread
- A late joiner has to land in the middle of a silence rather than starting a fresh one
- Two silence events overlap, or a silence is interrupted by a second directed beat
- An anticipatory silence has to complete before a reveal rather than overlap it

**Not for:** whether a moment should be quiet, or whether the device is spent (`/direct`,
`THRILL-BIBLE.md` §6.3/§10); which mode a moment wants, the sync ratio, the unpredictability
requirement, and the server-authored replication contract (`vfx-audio-sync` — this skill runs
inside it, cite it, do not fork it); the escalation state or the earned-breather budget a
recovery beat belongs to (`vfx-escalation`); how the bed is layered, synthesised, or split into
lanes (`sound-soundscape-construction` — a hard dependency, see pattern 5); pool economics,
node lifecycle, and the ≤24 concurrent-3D-player budget (`sound-optimization`); the proximity
voice pipeline (`scripts/voice/` — shipped, tuned, never forked); the urgency cue
(`/spec-urgency-cue`, `LEVEL-BIBLE.md` §8.1).

## What exists in the repo today

| Thing | State |
|---|---|
| Ambient bed | **Does not exist.** No looping or continuous audio anywhere. Every playback path in the repo is one-shot. |
| `"Ambient"` bus | **Does not exist.** Buses are `Master`, `Sfx`, `Voice`, `VoiceCapture`, `PA` — all created in code, no `default_bus_layout.tres`. |
| `AmbientBed.Withdraw` / `Restore` | **Specced, not present.** `docs/superpowers/2026-07-26-wp-ambient-bed-dispatch.md` §3 defines them; the code lives on branch `feat/ambient-bed` and is not on `feat/neighbourhood-exterior`. **That signature is the API this skill's envelope lands behind.** |
| Any silence controller | Does not exist. No `SilenceEvent`, no `WrongSilenceController`, no scheduler. |
| `scripts/game/sandbox/SfxLab.cs` | Exists. 14-slot `AudioStreamPlayer3D` pool, prefer-idle then **steal round-robin** via a `_next` cursor, dedicated `Sfx` bus, `EnsureBus()` as the bus-creation pattern to copy. Zero test coverage. |
| Audio asset files | **Zero.** No `.ogg`/`.wav`/`.mp3` in the tree. Everything is synthesised at runtime. |
| Event bus | **Deliberately not built.** `docs/ATMOSPHERIC-VFX-INTEGRATION.md` §3.4: *"Build the bus when a second listener actually exists."* |
| `scripts/ui/SettingsPanel.cs` | Master and Voice sliders only. **No Sfx or Ambient slider** — see *Integration points* for why that matters to a device whose magnitude is a volume difference. |
| Tests touching audio | **None.** No `Run-AudioTest.ps1`, no `Run-AmbientBedTest.ps1`, nothing asserting an `AudioStream` or an `AudioServer` bus. |
| Current level work | `docs/superpowers/specs/2026-07-28-neighbourhood-ring-design.md` §2 lists **audio explicitly out of scope** for the neighbourhood ring, alongside the forest, the school building, lighting and shaders. |

## Core patterns

### 1. The envelope is the apply step, not the event

The brief's `TriggerSilence()` is client-local: each client builds its own tween and starts its
own timer. **That is the exact failure `vfx-audio-sync` names as the whole device broken** —
every client cuts at a different moment, nobody looks up together, and the shared-stake quality
that makes it a group beat is gone. Do not re-derive the fix; `vfx-audio-sync` pattern 2 has it
(server owns the schedule, sim-tick advance rather than a wall-clock `Timer`,
`[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = ...)]`
with its own channel constant next to `NetCodec.CycleChannel`, latest-wins seq guard, and a
targeted late-join sync through `Gameplay.OnPeerConnected` carrying both *whether* a silence is
active and *how much remains*).

What is yours is the consequence of that last clause. **"How much remains" only means something
if the envelope is constructible from a mid-point** — which forces the envelope curve to be a
pure function of time that the server can evaluate without a scene tree, so it can hand a joiner
a starting offset and a truncated remainder rather than a total plus an elapsed. That pure
function is the `CyclePhase.FromElapsed` seam applied to audio, and it is the one genuinely
CI-provable thing in this skill:

```csharp
/// <summary>Pure envelope math, pulled out of the node so it is testable without an audio
/// device or a scene tree — the exact seam CyclePhase gives CycleDriver (CycleSelfTest).
/// Returns a dB OFFSET relative to whatever the bed's baseline is; never an absolute level.</summary>
public static class SilenceEnvelope
{
    public static float OffsetDbAt(float t, float fadeOutSec, float holdSec,
                                  float fadeInSec, float residualOffsetDb)
    {
        if (t <= 0f) return 0f;
        if (t < fadeOutSec)              // cut
            return Mathf.Lerp(0f, residualOffsetDb, t / fadeOutSec);
        if (t < fadeOutSec + holdSec)    // hold at the residual
            return residualOffsetDb;
        float back = t - fadeOutSec - holdSec;
        if (back < fadeInSec)            // return
            return Mathf.Lerp(residualOffsetDb, 0f, back / fadeInSec);
        return 0f;
    }
}
```

Headless can assert: `OffsetDbAt(0) == 0`, continuity at both seams, monotonicity inside each
segment, exact return to `0f` at total duration, and that a joiner at `t = 2.6` computes the
same offset the in-progress peers are currently at. It cannot assert that anyone noticed.

### 2. `async void` + `CreateTween()` + an independent `SceneTreeTimer` is three bugs

The brief's `TriggerSilence()` is `async void`, builds a `CreateTween().SetParallel()`, then
`await ToSignal(GetTree().CreateTimer(FadeOutSec + HoldSec), Timeout)` before building a second
tween for the return. Each of those three pieces fails separately.

**Verified against Godot 4.7** (class reference; repo runs `Godot.NET.Sdk/4.7.0`):

- `Node.create_tween()` binds the tween to the node — *"the Tween will halt the animation when
  the object is not inside tree and the Tween will be automatically killed when the bound object
  is freed."* So on a scene change the tween dies **and the `async void` continuation does not**.
  It resumes on a freed node, and because the method is `async void` there is no task for the
  caller to observe the exception on.
- `SceneTree.create_timer` *"will emit SceneTreeTimer.timeout and will be automatically freed"*
  after `time_sec`. It is a separate clock. It does not know the tween halted, so the return
  fade fires against layers still parked wherever the halted cut left them.
- `GetTree()` returns null on a node outside the tree, so the `await` line itself is the
  null-reference site during a teardown.
- *"Tweens are not designed to be reused and trying to do so results in an undefined behavior.
  Create a new Tween for each animation"* — so a controller that keeps one tween and re-runs it
  per silence is already outside the contract.

**The correct shape is one tween that declares the whole envelope up front, with no `await` and
nothing that can outlive it.** `tween_interval` *"can be used to create delays in the tween
animation, as an alternative to using the delay in other Tweeners"* (**verified**), and
`Finished` is *"emitted when the Tween has finished all tweening"* — the whole chain including
intervals — but *"never emitted when the Tween is set to infinite looping"* (**verified**), which
is why a self-rearming scheduler built on `Finished` would stop silently. In a server-authored
design that does not bite, because the schedule is not the client's.

```csharp
// CLIENT-SIDE APPLY STEP ONLY. The roll, the schedule, the RPC and the late-join sync are
// vfx-audio-sync's contract; this is what that contract calls on every peer.
private Tween? _envelope;
private float _baselineDb = float.NaN;   // captured once, on the 0 -> withdrawn transition
private int _ambientBus;                 // AudioServer.GetBusIndex("Ambient"), see EnsureBus

/// <param name="startOffsetDb">Where in the envelope this peer begins — 0 for a live event,
/// SilenceEnvelope.OffsetDbAt(elapsed, ...) for a peer that joined mid-silence.</param>
public void ApplySilence(float fadeOutSec, float holdSec, float fadeInSec,
                         float residualOffsetDb, float startOffsetDb)
{
    // One tween per event; kill rather than layer. Tween is RefCounted (verified: "Inherits:
    // RefCounted < Object"), so the field keeps it alive and IsValid() — "a valid Tween is a
    // Tween contained by the scene tree" — is the right check, not IsInstanceValid.
    if (_envelope is { } prev && prev.IsValid())
        prev.Kill();

    // Baseline capture, exactly once. A second event landing mid-withdrawal must NOT capture
    // the withdrawn level as its baseline, or the bed never comes back. See pattern 3.
    if (float.IsNaN(_baselineDb))
        _baselineDb = (float)AudioServer.GetBusVolumeDb(_ambientBus);

    Tween t = CreateTween();
    // Cut fast (EaseIn: the loss arrives late and hard), hold, return slow (EaseOut: the
    // return arrives early and trails, so the player cannot name the moment it was over).
    if (startOffsetDb > residualOffsetDb)   // still in the cut, or starting fresh
        t.TweenMethod(Callable.From<float>(SetAmbientOffsetDb), startOffsetDb, residualOffsetDb,
                      fadeOutSec).SetEase(Tween.EaseType.In).SetTrans(Tween.TransitionType.Sine);
    if (holdSec > 0f)
        t.TweenInterval(holdSec);
    t.TweenMethod(Callable.From<float>(SetAmbientOffsetDb), residualOffsetDb, 0f, fadeInSec)
     .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Sine);
    t.Finished += ReleaseBaseline;   // not a timer callback, not an await continuation

    _envelope = t;
}

private void SetAmbientOffsetDb(float offsetDb)
    => AudioServer.SetBusVolumeDb(_ambientBus, _baselineDb + offsetDb);

private void ReleaseBaseline() => _baselineDb = float.NaN;
```

`tween_method` *"calls a method over time with a tweened value provided as an argument"*
(**verified**); `set_ease` and `set_trans` set the default for tweeners appended after them, and
`PropertyTweener`/`MethodTweener` override per-tweener (**verified**). `Callable.From<float>` is
the standard .NET binding for a `MethodTweener` target — the GodotSharp C# surface over the same
method, `unverified against Godot 4.7`'s XML docs specifically but present across the 4.x C# API
reference.

Note what is deliberately *not* here: `SetParallel()`. The brief reaches for it because it is
tweening N layer volumes at once. Tweening the **bus** instead makes the whole withdrawal one
value, which removes the parallel step, removes N baselines, and survives players being added to
or removed from the bed mid-envelope. `set_parallel` is real and correct — *"the Tweeners
appended after this method will by default run simultaneously"*, with `chain` *"used to chain two
Tweeners after set_parallel() is called with true"* (**both verified**) — and you need both the
moment lanes get separate targets (pattern 5). One value is simpler; reach for parallel when the
lanes force it, not by default.

### 3. Absolute dB targets break; relative-to-a-captured-baseline does not

The brief tweens the residual layer to a hardcoded `-18f`. That is only the intended −18 dB of
attenuation if the layer started at exactly 0 dB, which it does the first time and never again
after a mix pass, a per-level bed level, or a player volume setting.

**Two failure cases, in the order they will happen:**

1. **A mix change.** The bed gets set to −6 dB for the interior. The withdrawal now takes it to
   −18, a 12 dB drop instead of the 18 that was tuned. The device quietly weakens and nothing
   in the code changed.
2. **Overlap, which is not hypothetical.** `vfx-audio-sync`'s scheduler rolls silences on an
   interval *and* a directed beat can trigger one. Both will eventually be in flight. If the
   second event captures its baseline while the first is at the residual, the second event's
   "restore to baseline" restores to the residual — **the bed is now permanently withdrawn and
   the only symptom is that ambience stopped and never came back.**

The fix is in pattern 2's code: **one owner of the withdrawal state per bus, one baseline
captured strictly on the un-withdrawn → withdrawn transition, released on `Finished`, and an
overlapping event `Kill()`s and re-targets the existing tween rather than starting a second
one.** The bed dispatch spec already demands the behavioural half of this: *"withdrawing during
dusk and restoring should land back at dusk's gain, not day's."*

**The bus-versus-player-volume fork, stated rather than drifted into.** `AudioServer` gives you
`GetBusVolumeDb` / `SetBusVolumeDb` / `SetBusMute` / `SetBusSend` (**all verified present**), but
a bus is not an object with a tweenable property — hence `TweenMethod` rather than
`TweenProperty`. On a node, `AudioStreamPlayer.volume_db` is *"volume of sound, in decibels. This
is an offset of the stream's volume"*, default `0.0`, and `volume_linear` exists as a convenience
that modifies it (**verified**). The string property path for `TweenProperty` on the node route
is `"volume_db"` — the engine's snake_case name, not the C# `VolumeDb` — because
`tween_property` takes a `NodePath` against the engine property (**verified**: *"You can find the
correct property name by hovering over the property in the Inspector"*). Getting that wrong fails
silently: the tween runs, nothing moves.

Prefer the bus for total withdrawal. Prefer per-layer or per-lane-bus for anything partial.

**One integration hazard the bus route creates and should be designed around now:** if an
`Ambient` volume slider is ever added to `SettingsPanel` (it does not exist today), the slider
and the envelope become two writers of the same `SetBusVolumeDb` value and the last write wins
every frame. The clean shape is a dedicated withdrawal bus that the ambient layers send into and
which sends to the player-facing `Ambient` bus — the player's setting and the device's envelope
then multiply instead of fighting. `SetBusSend` is the mechanism; `SfxLab.EnsureBus()` is the
lazy-idempotent creation pattern to copy.

### 4. Never total digital silence — and the reason, not just the rule

**Absolute silence is indistinguishable from a crashed audio device.** That is the whole
argument. A player whose game goes completely silent runs the diagnosis a player runs: check the
volume, check the headphones, alt-tab. Every one of those is the opposite of the attention state
the beat wanted, and the beat is over before it started. A residual proves the pipeline is alive,
which is what licenses the player to interpret the absence as content.

The residual has a second job that is more interesting than the first. **It is the thing the
player strains at.** A thin wind or low rumble left running at a level that is legible but not
informative is an invitation to listen harder — which is the actual mechanism behind "what you
don't hear makes you listen." Cut to nothing and there is nothing to strain at; the player's
attention has no target and disperses.

Two constraints follow:

- **The residual must be a layer that was already in the bed.** Introducing a new sound at cut
  time is an addition wearing a subtraction's clothes. The player hears an arrival, not a
  departure, and §6.3's whole claim is that the departure is the stronger of the two.
- **The residual is not a fixed number.** It is the level at which the bed is still audible in
  the player's actual room over their actual output, which is unknowable from here and is why
  this is the value most likely to need a headed session rather than a tuning pass.

**The harder version of the problem, and the reason this section currently has no bite.** Sail
has no bed. It is in total permanent silence right now. `vfx-audio-sync` states it exactly:
*"-80 dB of nothing is indistinguishable from 0 dB of nothing."* A residual doctrine presumes
something to leave running. Until the bed ships, every tier in the tuning guide below is a
number describing a duration of nothing.

### 5. Selective-lane withdrawal — the better device, and a hard dependency

Silencing one frequency lane — the high-mid insect chatter — rather than the whole mix is the
strongest thing in the brief, for a reason it does not state: **it reads as diegetically
motivated.** Insects going quiet at a predator's approach is a real signal players have
encountered outside a game, so the withdrawal reads as the world reacting rather than as an
author cutting a track. Total silence has no such alibi; it can only ever read as authored.

It is also the cheaper and more repeatable device. `vfx-audio-sync` says it directly: a partial
withdrawal *"is a different and more deniable cue than total silence, and it is the version that
survives repetition better."* Total silence is a device with a small budget. A lane withdrawal
can be spent more often before it decodes — but *how much* more often is the ratio, and the ratio
is `vfx-audio-sync`'s.

**The dependency is hard and must be stated before the bed is built, not after: you cannot
withdraw a lane that was never separated.** `sound-soundscape-construction` owns the bed's
frequency separation. If the bed ships as one synthesised stereo render, or as two crossfaded
day/night layers with no band structure, selective-lane silence is unbuildable and total
withdrawal is the only device available. That constraint has to reach the bed packet while the
bed is still being designed.

**Resume slower than you cut, and slower than a total-silence resume.** A lane that returns at
the rate it left announces itself as a switch; a lane that seeps back over roughly twice the cut
time is never a moment the player can point at. The brief's `FadeSec * 2f` on resume is the right
instinct and the right order of magnitude.

**Retargeted to the setting.** The 90s house and its ring give a legible lane vocabulary with
diegetic causes attached: cicadas outside a window, the furnace cutting out, the fridge
compressor stopping, rain on a roof, a TV in another room. "The bugs stopped" translates
directly and is the best of them, because it has a cause the player supplies themselves. The
appliance lanes are the cheapest to author — a furnace has a legible on/off already — but they
are also the most explicable: a player can attribute the fridge stopping to the fridge. Whether
that deniability is the point or the failure is a directorial call and belongs to `/direct`.

### 6. The recovery beat is a different envelope, not a shorter one

After a tension peak, a quiet that must read as **relief**. The mistake is treating it as
anticipatory silence with different numbers.

| | Anticipatory | Recovery |
|---|---|---|
| The cut | fast; the abruptness *is* the signal | slow; the tension layers ebb rather than stop |
| What remains | a thin residual, deliberately insufficient | the calm baseline, restored to full |
| What the player does | strains to listen | stops listening |
| What it resolves into | a reveal, or nothing — and nothing is often stronger | nothing, and that is the entire point |
| Shape | short fade-out → hold → long fade-in | long crossfade, calm lane entering under the tension lane's exit |

The brief's `RecoveryQuiet` gets this right and the delay is the load-bearing part: tension
layers to −80 over `TransitionSec`, calm layers back to 0 with `SetDelay(TransitionSec * 0.3f)`.
`PropertyTweener.set_delay` *"sets the time in seconds after which the PropertyTweener will start
interpolating"* (**verified**). **The 30 % overlap is what makes recovery a distinct technique**:
the calm bed must be audibly arriving before the tension bed has finished leaving, or the gap
between them is itself a silence, and a silence at the end of a tension peak re-arms dread at the
exact moment you were trying to release it. An accidental anticipatory beat inside a recovery
beat is the most likely failure in this whole file, because everything about it looks correct in
code.

**Whether a moment is a recovery beat is not this skill's call.** `vfx-escalation` owns the
tension budget and the earned breather; if the breather does not land, the budget never resets
and escalation has nothing to escalate from. Route the decision there and build the envelope
here.

### 7. Anticipatory silence completes before the reveal — and the ordering is server-side

Chain the silence directly *before* the reveal rather than overlapping it. The gap is what builds
anticipation; if sound is still present when the threat appears, the quiet beat never registered
and the silence was spent for nothing.

The brief's `AnticipatorySilenceThenReveal` triggers a silence, `await`s
`SilenceBeforeRevealSec`, then invokes a callback. **Under a server-authored contract that
ordering cannot live in a client-side await.** The reveal is itself a replicated event; two
clients whose envelopes ran at different frame rates, or one that hitched, will invoke the
callback at different moments — and §4.1's simultaneity condition is precisely what makes a group
beat a group beat. **The server schedules both offsets from one authoritative event**; the client
receives two ordered events and applies each.

The distributional risk — that a silence which reliably precedes a reveal becomes a telegraph,
teaching players when they are safe (§8.3) — is real and is **not this skill's to manage**. It is
`vfx-audio-sync`'s ratio, and it is the reason the ratio matters more than any individual
envelope. Named here so a scheduler built from this file does not quietly default to
always-precedes.

### 8. Silence makes the group audible to itself

The most interesting property of a withdrawal in a 2–6 player session, and the one the brief only
half-states.

Cutting the bed changes nothing about voice attenuation — `VoiceConfig.ProximityUnitSize = 6.0f`
and `ProximityMaxDistance = 24.0f` are untouched, and must stay untouched. What it changes is the
**masking floor**. A teammate at 15 m who was partially masked by the bed becomes fully legible
the instant the bed goes, so the withdrawal is also an unmasking, and the moment of maximum
silence is the moment of maximum group audibility in the entire session.

That cuts both ways and the sign depends on what the beat wanted:

- **Feature.** If the beat wants the group to hear each other go quiet, this is the *only* moment
  in the game where that is audible. §10's register lists proximity voice falloff as **`fresh` —
  shipped, never used as a device**; a withdrawal that unmasks voice spends it for free, with no
  new system. Six kids on a sleepover hearing each other stop talking is the beat, not a side
  effect of it.
- **Failure.** Total withdrawal in a group that is *not* going quiet reads as the game switching
  itself off and leaving everyone in a voice call. Self-consciousness is not dread, and once a
  player is aware of the software the beat is gone.

**The lever is the residual, and this is a real reason to run it higher in multiplayer than a
solo-tuned value would suggest** — not because silence is worse in co-op, but because the
residual's job now includes covering the seam between world audio and voice. For the same reason,
prefer selective-lane withdrawal in group contexts: a lane withdrawal moves the masking floor in
one band rather than across the spectrum, so voice is unmasked partially rather than nakedly.

**Boundary.** This is a **mix** observation on the world side. Do not touch `scripts/voice/`, do
not fork `VoiceConfig`'s attenuation values, do not add DSP to the `Voice` bus. Choosing the
residual's frequency band so it does not sit where voice sits, and ducking the bed under speech,
are both world-side and both fine.

**`LEVEL-BIBLE.md` §8.3 lands squarely here and is `[BLANK — Talon]`.** If Sail commits to a
dedicated diegetic channel that is *always* meaningful, then removing it is a state change with a
fairness contract attached, and the residual stops being a mix call and becomes a
correctness one. If Sail does not, the residual is free. The blank is not resolvable by
inference and this skill does not resolve it.

### 9. "Drop near-silent players from the pool during the hold" is a trap

The brief claims silence is essentially free — true of the tweens, and false as a licence to do
node lifecycle work during a beat.

`SfxLab.Rent` **prefers a non-`Playing` slot**, grows to `PoolSize = 14`, then steals
round-robin via a `_next` cursor. So a pooled `AudioStreamPlayer3D` stopped mid-fade is instantly
the *most* attractive rent target in the pool. A withdrawal that stops its own players hands its
slots to the next one-shot that fires, and the return re-acquires different nodes — at different
positions, with different pitch jitter, and possibly not at all if the pool is exhausted and
`Rent` steals the one it just got.

**The safe version, which the bed spec already implies:** the continuous bed is not in the
one-shot pool at all. It is its own long-lived player(s) on the `Ambient` bus; only the sparse
positional *event* layer rides `SfxLab.PlayStream3D` (bed dispatch §2 — *"do not add a new
`AudioStreamPlayer3D` pool"*). Withdrawing the event layer is therefore a **cadence** change —
stop scheduling new events — never a stop on shots already in flight. An event mid-playback when
the cut lands should be allowed to finish under the falling bus, which is also more truthful:
sounds do not stop mid-syllable in the world.

And the withdrawal frees no budget regardless. The bed's players still exist during the hold, at
−80 rather than gone; the ≤24 concurrent-3D-player figure (which already counts the five voice
speakers) is unchanged by silence. Any actual pooling economy is a separate decision and belongs
to `sound-optimization`, not to a beat.

## Tuning guide

Starting points with reasons. None is a gate, and **none has ever been run against Sail**,
because there is no bed to run them against.

**Duration tiers.** These are the closest thing this skill has to a table of law, so be explicit
about which are perceptual and which are guesses:

| Duration | Reads as | Basis |
|---|---|---|
| **< 1 s** | an audio bug | **The one tier with a real perceptual basis.** A sub-second dropout in a continuous stream is the exact signature of a buffer underrun or an output-device switch, and the ear's gap-detection threshold sits in the low milliseconds — the discontinuity is *detected* long before the absence can be *interpreted*. Players attribute it to the software. Avoid. |
| **1.5–4 s** | "something noticed" | Playtest guess, unmeasured. The reasoning that survives: it must be longer than the fade-out itself or there is no hold at all, and short enough that it does not demand a resolution. Good for a single warning cue. |
| **4–8 s** | full anticipatory silence | Playtest guess. The reasoning: past the point where a player checks whether the game broke, before the point where they act on the absence. The brief calls it *"the strongest dread tool, use sparingly"* — the sparingly is `vfx-audio-sync`'s ratio, not a duration. |
| **8 s+** | attention decays; players disengage | Plausible, unmeasured, and **probably wrong in co-op specifically.** The disengagement claim assumes a solo player with nothing to do. Six kids on proximity voice will fill 8 s of quiet with talking, which per pattern 8 is either the payoff or the death of the beat. Nobody has run it either way. |

**The brief's own default values contradict its own table, and this is worth catching before it
ships.** `FadeOutSec = 1.5f` + `HoldSec = 4f` + `FadeInSec = 3f` is an **8.5 second event** — the
top tier, the one reserved for a single set-piece per session, presented as the everyday default.
**The fades are inside the tier, not additional to it**: the player's experience of "quiet"
begins somewhere in the fade-out and ends somewhere in the fade-in. Budget the total.

Everything else:

- **Cut fast, return slow.** `vfx-audio-sync` owns this asymmetry and the reason: the abruptness
  of the loss is what registers as *wrong*, the slowness of the return is what keeps the player
  unsure whether it is over, and a symmetric fade reads as a mix change. The **ratio between**
  them is doing the work, not either number.
- **Residual around −18 dB relative to the bed's current level.** Relative, always — pattern 3.
  The real target is "still audible in the player's room, carrying no information", which is a
  headed call.
- **Lane resume at roughly 2× the lane cut.** A lane that returns as fast as it left announces
  the switch.
- **Recovery crossfade with the calm layer entering at ~30 % of the transition.** The overlap is
  the technique; the 30 % is a dial.
- **`Tween.EaseType.In` on the cut, `Out` on the return, `TransitionType.Sine` on both** as a
  starting curve. Linear reads mechanical on a long return; anything with overshoot
  (`Back`, `Elastic`) is disqualified outright because a bed that overshoots its baseline on the
  way back is a new sound.

## Integration points

- **`vfx-audio-sync`** — owns the device, the mode, the ratio, the unpredictability requirement,
  and the server-authored replication contract this skill's envelope runs inside. Invoke it
  first; this skill has nothing to do until it has decided a withdrawal is happening.
- **`vfx-escalation`** — owns the tension state a silence sits in and the earned breather a
  recovery beat *is*. Also owns any session-monotonic derivation.
- **`sound-soundscape-construction`** — the lanes. Pattern 5 is unbuildable without them, and the
  requirement has to reach the bed design before the bed is built.
- **`sound-optimization`** — pool economics, node lifecycle, the ≤24 concurrent-3D budget. Never
  optimise a pool from inside a beat.
- **`AmbientBed.Withdraw(float)` / `Restore(float)`** — `docs/superpowers/2026-07-26-wp-ambient-bed-dispatch.md`
  §3. The API this skill's envelope lands behind. Its spec already requires the relative-baseline
  behaviour of pattern 3 in words.
- **`CycleDriver`** — gate every phase read on `Instance.Synced`, as `DayNightSky` does. And do
  not bind a silence *schedule* to `Phase`: it is cyclic in `[0,1)` over a 120 s default period,
  so silences bound to it recur at identical points of every cycle, which is exactly the
  learnable cadence §8.3 forbids.
- **`NetCodec`** — the transfer-channel constant for an atmosphere channel, alongside
  `MoveChannel` (3), `PropChannel` (4), `CycleChannel` (5). Next free index is 6; confirm against
  the file at build time rather than trusting this line.
- **`Gameplay.OnPeerConnected`** — the late-join funnel, the same call site as
  `CycleDriver.SendPhaseTo` and `PropManager.SendDumpTo`.
- **`SfxLab.EnsureBus()`** — the lazy, idempotent bus-creation pattern any new `Ambient` or
  withdrawal bus copies verbatim.
- **`scripts/ui/SettingsPanel.cs`** — has no Sfx or Ambient slider. **A device whose magnitude is
  a volume difference has no floor once the player can set that volume**: a player running the bed
  at 20 % has pre-withdrawn it, and the beat lands on nobody. If a slider is added, the dedicated
  withdrawal bus of pattern 3 is what keeps the two independent.
- **`scripts/telemetry/`** — the live Firebase path makes the *replication* half measurable: log
  the server's cut timestamp and each peer's apply timestamp, and the spread across peers is a
  real number rather than an impression. It cannot measure whether anyone noticed.
- **No event bus, deliberately.** `docs/ATMOSPHERIC-VFX-INTEGRATION.md` §3.4. Do not build one
  for this.
- **`docs/superpowers/specs/2026-07-28-neighbourhood-ring-design.md`** — audio is explicitly out
  of scope for the neighbourhood ring. Nothing in this skill is scheduled work on that spec.

## Precedent

- **`scripts/game/world/CycleDriver.cs`** — the seam this skill copies is `CyclePhase`: pure math
  pulled out of a `Node` purely so it can be tested without a scene tree. `SilenceEnvelope` in
  pattern 1 is the same move for an audio envelope, and it is the only thing here headless can
  check.
- **`scripts/game/sandbox/SfxLab.cs`** — the pooling contract, the prefer-idle-then-steal rent
  behaviour that makes pattern 9 a trap, and `EnsureBus()` as the bus pattern.
- **`docs/superpowers/2026-07-26-wp-ambient-bed-dispatch.md`** — `Withdraw`/`Restore`, and the
  restore-to-dusk-not-day requirement that is pattern 3 stated as behaviour.
- **Phasmophobia** — the deliberately omitted asylum hum. §6.3's origin; `vfx-audio-sync` carries
  it properly.
- **Alien: Isolation** and **P.T.** — the brief cites both for pairing silence with a specific
  legible trigger (the vent system activating; the radio cutting out) so players associate the
  quiet with a cause rather than randomness. **The transferable part is the cause, not the
  silence.** Note that this is the brief's design-reading of two shipped games, not a verified
  developer statement, and should be treated as a hypothesis about why they land. The 90s house
  supplies its own causes: the furnace, the fridge compressor, the cicadas, the TV in the other
  room, rain stopping on the roof.

## Troubleshooting

Symptom → cause, in the order they will actually occur.

- **Nothing is audible before or after the cut** — there is no bed. This will be the answer more
  often than everything else in this list combined.
- **The cut reads as a crash; players check their headphones or alt-tab** — no residual, or the
  residual is below the player's room noise floor. Pattern 4.
- **Players hear the cut at different moments** — the trigger is being rolled per client instead
  of server-authored. `vfx-audio-sync` pattern 2, not a tuning problem.
- **A mid-round joiner hears the bed through everyone else's silence** — no targeted late-join
  sync, or the sync carries *whether* a silence is active without carrying *how much remains*.
- **The bed goes quiet and never comes back** — a second event captured a withdrawn level as its
  baseline. Pattern 3's overlap case. Check that the baseline is captured on the transition, not
  on every event.
- **The return fires while the cut is still running** — the `SceneTreeTimer` await is running on
  its own clock, independent of the tween. Pattern 2.
- **An exception on a freed object after a scene change** — an `async void` continuation resumed
  on a node whose bound tween was killed with it. Pattern 2.
- **The withdrawal weakens after a mix pass and no code changed** — absolute dB targets.
  Pattern 3.
- **Selective-lane silence is imperceptible** — the bed was not built in lanes; one render cannot
  be partially withdrawn. `sound-soundscape-construction`.
- **Voice suddenly feels loud, exposed, or awkward** — pattern 8. Raise the residual or switch to
  lane withdrawal. Do **not** touch `VoiceConfig`.
- **A recovery beat makes things tenser** — the calm layer is entering after the tension layer has
  fully left, so the join reads as a third silence. Pattern 6's overlap.
- **The silence lands once and never again** — total withdrawal spent repeatedly. Partial
  withdrawal survives repetition; how often either may fire is `vfx-audio-sync`'s ratio.
- **The timing feels arbitrary** — the trigger has no diegetic cause. Tie every cut to something
  in the house rather than to a scheduled timer, and the arbitrariness is what disappears.
- **"It went quiet and nobody could say what stopped"** — **that is a pass, not a bug.** It will
  be reported as one. It is the passing condition in the scope note below.

## Caveats

- **No numbers as law.** Every duration tier, every fade time, the −18 dB residual, the 2× lane
  resume, the 30 % recovery overlap — starting points with reasons attached, none run against
  Sail, and the `< 1 s` tier is the only one with a perceptual basis rather than a guess.
- **Unverified is stated, not implied.** `Tween.Finished`, `tween_interval`, `tween_method`,
  `set_parallel`/`chain`, `set_ease`/`set_trans`, `PropertyTweener.set_delay`, `Tween` inheriting
  `RefCounted`, `kill`/`is_valid`, the node-bound-tween lifecycle, `SceneTree.create_timer`'s
  auto-free, `AudioStreamPlayer.volume_db`/`volume_linear`, and the `AudioServer` bus volume API
  were checked against the Godot 4.x class reference against which `Godot.NET.Sdk/4.7.0` builds.
  `Callable.From<float>` is marked `unverified against Godot 4.7` inline. **The brief's code
  samples are not evidence** — three of them are wrong and the reasons are patterns 1, 2 and 3.
- **Not the decider.** Whether silence is spent, how often, and whether it has become predictable
  are `vfx-audio-sync`'s and `/direct`'s. The tension state around it is `vfx-escalation`'s. This
  skill picks a shape and a length for an absence someone else already authorised.
- **Not the replication contract's author.** Cite `vfx-audio-sync` for it; do not fork it, do not
  re-derive it, and do not let an envelope decision quietly change it.
- **Not the voice pipeline.** `scripts/voice/` is shipped and tuned. Pattern 8 is a world-side mix
  observation and stays there.
- **Not a resolver.** §9's tone **ratio** is open (the axis is decided); §6.2's night-floor
  conflict is live and no skill overrules a shipped decision to get its way; §4.4's spike ceiling
  is `[research default — pending Talon confirmation]`; `LEVEL-BIBLE.md` §8.3 is `[BLANK —
  Talon]` and constrains this skill more than any other line in the bibles.
- **The device ledger is forked and must be reconciled before anything writes to it.**
  `THRILL-BIBLE.md` §12 records the fork as of 2026-07-28 between this branch and
  `feat/house-interior-remake` (PR #76), which carries its own Part II — *"Neither register is
  complete and both will be trusted."* §12's neighbourhood-ring block records wrong silence as
  **`untouched`**; §10 records it **`unbuilt` — blocked: no ambient bed exists to withdraw**. A
  device register that disagrees with itself is how a device gets spent twice, and spending wrong
  silence twice is spending it once and then wasting it.
- **Headless cannot hear.** `SilenceEnvelope`'s math and the per-peer replication spread are
  genuinely CI-provable and should be tested the way `CycleSelfTest` tests phase arithmetic.
  Whether the silence *landed* needs a headed session with more than one human in it. This repo
  shipped an island rotated 90° through a green suite; an inaudible beat is easier to ship than
  that was.

*Scope note: written 2026-07-28 against `feat/neighbourhood-exterior` @ `0e715f3`, a branch with
no ambient bed, no `Ambient` bus, no continuous audio of any kind, no silence controller, no test
that asserts anything about an `AudioStream`, and a `THRILL-BIBLE.md` §10 register recording
wrong silence as `unbuilt — blocked: no ambient bed exists to withdraw`. The current level work
(`2026-07-28-neighbourhood-ring-design.md`) lists audio as out of scope. **Current bite: none —
there is nothing to withdraw, so no envelope in this file is executable today, and
`vfx-audio-sync` records exactly that.** First real test: an ambient bed running long enough in a
level to have fallen below attention, then one server-authored withdrawal inside it, in a
recorded 2–6 player session with a mid-round joiner. Passing looks like two players reacting in
the same second to a sound that stopped, the late joiner reacting with them, and neither able to
say afterwards what the sound had been.*
