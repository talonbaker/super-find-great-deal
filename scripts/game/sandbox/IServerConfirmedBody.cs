using Godot;

namespace MpFoundation.Game.Sandbox;

/// <summary>
/// A body that can distinguish where this process THINKS it is from where the authority has
/// actually put it.
///
/// <para><b>Why this exists (W6-3, 2026-08-30).</b> A networked owner's own
/// <c>GlobalPosition</c> is a PREDICTION: it is this client's guess at a simulation the server
/// has not run yet, and reconciliation revokes it whenever the guess was wrong. That is fine for
/// anything reversible — a movement request, a facing, a camera — and it is exactly wrong for
/// anything that cannot be taken back. The rule
/// <c>.claude/rules/test-suite.md</c> states it as: <i>a scripted bot may compute its next MOVE
/// from a predicted position, but must not take an irreversible action on one.</i> It was written
/// after two packets in one night independently measured the consequence:
/// <see cref="ScriptedGotoIntentSource"/> latched "arrived" on a predicted position, the next
/// reconciliation pulled the body ~1.8 m back out, and the bot held a brain that would never ask
/// to move again.</para>
///
/// <para><b>Why an interface rather than a direct <see cref="SandboxAvatar"/> reference.</b> The
/// scripted intent sources take a plain <see cref="Node3D"/> so they can be pointed at any body,
/// including fixtures that are not avatars. Asking that body whether it happens to know its own
/// authoritative position — and accepting "no" — keeps that generality: a source holding a body
/// with no authority information behaves exactly as it did before this interface existed, which
/// is what keeps every offline and server-side suite's timeline unchanged.</para>
/// </summary>
public interface IServerConfirmedBody
{
    /// <summary>The newest position the AUTHORITY has actually simulated for this body, or
    /// <c>null</c> when this process holds no authority information about it.
    ///
    /// <para>Null is a real answer and callers must handle it rather than substituting the live
    /// position: it means "unknown", not "the same as where I am". It is returned by a networked
    /// owner that has not yet received its first snapshot, and by a remote proxy, which only ever
    /// sees interpolated presentation state. A process that IS the authority for this body
    /// (offline, or the server's own simulation) answers with the body's live position, because
    /// there is nothing there to confirm.</para>
    ///
    /// <para>The value lags: it is the authority's state at the tick of the last snapshot to
    /// arrive, not this instant. That lag is the point — a decision that survives it is a decision
    /// the server has already agreed with.</para></summary>
    Vector3? ServerConfirmedPosition { get; }
}
