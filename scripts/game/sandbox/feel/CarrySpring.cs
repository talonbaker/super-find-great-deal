using Godot;

namespace MpFoundation.Game.Sandbox.Feel;

/// <summary>
/// THE CARRY, AS MATHS: one critically damped position spring, one trailing rotation slerp, and
/// the trailing tilt that makes a held thing read as carried rather than welded to a socket.
///
/// <para><b>Why this file exists (CARRY-1, 2026-09-19).</b> The packet offered two ways to give a
/// networked holder the feel system's carry: bind the lab's <see cref="Interactor"/> to the
/// networked prop's body, or port <see cref="Interactor"/>'s carry update into
/// <c>NetworkedProp</c> — "pick the one that leaves <c>feel/</c> intact and say why". Neither, as
/// written, does. Binding the whole <see cref="Interactor"/> drags in a SECOND targeting
/// authority (its own proximity <c>Area3D</c>, its own aim cone, its own line-of-sight rule) next
/// to <c>InteractTargeting.Pick</c>, which is this repo's one rule for what E acts on, and it
/// wants a <c>Camera3D</c> — the exact dependency CARRY-1 was told to avoid while FP-1 replaces
/// the camera. Copying the update into <c>NetworkedProp</c> leaves <c>feel/</c> untouched and
/// leaves the spring with TWO homes, which is how a feel system and its copy drift apart one
/// tuning session at a time.</para>
///
/// <para><b>So the maths moved here and BOTH callers read it.</b> <see cref="Interactor"/> keeps
/// every dial, every decision and every line of its targeting and release logic; what it lost is
/// the arithmetic, which is now called rather than inlined. <c>NetworkedProp</c>'s local-holder
/// carry constructs one of these with the same defaults. The lab's own gate
/// (<c>tests/Run-FeelTest.ps1</c>) is the proof that the extraction changed nothing: it asserts
/// the carry stays finite, stays bounded, and survives the spring at both ends of the response
/// dial.</para>
///
/// <para><b>Engine-light on purpose.</b> This is a plain class, not a Node: it owns state and
/// arithmetic and touches neither the scene tree nor the physics server, so the caller decides
/// what to do with the transform it produces. That is what lets the networked holder drive a
/// frozen kinematic <c>RigidBody3D</c> through the same code the lab drives a free one
/// with.</para>
/// </summary>
public sealed class CarrySpring
{
	// ------------------------------------------------------------------------------- the dials
	//
	// Defaults are Interactor's shipped export defaults, verbatim. They are duplicated as plain
	// fields rather than re-declared as [Export]s because this type is not a Node; Interactor
	// pushes its own exports into these every tick so a live slider in the lab panel still does
	// what the panel says it does.

	/// <inheritdoc cref="Interactor.MassReference"/>
	public float MassReference = 10.0f;

	/// <inheritdoc cref="Interactor.LightResponse"/>
	public float LightResponse = 20.0f;

	/// <inheritdoc cref="Interactor.HeavyResponse"/>
	public float HeavyResponse = 6.5f;

	/// <inheritdoc cref="Interactor.RotationResponse"/>
	public float RotationResponse = 0.7f;

	/// <inheritdoc cref="Interactor.SwingDegrees"/>
	public float SwingDegrees = 16.0f;

	/// <inheritdoc cref="Interactor.SwingReferenceSpeed"/>
	public float SwingReferenceSpeed = 3.2f;

	/// <inheritdoc cref="Interactor.SwingSmoothing"/>
	public float SwingSmoothing = 0.12f;

	// -------------------------------------------------------------------------------- the state

	private Vector3 _pos;
	private Vector3 _vel;
	private Basis _basis = Basis.Identity;
	private Vector3 _anchorPrev;
	private Vector3 _anchorVelSmooth;
	private Vector3 _itemPrev;
	private Vector3 _itemVelSmooth;

	/// <summary>Where the carried thing currently is, as this spring last resolved it.</summary>
	public Vector3 Position => _pos;

	/// <summary>The carried thing's own smoothed velocity — NOT the hand's. A release inherits
	/// this, which is the whole reason the lag is simulated rather than faked with an offset: a
	/// heavy item that trailed the hand leaves at the speed it was actually travelling.</summary>
	public Vector3 ItemVelocity => _itemVelSmooth;

	/// <summary>The pose this spring last resolved, ready to be written onto a body.</summary>
	public Transform3D Transform => new(_basis, _pos);

	/// <summary>0 for weightless, 1 at or above <see cref="MassReference"/>. Every weight dial in
	/// the system reads this and nothing else — see <see cref="Interactor.MassReference"/>.</summary>
	public float HeftOf(float massKg) =>
		Mathf.Clamp(massKg / Mathf.Max(0.0001f, MassReference), 0.0f, 1.0f);

