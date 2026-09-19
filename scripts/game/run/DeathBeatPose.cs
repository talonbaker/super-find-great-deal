using Godot;

namespace Sail.Game.Run;

/// <summary>
/// <b>What a dead body does between the kill and the respawn — the whole of it, with no scene tree
/// in it.</b>
///
/// <para>Split out of <see cref="RespawnService"/> for exactly the reason
/// <see cref="DrowningClock"/> is: the interesting failure is arithmetic. A beat that rises
/// without bound, a beat that spins so many turns it reads as a tornado, a beat that never
/// arrives anywhere — all three are statements about a curve, and a curve is worth proving
/// exhaustively without a scene, a server and three processes. <c>DeathBeatPoseTests</c> proves
/// this to the boundary; <c>BubbleTestSelfTest</c>'s drowning probe then measures the same
/// numbers end to end on a real body in real water.</para>
///
/// <para><b>Why this file exists at all (W7-4).</b> Talon, 2026-08-30, note 6:
/// <i>"When the player 'drowns', the body floats and spirals upward, to the surface, into the
/// sky, it looks like a tornado took them up."</i> The beat it replaces lofted the avatar's
/// visual 4.2 m on a sine arc and spun it 4.5 whole revolutions (1.5 in X, 3.0 in Y) over two
/// seconds, then teleported the body away on the same frame the arc returned to rest — so the
/// descent existed in the code and never once existed on screen.
/// <c>THRILL-BIBLE.md</c> §4.3 names that exact failure in its own words:
/// <i>"'We were teleported into the sky' is a bug."</i></para>
///
/// <para><b>The three rules this curve obeys, and they are constitutive rather than tuning.</b>
/// <list type="number">
/// <item><b>It arrives and it holds.</b> Every cause reaches a terminal pose strictly before the
/// beat ends and does not move again (<see cref="SettleU"/>). A death the player can name is a
/// death that ends somewhere; an excursion that merely returns to rest reads as something
/// happening TO the body rather than as the body being dead.</item>
/// <item><b>It never trends upward.</b> Up is the direction of being taken, and the register law
/// (<c>/CLAUDE.md</c>, <c>docs/CANON.md</c>) is absolute that nobody is ever taken. The vertical
/// term is clamped by <see cref="MaxRiseM"/> — the small clearance a body needs to lie flat
/// instead of pivoting through the floor — and for a drowning it is never positive at all.</item>
/// <item><b>It topples; it does not spin.</b> Total rotation is a quarter turn of pitch plus a
/// small roll, eased once and then still. <see cref="MaxRevolutions"/> is the bound.</item>
/// </list></para>
///
/// <para><b>Presentation only, and identical on every peer.</b> The pose is a pure function of
/// (cause, normalised beat time). The cause and the moment of death are replicated
/// (<c>RespawnService.BroadcastDied</c>); nothing here is simulated, sampled from local physics,
/// or fed back into the body's authoritative transform. Two clients handed the same cause and the
/// same <c>u</c> compute the same pose bit for bit, which is what "the body other players see is
/// the same body" reduces to for something that is not gameplay state.</para>
/// </summary>
public static class DeathBeatPose
{
    /// <summary>Normalised beat time at which the topple has finished and the pose is held still.
    /// Strictly less than 1 so there is a visible hold — the hold is what makes the beat read as
    /// arrival rather than as an excursion caught mid-flight by the respawn.</summary>
    public const float SettleU = 0.55f;

    /// <summary>How far a body sinks after drowning, metres, by <see cref="SettleU"/>. Negative Y,
    /// and the only vertical motion a drowning gets: the lake keeps what it took, which is the
    /// most legible possible read of the cause and the exact opposite of the one Talon saw.</summary>
    public const float DrownSinkM = 0.90f;

    /// <summary>Clearance a body pitched flat needs so it lies ON the ground rather than pivoting
    /// through it — the same trick, and very nearly the same number, as
    /// <c>AvatarVisual.SetIncapacity</c>'s knocked-out lift. This is the ONLY positive vertical
    /// term in the whole beat and it is the bound <see cref="MaxRiseM"/> asserts.</summary>
    public const float LieFlatLiftM = 0.18f;

    /// <summary>Hard ceiling on the beat's upward excursion, metres, over every cause and every
    /// <c>u</c>. Asserted exhaustively rather than trusted: the defect this file replaces was a
    /// 4.2 m arc that nobody had ever put a bound on.</summary>
    public const float MaxRiseM = LieFlatLiftM;

