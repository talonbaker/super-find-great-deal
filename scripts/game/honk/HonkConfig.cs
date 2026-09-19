using MpFoundation.Voice;

namespace MpFoundation.Game.Honk;

/// <summary>
/// <b>Every number the goose honk can be retuned by, in one place.</b> HONK-1, 2026-09-04.
///
/// <para>Talon asked for the feature and named exactly one value — the key, <c>H</c>. Everything
/// else here is a value fork that was picked rather than escalated, which is this repo's rule, and
/// every one of them is restated in <c>DECISION-LOG.md</c> so retuning is a text edit and not an
/// archaeology exercise. The sound's OWN numbers (pitch arc, length, timbre) are not here — they
/// live beside the recipe in <c>SfxLab.GooseHonkPcm</c>, because a waveform's knobs are only
/// meaningful next to the waveform.</para>
///
/// <para><b>The range numbers are ALIASES, not values.</b> The honk stands in for proximity voice,
/// so "how far a honk carries" is not a question this file is allowed to answer differently from
/// how voice answers it — <see cref="VoiceConfig.ProximityMaxDistance"/> and
/// <see cref="VoiceConfig.ProximityUnitSize"/> are referenced rather than copied, so the day
/// somebody retunes voice range the honk moves with it and no second edit is needed anywhere. That
/// is criterion 5 of the packet, expressed as a compile-time fact instead of a promise.</para>
/// </summary>
public static class HonkConfig
{
    /// <summary>The input action. Public so the controller, a test and any future rebind screen all
    /// name the same string.</summary>
    public const string ActionName = "honk";

    /// <summary><b>Minimum seconds between one player's honks</b>, enforced on the SERVER (the
    /// client holds the same latch, but only the server's answer counts — see
    /// <c>HonkManager.ServerHonk</c>).
    ///
    /// <para><b>0.6 s, and the number is picked off the sound rather than out of the air.</b> The
    /// call itself is 0.42 s long (<c>SfxLab.GooseHonkPcm</c>), so anything shorter than that lets
    /// one player's honks overlap themselves, which stops reading as a goose and starts reading as
    /// a stuck buzzer. The remaining 0.18 s is the gap that makes a held key sound like deliberate
    /// repeated honking — about 1.7 honks a second — rather than a machine gun. Deliberately NOT
    /// long enough to feel like a punishment: honk-honk-honk is the joke, and a two-second lockout
    /// would delete it. Retune here, in one place.</para></summary>
    public const double CooldownSec = 0.6;

    /// <summary><b>How much early the SERVER forgives, and why it has to forgive anything.</b>
    ///
    /// <para>The client spaces its sends at exactly <see cref="CooldownSec"/>; the server judges
    /// them on ARRIVAL. The gap between two arrivals is therefore
    /// <c>CooldownSec + (jitter₂ − jitter₁)</c>, and that term is negative half the time on any
    /// real connection. With both latches set to the same number, every press that is even a
    /// millisecond "early" through the network is refused — and because a refusal does not advance
    /// the latch, the next honk lands a FULL cooldown after the last granted one. A player holding
    /// <c>H</c> would honk at roughly half the intended cadence, at random, and the harder they
    /// held it the worse it would look.</para>
    ///
    /// <para>That is the exact failure <see cref="HonkGate"/>'s boundary note says it refuses to
    /// accept — <i>"the failure a player notices is the swallowed honk"</i> — reintroduced by the
    /// clock split rather than by the bound. So the AUTHORITATIVE latch is the looser of the two by
    /// this margin, which is the ordinary shape of a client-predicted, server-checked rule.</para>
    ///
    /// <para><b>50 ms</b>: comfortably past the jitter delta of a LAN or a healthy Steam relay, and
    /// it costs almost nothing on the side that matters. A client that has deleted its own latch
    /// entirely is capped at <c>1 / (0.6 − 0.05)</c> = 1.82 honks a second instead of 1.67 — a 9%
    /// loosening of the anti-spam rule to remove a 50%-loss bug from every honest player holding
    /// the key.</para></summary>
    public const double ServerJitterToleranceSec = 0.05;

    /// <summary><b>How far a honk carries: exactly as far as a voice does.</b> An alias, never a
    /// value — see the class doc. The server culls the broadcast at this distance and the
    /// receiving client's <c>AudioStreamPlayer3D.MaxDistance</c> is set to the same number, so the
    /// two boundaries are the same boundary and a honk can never be culled at a distance the ear
    /// would still have reached.</summary>
    public const float AudibleRangeM = VoiceConfig.ProximityMaxDistance;

    /// <summary>The falloff curve's shape, aliased for the same reason as
    /// <see cref="AudibleRangeM"/>. Godot's <c>UnitSize</c> is the distance at which the sound is
    /// at its authored volume; past it, inverse-distance attenuation takes over. Voice's tuned
    /// value, so a honk gets quieter with distance on the same curve a voice does.</summary>
    public const float UnitSizeM = VoiceConfig.ProximityUnitSize;

    /// <summary>Playback trim. Level with the pooled one-shot default (<c>-6 dB</c>) rather than
    /// louder: a honk is a voice substitute, and a voice substitute that is louder than a voice
    /// is a griefing tool. It sits ON the falloff curve above, so distance does the rest.</summary>
    public const float VolumeDb = -6f;

    /// <summary>Per-shot pitch jitter, ±%. Small on purpose. The one-shot pool jitters footsteps
    /// by 8% so a run does not machine-gun; a honk wants only enough that two honks in a row are
    /// not byte-identical, because a goose is a goose and a wildly detuned one reads as a
    /// different animal each press.</summary>
    public const float PitchJitter = 0.04f;
}
