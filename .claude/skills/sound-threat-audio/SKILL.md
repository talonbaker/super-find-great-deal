---
name: sound-threat-audio
description: Use when a Sail threat needs a voice — a growl bed, a bark, a call from above a ceiling or behind a door — or its nearness must be expressed in sound: acoustics that read as threatening, procedural vocalization in Godot 4.7, redundant distance and direction cueing. Never the perceptual proximity contract — that stays vfx-proximity's.
---

# sound-threat-audio

## Overview

**How a threat's voice is synthesized, and how its nearness is expressed in sound.** Three
things, and only three: the acoustic properties that read as threatening, procedural
vocalization in Godot 4.7, and cueing distance and direction redundantly enough that a player
on laptop stereo gets what a player on headphones gets.

This is HOW-layer substrate, the way `vfx-particles` is substrate for the `vfx-*` family. It
executes a direction and never originates one.

**Retarget before you build.** The source brief for this skill was written against a dark
forest with a parent creature defending young and a harvesting loop. None of that is the game.
The premise is a 90s sleepover — 2–6 kids, a house, and the thing in the attic. A threat voice
here is **something above a ceiling, behind a door, in a crawlspace**, and the hand's own
design contract says its takes are *"implied, off-screen, **heard**"*
(`docs/superpowers/specs/2026-07-26-hand-loop-v1-design.md` §6). That makes audio the primary
carrier of the antagonist and this skill load-bearing — for a thing that does not exist in code
yet. The retired `/spec-family`'s parent-defends-young model stays on record as a historical
design contract in `BEHAVIOR-BIBLE.md` §9; it is not what is being built — a bespoke threat's
behavioural contract is `/spec-entity` work now.

## Directed, not decided

`/direct` and `docs/THRILL-BIBLE.md` decide **whether** a threat is heard, **what** the sound
should make players feel, and **whether the device is already spent**. `vfx-audio-sync` owns
the sync ratio and when a cue is withheld. `vfx-escalation` owns the session escalation curve.
`vfx-proximity` owns the perceptual machinery of sensing a hidden threat. This skill decides
what the waveform is and what it costs.

Concretely: if the question is *should the hand make a sound here*, or *how often*, stop and
route it. If the question is *what does a subharmonic bed actually sound like at 48 kHz and how
many can run at once*, that is here.

**Forks this skill carries and does not resolve:**

- **`THRILL-BIBLE.md` §9 — the tone axis is decided (2026-07-26), the ratio is open.** Dread
  is doctrine; over-the-top absurdity is the register the horror *spends* itself in; the open
  question is *"how often a dread build cashes out absurd versus stays sincere."* **A threat
  voice is exactly what that ratio governs.** A low subharmonic growl and a wet slapstick
  squelch are both defensible §9 outputs, and picking one silently forecloses the ratio. Note
  also that §12.1 of the loop-v1 design splits the hand's registers: its **nature** stays
  never-fully-seen (dread), its **violence** may be quick and comic. Two registers means
  potentially two voices, and which one a given beat gets is `/direct`'s call. Ripeness
  trigger, unchanged: *the first playtest in which anyone is actually frightened.*
- **`LEVEL-BIBLE.md` §8.3 `[BLANK — Talon]`** — whether Sail commits to a dedicated diegetic
  audio channel that is *always* meaningful (the Deep Rock model: no idle chatter, so any bark
  at all carries system state). The bible says this *"constrains sound design broadly, which is
  why it is a call rather than a detail."* It is the single most consequential open question
  for this skill: a threat that vocalizes ambiently and a threat that only vocalizes when
  something is true are different games.
- **`LEVEL-BIBLE.md` §8.1, inherited and binding:** *"Audio alone is never sufficient, because
  the tether severs it on purpose."* Anything built here is a supporting channel.
- **`THRILL-BIBLE.md` §4.4** — one spike per session is
  `[research default — pending Talon confirmation]`. Carry that string verbatim into any
  comment or config that leans on it.
- **`THRILL-BIBLE.md` §6.2** — the night ambient floor vs. the night reversal is a live
  conflict between a shipped decision and doctrine. No skill overrules it.

## When to Use

- A threat needs a sustained voice — a growl bed, a breath, a presence under a ceiling
- A threat needs discrete vocalizations and the pitch/volume mapping needs deciding
- An existing threat sound reads as robotic, buzzy, or synthetic and needs diagnosing
- Distance or direction has to be legible in sound to a player who cannot see the source
- A vocalization budget collides with voice chat or the SFX pool

**Not for:** whether a threat should be heard at all, or what it should make players feel
(`/direct`); the perceptual machinery of sensing a hidden threat's bearing, and the rule that
intensity is computed per-client and **never broadcast** (`vfx-proximity` — read it, and defer
to it rather than reimplementing it); the sync ratio, the ambient bed, wrong silence, or when a
cue is withheld (`vfx-audio-sync`); `AudioStreamPlayer3D` positioning, attenuation models and
bus routing (`sound-spatial-audio`); the session escalation curve (`vfx-escalation`); the
player voice pipeline (`scripts/voice/` — shipped, tuned, out of bounds); UI and menu sound
(`SfxLab.PlayUi`).