	/// <summary>
	/// Seed the spring AT THE ITEM'S CURRENT POSE, not at the hand.
	///
	/// <para>THE DOCUMENTED ANTI-POP. Seeding at the anchor makes the item teleport into the hand
	/// on frame one, which throws away the one frame in which the grab is most readable. Every
	/// caller has to do this before its first <see cref="Step"/> or the first tick's velocity is
	/// computed against a pose the item never had.</para>
	/// </summary>
	public void Seed(Transform3D itemNow, Vector3 anchor)
	{
		_pos = itemNow.Origin;
		_basis = itemNow.Basis.Orthonormalized();
		_vel = Vector3.Zero;
		_itemPrev = _pos;
		_itemVelSmooth = Vector3.Zero;
		_anchorPrev = anchor;
		_anchorVelSmooth = Vector3.Zero;
	}

	/// <summary>
	/// One carry tick. <paramref name="anchor"/> is where the hand is this tick (droop and any
	/// per-item hold offset already folded in by the caller, because both are questions about the
	/// HAND rather than about the spring). <paramref name="poseBasis"/> is the orientation the
	/// item is being held at, in world space, before the trailing tilt is added.
	/// </summary>
	/// <returns>The pose to write onto the carried body.</returns>
	public Transform3D Step(float dt, Vector3 anchor, Basis poseBasis, float heft)
	{
		heft = Mathf.Clamp(heft, 0.0f, 1.0f);
		float omega = Mathf.Lerp(LightResponse, HeavyResponse, heft);

		Vector3 anchorVel = dt > 0.0f ? (anchor - _anchorPrev) / dt : Vector3.Zero;
		_anchorPrev = anchor;
		_anchorVelSmooth = _anchorVelSmooth.Lerp(anchorVel, Smoothing(SwingSmoothing, dt));

		// ---- position: one critically damped spring --------------------------------------
		Spring(ref _pos, ref _vel, anchor, omega, dt);

		// ---- rotation: the carry pose, plus a trailing tilt -------------------------------
		Basis target = poseBasis;
		Vector3 v = _anchorVelSmooth;
		float speed = v.Length();
		if (speed > 0.01f)
		{
			// AXIS = velocity CROSS up, so the tilt TRAILS the motion instead of leading it. The
			// opposite order leans the item INTO its direction of travel, which reads as the item
			// dragging the character along and is one sign flip away from correct.
			Vector3 axis = v.Cross(Vector3.Up);
			if (axis.LengthSquared() > 1e-6f)
			{
				float amt = Mathf.Clamp(speed / SwingReferenceSpeed, 0.0f, 1.0f) * heft;
				float ang = Mathf.DegToRad(SwingDegrees) * amt;
				target = new Basis(axis.Normalized(), ang) * target;
			}
		}

		_basis = _basis.Orthonormalized()
			.Slerp(target.Orthonormalized(), Smoothing(1.0f / (omega * RotationResponse), dt))
			.Orthonormalized();

		Vector3 itemVel = dt > 0.0f ? (_pos - _itemPrev) / dt : Vector3.Zero;
		_itemPrev = _pos;
		_itemVelSmooth = _itemVelSmooth.Lerp(itemVel, Smoothing(0.06f, dt));

		return new Transform3D(_basis, _pos);
	}

	/// <summary>How far the carried item is currently trailing the given hand anchor. This is the
	/// number CARRY-1's handoff reports as the spring-vs-anchor mismatch: the holder sees the
	/// spring, every other peer derives the prop straight from the holder's anchor, and this is
	/// the gap between the two views.</summary>
	public float LagTo(Vector3 anchor) => _pos.DistanceTo(anchor);

	// --------------------------------------------------------------------------------- maths

	/// <summary>
	/// A critically damped spring, in the closed form that is unconditionally stable.
	///
	/// <para>The naive integration (<c>v += (k*x - c*v) * dt</c>) blows up as soon as
	/// <c>omega * dt</c> approaches 1, which for a snappy light item at 60 Hz is a value the
	/// tuning range reaches. It does not blow up quietly: the item flies to infinity and the frame
	/// after that the transform is NaN and the object is gone. This form is exact enough at any dt
	/// and any omega, which is what lets the response dials be exposed at all.</para>
	/// </summary>
	public static void Spring(ref Vector3 x, ref Vector3 v, Vector3 target, float omega, float dt)
	{
		float f = omega * dt;
		float exp = 1.0f / (1.0f + f + 0.48f * f * f + 0.235f * f * f * f);
		Vector3 change = x - target;
		Vector3 temp = (v + change * omega) * dt;
		v = (v - temp * omega) * exp;
		x = target + (change + temp) * exp;
	}

	/// <summary>
	/// Frame-rate independent lerp factor for a given time constant.
	///
	/// <para>A bare <c>Lerp(a, b, 0.1f)</c> smooths TWICE AS FAST at 120 Hz as at 60, so every
	/// feel dial in a system that uses one is secretly a function of the player's monitor. This is
	/// the exponential form, and it costs one <c>exp</c>.</para>
	/// </summary>
	public static float Smoothing(float tau, float dt) =>
		tau <= 0.0f ? 1.0f : 1.0f - Mathf.Exp(-dt / tau);
}
