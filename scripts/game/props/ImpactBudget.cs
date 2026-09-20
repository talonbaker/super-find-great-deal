using Godot;

namespace MpFoundation.Game.Props;

/// <summary>
/// <b>Source-side limiting for prop impacts</b> (SFX-2, 2026-09-19) — the server decides which
/// contacts of one physics tick are worth a packet and a voice, and the rest are dropped where
/// they were born instead of being sent to every peer to be stolen out of a 18-slot pool.
///
/// <para><b>Why the source and not the pool.</b> SFX-1 measured a forty-prop collapse stealing
/// 39 % of 54 one-shots at a pool of 18, spent every slot the 24-voice ceiling had left getting
/// there, and wrote down that no pool inside the ceiling serves forty simultaneous impacts. It
/// named the fix and did not own it: <c>FootstepAudioDirector</c> already caps six sprinting
/// players at the source rather than widening the pool for them, and this is the same answer for
/// the same shape of problem. SFX-2 owns it because SFX-2 is the packet that puts impacts on the
/// wire, which is the moment a burst stops being one machine's problem.</para>
///
/// <para><b>Loudest wins, and the order is total.</b> A cap that dropped arbitrary members of a
/// burst would make the SEEKER'S ONLY REMOTE SENSE a lottery: the crash of the shelf going over
/// is the event, and the three cans that tick afterwards are not. Intensity descending, then
/// prop id ascending — the tie-break is what makes this deterministic rather than merely
/// sorted, and a burst of forty props all released on the same tick at the same height is
/// EXACTLY the case where every intensity is equal (measured: the phase-2 heap).</para>
///
/// <para>Godot-free apart from <c>Vector3</c>, which is ordinary managed struct math in
/// GodotSharp.dll — so <c>tests/unit/PropImpactWireTests.cs</c> drives every branch of this with
/// no engine present.</para>
/// </summary>
public static class ImpactBudget
{
    /// <summary><b>How many impacts the server will announce in one physics tick.</b>
    ///
    /// <para>Four, and the number comes off the measurement rather than off a feeling. The pool
    /// is 18 one-shot voices (<c>SfxLab.PoolSize</c>) and an impact recipe runs 0.10–0.40 s, so
    /// at 60 Hz a sustained 4/tick would want ~240 voices a second against a pool that recycles
    /// roughly 60 — which is still over-subscribed, and deliberately: this cap is for the BURST,
    /// not for a steady state that cannot happen (every prop also carries its own 0.4 s
    /// per-body cooldown, so forty props cannot produce 240 contacts a second between them).
    /// What four buys is that the tick a shelf goes over sends four packets instead of forty,
    /// and the four are the four loudest.</para></summary>
    public const int MaxImpactsPerTick = 4;

    /// <summary>One contact the server accepted this tick and has not yet announced.
    /// <paramref name="Intensity"/> is already the wire byte — quantised at the source so the
    /// limiter ranks exactly the values the peers will hear, never a float that rounds to a
    /// different order.</summary>
    public readonly record struct PendingImpact(int PropId, byte Intensity, Vector3 Position);

    /// <summary>
    /// <b>Keeps the loudest <paramref name="max"/> of <paramref name="pending"/>, in place.</b>
    /// Reorders the list so the survivors occupy indices <c>[0, returned)</c> and returns how
    /// many survived; the caller sends those and clears the list.
    ///
    /// <para>Sorts the whole list rather than running a selection, because <c>max</c> is 4 and a
    /// tick's burst is at most a few dozen — a partial selection here would be faster arithmetic
    /// and slower code to be sure of, and this runs once per physics tick on the server only,
    /// behind an early-out for the empty case that is true on essentially every tick.</para>
    ///
    /// <para>The comparison is (intensity DESC, prop id ASC) and both halves are load-bearing:
    /// the first is the rule, and the second is what makes two runs of the same collapse drop
    /// the same three cans. <c>List.Sort</c> is unstable, so without the id tie-break a burst of
    /// equal intensities — the common case, not the exotic one — would keep an arbitrary four.
    /// </para>
    /// </summary>
    public static int KeepLoudest(System.Collections.Generic.List<PendingImpact> pending, int max)
    {
        if (max <= 0)
            return 0;
        if (pending.Count <= max)
            return pending.Count;
        pending.Sort(static (a, b) =>
        {
            int byLoudness = b.Intensity.CompareTo(a.Intensity);
            return byLoudness != 0 ? byLoudness : a.PropId.CompareTo(b.PropId);
        });
        return max;
    }

    /// <summary><b>How hard the contact was, as one wire byte.</b> 0..1 mapped onto 0..255 with
    /// round-half-away-from-zero, so 0 stays 0, 1 stays 255 and the midpoint does not drift.
    /// Clamped, because <c>Carryable.ImpactIntensity</c> is clamped and a second guard here
    /// costs one comparison and removes the question.</summary>
    public static byte IntensityToByte(float intensity01)
    {
        float clamped = Mathf.Clamp(intensity01, 0f, 1f);
        // Not Mathf.RoundToInt on the product alone: NaN would clamp to 0 above, but an explicit
        // second clamp after the scale keeps the cast total even if the first ever changes.
        return (byte)Mathf.Clamp(Mathf.RoundToInt(clamped * 255f), 0, 255);
    }

    /// <summary>The intensity a receiving peer plays at. Exactly inverts
    /// <see cref="IntensityToByte"/> at the endpoints (0 -> 0, 255 -> 1) and to within half a
    /// step everywhere else, which is ~0.2 % of the volume ramp — far under the
    /// <c>PitchJitter</c> every response already randomises by.</summary>
    public static float ByteToIntensity(byte wire) => wire / 255f;
}
