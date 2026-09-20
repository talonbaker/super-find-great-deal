namespace MpFoundation.Game.Sandbox.Anim;

/// <summary>
/// <b>The crossfade table, as named code rather than as a convention.</b> The brief made this a
/// requirement in so many words: <i>"0.15 s everywhere; 0 s into sudden or forceful states (grab,
/// stagger, hit reactions, knock-out, net-close). This was an informal convention; make it a named
/// constant."</i>
///
/// <para><b>One table, one direction.</b> The fade is a property of the state being entered, not of
/// the pair, and that is deliberate rather than a simplification: a pair table is
/// <c>n²</c> entries of which all but a handful say "the default", and the handful that do not all
/// say the same thing for the same reason — the state is sudden. Encoding it per-destination means a
/// new state declares its own abruptness once and cannot be given two different answers by two
/// different origins.</para>
///
/// <para><b>Why zero and not "very short".</b> A blend is a lie about what the body did. Easing into
/// a knock-out over 0.15 s renders a body that is falling over gracefully — the frames say the hit
/// was absorbed, and the whole point of the hit is that it was not. Zero is the honest number and it
/// is also the cheap one. It is NOT a tuning knob: raising it is a design change to what a hit
/// reads as, and it belongs to <c>/direct</c>, not here.</para>
///
/// <para><b>Engine-free.</b> Covered by <c>AnimationCrossfadeTests</c> with no Godot in the
/// room.</para>
/// </summary>
public static class AnimationCrossfade
{
    /// <summary>The default transition, seconds. Everything that is not sudden takes this.</summary>
    public const float DefaultSec = 0.15f;

    /// <summary>The transition into a sudden or forceful state, seconds. Exactly zero — see the
    /// class remarks for why this is not "a very small number".</summary>
    public const float SuddenSec = 0f;

    /// <summary>True when entering <paramref name="state"/> is a sudden or forceful event, so the
    /// body must arrive in it on one frame. The list is the brief's, mapped onto what the clip
    /// library actually carries: stagger, knock-out and the net-close (the swing's strike, which is
    /// an upper-body override — see <see cref="OverrideSecondsFor"/>).</summary>
    public static bool IsSudden(AvatarActionState state) =>
        state is AvatarActionState.Stagger or AvatarActionState.KnockOut;

    /// <summary>Seconds to blend when entering <paramref name="state"/>.</summary>
    public static float SecondsFor(AvatarActionState state) =>
        IsSudden(state) ? SuddenSec : DefaultSec;

    /// <summary>True when entering <paramref name="hold"/> is sudden. The net-close is: the stroke
    /// is a strike, and easing the arm into it over 0.15 s costs the swing exactly the snap that
    /// makes it read as a swing. A carry or a ready-pose is a deliberate, unhurried gesture and
    /// takes the default.</summary>
    public static bool IsSudden(AvatarHoldState hold) => hold == AvatarHoldState.NetSwing;

    /// <summary>Seconds to blend when entering the upper-body override for
    /// <paramref name="hold"/>.</summary>
    public static float OverrideSecondsFor(AvatarHoldState hold) =>
        IsSudden(hold) ? SuddenSec : DefaultSec;

    /// <summary>
    /// <b>Inside the locomotion blend space there is no crossfade at all, and that is a
    /// measurement rather than a convention.</b>
    ///
    /// <para>Blending <c>Walk</c> into <c>Run</c> on a weight was measured, off the shipped
    /// <c>Greybox.glb</c> bytes, to move the planted foot up to <b>99.5 mm</b> off its line at
    /// weight 0.25 — against 0.09 mm for pure Walk and 1.09 mm for pure Run
    /// (<c>ClipTimeWarpTests.InterpolatedWalkRunBlend_SkatesFarWorseThanEitherEndpoint</c>). That is
    /// the "vibrating while skating" defect MOVE-1 removed, reintroduced by a blend weight. The
    /// cause is the duty-factor mismatch ANIM-M2 flagged and did not assume away: Walk's stance is
    /// 35.4% of its cycle and Run's is 14.1%, so through the overlap one clip's foot is planted
    /// while the other's is already swinging, and the average of the two is a foot doing
    /// neither.</para>
    ///
    /// <para>The library's two gait clips share an identical 0.6859 m foot sweep — forced, not
    /// maintained: above ~1.44 m/s the wanted reach exceeds what a 0.548 m leg can reach and both
    /// clips sit on the same <c>leg × sin(MaxLegSwingRad)</c> cap. So they differ in cadence and
    /// duty and NOT in amplitude, which is exactly the condition under which a phase-carrying hard
    /// switch is invisible and a weighted blend is not.</para>
    /// </summary>
    public const float LocomotionBlendSec = 0f;
}
