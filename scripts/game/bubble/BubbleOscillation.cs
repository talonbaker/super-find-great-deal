using Godot;
using MpFoundation.Game.World;

namespace Sail.Game.Bubble;

/// <summary>
/// <b>How a bubble idles, and how brightly it glows.</b> Pure functions of
/// <c>(bubbleId, time)</c> and <c>(cyclePhase, cyclesElapsed)</c> — nothing here reads a node,
/// allocates, or sends a byte.
///
/// <para><b>Why pure, and why that is the networking decision.</b> Program D6: idle motion is
/// "a pure function of (bubbleId, time) — identical on every peer, zero traffic." A bubble that
/// bobbed off a local RNG would put ~100 drifting Area3Ds out of agreement between the server's
/// physics and every client's render, and the first thing that breaks is the one thing the
/// bubble exists to do: a player watching the visual pop nothing, because the server's collider
/// was 20 cm away. So the offset is derived, never simulated, and the server's clock is the only
/// input that has to agree.</para>
///
/// <para><b>The bound is a contract, not a consequence.</b> <see cref="MaxOffsetM"/> is enforced
/// by an explicit clamp in <see cref="OffsetAt"/> as well as by the amplitudes summing under it.
/// Placement (BT-11) hides bubbles by tucking them behind geometry; a bob that could wander
/// further than the packet's 0.15 m would let a hidden bubble surface through a wall it was
/// placed behind, which is program §6.11's "must not be pop-able through a wall" arriving from
/// the other direction.</para>
/// </summary>
public static class BubbleOscillation
{
    /// <summary>Vertical bob amplitude, metres (packet scope item 7).</summary>
    public const float AmplitudeM = 0.12f;

    /// <summary>Horizontal wobble amplitude, metres — "a smaller xz wobble". Chosen so the worst
    /// case <c>sqrt(0.12² + 2·0.045²)</c> = 0.1356 m sits under <see cref="MaxOffsetM"/> with
    /// margin, so the clamp below is a guard rather than a shaper of the motion.</summary>
    public const float WobbleM = 0.045f;

    /// <summary>Hard ceiling on how far a bubble may sit from its authored position (acceptance
    /// criterion 6).</summary>
    public const float MaxOffsetM = 0.15f;

    /// <summary>Slowest and fastest bob, rad/s (packet scope item 7). Spread across ids so a
    /// cluster of bubbles never breathes in unison — the tell that they are one system rather
    /// than a hundred things.</summary>
    public const float OmegaMinRadPerSec = 0.8f;
    public const float OmegaMaxRadPerSec = 1.3f;

    /// <summary>Peak emission energy at deep night. BT-6 specified 0.8; <b>DARK-1 raised it to
    /// 2.4 on a measurement, 2026-08-28</b>, and the reason is bloom thresholding rather than
    /// taste.
    ///
    /// <para>The film is TRANSPARENT, so what the glow pass sees is not <c>EMISSION</c> but the
    /// COMPOSITED pixel — <c>emission x alpha + background x (1 - alpha)</c>. At 0.8 the film's
    /// own alpha coupling puts that composite at roughly 0.2 in linear HDR, which is below any
    /// bloom threshold that does not also bloom every unshaded white label in the level (an
    /// unshaded white surface sits at exactly 1.0, and the first measured pass at threshold 0.62
    /// blew the hub's 5 m billboard into a white disc). 2.4 puts the composite above 1.15, which
    /// is where the labels are safely excluded. <b>The old value was not too dim to see; it was
    /// too dim to cross the only threshold that leaves the rest of the frame alone.</b></para>
    ///
    /// <para>Day is untouched — <see cref="Darkness"/> is 0 at noon, so this multiplies to zero
    /// and the day film is bit-identical to what it was.</para></summary>
    public const float MaxEmissionEnergy = 2.4f;

    /// <summary>This bubble's bob rate. A hash of the id rather than <c>id % k</c>: consecutive
    /// ids are what a placement pass produces along a path, and a modulus would make every
    /// k-th bubble in a row move identically.</summary>
    public static float OmegaFor(int id)
    {
        float t = Frac(Hash(id, 0x9E3779B9u));
        return Mathf.Lerp(OmegaMinRadPerSec, OmegaMaxRadPerSec, t);
    }

    /// <summary>This bubble's phase offset, radians.</summary>
    public static float PhaseFor(int id) => Frac(Hash(id, 0x85EBCA6Bu)) * Mathf.Tau;

