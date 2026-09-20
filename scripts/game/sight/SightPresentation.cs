using Godot;

namespace MpFoundation.Game.Sight;

/// <summary>
/// <b>The dark you can see: how the server's sight number becomes a rendered frame.</b>
/// <see cref="PlayerSightCurve"/> decides how far a player can see; this decides what that looks
/// like. It is the presentation half of canon fact 4, and it is deliberately the whole of it — one
/// pure static class holding every constant and every formula, so a feel pass retunes the look of
/// the night by changing a number here and nothing else moves.
///
/// <para><b>PRESENTATION READS, IT NEVER WRITES.</b> Nothing in this file, and nothing that calls
/// it, may feed a value back into <see cref="PlayerSightService"/>, recompute a sight range of its
/// own, or become a second opinion about how far anyone can see. The service is the only producer.
/// This file's entire input from it is one float and one bool.</para>
///
/// <para><b>THE PARITY LAW SURVIVES THE RENDERER, OR IT WAS NEVER HELD.</b>
/// <see cref="PlayerSightCurve"/> proves the <i>number</i> is bit-identical on Low, Medium and
/// High. That proof buys nothing if the renderer then spends it — if fog, draw distance or a
/// shader dial differed by tier such that a player on Low could make out a shape a player on High
/// could not, the law would be broken in the only place the player actually experiences it. So the
/// same rule binds here, in the same words: <b>no function in this file reads
/// <see cref="MpFoundation.World.GraphicsQuality"/>, a viewport size, a frame time, a camera, or
/// any other machine-local quantity, and none ever may.</b> Every input below is the replicated
/// sight range, the replicated phase clock's night factor, or a constant in this file.
/// <c>SightPresentationTests</c> pins that across all three tiers with a negative control, and
/// scans this directory's source for the forbidden symbol so the <i>next</i> edit has to come past
/// a red test rather than past a comment.</para>
///
/// <para><b>Why fog, and not the ambient floor.</b> The obvious way to make the dark cost sight is
/// to turn the lights down, and that dial —
/// <c>OutdoorAtmosphere.NightAmbientFloor</c> — is <b>THRILL-BIBLE §6.2's night-floor fork and is
/// not this pass's to move</b> (see that constant's own doc, and the collision note in
/// <see cref="FogDensityWithSight"/>). It is also the wrong instrument: ambient energy is a global
/// scalar with no distance term, so it cannot express "twelve metres, right now, because you are
/// standing at the edge of the pit" — it can only make everything uniformly dimmer, everywhere,
/// for everyone. Depth fog is the channel that already carries distance, the atmosphere already
/// owns it and writes it every frame, and it costs a float write and zero draw calls (the perf
/// floor is a GTX 970 on Forward+; a volumetric froxel buffer is the named budget-breaker and is
/// not used here).</para>
///
/// <para><b>Light sources are exempt from their own darkness, on purpose.</b> Depth fog attenuates
/// every surface at range, emissive ones included, so a sight fog thick enough to blind you would
/// also extinguish the fire you are trying to walk back to — which would break canon fact 3
/// (the fire is the centrepiece) and canon fact 6 (a trail of lights you follow home) in the act
/// of delivering canon fact 4. The rule is therefore: <b>the fog swallows the world, never the
/// lights.</b> It is not new — <c>StarDome.gdshader</c> and the
/// moon disc already set <c>fog_disabled</c>/<c>DisableFog</c> for exactly this reason — and this
/// pass extended it to the since-removed fire flame, which was the one beacon in the game still being fogged.
/// A lit thing stays a point of light at any distance; the ground it lights, the trees around it
/// and anything walking between them do not.</para>
///
/// <para><b>What this deliberately does NOT do.</b> It does not pull the camera's far plane in
/// (a hard cull pops, and it would cull the beacons too), it does not add a vignette, a
/// post-process pass, or a per-pixel technique of any kind, and it does not touch ambient, moon,
/// sun or sky. It also does not know what a fire is: it takes a range in metres and has no way to
/// ask where that range came from.</para>
/// </summary>
public static class SightPresentation
{
    /// <summary><b>Optical depth at exactly the sight range — the constant that defines what
    /// "you can see this far" means as a picture.</b> Godot's depth fog is exponential, so a
    /// surface at distance <c>d</c> keeps <c>exp(-density * d)</c> of its own contrast against the
    /// fog colour. Setting <c>density = ExtinctionAtSightRange / range</c> makes the sight range
    /// the distance at which exactly <c>exp(-3) ≈ 5%</c> survives.
    ///
    /// <para><b>Why 3.0 and not 1.0 or 6.0.</b> At an optical depth of 1 a full 37% of contrast
    /// remains at the stated range, so the range would read as "getting hazy" rather than as a
    /// limit and the number would be a lie. At 6 (0.25%) the fall is so steep that the last third
    /// of the range is already black and the useful gradient collapses into the near field — the
    /// same argument <see cref="PlayerSightCurve"/> makes for rejecting an inverse-square falloff.
    /// 5% residual against the near-black night fog colour sits under the contrast a player can
    /// resolve on a dim display, while leaving the whole approach to the range readable as a
    /// gradual loss the player can feel themselves paying.</para>
    ///
    /// <para>It is also what makes the mapping invertible — see
    /// <see cref="RenderedVisibilityM"/>, which is how the tests measure "how far can this frame
    /// actually see" as a number rather than as an opinion.</para></summary>
    public const float ExtinctionAtSightRange = 3.0f;

