namespace MpFoundation.Dev.Playground;

/// <summary>
/// <b>The handling model's own numbers</b> (BIKE-2x, 2026-09-02): the lean, the speed-dependent
/// turn curve, and the deepened drift with its charge tiers.
///
/// <para><b>Why this is a second record and not seventeen more rows in
/// <see cref="BikeTuning"/>.</b> <c>BikeTuning</c> is the first agent's file and the bike's state
/// layer reads it; this packet's brief is explicit that nothing here may be added to it. The split
/// is also honest about ownership: every row below is read by <see cref="BikeHandling"/> and by the
/// harness that draws it, and by <b>nothing that writes velocity</b>. A row here can never change
/// where the body goes except through the two seams named in the class doc of
/// <see cref="BikeHandling"/>.</para>
///
/// <para><b>Talon's rulings this record is built under</b> (2026-09-02, quoted so a later reader
/// does not have to find the dispatch):
/// <list type="bullet">
/// <item><i>"Lean is PRESENTATION plus turn shaping, not a new motor."</i> So the lean rows drive
/// a roll angle and the turn curve, and the bike stays a rewrite of the shipped motor's tuning
/// through <c>BikeRig.Ride</c>. There is no second movement system here and no new state on
/// <c>MoveState</c> or the wire.</item>
/// <item><i>"the skid is a steering tool, never a brake"</i> — which is why the drift's charge
/// pays out as an exit boost and its only cost is the grip it has to earn back.</item>
/// <item><i>"no recovery lockouts anywhere"</i> — <see cref="DriftGripRecoverSec"/> is a
/// <b>recovery curve</b>, not a lockout: the body steers throughout, it simply steers through a
/// grip term that climbs back to 1. Nothing below can take control away.</item>
/// </list></para>
///
/// <para>Lab-only, like <see cref="BikeTuning"/>: <see cref="Current"/> is settable without a guard
/// because there is no session to keep in parity with and there will not be one until the feel is
/// ruled on.</para>
/// </summary>
public readonly record struct BikeHandlingTuning
{
    // --- Lean: presentation, and the input to the turn curve -----------------------------------

    /// <summary>The most the bike ever leans, degrees. A hard clamp on the steady state, so an
    /// absurd yaw rate at an absurd speed still produces an angle a body could hold.
    ///
    /// <para>45 degrees is the physical angle at which the lateral acceleration equals gravity —
    /// <c>tan 45 = 1</c> — so a default at or under it means the greybox never draws a lean the
    /// arithmetic could not justify. It is a starting point; a stylised bike may want more, and
    /// only a headed run can say.</para></summary>
    public float LeanMaxDeg { get; init; }

    /// <summary>How fast the lean chases its steady state, 1/s. A first-order lag rate, not a
    /// degrees-per-second slew: <c>lean += (target - lean) * (1 - exp(-rate * dt))</c>, which is
    /// frame-rate independent and never overshoots. The time to 63 % is <c>1 / rate</c>, so 8 /s is
    /// about an eighth of a second to most of the way — fast enough that a flick reads as a flick,
    /// slow enough that the bike is visibly <i>settling</i> into a corner rather than snapping to
    /// it.</summary>
    public float LeanRatePerSec { get; init; }

    /// <summary>How much of the physical lean is drawn, as a multiple. 1.0 is the honest angle
    /// (<c>atan(v w / g)</c>); above 1 is the stylisation dial, and <c>PROPORTION-STYLE.md</c> is
    /// the document that governs how far that goes once anyone is judging pixels. Clamped by
    /// <see cref="LeanMaxDeg"/> either way, so this can never produce an angle the max forbids.
    ///
    /// <para>Above 1 by default because the lab's speeds are small: at 9.4 m/s and the shipped
    /// <c>TurnLerp</c> the honest angle is single digits, and a lean nobody can see is a lean
    /// nobody can tune.</para></summary>
    public float LeanExaggeration { get; init; }

    // --- The low-speed wobble: presentation, and ONLY presentation (BIKE-4B) --------------------

    /// <summary>
    /// <b>How far the bike rocks at walking pace, degrees of peak roll.</b> Read
    /// <see cref="BikeHandling.WobbleDeg"/> for the shape; this row is its whole size. Zero
    /// switches the feature off and, when it is zero, the drawn roll is bit-identical to the corner
    /// lean alone — the wobble is provably free when nobody wants it.
    ///
    /// <para><b>Why 2.5.</b> Peak roll, so peak-to-peak is 5 degrees. On the greybox that is about
    /// seven centimetres of lateral sway at head height from the chase camera — plainly visible in
    /// motion, and an order of magnitude under the 38-degree
    /// <see cref="LeanMaxDeg"/> clamp, so the wobble can never be mistaken for or compete with a
    /// corner lean. It is also deliberately under the 3-degree floor BIKE-3A's drawn-lean check
    /// samples above, so the wobble cannot flip the sign that check pins.</para>
    ///
    /// <para>Nobody has felt this number. It is the first honest guess at "alive, not broken", and
    /// it is a slider precisely because that judgement is Talon's.</para></summary>
    public float WobbleAmplitudeDeg { get; init; }

    /// <summary>
    /// <b>The speed the wobble has completely died by, m/s.</b> At and above it the wobble is
    /// exactly zero; below it the amplitude climbs on a smoothstep to full at a standstill. This
    /// row is the whole of the speed-stability coupling BIKE-3C ranked first: slow is alive, fast
    /// is serene.
    ///
    /// <para><b>Why 4.0.</b> BIKE-3C row 13 carries Wobble's own measured stability crossover
    /// (~4.3 m/s) and rules that this lab should fade <i>earlier</i> than Wobble did, because
    /// Wobble's wobble was a pedalling artefact and this one is idle texture. 4.0 sits on that
    /// crossover, well above any walking pace, comfortably under the 6.1 m/s foot sprint and far
    /// under the 9.4 m/s ride cap — so anything a player would call "riding" is dead calm, and only
    /// a creep or a track-stand rocks.</para></summary>
    public float WobbleFadeSpeedMps { get; init; }

    // --- Turn shaping: a multiplier ON TOP of the ride's own turn rows --------------------------

    /// <summary>
    /// <b>The turn multiplier at a standstill.</b> <c>BikeRig.Ride</c> already blends the foot
    /// <c>TurnLerp</c> and <c>TurnAcceleration</c> by <c>RideTurnMul</c> / <c>RideTurnAccelMul</c>;
    /// this curve multiplies that result again, by speed. Above 1 means a stopped or slow bike
    /// turns tighter than the ride rows alone would give it.
    ///
    /// <para>The whole point of the curve is that a bike's turning circle grows with speed, and the
    /// shipped motor's <c>TurnLerp</c> does not know that — it turns a body at 1 m/s and at 9 m/s
    /// at the same rate, which is what makes a fast bike feel like a hovercraft.</para></summary>
    public float TurnLowSpeedMul { get; init; }

    /// <summary>The turn multiplier at and above the ride cap. Below 1: wide at speed. The drift is
    /// what buys a tight corner back, which is the design — a fast line is committed, and RMB is
    /// the way out of the commitment.</summary>
    public float TurnHighSpeedMul { get; init; }

    /// <summary>The shape between the two. The curve is
    /// <c>lerp(low, high, pow(speed01, exponent))</c>, so 1 is linear in normalised speed, above 1
    /// holds the tight end longer (the widening arrives late, near the cap) and below 1 gives it
    /// away early. Never at or below zero — <see cref="BikeHandling.TurnMultiplier"/> clamps.</summary>
    public float TurnCurveExponent { get; init; }

    // --- The drift, deepened -------------------------------------------------------------------

    /// <summary>
    /// <b>The minimum speed the drift may be entered at, m/s.</b> The packet's hard rule: <i>the
    /// drift must never enter from ordinary cornering.</i> A body pottering round a corner at
    /// walking pace with the button held gets nothing; the drift is a thing you commit to at speed.
    ///
    /// <para>Defaulted just under the ride's jog so a deliberate run-up always clears it and a
    /// pootle never does. <b>This gate is the handling model's own</b> — <c>BikeLayer.Drifting</c>
    /// has no entry speed of its own (it is <c>Mounted &amp;&amp; held &amp;&amp; on floor</c>), so
    /// the charge, the tier and the boost below are gated here and the layer's own nose-swing is
    /// not. That gap is written up in the report; closing it is a change to
    /// <see cref="BikeLayer"/>, which this packet does not own.</para></summary>
    public float DriftEntrySpeedMps { get; init; }

    /// <summary>The longest a single drift may run, seconds. Past it the drift is <i>over</i> —
    /// the charge stops accruing and the tier is banked — even with the button still down, so a
    /// player cannot hold a corner indefinitely and arrive with a full charge they did not steer
    /// for. 0 means no cap.</summary>
    public float DriftMaxSec { get; init; }

    /// <summary>
    /// <b>Seconds to 99 % grip after the button is released.</b> Read literally: this row IS the
    /// number the curve is measured against, and <see cref="BikeHandling.GripStep"/> derives its
    /// time constant from it (<c>tau = sec / ln 100</c>) rather than the other way round. A row
    /// whose meaning needs a conversion before it can be judged is a row nobody tunes.
    ///
    /// <para><b>It is not a lockout.</b> Grip is a term the lean and the turn curve read; steering,
    /// jumping and the bike's own drift are all available at grip 0. Talon: <i>"no recovery
    /// lockouts anywhere."</i></para></summary>
    public float DriftGripRecoverSec { get; init; }

    /// <summary>Charge, in seconds, at which the drift reaches tier 1. Mario Kart Wii's model:
    /// hold the drift and the sparks change colour; each colour is worth a bigger boost on
    /// release.</summary>
    public float DriftTier1Sec { get; init; }

    /// <summary>Charge, in seconds, at which the drift reaches tier 2. Must exceed
    /// <see cref="DriftTier1Sec"/>; <see cref="BikeHandling.DriftTier"/> reads the thresholds in
    /// order and a mis-ordered set simply means the higher tier is unreachable rather than that
    /// anything breaks.</summary>
    public float DriftTier2Sec { get; init; }

    /// <summary>Charge, in seconds, at which the drift reaches tier 3 — the top. Above
    /// <see cref="DriftMaxSec"/> would make tier 3 unreachable by duration alone; the default sits
    /// under it deliberately, so the ladder is climbable in one held corner.</summary>
    public float DriftTier3Sec { get; init; }

    /// <summary>The exit boost tier 1 pays, m/s along the heading. <b>May be zero</b> — the packet
    /// says so explicitly, and a zero here turns the tiers into pure feedback with no speed
    /// consequence, which is a real option Talon may prefer once he has felt it.</summary>
    public float DriftTier1BoostMps { get; init; }

    /// <summary>The exit boost tier 2 pays, m/s.</summary>
    public float DriftTier2BoostMps { get; init; }

    /// <summary>The exit boost tier 3 pays, m/s.</summary>
    public float DriftTier3BoostMps { get; init; }

    /// <summary>
    /// <b>Which charge model is live.</b> False is the Mario Kart Wii model the packet names:
    /// charge is time held. True is the older stick-wiggle model (MK64 / MKDS): charge comes from
    /// working the stick side to side inside the drift, and simply holding a line earns nothing.
    ///
    /// <para>Both are built because the packet asks for both to be <i>felt</i>, and they are not
    /// separable by argument — one rewards commitment to a line, the other rewards busy hands, and
    /// which is fun on a bike is exactly the sort of question this lab exists to answer. They share
    /// one ladder: a flick is worth <see cref="DriftWiggleFlickSec"/> of the same charge duration
    /// buys, so the tier thresholds mean the same thing under either model and a note written
    /// against one is readable against the other.</para></summary>
    public bool DriftWiggleCharge { get; init; }

    /// <summary>What one qualifying flick is worth, in seconds of charge. At the default tier
    /// thresholds, tier 3 is nine flicks — brisk, but reachable inside a long corner.</summary>
    public float DriftWiggleFlickSec { get; init; }

    /// <summary>How far the steering input must be thrown, 0..1 of full lateral, for a reversal to
    /// count as a flick. Not zero: a stick resting near centre crosses it constantly on noise, and
    /// a charge that fills itself while nobody moves is not a charge.</summary>
    public float DriftWiggleFlickMin { get; init; }

    /// <summary>The values the prototype opens on. Every one is a starting point, and none of them
    /// has been felt by anyone — this packet ran headless.</summary>
    public static readonly BikeHandlingTuning Default = new()
    {
        LeanMaxDeg = 38f,
        LeanRatePerSec = 8f,
        LeanExaggeration = 2.2f,

        WobbleAmplitudeDeg = 2.5f,
        WobbleFadeSpeedMps = 4.0f,

        TurnLowSpeedMul = 1.35f,
        TurnHighSpeedMul = 0.55f,
        TurnCurveExponent = 1.4f,

        DriftEntrySpeedMps = 3.5f,
        DriftMaxSec = 4.0f,
        DriftGripRecoverSec = 0.55f,

        DriftTier1Sec = 0.60f,
        DriftTier2Sec = 1.40f,
        DriftTier3Sec = 2.40f,
        DriftTier1BoostMps = 1.2f,
        DriftTier2BoostMps = 2.4f,
        DriftTier3BoostMps = 4.0f,

        DriftWiggleCharge = false,
        DriftWiggleFlickSec = 0.28f,
        DriftWiggleFlickMin = 0.55f,
    };

    /// <summary>
    /// <b>The presets the turn curve and the drift can be swapped between in one keystroke.</b> The
    /// packet asks for the turn curve to be "preset-able"; a curve nobody can A/B against another
    /// curve in the same run-out is a curve that gets judged once, badly. These live here rather
    /// than in <c>BikePresets</c> for the same reason this record does: that file is the first
    /// agent's.
    ///
    /// <para>Each is a whole record, not a patch, so loading one can never leave half of a previous
    /// preset in force — the failure the foot presets already guard against.</para>
    /// </summary>
    public static readonly (string Key, string Name, BikeHandlingTuning Tuning)[] Presets =
    {
        ("0", "handling default — a curve that widens, MKWii charge", Default),

        // The curve turned off, so the question "does the curve do anything" has a control. A flat
        // 1.0 at both ends with any exponent is exactly the ride rows on their own.
        ("1", "NO CURVE — the ride's turn rows alone (the control)", Default with
        {
            TurnLowSpeedMul = 1f,
            TurnHighSpeedMul = 1f,
        }),

        // The curve pushed hard both ways. If the shaped curve is worth having, this is where it
        // shows; if it is a gimmick, this is where it feels wrong.
        ("2", "HARD CURVE — pivots at rest, committed at speed", Default with
        {
            TurnLowSpeedMul = 1.8f,
            TurnHighSpeedMul = 0.35f,
            TurnCurveExponent = 1.0f,
        }),

        // The wiggle charge, everything else at default: the A/B the packet asks for, one press
        // apart, so both charge models can be felt on the same corner of the same lane.
        ("3", "WIGGLE CHARGE — work the stick, not the clock", Default with
        {
            DriftWiggleCharge = true,
        }),

        // Tiers that pay nothing. The packet says the exit boost may be zero; this is that option
        // made pressable, so "are the sparks enough on their own" is a question with an answer.
        ("4", "TIERS, NO PAYOUT — the sparks are the whole reward", Default with
        {
            DriftTier1BoostMps = 0f,
            DriftTier2BoostMps = 0f,
            DriftTier3BoostMps = 0f,
        }),

        // A drift that has to be earned and pays properly when it is. The far end of the design.
        ("5", "COMMITTED DRIFT — late tiers, big payout, slow grip", Default with
        {
            DriftEntrySpeedMps = 5.5f,
            DriftTier1Sec = 0.9f,
            DriftTier2Sec = 2.0f,
            DriftTier3Sec = 3.2f,
            DriftTier1BoostMps = 1.5f,
            DriftTier2BoostMps = 3.5f,
            DriftTier3BoostMps = 6.0f,
            DriftGripRecoverSec = 0.95f,
        }),

        // No lean at all, and no curve: the "is any of this doing anything" control for the whole
        // workstream. Everything the handling model adds, switched off in one press.
        ("6", "HANDLING OFF — no lean, no curve, drift as BIKE-0 shipped it", Default with
        {
            LeanMaxDeg = 0f,
            LeanExaggeration = 0f,
            // BIKE-4B: the wobble is part of "everything the handling model adds", so the control
            // has to switch it off too or preset 6 stops being a control.
            WobbleAmplitudeDeg = 0f,
            TurnLowSpeedMul = 1f,
            TurnHighSpeedMul = 1f,
            DriftTier1BoostMps = 0f,
            DriftTier2BoostMps = 0f,
            DriftTier3BoostMps = 0f,
        }),
    };

    /// <summary>The preset a digit names, or null. Same shape as <c>BikePresets.ForKey</c>.</summary>
    public static (string Key, string Name, BikeHandlingTuning Tuning)? ForKey(int digit)
    {
        string want = digit.ToString(System.Globalization.CultureInfo.InvariantCulture);
        foreach (var p in Presets)
            if (p.Key == want)
                return p;
        return null;
    }

    /// <summary>What the handling model reads. Lab-only; no guard, no session to protect.</summary>
    public static BikeHandlingTuning Current { get; set; } = Default;
}
