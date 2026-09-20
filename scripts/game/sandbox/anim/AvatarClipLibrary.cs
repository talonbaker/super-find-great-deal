namespace MpFoundation.Game.Sandbox.Anim;

/// <summary>
/// <b>What the played body can be doing, as far as the clip library is concerned.</b> One value per
/// authored full-body clip, plus <see cref="Locomotion"/> which stands for the 1D blend space
/// (Idle/Walk/Run) rather than for one clip.
///
/// <para><b>Not replicated, and it must never become so</b> (the parity law, <c>/CLAUDE.md</c> canon
/// fact 4). Every value here is DERIVED on each client from state that already rides the wire —
/// grounded, vertical velocity, ground speed, skid remaining, incapacity, swing phase. A field for
/// this on the snapshot would be a second, contradictable copy of facts the peer already has, and
/// it would let a client assert a pose. See <see cref="AvatarActionStates.Derive"/>.</para>
///
/// <para>Ordered so that a higher value wins when two are simultaneously true — the resolution order
/// in <see cref="AvatarActionStates.Derive"/> reads down this list.</para>
/// </summary>
public enum AvatarActionState
{
    /// <summary><b>Entry:</b> on the floor, not skidding, not down. <b>Behaviour:</b> the
    /// Idle/Walk/Run blend space, time-warped from ground speed. <b>Exit:</b> any other row.</summary>
    Locomotion = 0,

    /// <summary><b>Entry:</b> the turnaround brake begins (<c>MoveState.SkidRemaining &gt; 0</c>).
    /// <b>Behaviour:</b> the <c>Skid</c> plant pose. <b>Exit:</b> the brake ends.</summary>
    Skid = 1,

    /// <summary><b>Entry:</b> the frame the body leaves the ground with upward velocity.
    /// <b>Behaviour:</b> the <c>Jump_Launch</c> one-shot. <b>Exit:</b> the clip ends → Airborne.</summary>
    JumpLaunch = 2,

    /// <summary><b>Entry:</b> airborne past the launch clip. <b>Behaviour:</b> the looping
    /// <c>Jump_Air</c> tuck. <b>Exit:</b> the body touches down → Land.</summary>
    Airborne = 3,

    /// <summary><b>Entry:</b> the frame the body regains the floor from a fall.
    /// <b>Behaviour:</b> the <c>Jump_Land</c> one-shot. <b>Exit:</b> the clip ends → Locomotion.</summary>
    Land = 4,

    /// <summary><b>Entry:</b> a replicated stagger. <b>Behaviour:</b> the <c>Stagger</c> one-shot,
    /// entered with NO crossfade. <b>Exit:</b> the clip ends.</summary>
    Stagger = 5,

    /// <summary><b>Entry:</b> <c>MoveState.Incapacity</c> becomes non-none. <b>Behaviour:</b> the
    /// <c>KnockOut</c> one-shot, entered with NO crossfade, then held on its last frame while the
    /// failure skin owns the body. <b>Exit:</b> recovery.</summary>
    KnockOut = 6,
}

/// <summary>
/// <b>The upper-body override, per hold-state.</b> Layered OVER whatever
/// <see cref="AvatarActionState"/> is playing, on the arm channels only — never a second full-body
/// cycle per item (the brief's Scope section).
/// </summary>
public enum AvatarHoldState
{
    /// <summary>Nothing in hand. <c>Hold_Empty</c>.</summary>
    Empty = 0,

    /// <summary>The net held up ready. <c>Hold_NetReady</c>.</summary>
    NetReady = 1,

    /// <summary>A net stroke in flight. <c>Hold_NetSwing</c>, seeked from the swing's own
    /// replicated progress — see <see cref="ClipTimeAnchor"/>.</summary>
    NetSwing = 2,

    /// <summary>Carrying by a handle (CARRY-1). <c>Carry_Handle</c>.</summary>
    CarryHandle = 3,

    /// <summary>Carrying an armful (CARRY-1). <c>Carry_Armful</c>.</summary>
    CarryArmful = 4,
}

/// <summary>
/// <b>The names of the fourteen authored clips, in one place, spelled exactly as ANIM-M2 exported
/// them.</b> Engine-free constants so the mapping is assertable in xUnit against the shipped bytes
/// rather than discovered at runtime by a track that resolves to nothing.
///
/// <para><b>An unresolved <c>AnimationPlayer</c> track is silent in Godot</b> — that is ANIM-M0
/// §3.1(b)'s whole argument against a name table, and it applies to clip NAMES for exactly the same
/// reason. <c>AvatarClipContractTests</c> reads <c>Greybox.glb</c> and asserts every name below is
/// in it, so a rename in Blender is a red test rather than a body that stops moving.</para>
/// </summary>
public static class AvatarClipNames
{
    public const string Idle = "Idle";
    public const string Walk = "Walk";
    public const string Run = "Run";
    public const string JumpLaunch = "Jump_Launch";
    public const string JumpAir = "Jump_Air";
    public const string JumpLand = "Jump_Land";
    public const string Skid = "Skid";
    public const string HoldEmpty = "Hold_Empty";
    public const string HoldNetReady = "Hold_NetReady";
    public const string HoldNetSwing = "Hold_NetSwing";
    public const string CarryHandle = "Carry_Handle";
    public const string CarryArmful = "Carry_Armful";
    public const string KnockOut = "KnockOut";
    public const string Stagger = "Stagger";

