using Godot;
using MpFoundation.Game.Sandbox;

namespace MpFoundation.Game.World;

/// <summary>
/// A continuous positional sound at this node's position — the counterpart to
/// <see cref="SparseSfxEmitter"/>, which fires one-shots at intervals and holds nothing open.
///
/// <b>What it is for.</b> Plan §1.5's darkness law: away from light at night the player is nearly
/// blind and sound becomes the information channel. A layer a player can take a *bearing* off has
/// to be continuous — a sparse emitter tells you where something was a second and a half ago, and
/// at the moment you most need it (walking, in the dark, wanting to know if you are still heading
/// at the fire) the gaps are exactly where the information is missing. This node is the first
/// continuous positional layer in the repo.
///
/// <b>The budget, and why this node cannot simply be spawned wherever it is wanted.</b> It holds
/// a real 3D voice open for as long as it sounds, out of a partition of
/// <see cref="SfxLab.LoopPoolSize"/>. That partition is the last of a ≤24 ceiling that is
/// otherwise fully spent (see <see cref="AudioVoiceBudget"/>). The pool never steals: an emitter
/// that asks while the partition is full stays silent and asks again next frame. A world that
/// places more of these than the partition holds needs a director ranking them by distance
/// (<see cref="NearestKAudio"/> is the pure rule for exactly that); no world in this build does.
///
/// <b>Every level change is a fade, and that is a rule rather than a nicety.</b> A hard cut would
/// produce a sound that stops with no visible cause, for free. That is THRILL-BIBLE §6.3's
/// wrong-silence device — `fresh`, the strongest entry in that register, and spendable only by a
/// directed beat through `/direct`. §10's row now records this refusal explicitly. It is also
/// §7.2: a beacon that snaps off is a rule the player cannot learn. So: fade in on activation,
/// fade out on deactivation, and the voice is only handed back once the fade has actually reached
/// silence.
///
/// Client-local scenery-side presentation, like <see cref="SparseSfxEmitter"/> — every peer runs
/// its own emitters over its own copy of the world and nothing here touches the wire.
/// </summary>
public partial class LoopingSfxEmitter : Node3D
{
    /// <summary>Which looping layer this emitter sounds.</summary>
    [Export] public SfxLoop Kind { get; set; } = SfxLoop.FireBody;

    /// <summary>Level at full presence. Well below unity because this is a bed layer that has to
    /// sit under a speaking teammate without a compressor doing it (see <see cref="Bus"/>).</summary>
    [Export] public float VolumeDb { get; set; } = -13f;

    /// <summary>Where the sound has fallen to nothing. Far larger than
    /// <see cref="SparseSfxEmitter"/>'s 14 m crackle because this layer's whole job is to be
    /// audible from inside the woods — THRILL-BIBLE's *refuge maintained only by leaving it*
    /// requires the fire's state to be legible from out there, and §1.5 deletes the sightline
    /// that requirement originally assumed.</summary>
    [Export] public float MaxDistance { get; set; } = 60f;

    /// <summary>Distance at which attenuation begins in earnest. Generous, so the fire stays
    /// present across a clearing rather than collapsing into the floor a few metres out — a
    /// beacon you can only hear once you have arrived is not a beacon.</summary>
    [Export] public float UnitSize { get; set; } = 9f;

    /// <summary>Above 1 exaggerates the inter-aural difference. This is the single dial that most
    /// directly decides whether a blind player can tell WHICH WAY, which Issue #182 names as the
    /// thing the darkness law depends on most. Unverified by ear — see the PR body.</summary>
    [Export] public float PanningStrength { get; set; } = 1.4f;

    /// <summary>The clean scenery lane, never the ducked bed lane. Two reasons, both load-bearing.
    /// Material: this layer is a rumble full of transient snaps, and Godot's compressor tops out
    /// at a 2 ms attack, so a sidechain across it would pump on every crackle. Role: a continuous
    /// positional layer is a navigation cue, and ducking the thing that tells you where home is
    /// under the voice of the friend you are talking to is a fairness defect, not a mix
    /// choice.</summary>
    [Export] public string Bus { get; set; } = AudioBuses.Scenery;