    /// <summary>Hard ceiling on total rotation, in whole turns, over every cause and every
    /// <c>u</c>. The beat this replaces spent 4.5; a topple needs a quarter turn and a lean.</summary>
    public const float MaxRevolutions = 0.35f;

    /// <summary>Terminal pitch: a quarter turn onto the back. Negative X matches
    /// <c>AvatarVisual.SetIncapacity</c>'s knocked-out convention, deliberately — the repo has
    /// exactly one ratified "comic body lying down" pose and a second one would be a second
    /// vocabulary to keep in register. What separates death from a knock-out is not the angle: a
    /// knocked-out body keeps its orbiting birds and stays where it fell, a dead one settles and
    /// then is not there any more.</summary>
    public const float TopplePitchRad = -Mathf.Pi * 0.5f;

    /// <summary>Terminal roll — about fifteen degrees. A body that lands square reads as a placed
    /// prop; a body that lands slightly wrong reads as a body. This is the entire budget for
    /// "chosen comic" in the pose, and it is what the excursion it replaces was trying and
    /// failing to buy with revolutions.</summary>
    public const float ToppleRollRad = 0.26f;

    /// <summary>The pose, in the visual's own local space: an offset to add to wherever the render
    /// layer already puts the mesh, and an euler rotation to add to its rest orientation.</summary>
    public readonly struct Pose
    {
        public Pose(Vector3 offset, Vector3 rotation)
        {
            Offset = offset;
            Rotation = rotation;
        }

        /// <summary>Local-space translation. <c>Offset.Y</c> is never greater than
        /// <see cref="MaxRiseM"/>, for any cause, at any <c>u</c>.</summary>
        public readonly Vector3 Offset;

        /// <summary>Local-space euler rotation to add to the rest pose, radians.</summary>
        public readonly Vector3 Rotation;

        /// <summary>The pose that changes nothing — a body standing exactly as it was.</summary>
        public static Pose Rest => new(Vector3.Zero, Vector3.Zero);
    }

    /// <summary>
    /// Sample the beat. <paramref name="u"/> is normalised beat time and is clamped, so a caller
    /// that overshoots on a long frame gets the terminal pose rather than an extrapolation — the
    /// class of bug that turns "a body lies down" into "a body keeps rotating".
    ///
    /// <para><b>Only NaN is meaningless; the infinities are not.</b> A first cut treated every
    /// non-finite <c>u</c> as 0 and <c>DeathBeatPoseTests.OvershootClampsToTheTerminalPose</c>
    /// caught it on the first run: <c>+∞</c> is a time PAST the end of the beat and must give the
    /// terminal pose, exactly as 1.5 does — snapping it back to rest would put a dead body on its
    /// feet. So NaN alone is mapped to 0 and the clamp then handles both infinities correctly.
    /// A NaN must never reach a transform regardless: a NaN transform silently removes a whole
    /// subtree from the renderer, which looks precisely like a body being taken.</para>
    /// </summary>
    public static Pose Sample(RespawnCause cause, float u)
    {
        if (float.IsNaN(u)) u = 0f;
        u = Mathf.Clamp(u, 0f, 1f);

        // One eased ramp drives everything, and it saturates at SettleU. Smoothstep rather than a
        // linear ramp so the body has weight going over and stops without a corner; saturating
        // rather than oscillating is rule 1 — the pose arrives.
        float t = Mathf.Clamp(u / SettleU, 0f, 1f);
        float e = t * t * (3f - 2f * t);

        // Vertical. A drowning sinks and nothing else does; every other cause gets the flat-lie
        // clearance and no more. Note there is no cause whose vertical term is a round trip:
        // whatever the body does with its height, it is still doing it when the beat ends.
        float y = cause == RespawnCause.Drowned
            ? -DrownSinkM * e
            : LieFlatLiftM * e;

        return new Pose(
            new Vector3(0f, y, 0f),
            new Vector3(TopplePitchRad * e, 0f, ToppleRollRad * e));
    }

    /// <summary>Total rotation this beat spends, in whole turns — the number Talon's "spirals"
    /// is a description of. Exposed so the bound is asserted against the same arithmetic the
    /// beat runs rather than against a literal copied into a test.</summary>
    public static float RevolutionsFor(RespawnCause cause)
    {
        Pose end = Sample(cause, 1f);
        return (Mathf.Abs(end.Rotation.X) + Mathf.Abs(end.Rotation.Y) + Mathf.Abs(end.Rotation.Z))
               / Mathf.Tau;
    }
}