## What exists in the repo today

| Thing | State |
|---|---|
| Any threat sound | **Does not exist.** Nothing threat-shaped in audio on this branch. |
| Audio asset files (`.ogg`/`.wav`/`.mp3`) | **Zero, repo-wide.** All Sail audio is synthesized at runtime. |
| `scripts/game/sandbox/SfxLab.cs` | 12 cartoon one-shots synthesized to 48 kHz `AudioStreamWav` at first use and cached; 14-slot `AudioStreamPlayer3D` pool on an `Sfx` bus. The house contract. |
| `SfxLab` synthesis helpers | `EffortGrunt/Thump/Bonk/Warble/Sweep/Oof/TwoNote/Squeak`, plumbing `Render/Envelope/EaseOut/ToWav`. **All `private static`** — extending means editing the file, not calling into it. |
| `scripts/voice/VoiceSpeaker.cs` | The **only** `AudioStreamGenerator` in the repo, on an `AudioStreamPlayer3D` at 48 kHz. The precedent for real-time synthesis. |
| Buses | `Master`, `Sfx`, `Voice`, `VoiceCapture`, `PA` — all created in code. No `Ambient`, no bus layout resource. |
| `AudioEffect*` usage | Three, all on the PA bus (`VoiceManager.EnsurePaBusName`): distortion, lowpass, reverb. Nowhere else. |
| Ambient bed | **Does not exist on this branch.** Specced in `docs/superpowers/2026-07-26-wp-ambient-bed-dispatch.md`, code lives on `feat/ambient-bed`. |
| Event bus | **Deliberately not built** — `docs/ATMOSPHERIC-VFX-INTEGRATION.md` §3.4. Nearest shipped dispatch layer is `ActorFx` + `EventResponse`. |
| Mixer UI | `scripts/ui/SettingsPanel.cs` — Master and Voice sliders only. **No SFX slider.** |
| Test coverage of audio | **None.** No test asserts an `AudioStream`, an `AudioServer` bus, or `SfxLab`. |
| `THRILL-BIBLE.md` §10 audio devices | Every one `unbuilt`. Only the day/night clock and proximity voice falloff are built at all. |

## Core patterns

### 1. Offline-render-and-cache is the house pattern and usually the right answer

`SfxLab.Get` synthesizes into a `float[]`, packs it to a 16-bit mono `AudioStreamWav`, caches
it in a dictionary, and never computes it again. **The per-frame cost after first use is zero.**
Playback then jitters pitch per shot so repeats do not sound machine-gunned — which is already
the brief's "pitch-varied playback of a small set of sources", implemented, minus the sources.

**The decision rule: real-time `AudioStreamGenerator` earns its cost only when a parameter must
vary *continuously during* the sound.** A bark, a snap, a thud, a wet impact — all fixed-length
events with no mid-sound parameter — belong in the offline path. A sustained growl whose
roughness rises while it is sounding is the case that justifies a generator, and it is the only
one on the board.

Consequences worth stating before anyone reaches for a generator:

- **`SfxLab`'s recipes are `private static`.** Adding a threat recipe means adding an `Sfx` enum
  member and a `Get` switch arm inside `SfxLab.cs`. There is no extension seam. That is also why
  none of the synthesis math is reachable from a headless self-test — unlike `CyclePhase`, which
  was deliberately pulled out of its `Node` so it could be.
- **A generator has a latency floor the offline path does not.** Everything pushed into the
  buffer is committed and cannot be re-voiced. Fill `GetFramesAvailable()` to the brim on a
  0.2 s buffer and a parameter change takes up to 0.2 s to be heard. An offline variant swapped
  with `PitchScale` often responds *faster* than the "real-time" system.
- **A sustained generator cannot ride `SfxLab`'s pool.** `Rent` steals round-robin when all 14
  slots are busy (`victim.Stop()`), which would cut a growl mid-breath. A sustained threat voice
  is its own long-lived node, counted separately against the concurrency budget.

### 2. The corrected growl — and the four bugs in the brief's version

The brief's `ProceduralGrowl` is wrong in four independent ways, and one of them is the exact
symptom its own troubleshooting section blames on tuning.

**Bug 1 — the subharmonic is discontinuous, and that is the "buzzy, not organic" complaint.**
`_phase` wraps at `1.0`, but `sub` is computed as `sin(2π · _phase · 0.5)` from that same
accumulator. Half a cycle of sine gets traversed across a full wrap of `_phase`, so at every
wrap `sub` jumps from `sin(π) = 0` to `sin(0) = 0` **through a sign flip in its slope** — and
for any non-zero roughness the summed waveform has a corner at the fundamental's rate. That is
a click, repeating at 40–90 Hz, which is heard as a buzz. **A true subharmonic needs its own
accumulator advancing at half the rate and wrapping independently.** This is not a tuning
issue; no amount of jitter or mix adjustment removes it.