    /// <summary>Seconds to fade between silence and full presence. 2.5 s is picked and stated:
    /// long enough to read as distance rather than as an event, short enough that it completes
    /// before a walking player has gone anywhere interesting. It is deliberately far slower than
    /// any fade that would read as a cut.</summary>
    [Export] public float FadeSeconds { get; set; } = 2.5f;

    /// <summary>Whether this emitter WANTS to sound — the source's own live state. Distinct from
    /// whether it currently holds a voice: wanting and having are different questions and
    /// conflating them is how a silent source becomes indistinguishable from one that lost a
    /// budget fight. Turning this off fades out and releases, because a source going quiet is
    /// legible and caused and should sound like the ordinary thing it is.</summary>
    [Export] public bool Active { get; set; } = true;

    private AudioStreamPlayer3D? _voice;
    private float _gain;        // 0..1, the fade's own state

    /// <summary>True when this emitter currently holds one of the looping partition's voices.
    /// Read by the self-test for accounting.</summary>
    public bool HoldsVoice => _voice != null;

    /// <summary>Current fade gain, 0..1. Exposed for the self-test, which has to prove that a
    /// deactivated emitter reaches silence BEFORE its voice is handed back — a release that
    /// happened first would be an audible cut wearing a fade's clothes.</summary>
    public float Gain => _gain;

    /// <summary>Wants to sound.</summary>
    public bool ShouldSound => Active;

    public override void _Ready()
    {
        AudioBuses.EnsureLayout(); // idempotent; no boot-order guarantee against the world script
    }

    /// <summary>Hands back any voice still held.
    ///
    /// The release is the part that matters and it is easy to omit: an emitter torn down mid-fade
    /// — a scene change, a source node freed — would otherwise take a slot out of a partition of
    /// five with nothing left holding a reference to give it back. The pool would shrink by one
    /// permanently, silently, and the symptom would be a world where the fifth emitter stopped
    /// being audible after the first scene transition.
    ///
    /// This is the one place a voice is dropped WITHOUT a fade, and it is not an exception to the
    /// fade rule: a node leaving the tree has taken its sound with it regardless, so there is
    /// nothing left to fade. Every in-world deactivation still goes through the fade path.</summary>
    public override void _ExitTree()
    {
        if (_voice != null)
        {
            SfxLab.ReleaseLoop(_voice);
            _voice = null;
            _gain = 0f;
        }
    }

    public override void _Process(double delta)
    {
        bool want = ShouldSound;

        // Acquire on the way UP only, and only once the emitter actually wants to be heard: a
        // voice held at zero gain is a slot another fire could have used.
        if (want && _voice == null)
        {
            _voice = SfxLab.AcquireLoop(this, SfxLab.GetLoop(Kind), Bus, MaxDistance, UnitSize,
                PanningStrength);
            if (_voice == null)
            {
                // Refused — the partition is full. Stay silent and try again next frame. The pool
                // never steals (see SfxLab's policy note), so this resolves when a deactivated
                // emitter finishes fading and hands its voice back.
                _gain = 0f;
                return;
            }
        }

        if (_voice == null)
            return;

        float step = FadeSeconds <= 0f ? 1f : (float)delta / FadeSeconds;
        _gain = Mathf.Clamp(_gain + (want ? step : -step), 0f, 1f);

        _voice.GlobalPosition = GlobalPosition;
        _voice.VolumeDb = GainToDb(_gain, VolumeDb);

        // Release only at true silence. This ordering is the whole point of the fade.
        if (!want && _gain <= 0f)
        {
            SfxLab.ReleaseLoop(_voice);
            _voice = null;
        }
    }

    /// <summary>Fade gain (0..1) applied to a target level, in dB. Exactly zero maps to
    /// <see cref="SfxLab.SilentDb"/> rather than to a very small number, so a fade-out lands on
    /// silence instead of approaching it — the same guarantee <c>AmbientBedMath.FadeValue</c>
    /// makes, for the same reason.
    ///
    /// The curve is linear in AMPLITUDE, not in dB. A linear-in-dB fade spends most of its
    /// duration inaudible and then arrives suddenly, which would give a slow fade the perceptual
    /// shape of a cut and defeat the reason it is slow.</summary>
    public static float GainToDb(float gain01, float fullLevelDb)
    {
        if (gain01 <= 0f)
            return SfxLab.SilentDb;
        return fullLevelDb + Mathf.LinearToDb(Mathf.Clamp(gain01, 0f, 1f));
    }
}