    /// <summary><b>How sharply the sight fog arrives across the dusk sweep.</b> The weight applied
    /// to the sight term is <c>nightFactor^SightFogWeightExponent</c>
    /// (<see cref="SightFogWeight"/>).
    ///
    /// <para><b>Why 2.0 (quadratic) and not 1.0.</b> Copied in form and in reasoning from
    /// the original level's night-front enclosure curve, which eased its own closing-in
    /// quadratically and stated why: at the halfway point of the sweep only a quarter of the
    /// effect is in, so the wide
    /// golden vista at the top of dusk — the read Talon praised in the 2026-07-26 playtest, and
    /// the game's warning channel per THRILL-BIBLE §6.6 — is still intact while the contraction is
    /// already underway. Linear would put half the fog into the middle of the golden hour and
    /// spend an effect that is already landed to buy one that is arriving anyway.</para>
    ///
    /// <para>Any exponent ≥ 1 keeps the two structural guarantees: the weight is exactly 0 at
    /// <c>nightFactor = 0</c> (so <b>day is bit-identically untouched</b>) and exactly 1 at
    /// <c>nightFactor = 1</c> (so deep night is exactly the sight range), and it is continuous
    /// between.</para></summary>
    public const float SightFogWeightExponent = 2.0f;

    /// <summary><b>Half-life of the renderer's own smoothing of the sight range, seconds.</b>
    ///
    /// <para><b>Why any smoothing at all — this one is not polish.</b>
    /// <see cref="PlayerSightService"/> broadcasts at 5 Hz and clients deliberately do <i>not</i>
    /// extrapolate between samples (that class's doc explains why: extrapolating would derive
    /// gameplay visibility from client-side interpolation, which is the parity law's failure mode
    /// arriving through the back door). So the raw value a client holds is a <b>staircase</b>, and
    /// rendering it raw would produce exactly the stepped, popping night the direction forbids.
    /// The service's own doc blesses the fix in one sentence: "a renderer may smooth it for
    /// presentation, and must never feed the smoothed value back into anything."</para>
    ///
    /// <para><b>Why 0.25 s.</b> It is comfortably longer than the 0.2 s broadcast interval, so no
    /// individual sample lands as a visible step, and short enough that the lag is not felt: the
    /// time constant is 0.25/ln2 ≈ 0.36 s, and the steepest sight gradient in the shipped game
    /// (sprinting at 4.86 m/s across a full fire pit's 18 m pool, which spans 3 m → 34.5 m of
    /// sight) is 8.5 m/s, so the rendered range trails the authoritative one by at most ≈ 3 m.
    /// That error is presentation-only, it is bounded, and it is the same on every peer.</para>
    ///
    /// <para><b>And it is frame-rate independent by construction, which is a parity property, not
    /// a nicety.</b> <see cref="Smooth"/> uses <c>1 - 2^(-dt/halfLife)</c>, whose per-step factors
    /// compose exactly (<c>2^-a · 2^-b = 2^-(a+b)</c>). A machine running at 30 fps and one running
    /// at 144 fps therefore reach the <i>same</i> rendered range at the same wall-clock instant —
    /// a naive <c>lerp(current, target, 0.1)</c> would not, and would hand the faster machine a
    /// faster-responding, marginally further-seeing night. Pinned by test.</para></summary>
    public const float SmoothingHalfLifeSec = 0.25f;

