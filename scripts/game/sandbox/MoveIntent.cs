using Godot;

namespace MpFoundation.Game.Sandbox;

/// <summary>
/// One tick of avatar input, already resolved to world space. This is the seam the
/// future net layer plugs into: a remote peer's replicated input (or a server-driven
/// correction) becomes just another IIntentSource. Nothing in the sandbox knows or
/// cares whether an intent came from a keyboard, an AI brain, or the network.
/// </summary>
public readonly struct MoveIntent
{
    /// <summary>World-space movement direction, length 0..1.</summary>
    public Vector3 MoveDir { get; init; }

    /// <summary>Jump pressed this tick (edge, not level).</summary>
    public bool Jump { get; init; }

    /// <summary>Pick up / drop toggle pressed this tick (edge).</summary>
    public bool Interact { get; init; }

    /// <summary>Throw pressed this tick (edge).</summary>
    public bool Throw { get; init; }

    /// <summary>Sprint held this tick (level, not edge).</summary>
    public bool Sprint { get; init; }

    /// <summary>Jump held this tick (level, not edge) — mirrors <see cref="Sprint"/>'s shape.
    /// Consumed ONLY by the rising-release gravity cut in <c>AvatarMotor.GravityFor</c>; it never
    /// fires a jump. The edge <see cref="Jump"/> is still the sole trigger, and its meaning is
    /// unchanged.
    ///
    /// <para>Additive and init-only, the same shape <see cref="AimRaise"/> and
    /// <see cref="Fire"/> use, so every existing construction site compiles unchanged. It
    /// is the first input flag that could NOT ride a spare bit: the buttons byte has been
    /// documented full since v8, so this is the second buttons byte and a real layout change — see
    /// <c>NetCodec.Flags2JumpHeld</c> and <c>NetProfile.ProtocolVersion</c>'s v12 history line.</para>
    ///
    /// <para>On the tick a jump is pressed, <c>IsActionJustPressed</c> and <c>IsActionPressed</c>
    /// are both true, so a jump is always launched with this true and the earliest a cut can
    /// register is the following tick — which is what makes 0.67 m the true minimum apex rather
    /// than something lower. Scripted/bot intent sources leave it at its default <c>false</c>, so
    /// every bot hop is a minimum hop; deterministic, and stated so nobody randomises it.</para></summary>
    public bool JumpHeld { get; init; }

    /// <summary>Aim-rig raise held this tick (level, not edge) — mirrors Sprint's shape. The
    /// shared aim substrate (WP-L3, Issue #106) reads this unconditionally; it has no
    /// equipment/held-item concept of its own, so a verb that wants "only raise while my item is
    /// equipped" must gate its OWN feed of this bit before wiring it in — see AimController's
    /// class doc and the WP-L3 PR's consumption contract.</summary>
    public bool AimRaise { get; init; }

    /// <summary>The primary ("use held item") button pressed this tick (edge, not level) — same
    /// shape as <see cref="Interact"/>/<see cref="Throw"/>. The shared aim substrate (WP-L3) has
    /// no fire/shoot bit of its own (by design — see AimController's class doc); this is the
    /// verb router's own additive field (init-only, defaults false). Unconditional on bare input,
    /// same as every other edge here: what the press DOES is resolved by
    /// <c>SandboxAvatar.HandleFireIntent</c> from what is in the hand, and any registered verb is
    /// itself a request to the server, never a local resolution.
    ///
    /// <para><b>Consumed on the owning client only and NOT carried on the wire.</b> The router
    /// runs in the owner's own tick against the intent it just sampled; a verb that needs the
    /// server rides its own RPC. Nothing server-side ever read this bit.</para></summary>
    public bool Fire { get; init; }

    /// <summary>World-space look yaw, radians, same convention as SandboxCamera.Yaw — sampled
    /// fresh every tick (a continuous value, not an edge, like MoveDir). This is deliberately
    /// NOT the same as the body's own facing (MoveState.Yaw, which tracks travel direction, not
    /// where the camera looks) — it is the one piece of data that lets the SERVER reconstruct
    /// the exact aim ray a verb's validity check needs, since the server never has a Camera3D of
    /// its own. Sanitized on receipt by AvatarMotor.SanitizeAimYaw.</summary>
    public float AimYaw { get; init; }

    /// <summary>World-space look pitch, radians, clamped to SandboxCamera.PitchMin/PitchMax —
    /// same per-tick contract as <see cref="AimYaw"/>. Sanitized on receipt by
    /// AvatarMotor.SanitizeAimPitch.</summary>
    public float AimPitch { get; init; }

    /// <summary>
    /// Monotonic input sequence number, stamped by the owning client (one per fixed
    /// tick; zero for offline/unsequenced intents). The server acks the last sequence
    /// it simulated so the owner knows which predicted inputs still need replaying.
    /// Additive netcode field — intent sources never set it; the owner tick stamps it.
    /// </summary>
    public uint Seq { get; init; }

    public static readonly MoveIntent None = new();
}

/// <summary>
/// Supplies one MoveIntent per physics tick. Implementations: local keyboard+camera,
/// the dummy wander brain, and (later, outside this sandbox) a network-replicated feed.
/// </summary>
public interface IIntentSource
{
    MoveIntent NextIntent(double delta);

    /// <summary>
    /// <b>Is there a person at the other end of this intent stream?</b> True only for a real
    /// keyboard/mouse/controller feed; false for every scripted or wandering bot brain.
    ///
    /// <para><b>Why an intent source has to answer this</b> (W7-8, 2026-08-30). Achievements are
    /// earned by <i>doing a thing</i>, and a bot does things — <c>Run-AimTest</c>'s scripted 2 s →
    /// 6 s aim hold clears the 3.0 s "Tough guy" threshold every time it runs. <c>user://</c>
    /// resolves by project NAME, so every worktree on this machine shares one
    /// <c>settings.cfg</c>: that is not a test sandbox, it is the real profile. The suite had
    /// therefore already spent Talon's first-time "Tough guy" — measured on his live file, where it
    /// was the only achievement key present, which is precisely the one a bot can reach.</para>
    ///
    /// <para><b>Asked HERE rather than of a launch flag, deliberately.</b> "Is this a bot" is a
    /// property of the body being driven, not of the process: a gate on <c>--bot</c> would be a
    /// global answer to a per-avatar question, and a gate on "is this a test build" would be a
    /// flag a real playtest could also carry. The intent source IS the seam between a person and a
    /// script, and it is per-avatar by construction.</para>
    ///
    /// <para><b>Defaults to false, and the direction matters.</b> A new scripted source is
    /// excluded without anyone remembering to exclude it; a new HUMAN source (a controller feed, a
    /// remapped input path) must opt in, and until it does its player earns nothing — visible and
    /// reported, where the opposite default fails silently by writing into a real profile. A
    /// decorator that wraps a human source must forward this rather than take the default.</para>
    /// </summary>
    bool IsHumanInput => false;
}