**Bug 2 — `Mathf.Lerp(fundamental, sub, Roughness)` is a crossfade, not a mix.** At
`Roughness = 1.0` the fundamental is entirely gone and the voice drops an octave. The brief's
own escalation advice is *"increase `RoughnessAmount` as aggression rises"* — which, as written,
walks the pitch centre down and out. Roughness is a **depth**: add the subharmonic, then
normalise.

**Bug 3 — `AudioStreamPlayer` is non-positional.** A threat voice with no position is the one
thing a threat voice must not be. `VoiceSpeaker.CreateNode` shows the correct shape: an
`AudioStreamGenerator` on an `AudioStreamPlayer3D`.

**Bug 4 — 44100 is the wrong mix rate for this repo.** `project.godot` pins
`driver/mix_rate=48000`; `SfxLab.SampleRate` is 48000; `VoiceConfig.SampleRate` is 48000. A
generator at 44100 against a 48 kHz mix is a resample nobody asked for. **Verified against Godot
4.7:** `AudioStreamGenerator.MixRate` is documented *"not automatically resampling input data,
to produce expected result should match the sampling rate of input data"* — the mismatch is
silent, not an error.

Secondary, worth knowing: `2.0 * (_phase - floor(_phase + 0.5))` is a **naive sawtooth**, not a
sine. At a 40 Hz fundamental its harmonic series runs far past Nyquist and folds back
inharmonically — a second, independent source of metallic buzz. If a saw is wanted for its
harmonic richness, band-limit it or accept the aliasing knowingly.

```csharp
using Godot;

namespace MpFoundation.Game.Sandbox;

/// <summary>
/// Real-time subharmonic growl bed. Positional by construction; 48 kHz to match the pinned
/// project mix rate; NOT a tenant of SfxLab's one-shot pool (Rent steals round-robin, which
/// would cut a sustained voice mid-breath). Counts as ONE node against the §4 budget of ≤24
/// concurrent 3D players, which already includes the 5 voice speakers.
/// Escalation drives Roughness/MinFreq/MaxFreq from outside — see vfx-escalation. This class
/// decides nothing about when it sounds.
/// </summary>
public partial class ThreatGrowl : AudioStreamPlayer3D
{
    [Export] public float MinFreq = 70f;
    [Export] public float MaxFreq = 110f;

    /// <summary>Subharmonic depth, 0..1 — ADDITIVE, then normalised. Never a crossfade: at
    /// full depth the fundamental must still be present or the voice loses its pitch centre.</summary>
    [Export] public float Roughness = 0.4f;

    /// <summary>Per-sample frequency wander, Hz. Zero here is what "buzzy, not organic"
    /// sounds like; a couple of Hz is what makes it read as a body rather than an oscillator.</summary>
    [Export] public float JitterHz = 1.5f;

    /// <summary>How far ahead to commit. Everything pushed is unre-voiceable, so this is the
    /// response latency to an escalation change, not a performance knob.</summary>
    [Export] public float LeadSeconds = 0.02f;

    private readonly RandomNumberGenerator _rng = new();
    private AudioStreamGeneratorPlayback? _playback;
    private Vector2[] _scratch = System.Array.Empty<Vector2>();
    private float _mixRate = 48000f;
    private double _phase;     // fundamental, wraps at 1.0
    private double _subPhase;  // one octave down, wraps INDEPENDENTLY at 1.0
    private float _baseFreq;

    public override void _Ready()
    {
        var gen = new AudioStreamGenerator { MixRate = 48000f, BufferLength = 0.1f };
        Stream = gen;
        _mixRate = gen.MixRate;   // never hard-code the increment denominator
        _rng.Randomize();
        _baseFreq = _rng.RandfRange(MinFreq, MaxFreq);
        Play();
        _playback = GetStreamPlayback() as AudioStreamGeneratorPlayback;
    }

    public override void _Process(double delta)
    {
        if (_playback == null)
            return;

        int frames = Mathf.Min(_playback.GetFramesAvailable(), (int)(_mixRate * LeadSeconds));
        if (frames <= 0)
            return;
        if (_scratch.Length < frames)
            _scratch = new Vector2[frames];

        float depth = Mathf.Clamp(Roughness, 0f, 1f);
        float norm = 1f / (1f + depth);
        for (int i = 0; i < frames; i++)
        {
            float f = _baseFreq + _rng.RandfRange(-JitterHz, JitterHz);
            _phase += f / _mixRate;
            _subPhase += f / (2.0 * _mixRate);
            if (_phase >= 1.0) _phase -= 1.0;
            if (_subPhase >= 1.0) _subPhase -= 1.0;

            float fundamental = Mathf.Sin(Mathf.Tau * (float)_phase);
            float sub = Mathf.Sin(Mathf.Tau * (float)_subPhase);
            float s = (fundamental + depth * sub) * norm * 0.3f;
            _scratch[i] = new Vector2(s, s);
        }
        _playback.PushBuffer(_scratch.AsSpan(0, frames));
    }

    /// <summary>Re-voice for a differently sized implied body. Phase is deliberately NOT reset
    /// — a discontinuity here is the same click as bug 1.</summary>
    public void Reroll(float sizeScale) =>
        _baseFreq = _rng.RandfRange(MinFreq, MaxFreq) / Mathf.Max(sizeScale, 0.1f);
}
```