    /// <summary><b>The gate: what the renderer is allowed to believe about how far the local
    /// player can see.</b> Takes the two primitives rather than the node so the whole degrade
    /// table is provable by <c>dotnet test</c> with no engine present — the same seam
    /// <see cref="PlayerSightCurve.ResolveForClock"/> gives the server side.
    ///
    /// <para><b>Every failure degrades to blind, and never to omniscient.</b> Unsynced service,
    /// absent service, unknown peer, NaN, zero, negative — each returns
    /// <see cref="PlayerSightCurve.DarkFloorM"/>. The other direction has already cost this repo
    /// once: a radius that silently read as its degenerate default
    /// inverted a gameplay rule for a whole session while every test stayed green. The degenerate
    /// default here is deliberately the one that takes sight away.</para>
    ///
    /// <para><b>And a value that is too LARGE is clamped, not trusted.</b> A corrupt or
    /// future-tuned range above <see cref="PlayerSightCurve.DaylightSightM"/> would drive the fog
    /// density toward zero, which is the inversion in its rendering form — the frame would say
    /// "see everything" precisely because the input was wrong. The ceiling is daylight because
    /// daylight is by definition the most anyone may see.</para></summary>
    public static float TargetRangeM(bool synced, float rawRangeM)
    {
        if (!synced || float.IsNaN(rawRangeM) || rawRangeM <= 0f)
            return PlayerSightCurve.DarkFloorM;
        return Mathf.Clamp(rawRangeM, PlayerSightCurve.DarkFloorM, PlayerSightCurve.DaylightSightM);
    }

    /// <summary>Depth-fog density that makes <paramref name="rangeM"/> the distance at which
    /// <see cref="ExtinctionAtSightRange"/> optical depths have accumulated. Monotonically
    /// decreasing in the range — more sight is always thinner fog, never thicker — and bounded
    /// above by the dark floor's own density (3.0/3.0 = 1.0 m⁻¹), because the range is clamped
    /// before it gets here.</summary>
    public static float DensityForRange(float rangeM) =>
        ExtinctionAtSightRange / Mathf.Clamp(
            float.IsNaN(rangeM) ? PlayerSightCurve.DarkFloorM : rangeM,
            PlayerSightCurve.DarkFloorM, PlayerSightCurve.DaylightSightM);

    /// <summary>The inverse of <see cref="DensityForRange"/>: how far a frame with this fog
    /// density can actually see, in metres. <b>This is the measurement the tier-parity test takes
    /// — the rendered quantity, not the gameplay one.</b> Expressing the check as "how far can
    /// each tier see" rather than "are these two floats equal" is what makes a failure legible as
    /// the law it breaks.</summary>
    public static float RenderedVisibilityM(float fogDensity) =>
        fogDensity <= 0f ? float.PositiveInfinity : ExtinctionAtSightRange / fogDensity;

    /// <summary>How much of the sight treatment is live, from the atmosphere's own night factor
    /// (0 by day, 1 at deep night — <c>OutdoorAtmosphere.AtmoState.NightFactor</c>). Exactly 0 at
    /// 0 and exactly 1 at 1, continuous between; see <see cref="SightFogWeightExponent"/> for the
    /// curve's shape and why.
    ///
    /// <para><b>This is the whole of "day is not night".</b> Daylight sight is 80 m against a 36 m
    /// night maximum, and a fog density sized for 80 m would still be a visible haze over a camp
    /// whose cleared core is only 50 m across. Rather than special-casing the Day band, the weight
    /// makes the night treatment structurally absent by day: at <c>nightFactor = 0</c> the blend
    /// returns the phase-authored value <i>bit-identically</i>, so no retune of any constant in
    /// this file can leak into the day. Reusing the atmosphere's existing curve rather than
    /// authoring a second one is the same one-fact-one-owner discipline that carries
    /// <c>NightFactor</c> out of <c>Evaluate</c> instead of recomputing it per consumer.</para>
    ///
    /// <para>It also makes the blind fallback safe in both directions: an absent sight service at
    /// noon resolves to the 3 m floor, and the weight then discards it entirely, so the failure
    /// that must not blind a player in daylight cannot.</para></summary>
    public static float SightFogWeight(float nightFactor)
    {
        float n = float.IsNaN(nightFactor) ? 0f : Mathf.Clamp(nightFactor, 0f, 1f);
        // Exact at both endpoints — Mathf.Pow(0,2)=0 and Mathf.Pow(1,2)=1 — which is what lets the
        // day case be bit-identical rather than merely close.
        return Mathf.Pow(n, SightFogWeightExponent);
    }

