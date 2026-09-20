namespace MpFoundation.Game.Sandbox.Anim;

/// <summary>
/// The one switch that decides whether the played body's base pose comes from the fourteen authored
/// clips in <c>Greybox.glb</c> or from the shipped procedural gait.
///
/// <para><b>Gated OFF by default, and the reason is risk separation rather than doubt.</b> ANIM-M3
/// changes two unrelated things at once: the greybox row stops being harvested and becomes the
/// imported hierarchy (<see cref="AvatarVisual"/>'s <c>Build.AuthoredRig</c>), and a clip library
/// starts driving the base pose. The first is structural and is asserted by
/// <c>GreyboxAssetContractTests</c> and <c>Run-SandboxTest</c> — it ships on. The second replaces
/// MOVE-1's and SKID-1's <i>ratified</i> gait, which is asserted by those same suites and was tuned against Talon's eyes over two packets. Landing both in one
/// default is exactly the shape <c>AvatarVisual</c>'s own roster comment refuses — <i>"pulling them
/// in the same pass would put two unrelated risks in one change"</i>.</para>
///
/// <para><b>It is also the instrument the migration exists to provide.</b> The brief's own closing
/// line: <i>"'Feels right' needs Talon's eyes. Your job is to make that judgment possible — a
/// scrubbable timeline and a build he can open — not to make it for him."</i> One build, one flag,
/// the procedural gait and the authored one side by side in the same session. A default flip is a
/// one-line change here the day he says which.</para>
///
/// <para><b>Written in exactly one place in shipping code</b> — <c>Boot._Ready</c>, honouring
/// <c>--authored-clips</c> — because disablement scattered across call sites is how a mechanic
/// becomes impossible to switch back on. It is
/// process-wide rather than per-scene because the gameplay scene can be entered more than once in a
/// session and the body must not change between entries.</para>
///
/// <para><b>Presentation only, in both positions.</b> Nothing downstream of this flag decides
/// anything: no simulation reads it, no snapshot carries it, and two players in one session with
/// different values still agree on every outcome (canon fact 4, the parity law). What differs is
/// which pose a body is drawn in — and the readouts other systems consume (<c>CadenceHz</c>,
/// <c>GaitPhase</c>, <c>DutyFactor</c>, <c>StanceReachM</c>, <c>KneeBendRad</c>, …) keep being
/// written in both.</para>
/// </summary>
public static class AvatarClipFlag
{
    /// <summary>False: the procedural gait drives the base pose exactly as it shipped, the
    /// <c>AnimationTree</c> is never constructed, and nothing in the clip library is evaluated.
    /// Settable rather than <c>const</c> so the launch hook and the capture scripts can drive it
    /// without a recompile. No shipping code path other than <c>Boot</c> writes it.</summary>
    public static bool AuthoredClips { get; set; }
}