**Verified against Godot 4.7** (`GodotSharp` 4.7.0 XML docs and the 4.7 class reference):

- `AudioStreamGenerator.MixRate` — *"The sample rate to use (in Hz)"*; `BufferLength` — *"The
  length of the buffer to generate (in seconds). Lower values result in less latency, but
  require the script to generate audio data faster… more risk for audio cracking."*
- `AudioStreamGeneratorPlayback.GetFramesAvailable()` → `int`, *"the number of frames that can
  be pushed… without overflowing it. If the result is 0, the buffer is full."* It does not
  block or throw; it returns 0.
- `PushFrame(Vector2)` → **`bool`**, and *"usually less efficient than `push_buffer()` in C#…
  but may be more efficient in GDScript."* The brief's per-frame `PushFrame` loop is the
  GDScript-shaped path; **use `PushBuffer`** — it has a `ReadOnlySpan<Vector2>` overload in
  GodotSharp 4.7.0, so the scratch buffer never needs a copy, only the zero-allocation
  `.AsSpan(0, frames)` slice already in the sample. **Verified 2026-07-29 by reflection against
  the installed `GodotSharp.dll` 4.7.0** (`AudioStreamGeneratorPlayback.GetMethods()` lists both
  `PushBuffer(Godot.Vector2[])` and `PushBuffer(System.ReadOnlySpan<Godot.Vector2>)`) — the Span
  overload is real in 4.7, not just proposed by the docs XML.
- `GetStreamPlayback()` in the same call as `Play()` is the **documented** pattern: the
  `AudioStreamGenerator` class reference's own C# sample does `Player.Play();` then
  `(AudioStreamGeneratorPlayback)Player.GetStreamPlayback();` inside `_Ready`, then fills
  immediately. `AudioStreamPlayer.GetStreamPlayback` warns *"If no sounds are playing, this
  method fails and returns an empty playback"* — the 3D variant's doc text carries no such
  note, so whether `AudioStreamPlayer3D.GetStreamPlayback` has the same precondition is
  **unverified against Godot 4.7**. `VoiceSpeaker` handles it the safe way: `as`-cast and
  null-check rather than a blind cast.
- `AudioStreamGeneratorPlayback.GetSkips()` — *"the number of times the playback skipped due to
  a buffer underrun."* Log it. It is the only honest measurement of whether the fill loop is
  keeping up, and it is available headless.
- `RandomNumberGenerator.Randi()` returns **`uint`** — see pattern 5.

### 3. What reads as threat, and the part the brief gets acoustically backwards

The claimed cross-cultural properties are reasonable and worth building to: a **low fundamental
relative to implied size**, **non-periodic / rough phonation** (growl, vocal fry, subharmonics),
**sudden onset**, **downward pitch glides** (upward glides read as curiosity or a question), and
**irregular rhythm**, which reads as agentic rather than mechanical. `SfxLab` already encodes
two of these without naming them: `Oof` glides 240 → 150 Hz because *"dropping pitch =
deflating"*, and `Thump` sags 35 % across its length.

**The correction: `MinFreq = 40f` is a fundamental most players will never hear.** Sail's floor
spec is a GTX 970 gaming PC, which says nothing about the speakers. On laptop drivers, small
desk monitors, or a TV, 40 Hz reproduces as approximately nothing — and what survives is the
*harmonic series*, which is why an actual sub-bass fundamental is the least reliable way to
deliver "low". Deliver the lowness where it is reproducible — a fundamental in the 70–120 Hz
region with a strong subharmonic and enough roughness to generate upper partials — and let the
implied depth come from the harmonic structure rather than from energy nobody's hardware emits.
Then check it on the worst plausible output, not on headphones.

**`PitchScale` is cheap but it is not free and it is not neutral. Verified against Godot 4.7:**
`AudioStreamPlayer3D.PitchScale` is *"the pitch and the tempo of the audio, as a multiplier of
the audio sample's sample rate."* That is resampling — pitch and **duration** move together, and
formants move with them. The brief's `Mathf.Lerp(0.9f, 1.3f, intensity01)` therefore makes an
angry bark 23 % *shorter* as well as higher, and speeds up whatever roughness the source had
until vocal fry stops reading as fry. Both may be what you want; neither is a free knob. Whether
`PitchScale` composes meaningfully with an `AudioStreamGenerator` stream (which has no fixed
sample to resample) is **unverified against Godot 4.7** — vary `_baseFreq` instead.

### 4. The character layer has no assets, and writing around that is the failure

The brief's approach is *"pure synthesis for texture, sampled audio for character"*, with a
`CreatureBarkPlayer` picking from a `BarkVariants` array of `AudioStream`s. **There are no
`BarkVariants`. There are no audio files in this repository at all** — not one `.ogg`, `.wav`
or `.mp3`. Half of the recommended approach has nothing behind it, and half a strategy quietly
implemented as a stub is worse than a stated gap.

The honest options, neither of which this skill picks:

1. **Synthesize the character layer too**, extending `SfxLab`'s recipes. `Oof` and `EffortGrunt`
   are already voiced-source-plus-filtered-noise — a wet, low, multi-formant version is the same
   shape with different constants. Costs: it is genuinely hard to make a synthesized bark sound
   like a body, and `SfxLab`'s helpers are `private static`, so this is an edit to a shipped
   file rather than a new one. Keeps the "no asset files, no licensing, tunable from one place"
   property `SfxLab`'s own doc comment names as the point.
2. **Import audio assets** — a decision nobody has made. It needs a licensing call, an import
   pipeline, a repo-size call, and a position on whether Sail's all-procedural audio is a
   principle or an accident. That is Talon's, and it is a real fork worth surfacing rather than
   pre-empting: the plumbing for it already exists, since `EventResponse.CustomSound` is an
   `[Export] AudioStream?` that wins over the `Sfx` enum in `ActorFx`'s dispatch.

Two plumbing gaps to fix if a bark player is built on the compliant path:

- **`SfxLab.PlayStream3D` has no pitch parameter.** It sets
  `PitchScale = 1f ± pitchJitter` around 1.0 and offers no base. An intensity-mapped
  `Lerp(0.9f, 1.3f, intensity01)` cannot be expressed through it today; it needs a `pitchScale`
  argument added alongside `volumeDb`.
- **`PlayStream3D` has no bus override.** Every pool node is on `"Sfx"`, alongside footsteps —
  and `SettingsPanel` has no SFX slider at all, so a threat voice on that bus is one a player
  can neither isolate nor turn down. That is an accessibility gap, not a mixing preference.
- **`AudioStreamPlayer3D.MaxPolyphony` defaults to 1** and *"playing additional sounds after
  this value is reached will cut off the oldest sounds"* (**verified against Godot 4.7**). One
  creature truncating its own previous bark is often correct — a body cannot say two things at
  once — but it should be a stated decision.

### 5. Coordinated calls — the shuffle is broken, and there is nothing to coordinate yet

The brief's `CoordinatedCallSystem.TriggerCallAndResponse` shuffles emitter order with:

```csharp
order.Sort((a, b) => _rng.Randi() % 3 - 1);   // do not ship this
```

**This is broken twice over, and it fails at the moment the beat fires.**

1. **It does not compile as written.** `RandomNumberGenerator.Randi()` returns `uint`
   (**verified against Godot 4.7**). Under C#'s binary numeric promotion, `uint % int` and
   `uint - int` both widen to `long`, so the lambda returns `long` where `Comparison<int>`
   requires `int`. Cast it to `uint` arithmetic instead and `% 3 - 1` underflows to
   `4294967295` whenever the modulo lands on 0.
2. **Once it compiles, it still throws.** A comparator returning random results is
   *inconsistent* — it does not return 0 for `Compare(x, x)`, and repeated comparisons of the
   same pair disagree. .NET's introsort detects this and throws: *"Unable to sort because the
   IComparer.Compare() method returns inconsistent results."* Worse, it does not throw
   **reliably** — for some inputs it returns a badly-ordered list instead, so the bug is
   nondeterministic and passes small tests.

The correct shuffle is Fisher-Yates, and it is not longer:

```csharp
for (int i = order.Count - 1; i > 0; i--)
{
    int j = _rng.RandiRange(0, i);          // RandiRange returns int, inclusive both ends
    (order[i], order[j]) = (order[j], order[i]);
}
```

**Why this bug class matters more than this bug.** A one-line "clever" shuffle inside a horror
beat system fails at exactly the moment the beat fires — which is the moment nobody is watching
the console, the moment a crash is most expensive, and the moment a nondeterministic failure is
least reproducible. The repo has been here before: an island shipped rotated 90° through a
green headless suite. Cleverness in the scheduling path of an affect device is a category
error; correctness there is worth more than concision.

Two more problems in the same sample: `await _rng.RandfRange(0.4f, 1.2f)` is not awaitable (it
needs `await ToSignal(GetTree().CreateTimer(t), SceneTreeTimer.SignalName.Timeout)`), and an
`async void` sequence on a `Node` that can be freed mid-await is a use-after-free waiting to
happen.

**And the retarget.** Staggered call-and-response *does* read as communication — that part of
the claim is sound. But there is no plural threat in this game. The hand is singular, always
present, and its voice comes from above a ceiling. The only plural-creature candidate on the
board is the **night-puffling** (loop-v1 §8, decided as a creature, lethality `TBD — Talon`),
and re-grounding coordinated calls onto it is a design proposal for `/direct` and
`/spec-entity`, not a thing to build because the code sample was interesting.
**Currently: not applicable.** If it becomes applicable, the call order and the inter-call
delays must be **server-authored and replicated** in `CycleDriver`'s shape — Authority-mode
RPC, own transfer channel, sequence guard, targeted reliable resend on peer connect — because
four clients each rolling their own order is four different beats, not one.

### 6. Proximity in sound — defer to `vfx-proximity`, and do not build the heartbeat