    /// <summary><b>The binding itself: the phase-authored fog density, with the sight range folded
    /// in.</b> The one function that turns a number nobody could see into a frame.
    ///
    /// <para><b>The sight term may only ever THICKEN the fog, never thin it</b> — hence the
    /// <c>Max</c>. The blend target is "whichever of the two hides more", so no future retune of
    /// the atmosphere's phase curve can be quietly undone by the sight binding, and no sight range
    /// can be used to buy back visibility the clock already took away. Today the sight density
    /// (0.083 m⁻¹ at the 36 m maximum, 1.0 m⁻¹ at the 3 m floor) exceeds the authored night haze
    /// (0.009 m⁻¹) by an order of magnitude at every range, so the <c>Max</c> is inert — it is
    /// there as the guard, and it is pinned by test.</para>
    ///
    /// <para><b>Continuous in both arguments, which is the requirement that rules out the obvious
    /// alternatives.</b> A stepped "near/mid/far" fog preset, or a draw-distance cull, would each
    /// pop as a fire burned down or a player walked; this is a lerp between two continuous
    /// functions of continuous inputs, and the caller feeds it a smoothed range
    /// (<see cref="Smooth"/>) so even the 5 Hz replication staircase arrives as a slope.</para>
    ///
    /// <para><b>THE §6.2 COLLISION, AND WHY THERE ISN'T ONE — read this before "just turning the
    /// ambient down".</b> THRILL-BIBLE §6.2 forks over
    /// <c>OutdoorAtmosphere.NightAmbientFloor</c>: navigability wants the dark survivable by
    /// sight, the night reversal wants it to cost sight. <b>This pass does not move that number
    /// and does not need to.</b> Fog composites <i>over</i> whatever the ambient term produced —
    /// at an optical depth of 3 the surface's own lit colour is 5% of the result regardless of how
    /// brightly it was lit — so the near-blind read is reachable at the shipped floor, at a
    /// lowered floor, and at a raised one. Whichever way the fork lands, the only thing that
    /// changes is how the last few metres <i>inside</i> the sight range look, and nothing in this
    /// file has to be retuned to follow it. If a future pass finds itself needing to move the
    /// floor to make sight read, that is a genuine design fork and it belongs to Talon — it is not
    /// a constant to adjust in passing.</para></summary>
    /// <param name="phaseFogDensity">What the phase curves authored for this instant.</param>
    /// <param name="nightFactor">0 by day … 1 at deep night.</param>
    /// <param name="renderedRangeM">The smoothed, gated sight range — <see cref="Smooth"/> of
    /// <see cref="TargetRangeM"/>.</param>
    public static float FogDensityWithSight(float phaseFogDensity, float nightFactor,
        float renderedRangeM)
    {
        float weight = SightFogWeight(nightFactor);
        if (weight <= 0f)
            return phaseFogDensity; // day, bit-identically.
        float target = Mathf.Max(phaseFogDensity, DensityForRange(renderedRangeM));
        return Mathf.Lerp(phaseFogDensity, target, weight);
    }

    /// <summary><b>The fog's colour has to be the colour of the dark, or the dark reads as
    /// weather.</b> The phase curves author a cold blue-grey night fog (0.12, 0.15, 0.24) which is
    /// correct for the thin haze it was tuned as, and badly wrong once the density is sized to
    /// blind: distant geometry would fade to a pale grey wall that is <i>brighter</i> than the
    /// near-black night sky behind it — a glowing fog bank rather than a world being swallowed.
    /// So the sight term drags the fog colour toward <paramref name="nightBackdropColor"/> on the
    /// same weight that thickens it, and a tree at the edge of your sight fades into exactly the
    /// value of the sky it is standing against.
    ///
    /// <para>Weighted identically to the density on purpose: two curves for one effect is how they
    /// drift apart, and a colour that arrived on a different schedule from the density would read
    /// as a second, unexplained event during the dusk sweep.</para>
    ///
    /// <para>Note that in the shipped camp the <c>NightDome</c> also lerps the fog colour to its
    /// own near-black inside-value once the night front has swallowed the player, so this is
    /// belt-and-braces there. It is <i>not</i> belt-and-braces in a world with an atmosphere and
    /// no dome (the labs), which is exactly where an uncoloured sight fog would have been found by
    /// a headed capture rather than by a test.</para></summary>
    public static Color FogColorWithSight(Color phaseFogColor, Color nightBackdropColor,
        float nightFactor)
    {
        float weight = SightFogWeight(nightFactor);
        return weight <= 0f ? phaseFogColor : phaseFogColor.Lerp(nightBackdropColor, weight);
    }

    /// <summary><b>Exponential smoothing with an exact half-life — the renderer's only state.</b>
    /// See <see cref="SmoothingHalfLifeSec"/> for why the smoothing exists (a 5 Hz staircase) and
    /// why this particular form (composes exactly, so the result is frame-rate independent and two
    /// machines at different frame rates cannot end up seeing different distances).
    ///
    /// <para>Never overshoots and never reverses: the returned value always lies between
    /// <paramref name="currentM"/> and <paramref name="targetM"/> inclusive. A non-positive or NaN
    /// <paramref name="deltaSec"/> returns the current value untouched rather than teleporting —
    /// a paused or first frame must not be a visual event.</para></summary>
    public static float Smooth(float currentM, float targetM, double deltaSec)
    {
        if (double.IsNaN(deltaSec) || deltaSec <= 0.0)
            return currentM;
        float k = 1f - Mathf.Pow(2f, (float)(-deltaSec / SmoothingHalfLifeSec));
        return currentM + (targetM - currentM) * Mathf.Clamp(k, 0f, 1f);
    }
}
