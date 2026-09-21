using Godot;
using MpFoundation.Net;

namespace MpFoundation.Game;

/// <summary>
/// <b>This game's own walking speed</b> (FEEL-1, 2026-09-20). Talon, after his first ride of the
/// supermarket: <i>"slow down the players' movement overall; no sprint button; they won't be
/// moving far enough for it to matter; keep jumping."</i>
///
/// <para><b>2.4 m/s, derived against the level rather than picked off a feel dial.</b> Talon named
/// the band — a browse is 2.0–2.5 — and the room decides inside it: the search room's aisles are
/// 9.2 m long and its walkways 1.6 m wide, so 2.4 m/s walks an aisle in 3.8 s and crosses a
/// walkway in two thirds of a second. Fast enough that hunting one wrong object among 130 shelved
/// products is not a chore; slow enough that you read the shelf you are passing, which is the
/// whole verb of the seeking half. It is 0.63× the 3.8 m/s MOVE-8 ruled for a forest, and every
/// gear declared as a FRACTION of the top speed comes down with it — the walk modifier
/// (Left Ctrl, <c>LocomotionProfile.WalkFraction</c>) becomes 1.08 m/s, an actual browse, and skid
/// and slide entry follow.</para>
///
/// <para><b>Why this is applied through <see cref="MotorTuning.TryApply"/> and does NOT move
/// <see cref="MotorTuning.Default"/>.</b> That default is MOVE-8's ruling — Talon at a keyboard on
/// 2026-08-28, thirteen rows — and roughly thirty tests pin the jump arc, the skid distances, the
/// gear ladder, the cadence and the wind curve that were MEASURED at it. Editing the row would not
/// slow this game down so much as delete that record, and it would leave every one of those tests
/// asserting a literal nobody had re-measured. <c>Current</c> is what the motor actually reads and
/// <c>TryApply</c> is its documented single writer — the seam MOVE-4b shipped and MOVE-8 spent —
/// so the supermarket states its own body here and the foundation keeps its. A future game off the
/// same foundation states its own beside this one.</para>
///
/// <para><b>Sprint is gone at BOTH ends, and it takes both to be gone rather than hidden.</b> The
/// <c>sprint</c> action is removed from <c>project.godot</c> and <c>HumanInputSampler</c> no longer
/// samples it or the double-tap latch, so no key asks; and <see cref="Tuning"/> sets the
/// multiplier to its no-op 1, so a bot, a replay or a doctored client that sets
/// <c>MoveIntent.Sprint</c> gets exactly the walk. The knob itself is left in the table: deleting a
/// numbered row renumbers every knob after it, and 1.0 is an honest no-op of the same shape
/// <c>ChainBonusMps</c> has carried since MOVE-7.</para>
/// </summary>
public static class BrowsePace
{
    /// <summary>The shipped top speed, m/s. See the class doc for the derivation.</summary>
    public const float DefaultWalkSpeedMps = 2.4f;

    /// <summary>The supermarket's body: the foundation's, with the top speed brought down to a
    /// browse and sprint made a no-op. Two rows, and no third — the test asserts the whole record
    /// rather than row by row so a quiet third edit is a red.</summary>
    public static MotorTuning Tuning(float walkSpeedMps) => MotorTuning.Default with
    {
        MoveSpeed = walkSpeedMps,
        SprintMultiplier = 1f,
    };

    /// <summary>
    /// A <c>--walk-speed</c> request, made safe. Anything the knob table itself would refuse —
    /// NaN, or outside <c>MotorTuningKnobs.MoveSpeed</c>'s own window — falls back to
    /// <see cref="DefaultWalkSpeedMps"/> rather than being clamped into range.
    ///
    /// <para><b>Refused, not clamped, and that is the safe direction.</b> This is a MOTOR
    /// constant: every peer in a session must hold the same one or the owner's prediction and the
    /// server's authority disagree about where a body is. A typo that silently becomes the
    /// nearest legal value is a session that desyncs quietly; a typo that becomes the default is a
    /// session that plays at the shipped speed, which is what the other peers are doing
    /// anyway.</para>
    /// </summary>
    public static float SanitizeWalkSpeed(float asked)
    {
        if (!float.IsFinite(asked))
            return DefaultWalkSpeedMps;
        MotorKnob knob = MotorTuningKnobs.MoveSpeed;
        return asked >= knob.Min && asked <= knob.Max ? asked : DefaultWalkSpeedMps;
    }

    /// <summary>
    /// Put this game's body on the motor, once, at boot. Called from <c>Boot._Ready</c> — before
    /// any world is built and before any peer connects, because <see cref="MotorTuning.TryApply"/>
    /// refuses while a session is live (and it is right to: a motor constant that changes
    /// mid-session is a desync generator).
    ///
    /// <para>Every process in a session runs this with the same default, so a plain launch can
    /// never disagree. <c>--walk-speed</c> is a ride-session knob and has to be given to EVERY
    /// process of that session; the printed line below is what a suite or a log reader checks
    /// that against.</para>
    /// </summary>
    public static void ApplyAtBoot(float walkSpeedMps)
    {
        float speed = SanitizeWalkSpeed(walkSpeedMps);
        if (!MotorTuning.TryApply(Tuning(speed), out string refusal))
        {
            GD.PushWarning($"[motor] browse pace refused: {refusal}");
            return;
        }
        GD.Print($"[motor] browse pace {speed:F2} m/s, sprint disabled"
            + (Mathf.IsEqualApprox(speed, DefaultWalkSpeedMps) ? "" : " (--walk-speed override)"));
    }
}