**`vfx-proximity` owns this.** Read it. Its rules are not negotiable from here: the threat's
world position is replicated and **the computed intensity never is**; each client derives its
own distance locally; bearing, never location; the stale-bearing rate is a §8.3 control surface
that belongs to `/direct`. This skill supplies the *acoustic* half of a cue that skill has
already decided the shape of. It does not run a second proximity system.

**The brief's `ThreatProximityIndicator` is a §8.3 violation by construction, and this is the
single most likely way this skill gets used wrongly.** A heartbeat pulse whose pitch and volume
rise monotonically with `1 - clamp((dist - 8) / (60 - 8))` is a perfectly reliable telegraph:

> §8.3: *"If a cue always precedes a threat, players learn to relax in its absence — the cue
> has taught them when they are safe."*

A monotonic proximity meter does not merely *sometimes* tell players they are safe; it tells
them continuously and exactly. Within one session it stops being dread and becomes a distance
readout with a drum on it — §8.1's over-exposure arriving through the ear. It is also a
**non-positional** (`AudioStreamPlayer`) cue driven by a *known exact* distance, which leaks
precisely the information `vfx-proximity` deliberately withholds.

**Redundant cueing is still required, and it is a different thing.** `LEVEL-BIBLE.md` §8.1
inherits the redundant-channel rule and states outright that **audio alone is never
sufficient** — the design severs audio on purpose. So a threat cue does need a second channel,
and a player with hearing loss or on a stereo downmix needs the same information a headphone
player gets. The reconciliation:

- **Redundancy means the same ambiguity carried on another channel**, not a cleaner version of
  it on a second audio channel. If the pulse is more legible than the growl, the pulse has
  become the cue and the growl is decoration.
- **Real 3D positioning plus one independent, non-directional cue** is a defensible shape — but
  the independent cue must be as coarse and as unreliable as the primary. A three-state
  *nothing / something / close* is honest; a continuous meter is not.
- **The visual half is `vfx-proximity`'s**, and it should carry the same coarseness.
- If a beat genuinely needs an always-honest rising signal, that is an **urgency** cue, not a
  dread cue — `/spec-urgency-cue` owns it, `vfx-audio-sync`'s two-channel rule applies, and it
  may not share a bus, sound family, frequency band or spatial signature with the threat voice.

The brief's constants (`MaxAudibleDistance = 60f`, `CloseDistance = 8f`) are also unanchored to
anything in this game. For reference, `VoiceConfig.ProximityMaxDistance` is 24 m — a 60 m threat
field reliably produces players who feel something and have nobody in earshot to tell. Whether
that is the point or a fairness problem is `/direct`'s call (§7.1 / §5.4), not a constant to
pick here.

### 7. The budget arithmetic, stated properly

`SfxLab.cs:42-44`, verbatim: *"§4 budgets ≤24 concurrent 3D players INCLUDING the 5 voice
speakers and ambience; 14 one-shot slots keeps the worst case comfortably inside."*

So the node ledger today is **5 voice speakers + 14 pool slots = 19 of 24**, leaving about 5 —
and the ambient bed, which `THRILL-BIBLE.md` §9 puts on the critical path, is specced to land in
that same remainder. "2–3 simultaneous growls" is therefore 2–3 of roughly 5 free nodes, before
the bed takes its share. That is tight, not comfortable.

**Node count and CPU are two separate budgets and only one of them is written down.** Each
`AudioStreamGenerator` is a managed per-frame fill callback that must never miss, running on the
same CPU as up to five Opus decoders (`VoiceSpeaker.Submit`, 50 packets/sec each) and whatever
`_Process` work the atmosphere layer has. Nothing in Sail has ever been profiled on the floor
spec. So: **budget 2–3 generators as a starting hypothesis with a reason (node headroom, and
voice contention), then measure `GetSkips()` and the frame-time monitor headed before treating
it as a number.** Beyond that, pre-render variants to `AudioStreamWav` and play them through
the pool — which is pattern 1's decision rule arriving from the performance side.

### 8. Escalation parameters, driven from outside

Raising **roughness**, shortening **inter-call silence**, and widening **pitch variance** are the
right levers for rising aggression, and raising **volume** is the wrong one — a threat that gets
louder is a threat that is closer or bigger, which is a different statement. This skill exposes
those parameters; it does not schedule them.

**`vfx-escalation` owns the curve.** Do not bind any of this to `CycleDriver.Instance.Phase`:
`Phase` is cyclic in `[0,1)` over a 120 s default period, so an escalation bound to it resets
every two minutes. Any session-monotonic quantity must be derived, and that derivation lives in
`vfx-escalation`. Gate every read on `CycleDriver.Instance.Synced` — a client's `Phase` sits at
its zero default until the first authoritative update lands.

## Tuning guide

Starting points with reasons. None of these is a gate.

- **Fundamental 70–120 Hz, not 40–90.** Reproducible on the worst plausible output; the implied
  depth comes from the subharmonic and the harmonic series. Pattern 3.
- **Roughness 0.3–0.6 as an additive depth.** Below ~0.2 it reads as a test tone; at 1.0 the
  fundamental is half the mix and the pitch centre softens. This is the escalation lever.