    /// <summary>Every clip the library is contracted to carry.</summary>
    public static readonly string[] All =
    {
        Idle, Walk, Run, JumpLaunch, JumpAir, JumpLand, Skid,
        HoldEmpty, HoldNetReady, HoldNetSwing, CarryHandle, CarryArmful, KnockOut, Stagger,
    };

    /// <summary>The full-body clip an action state plays. <see cref="AvatarActionState.Locomotion"/>
    /// has no single answer — it is the blend space — and returns null.</summary>
    public static string? FullBodyClipFor(AvatarActionState state) => state switch
    {
        AvatarActionState.Locomotion => null,
        AvatarActionState.Skid => Skid,
        AvatarActionState.JumpLaunch => JumpLaunch,
        AvatarActionState.Airborne => JumpAir,
        AvatarActionState.Land => JumpLand,
        AvatarActionState.Stagger => Stagger,
        AvatarActionState.KnockOut => KnockOut,
        _ => null,
    };

    /// <summary>The upper-body override a hold-state plays. Always a real clip: "empty-handed" is a
    /// pose too, and leaving it unauthored is what makes an idle character's arms read as forgotten
    /// rather than as at rest.</summary>
    public static string OverrideClipFor(AvatarHoldState hold) => hold switch
    {
        AvatarHoldState.NetReady => HoldNetReady,
        AvatarHoldState.NetSwing => HoldNetSwing,
        AvatarHoldState.CarryHandle => CarryHandle,
        AvatarHoldState.CarryArmful => CarryArmful,
        _ => HoldEmpty,
    };

    /// <summary>True for a clip that plays once and holds its last frame rather than looping.
    /// Mirrors the loop column of ANIM-M2's clip table, which is also what
    /// <c>Greybox.glb.import</c> sets per clip — asserted against the import file by
    /// <c>AvatarClipContractTests</c> so the two cannot drift.</summary>
    public static bool IsOneShot(string clip) => clip switch
    {
        JumpLaunch or JumpLand or Skid or HoldNetSwing or KnockOut or Stagger => true,
        _ => false,
    };
}

/// <summary>
/// <b>Deriving the action state from replicated facts, and nothing else.</b> Pure and engine-free so
/// the derivation is a test rather than an observation, and static so there is exactly one of it —
/// every peer rendering the same body runs this same function over the same snapshot and therefore
/// resolves the same state, which is what "they converge without syncing anything animation-shaped"
/// means concretely.
/// </summary>
public static class AvatarActionStates
{
    /// <summary>Resolve the action state for one rendered frame.</summary>
    /// <param name="incapacitated">Replicated: <c>MoveState.Incapacity</c> is non-none.</param>
    /// <param name="staggering">Replicated: a stagger is in flight.</param>
    /// <param name="grounded">Replicated: <c>MoveState.Grounded</c>.</param>
    /// <param name="skidding">Replicated: <c>AvatarMotor.IsSkidding(state)</c>.</param>
    /// <param name="verticalVelocity">Replicated: <c>MoveState.Velocity.Y</c>.</param>
    /// <param name="previous">The state this body was in last frame — the ONLY local memory, and it
    /// carries no information a peer could disagree about because every transition into it is a
    /// function of the replicated facts above.</param>
    /// <param name="stateElapsedSec">How long <paramref name="previous"/> has been running, so the
    /// two one-shots (launch, land) can retire themselves.</param>
    /// <param name="launchSec">Length of <c>Jump_Launch</c>.</param>
    /// <param name="landSec">Length of <c>Jump_Land</c>.</param>
    public static AvatarActionState Derive(
        bool incapacitated, bool staggering, bool grounded, bool skidding,
        float verticalVelocity, AvatarActionState previous, float stateElapsedSec,
        float launchSec, float landSec)
    {
        // Read down the enum. The order is the resolution order, and it is the order of severity:
        // being down outranks everything, a stagger outranks the gait, and the gait is what is left.
        if (incapacitated)
            return AvatarActionState.KnockOut;
        if (staggering)
            return AvatarActionState.Stagger;

        if (!grounded)
        {
            // The launch clip retires itself into the air loop; a body that left the ground already
            // falling (walked off a ledge) never plays it, which is correct — there was no push-off.
            if (previous == AvatarActionState.JumpLaunch && stateElapsedSec < launchSec)
                return AvatarActionState.JumpLaunch;
            if (previous is AvatarActionState.Airborne or AvatarActionState.JumpLaunch)
                return AvatarActionState.Airborne;
            return verticalVelocity > 0f ? AvatarActionState.JumpLaunch : AvatarActionState.Airborne;
        }

        // Grounded. The landing one-shot only fires out of a real air state, so touching down after a
        // 2 cm step-off does not interrupt a stride with a landing pose.
        if (previous is AvatarActionState.Airborne or AvatarActionState.JumpLaunch)
            return AvatarActionState.Land;
        if (previous == AvatarActionState.Land && stateElapsedSec < landSec)
            return AvatarActionState.Land;

        return skidding ? AvatarActionState.Skid : AvatarActionState.Locomotion;
    }
}
