using Godot;

namespace MpFoundation.Game.Sandbox.Anim;

/// <summary>How much animation work one body is worth this frame.</summary>
public enum AnimationLodTier
{
    /// <summary><b>Entry:</b> the body is within <see cref="AvatarAnimationLod.FullRateM"/>, or it is
    /// the local player at any distance. <b>Behaviour:</b> the tree advances every rendered frame.
    /// <b>Exit:</b> the body passes the half-rate distance.</summary>
    Full = 0,

    /// <summary><b>Entry:</b> past <see cref="AvatarAnimationLod.FullRateM"/>.
    /// <b>Behaviour:</b> the tree advances every other frame, with the skipped frame's delta carried
    /// into the next advance so the clip still runs at the right SPEED — a halved rate would be a
    /// body walking in slow motion, which reads as a different animation rather than as a cheaper
    /// one. <b>Exit:</b> back inside, or past the freeze distance.</summary>
    HalfRate = 1,

    /// <summary><b>Entry:</b> past <see cref="AvatarAnimationLod.FrozenM"/>.
    /// <b>Behaviour:</b> the tree stops advancing and the body holds its last evaluated pose; the
    /// procedural modifiers (lean, squash, crouch) stop with it. <b>Exit:</b> back inside the freeze
    /// distance, which re-advances from wherever the clip was — phase is carried, never reset, so a
    /// body that walks back into view does not restart its stride.</summary>
    Frozen = 2,
}

/// <summary>
/// <b>Animation LOD: what a body 40 m away is allowed to cost.</b> The brief's §11 requirement,
/// tuned for the case canon fact 15 (Talon, 2026-08-21) names as the one to design for — <b>four
/// players</b> — with six stated as the verified ceiling rather than the target, and solo supported
/// and working.
///
/// <para><b>The distances, and where they come from.</b> They are not round numbers picked for
/// tidiness; each is the point past which the thing the tier drops stops being perceptible on the
/// shipped body:</para>
/// <list type="bullet">
/// <item><see cref="FullRateM"/> = 18 m. The gumdrop's whole trunk is 1.20 m tall and its arm swing
/// sweeps ~0.29 rad about a 0.38 m arm, so the largest per-frame limb displacement at sprint is
/// about 11 mm. On a 1080p 70° viewport, 11 mm at 18 m subtends under a third of a pixel — the frame
/// a half-rate advance skips cannot be resolved.</item>
/// <item><see cref="FrozenM"/> = 35 m. Past this the whole body is under ~40 px tall and the
/// stride's peak silhouette change is around 2 px. This is also comfortably outside the sight range
/// where contrast reads at all at night (memory: sight range is measured imperceptible past
/// ~15 m), so the frozen tier is invisible in the mode where most bodies are far away.</item>
/// </list>
///
/// <para><b>The local player is never LODed.</b> <see cref="TierFor"/> takes an <c>isLocal</c> flag
/// rather than relying on the distance being zero, because a third-person camera can legitimately be
/// pushed several metres back and "the body I am controlling stutters" is not a saving worth
/// making.</para>
///
/// <para><b>Hysteresis, for the same reason <see cref="Gear"/> has it.</b> A body hovering exactly on
/// a boundary would flip tiers every frame, and a tier flip is a visible change in how a stride
/// moves. Enter and exit are different numbers, never one threshold.</para>
///
/// <para>Engine-free apart from <c>Godot.Mathf</c>; asserted in
/// <c>AvatarAnimationLodTests</c>.</para>
/// </summary>
public static class AvatarAnimationLod
{
    /// <summary>Beyond this many metres a body drops to <see cref="AnimationLodTier.HalfRate"/>.</summary>
    public const float FullRateM = 18f;

    /// <summary>Beyond this many metres a body drops to <see cref="AnimationLodTier.Frozen"/>.</summary>
    public const float FrozenM = 35f;

    /// <summary>How far inside a boundary a body must come back before it is promoted again, metres.
    /// 2 m — roughly one and a half body lengths, enough that ordinary jogging cannot cross it twice
    /// in a frame.</summary>
    public const float HysteresisM = 2f;

    /// <summary>
    /// The tier for one body this frame.
    /// </summary>
    /// <param name="distanceM">Camera-to-body distance.</param>
    /// <param name="isLocal">True for the body this client is controlling — never LODed.</param>
    /// <param name="previous">The tier this body had last frame, for the hysteresis.</param>
    public static AnimationLodTier TierFor(float distanceM, bool isLocal, AnimationLodTier previous)
    {
        if (isLocal)
            return AnimationLodTier.Full;

        float d = float.IsFinite(distanceM) ? Mathf.Abs(distanceM) : 0f;

        // Promotion needs the body to come HysteresisM inside the boundary it last crossed;
        // demotion happens at the boundary itself. Asymmetric on purpose: being slow to cheapen a
        // body that is walking away costs one frame of work, and being quick to cheapen one that is
        // walking toward you costs a visible stutter.
        return previous switch
        {
            AnimationLodTier.Full =>
                d > FrozenM ? AnimationLodTier.Frozen
                : d > FullRateM ? AnimationLodTier.HalfRate
                : AnimationLodTier.Full,

            AnimationLodTier.HalfRate =>
                d > FrozenM ? AnimationLodTier.Frozen
                : d < FullRateM - HysteresisM ? AnimationLodTier.Full
                : AnimationLodTier.HalfRate,

            _ =>
                d < FrozenM - HysteresisM
                    ? (d < FullRateM - HysteresisM ? AnimationLodTier.Full : AnimationLodTier.HalfRate)
                    : AnimationLodTier.Frozen,
        };
    }

    /// <summary>How many rendered frames pass between tree advances at <paramref name="tier"/>.
    /// Zero means the tree does not advance at all.</summary>
    public static int FramesPerAdvance(AnimationLodTier tier) => tier switch
    {
        AnimationLodTier.Full => 1,
        AnimationLodTier.HalfRate => 2,
        _ => 0,
    };

    /// <summary>
    /// <b>The budget, stated as the number this was tuned against.</b> Cost per body per second is
    /// <c>tree advances × tracks</c>. The library animates six nodes and Godot's importer pads every
    /// clip to six tracks (ANIM-M2 §7 — <c>remove_immutable_tracks</c> does not remove them), and the
    /// tree evaluates a base clip plus one filtered upper-body override, so a full-rate body at
    /// 60 fps costs 60 × 12 = <b>720 track evaluations per second</b>.
    ///
    /// <para>At four players all in view and all full-rate that is 2 880/s; at six it is 4 320/s.
    /// Both are trivial against the GTX 970 Forward+ floor — the interesting number is not the
    /// arithmetic but the fact that this stays true as the roster grows, which is what the tiers
    /// buy.</para>
    /// </summary>
    public static int TrackEvaluationsPerSecond(int bodiesAtFullRate, int bodiesAtHalfRate, float fps)
    {
        const int tracksPerAdvance = 12; // six padded tracks × (base + one filtered override)
        return (int)(fps * tracksPerAdvance * (bodiesAtFullRate + bodiesAtHalfRate * 0.5f));
    }
}