- **Frequency jitter ±1–2 Hz per sample.** The brief's own troubleshooting names this and it is
  right: a perfectly stable oscillator is the tell that separates "synth" from "body". It is not
  a substitute for fixing the subharmonic wrap.
- **`BufferLength` 0.1 s, lead 0.02 s.** Buffer length bounds underrun risk; the *lead* bounds
  responsiveness. They are separate numbers and the brief conflates them.
- **Attack under ~10 ms** for sudden onset; `SfxLab`'s existing envelopes sit at 0.004–0.02 s
  and are a usable reference scale.
- **Downward glide for threat, upward for question.** `SfxLab.Oof` (240 → 150 Hz) is the house
  example of the first; `Sweep` and `Squeak` are the second, which is why they read as friendly.
- **Distance constants: anchor them to the encounter, not to a round number.** A house hallway
  is ~10 m end to end; 60 m is outdoor-neighbourhood scale. The brief's own troubleshooting says
  the pulse feels disconnected when these do not match real ranges — that is the whole entry.
- **Irregular inter-call intervals beat short ones.** The irregularity is the load-bearing part
  (`vfx-audio-sync` states this properly for the ambient case) — but the schedule itself is
  `/direct`'s and `vfx-escalation`'s, not a timer in a growl node.

## Integration points

- **`SfxLab`** — `Get`/`PlayStream3D` for every one-shot; the pool and the ≤24 budget; the
  synthesis recipes to extend. Note `SfxLab.cs:77`'s doc comment references `LabAmbience`, which
  does not exist anywhere in the repo.
- **`ActorFx` + `EventResponse`** — the shipped data-driven dispatch layer, and the closest
  thing to an event bus. `CustomSound` beats `Sound`; controllers never call `SfxLab` directly.
- **There is no event bus and that is a decision.** `docs/ATMOSPHERIC-VFX-INTEGRATION.md` §3.4:
  *"Build the bus when a second listener actually exists."* Do not open by building `GameEvents`.
- **`vfx-proximity`** — owns sensing. Consume its per-client intensity; never compute a second
  one and never put one on the wire.
- **`vfx-audio-sync`** — owns the ambient bed, wrong silence, the sync ratio, and the
  two-channel rule separating dread cues from urgency cues. A threat voice is a dread cue.
- **`sound-spatial-audio`** — owns `AudioStreamPlayer3D` setup: attenuation model, `UnitSize`,
  `MaxDistance`, `AttenuationFilterCutoffHz`, bus routing. This skill produces the signal.
- **`vfx-escalation`** — owns the session curve and the monotonic quantity `Phase` is not.
- **`scripts/voice/`** — out of bounds as a system, and the mix relationship runs one way: a
  threat voice that makes a teammate at 10 m harder to understand has cost more than it bought.
  §5.1 ranks proximity voice the highest-leverage instrument in the game.
- **`scripts/ui/SettingsPanel.cs`** — any new bus a player should be able to lower needs a
  slider added here. There is currently no SFX slider.
- **`scripts/telemetry/`** — the live Firebase path has zero audio hooks today. Logging
  vocalization counts, `GetSkips()`, and generator concurrency is buildable on what ships and is
  the only way to know whether the budget above survives a real session.

## Precedent

- **`scripts/voice/VoiceSpeaker.cs`** — the only `AudioStreamGenerator` in the repo and the
  template for this whole skill: generator on an `AudioStreamPlayer3D`, 48 kHz, explicit
  `BufferLength`, `as`-cast on `GetStreamPlayback` with a null guard, bounded pending queue,
  clean stop rather than a stalled buffer. Copy its shape, not its constants.
- **`scripts/game/sandbox/SfxLab.cs`** — the offline-render-and-cache contract, the pooled
  positional path, the pitch-jitter-per-shot habit, the written concurrency budget, and the
  `EnsureBus` pattern every new bus must copy.
- **`scripts/voice/VoiceManager.EnsurePaBusName`** — the only `AudioEffect*` usage in the repo
  (overdrive + 2200 Hz lowpass + boxy reverb) and proof that bus-level processing works here.
  A threat voice heard *through a ceiling* is the same problem the PA bus already solves once.
- **`scripts/game/world/CycleDriver.cs`** — the replication contract for anything multi-client.
- **Alien: Isolation** — layered synthesis plus sampled character, with proximity-driven mixing.
  The reference is sound and it presupposes a sample library Sail does not have; see pattern 4.
- **Lethal Company** — unpredictable vocalization timing selling "something out there thinking."
  Evaluate this honestly: it is §8.3 restated in a specific game's terms, and the mechanism is
  the *schedule*, which this skill does not own.

## Troubleshooting

In roughly the order these occur.

- **Nothing plays at all** — `GetStreamPlayback()` returned null or a non-generator playback.
  Check `Stream` is an `AudioStreamGenerator` and that `Play()` ran first.
- **Crackling, dropouts, or a stuttering growl** — buffer underrun. Check `GetSkips()`; it is
  non-zero and rising. Either the fill loop is missing frames or `BufferLength` is too short.