    /// <summary>
    /// Local offset from the authored position at <paramref name="timeSec"/>. Magnitude is
    /// always &lt;= <see cref="MaxOffsetM"/>.
    ///
    /// <para>An id outside <see cref="BubbleCounterState.Capacity"/> — including the −1 an
    /// un-adopted bubble carries — returns <see cref="Vector3.Zero"/>: a bubble that has not
    /// been given an identity yet sits exactly where the level author put it, which is also what
    /// makes the authored position readable in the editor.</para>
    /// </summary>
    public static Vector3 OffsetAt(int id, double timeSec)
    {
        if (!BubbleCounterState.IsValidId(id))
            return Vector3.Zero;
        float w = OmegaFor(id);
        float phi = PhaseFor(id);
        var t = (float)timeSec;
        // The xz pair runs at a deliberately different rate (0.61x) and its own quarter-turn
        // offsets, so the path is a slow lissajous rather than a straight diagonal line — a
        // bubble that bobbed along one axis reads as a mechanism, not as something floating.
        float y = AmplitudeM * Mathf.Sin(w * t + phi);
        float x = WobbleM * Mathf.Sin(w * 0.61f * t + phi + Mathf.Pi * 0.5f);
        float z = WobbleM * Mathf.Cos(w * 0.47f * t + phi);
        var offset = new Vector3(x, y, z);
        float len = offset.Length();
        return len > MaxOffsetM ? offset * (MaxOffsetM / len) : offset;
    }

    /// <summary>
    /// The wander hook (program §7, "wandering bubbles"): a slow drift along an authored
    /// <c>Path3D</c>, still deterministic from <c>(id, time)</c>. <b>Deliberately zero today</b>
    /// — the stretch goal is flagged, not built (packet: "Leave a clearly named hook … no wander
    /// now"). It is a named function rather than a comment so the future packet has a single
    /// place to fill in and a single place the bound below is already enforced for it: whatever
    /// this returns is added BEFORE <see cref="OffsetAt"/>'s clamp is re-applied by the caller,
    /// so a wander cannot silently break acceptance criterion 6 by arriving later.
    /// </summary>
    public static Vector3 WanderOffsetAt(int id, double timeSec) => Vector3.Zero;

    /// <summary>
    /// How dark it is right now, 0 (broad day) to 1 (deep night), derived from the SHIPPED cycle
    /// bands rather than from a second clock: <see cref="CycleBands.GetBand"/> is what
    /// <c>OutdoorAtmosphere</c> and <c>NightDome</c> already key off, and the night length moves
    /// per day (0.250 on day 1 to 0.500 by day 5), so anything that re-derived "night" from a
    /// fixed phase number would drift away from the visible sky as the run escalates.
    ///
    /// <para>Day is flat 0 and Night is flat 1; the two sweeps ramp linearly between them. That
    /// deliberately makes the glow arrive WITH the dusk sweep the player can see coming, not
    /// after it.</para>
    /// </summary>
    public static float Darkness(float cyclePhase, int cyclesElapsed)
    {
        CycleBands.Band band = CycleBands.GetBand(cyclePhase, cyclesElapsed, out float progress);
        return band switch
        {
            CycleBands.Band.Day => 0f,
            CycleBands.Band.DuskSweep => Mathf.Clamp(progress, 0f, 1f),
            CycleBands.Band.Night => 1f,
            _ => Mathf.Clamp(1f - progress, 0f, 1f), // DawnSweep
        };
    }

    /// <summary>Emission energy for the bubble film at this phase — <c>lerp(0, 0.8,
    /// darkness)</c> (packet scope item 7, acceptance criterion 7).</summary>
    public static float EmissionEnergy(float cyclePhase, int cyclesElapsed) =>
        MaxEmissionEnergy * Darkness(cyclePhase, cyclesElapsed);

    // --- id hashing -----------------------------------------------------------------------
    // A 32-bit integer avalanche (the finalizer shape used by murmur/splitmix), so adjacent ids
    // land far apart. Deterministic, allocation-free, and identical on every peer and platform —
    // the whole zero-traffic argument above rests on that last property, so it is arithmetic on
    // uint rather than anything that could reach for a Random or a float seed.
    private static uint Hash(int id, uint salt)
    {
        unchecked
        {
            uint h = (uint)id + salt;
            h ^= h >> 16;
            h *= 0x7FEB352Du;
            h ^= h >> 15;
            h *= 0x846CA68Bu;
            h ^= h >> 16;
            return h;
        }
    }

    private static float Frac(uint h) => (h & 0xFFFFFFu) / (float)0x1000000u;
}