- **Buzzy, metallic, not organic** — the subharmonic discontinuity in pattern 2, bug 1. Fix the
  second phase accumulator before touching any tuning value. If it is still buzzy after that,
  the source is a naive sawtooth aliasing, and third, per-sample jitter is zero.
- **The growl loses its pitch centre as it gets angrier** — `Lerp` crossfade instead of an
  additive mix, pattern 2 bug 2.
- **The sound is thin or absent on some machines and fine on headphones** — the fundamental is
  below what the output reproduces. Pattern 3.
- **The threat voice has no direction** — it is on an `AudioStreamPlayer`, not an
  `AudioStreamPlayer3D`. Pattern 2 bug 3.
- **A burst appears at the wrong position** — `GlobalPosition` assigned before the node entered
  the tree. `JuiceFx` documents the same ordering hazard for particles.
- **A sustained voice cuts off mid-breath** — it was rented from `SfxLab`'s pool, which steals
  round-robin. Sustained voices get their own node.
- **Repeated barks truncate each other** — `MaxPolyphony` is 1. Decide whether that is correct.
- **Robotic or repetitive** — randomize pitch **and** rhythm **and** pause length. The brief is
  right that pitch alone is not enough; irregular *timing* is what reads as agentic.
- **Players stop reacting to the proximity cue after one session** — §8.3. The cue is a reliable
  telegraph and it has taught them when they are safe. Pattern 6; route the fix through
  `/direct`, not through a constant.
- **The pulse feels disconnected from the danger** — the distance constants do not match real
  encounter ranges. A hallway is not 60 m.
- **`CoordinatedCallSystem` throws or produces an unshuffled order** — the inconsistent
  comparator, pattern 5.
- **Green headless suite, silent or wrong game** — expected. Headless cannot hear.

## Caveats

- **No numbers as law.** 70–120 Hz, roughness 0.3–0.6, ±1–2 Hz jitter, 0.1 s buffer, 0.02 s
  lead, 2–3 generators, every distance constant — starting points with stated reasons, and most
  of them are playtest or profiler calls.
- **Unverified is stated, not implied.** Every Godot claim above is either marked
  **Verified against Godot 4.7** against `GodotSharp` 4.7.0 / the 4.7 class reference, or marked
  `unverified against Godot 4.7` inline. The brief's code samples are not evidence — four of
  them shipped bugs.
- **No affect.** This skill never argues that a threat should be heard, how often, or what it
  should mean. Being able to synthesize a convincing growl is not an argument for one.
- **Not proximity.** `vfx-proximity` owns sensing, the never-broadcast intensity rule, bearing
  over location, and the stale-bearing rate. This skill supplies waveforms to a cue that skill
  shaped.
- **Not the voice pipeline.** `scripts/voice/` is shipped and tuned. Never fork `VoiceConfig`,
  never add DSP to the `Voice` bus.
- **Not a resolver.** §9's tone **ratio** stays open and a growl-versus-something-sillier call
  forecloses it — say so and flag it. `LEVEL-BIBLE.md` §8.3's diegetic-channel `[BLANK — Talon]`
  constrains this skill more than any other and stays blank. §6.2's night-floor conflict stays
  open. §4.4's ceiling stays `[research default — pending Talon confirmation]`.
- **The asset fork is real and unmade.** Half the brief's recommended approach needs audio files
  that do not exist. Report it; do not stub it.
- **Headless cannot hear.** What `tests/Run-*.ps1` could genuinely prove: that a synthesis
  function produces the expected sample count, RMS and no NaNs (given a *public* pure function —
  `SfxLab`'s recipes are `private static`, so today it could not); that `GetSkips()` stays zero
  under a simulated fill loop; that a vocalization trigger replicated to every peer with the
  right sequence and the right late-join resend, the way `Run-CycleTest.ps1` proves phase
  convergence. What it cannot prove: whether the growl sounds like a body, whether the low end
  survives a laptop speaker, whether the mix sits under proximity voice, or whether anyone was
  frightened. The repo shipped an island rotated 90° through a green suite; that precedent is
  the whole argument for a **headed, multi-human session** as the real gate.

*Scope note: written 2026-07-28 against `feat/neighbourhood-exterior`, a branch on which nothing
threat-shaped exists in audio, no threat entity publishes a position, there are zero audio asset
files in the repository, there is no ambient bed, and `THRILL-BIBLE.md` §10 records every audio
device as `unbuilt`. What does exist: `SfxLab`'s offline-render-and-cache palette with a pooled
positional path, `VoiceSpeaker`'s real-time `AudioStreamGenerator` on an `AudioStreamPlayer3D`,
a PA bus proving bus-level processing works, and `CycleDriver`'s replication contract.
**Current bite: none.** The API corrections and the four bug fixes in pattern 2 are checked and
usable; nothing here is implemented, and the thing this skill would give a voice to does not
exist in code. First real test: the hand's first authored take — heard from above a ceiling,
never seen — in a headed session with two or more kids in the house and proximity voice live.
Passing looks like both players stopping and looking up at the same ceiling, neither able to
say afterwards what the sound was, and `GetSkips()` at zero for the whole run.*
