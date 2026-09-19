using Godot;
using System.Collections.Generic;

namespace MpFoundation.Game.Sandbox.Feel;

/// <summary>
/// THE DROP-ON COMPONENT. Add one of these as a child of any <see cref="RigidBody3D"/> and that
/// object can be hovered, grabbed, carried and released. Nothing else about the prop has to change:
/// not its mesh, not its collision shape, and — the part that matters — NOT ITS MATERIAL.
///
/// ITEM-AGNOSTIC IS A CONSTRAINT, NOT AN ASPIRATION. This class never reads a node name, never
/// looks for a specific mesh, and never assumes a prop's material is one it is allowed to touch.
/// The hover cue is drawn on a hidden mesh this component builds and owns (see <see cref="Hull"/>),
/// so a prop whose material arrived from an asset pack, a shared resource or another project still
/// gets a highlight and still ends the frame with the material it started with.
///
/// -----------------------------------------------------------------------------------------------
/// WHY IT IS A `Node` CHILD RATHER THAN A `RigidBody3D` SUBCLASS
/// -----------------------------------------------------------------------------------------------
/// A subclass is the tidier-looking option and it is the wrong one: it forces every prop that wants
/// to be interactable to be RE-ROOTED onto this type, which means editing scenes you may not own,
/// and it collides with any other system that wanted to subclass the body. A child component
/// attaches to a prop that already exists, in one drag, and detaches the same way.
///
/// The cost of that choice, stated: this component has to FIND its body, and it fails loudly if
/// there isn't one. A silent failure here would look like "the interaction system is broken"
/// rather than like "this prop has no rigid body", and those send you to different files.
///
/// -----------------------------------------------------------------------------------------------
/// PER-FRAME COST: ZERO WHEN IDLE
/// -----------------------------------------------------------------------------------------------
/// `_Process` is DISABLED unless this specific object is currently doing something — fading a
/// hover cue in or out, ringing from a grab, or settling into a slot. A room with two hundred
/// interactable props costs nothing per frame; the only steady-state work in the whole system is
/// the single interactor's own scoring pass. Every path that starts an animation calls
/// <see cref="Wake"/>, and <c>_Process</c> turns itself back off when the last one finishes. If a
/// future animation is added and forgets to call Wake, its symptom is "the effect never plays",
/// which is the loud kind of wrong.
/// </summary>
public partial class Interactable : Node
{
	// ------------------------------------------------------------------------------- tunables

	/// <summary>
	/// Multiplies the body's own <c>Mass</c> to give the value the FEEL system reads. See
	/// <see cref="HeftKg"/>.
	/// </summary>
	[Export(PropertyHint.Range, "0.05,10.0,0.01")] public float HeftScale { get; set; } = 1.0f;

	/// <summary>What happens when the carry ends. Per-item, and overridable per-instance from the
	/// lab panel, because the brief asks for both behaviours from ONE grab/hover base rather than
	/// for two systems.</summary>
	[Export] public ReleaseMode Release { get; set; } = ReleaseMode.Tumble;

	/// <summary>Matched against <see cref="InteractionSlot.AcceptTag"/>. Empty fits every untagged
	/// slot and no tagged one.</summary>
	[Export] public string Tag { get; set; } = "";

	/// <summary>Where the item sits relative to the hand anchor, in the CARRIER's frame. A long
	/// object usually wants pushing forward so it does not clip the character.</summary>
	[Export] public Vector3 HoldOffset { get; set; } = Vector3.Zero;

	/// <summary>The pose the item is carried in, relative to the carrier's facing.</summary>
	[Export] public Vector3 HoldRotationDegrees { get; set; } = Vector3.Zero;

	/// <summary>Per-item multiplier on the grab squash. 0 for something that should read as rigid —
	/// a stone block that wobbles is a stone block that reads as rubber.</summary>
	[Export(PropertyHint.Range, "0.0,3.0,0.01")] public float SquashScale { get; set; } = 1.0f;

	/// <summary>Human-readable, for the lab readout only. Never matched against.</summary>
	[Export] public string DisplayName { get; set; } = "";

	public enum ReleaseMode
	{
		/// <summary>Hand it back to the physics engine and let it fall where it falls.</summary>
		Tumble,
		/// <summary>Look for a slot; settle into it if there is one, tumble if there is not.</summary>
		Snap,
	}

	// --------------------------------------------------------------------------------- wiring

	public RigidBody3D Body { get; private set; } = null!;

	/// <summary>Body instance id -> component. The interactor's proximity Area3D reports BODIES, and
	/// this is how a body becomes an interactable without a name lookup or a cast on every overlap
	/// callback.</summary>
	private static readonly Dictionary<ulong, Interactable> ByBody = new();

	public static Interactable? For(Node3D body) =>
		ByBody.TryGetValue(body.GetInstanceId(), out var it) && GodotObject.IsInstanceValid(it)
			? it : null;

	/// <summary>The prop's own meshes, and the hidden hull built for each. Paired by index.</summary>
	private readonly List<MeshInstance3D> _meshes = new();
	private readonly List<MeshInstance3D> _hulls = new();
	private readonly List<Vector3> _restScales = new();

	private const string HullName = "HoverHull";
	private static readonly StringName HoverParam = "hover_amt";

	public override void _Ready()
	{
		Node? n = GetParent();
		while (n != null && n is not RigidBody3D) n = n.GetParent();
		if (n is not RigidBody3D body)
		{
			// LOUD, and it names the fix. A component that quietly does nothing produces a bug
			// report about the interaction system rather than about this prop.
			GD.PushError($"[Interactable] '{GetPath()}' has no RigidBody3D ancestor. This component " +
				"attaches to a rigid body; the prop will not be interactable.");
			SetProcess(false);
			return;
		}

		Body = body;
		// THE ID IS CACHED, not re-derived at teardown. _ExitTree runs while the subtree is being
		// destroyed and the parent body may already be gone, so `Body.GetInstanceId()` there is a
		// call on a dead object — the entry never gets removed and the component stays reachable
		// from a STATIC dictionary for the life of the process. That is a real leak with a real
		// consequence: a stale entry can hand a freed component back to the interactor.
		_bodyId = body.GetInstanceId();
		ByBody[_bodyId] = this;

		// CHEAP RIGID-BODY SETTINGS, APPLIED ONCE HERE RATHER THAN AT RELEASE, so that a prop
		// dropped into a scene without ever being picked up already has them.
		//
		//   ContinuousCd OFF — the brief asks for it explicitly and it is the right default anyway.
		//     Swept CCD costs a continuous test per moving body per substep; discrete collision at
		//     these speeds and these sizes does not tunnel. A prop thrown hard enough to tunnel
		//     wants a speed clamp, not CCD, because the clamp is free and CCD is not.
		//   CanSleep ON — a settled prop should stop costing anything. This is the single largest
		//     CPU saving available in a room full of props and it is one bool.
		//   ContactMonitor OFF — contact reporting allocates and marshals per contact per frame and
		//     nothing here reads contacts.
		//
		// Damping is deliberately not zero: a prop that rolls for eight seconds after being put
		// down reads as ice, not as physics, and no amount of tuning the release fixes it.
		Body.ContinuousCd = false;
		Body.CanSleep = true;
		Body.ContactMonitor = false;
		Body.MaxContactsReported = 0;
		if (Body.LinearDamp <= 0.0f) Body.LinearDamp = 0.35f;
		if (Body.AngularDamp <= 0.0f) Body.AngularDamp = 1.2f;

		// CAPTURE THE PROP'S ORIGINAL PHYSICS IDENTITY NOW, not at grab time. A grab is not the only
		// path that ends in a restore: a prop can be seated straight into a slot at build time, and
		// a release that restored a layer mask captured by a grab that never happened would write
		// zeroes — leaving the item on no collision layer at all, visible, solid-looking and
		// silently intangible.
		_savedLayer = Body.CollisionLayer;
		_savedMask = Body.CollisionMask;
		_savedFreezeMode = Body.FreezeMode;

		CollectMeshes(Body);
		SetProcess(false);
	}

	private ulong _bodyId;

	public override void _ExitTree()
	{
		if (_bodyId != 0) ByBody.Remove(_bodyId);
	}

	private void CollectMeshes(Node node)
	{
		if (node is MeshInstance3D mi && mi.Name != HullName && mi.Mesh != null)
		{
			_meshes.Add(mi);
			_restScales.Add(mi.Scale);
		}
		foreach (var c in node.GetChildren()) CollectMeshes(c);
	}

	/// <summary>
	/// Build the hover cue: for each of the prop's meshes, a HIDDEN CHILD sharing the same
	/// <c>Mesh</c> resource and carrying the shared hull material.
	///
	/// A CHILD, so it inherits the prop's transform for free and can never drift out of alignment
	/// — an outline tracked by copying a transform every frame is an outline that lags by one
	/// frame on exactly the objects that move, which are the ones being highlighted.
	///
	/// SHARING THE SAME <c>Mesh</c> RESOURCE, so this costs a node and a handful of bytes, not a
	/// second copy of the geometry.
	/// </summary>
	public void BuildHull(Material hullMaterial)
	{
		foreach (var mi in _meshes)
		{
			var hull = new MeshInstance3D
			{
				Name = HullName,
				Mesh = mi.Mesh,
				MaterialOverride = hullMaterial,
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
				Visible = false,
			};
			mi.AddChild(hull);
			// WRITE THE ZERO EXPLICITLY. An `instance uniform` that has never been written does not
			// reliably read as its declared default — it reads whatever is in that object's
			// per-instance slot, which is uninitialised. room.gd records what that looks like when
			// it goes wrong: the entire scene rendering as nothing, with no error at all.
			hull.SetInstanceShaderParameter(HoverParam, 0.0f);
			_hulls.Add(hull);
		}
	}

	// ---------------------------------------------------------------------------------- state

	public bool Held { get; private set; }
	public InteractionSlot? Slot { get; private set; }

	/// <summary>The mass the FEEL system reads: the body's real mass, times a per-item override.
	///
	/// ONE NUMBER, NOT TWO. The obvious build gives an item a physics mass and a separate "weight
	/// feel" value, and they drift: an item that lags heavily in the hand and then bounces away
	/// like a balloon has told the player two different things about itself in one second. Making
	/// the body's own mass the source means a heavy item lags in the hand AND thuds when it lands,
	/// from the same number, for free. <see cref="HeftScale"/> exists for the rare case where the
	/// two genuinely must disagree, and being a multiplier it keeps them tied together.</summary>
	public float HeftKg => (Body?.Mass ?? 1.0f) * HeftScale;

	// Saved across a carry, so a release restores the prop exactly as it was found. Capturing this
	// at grab time rather than assuming defaults is what lets the component be dropped onto a prop
	// that already has its own layer/mask setup.
	private uint _savedLayer;
	private uint _savedMask;
	private RigidBody3D.FreezeModeEnum _savedFreezeMode;

	public void OnGrabbed(bool collideWhileHeld)
	{
		// Leaving a slot frees it immediately, not when the carry ends. A slot that stays claimed
		// by an object being carried away is a slot nothing else can use for as long as you hold it.
		if (Slot != null) { Slot.Release(this); Slot.RefreshMarker(); Slot = null; }

		_savedLayer = Body.CollisionLayer;
		_savedMask = Body.CollisionMask;
		_savedFreezeMode = Body.FreezeMode;

		Body.LinearVelocity = Vector3.Zero;
		Body.AngularVelocity = Vector3.Zero;
		Body.FreezeMode = RigidBody3D.FreezeModeEnum.Kinematic;
		Body.Freeze = true;

		// HELD ITEMS DEFAULT TO NOT COLLIDING, and this is the one place in the system where the
		// cheap option and the good option are the same option.
		//
		// A held body that still collides fights the spring that is carrying it: the spring pulls
		// toward the hand, the world pushes back, and the item judders against every doorframe the
		// character walks through. It also makes the carry non-deterministic, which is what breaks
		// a self-test. Turning collision off makes the held item pass through geometry — a real
		// artefact, and a much smaller one than the judder, which is why most shipping games do
		// exactly this. The toggle is exposed so the trade can be seen rather than argued about.
		if (!collideWhileHeld) { Body.CollisionLayer = 0; Body.CollisionMask = 0; }

		Held = true;
	}

	/// <summary>Hand the prop back to the physics engine with the carry's own momentum.</summary>
	public void OnTumbleReleased(Vector3 linear, Vector3 angular)
	{
		Held = false;
		Body.CollisionLayer = _savedLayer;
		Body.CollisionMask = _savedMask;
		Body.FreezeMode = _savedFreezeMode;
		Body.Freeze = false;
		Body.LinearVelocity = linear;
		Body.AngularVelocity = angular;
	}

	/// <summary>Begin the settle into a slot. The body stays frozen for the whole animation and is
	/// driven by transform, so the settle is exactly the curve authored here and cannot be perturbed
	/// by a collision half way through — a snap that sometimes lands slightly off is worse than one
	/// that never snaps.</summary>
	public void OnSnapReleased(InteractionSlot slot, Transform3D from, float settleTime)
	{
		Held = false;
		Body.CollisionLayer = _savedLayer;
		Body.CollisionMask = _savedMask;
		Body.FreezeMode = RigidBody3D.FreezeModeEnum.Kinematic;
		Body.Freeze = true;
		Body.LinearVelocity = Vector3.Zero;
		Body.AngularVelocity = Vector3.Zero;

		Slot = slot;
		slot.Claim(this);
		slot.RefreshMarker();

		_settleFrom = from;
		_settleTo = slot.RestPose(from);
		_settleT = 0.0f;
		_settleLen = Mathf.Max(0.01f, settleTime);
		_settling = true;
		Wake();
	}

	// ----------------------------------------------------------------------------- animation

	private float _hover;
	private float _hoverTarget;
	private float _hoverRate = 8.0f;

	private float _squashT = -1.0f;
	private float _squashAmp;
	private float _squashHz = 7.0f;
	private float _squashDecay = 9.0f;

	private bool _settling;
	private float _settleT;
	private float _settleLen;
	private Transform3D _settleFrom;
	private Transform3D _settleTo;

	/// <summary>Target hover strength, 0 or 1. The fade is driven here rather than by the interactor
	/// so that an item losing focus finishes fading out even after the interactor has forgotten
	/// about it — otherwise a fast flick between two props leaves the first one stuck lit.</summary>
	public void SetHover(bool on, float fadeTime)
	{
		_hoverTarget = on ? 1.0f : 0.0f;
		_hoverRate = 1.0f / Mathf.Max(0.01f, fadeTime);
		if (!Mathf.IsEqualApprox(_hover, _hoverTarget)) Wake();
	}

	/// <summary>Kick the squash-and-wobble. One decaying oscillation, not a canned animation, so it
	/// works on any mesh — including one this lab has never seen.</summary>
	public void Kick(float amplitude, float hz, float decay)
	{
		_squashAmp = amplitude * SquashScale;
		_squashHz = hz;
		_squashDecay = decay;
		_squashT = 0.0f;
		if (_squashAmp > 0.0001f) Wake();
	}

	private void Wake() => SetProcess(true);

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		bool busy = false;

		// ---- hover fade --------------------------------------------------------------------
		if (!Mathf.IsEqualApprox(_hover, _hoverTarget))
		{
			_hover = Mathf.MoveToward(_hover, _hoverTarget, _hoverRate * dt);
			busy = true;
			foreach (var h in _hulls)
			{
				// Visibility is toggled as well as the uniform. The shader discards below 0.002, so
				// a fully faded hull would already cost no fragments — but it would still be
				// submitted, culled and vertex-shaded every frame, for every prop the player has
				// ever looked at. Hiding it takes the draw call away entirely, which is the actual
				// claim this system makes about its cost.
				h.Visible = _hover > 0.002f;
				if (h.Visible) h.SetInstanceShaderParameter(HoverParam, _hover);
			}
		}

		// ---- squash / wobble ---------------------------------------------------------------
		if (_squashT >= 0.0f)
		{
			_squashT += dt;
			float env = Mathf.Exp(-_squashDecay * _squashT);
			if (env < 0.004f)
			{
				_squashT = -1.0f;
				for (int i = 0; i < _meshes.Count; i++) _meshes[i].Scale = _restScales[i];
			}
			else
			{
				busy = true;
				float s = _squashAmp * env * Mathf.Sin(Mathf.Tau * _squashHz * _squashT);
				// VOLUME-PRESERVING, and that is why it reads as squash rather than as scaling.
				// Stretching along one axis without pulling the other two in is an object changing
				// size, which the eye reads as a rendering glitch. Clamped so a large amplitude
				// cannot drive the scale through zero and invert the mesh.
				float y = Mathf.Max(0.15f, 1.0f + s);
				float xz = 1.0f / Mathf.Sqrt(y);
				// LOCAL Y, not world up. The axis is a property of the object being squeezed, so a
				// prop lying on its side squashes across the way it is lying. A world-up squash
				// looks correct only for objects that happen to be upright.
				for (int i = 0; i < _meshes.Count; i++)
					_meshes[i].Scale = _restScales[i] * new Vector3(xz, y, xz);
			}
		}

		// ---- settle into a slot ------------------------------------------------------------
		if (_settling)
		{
			_settleT += dt;
			float u = Mathf.Clamp(_settleT / _settleLen, 0.0f, 1.0f);
			// Ease-out cubic. THE EASE IS THE WHOLE REQUIREMENT: an instant snap is a teleport and
			// reads as a bug even when it is exactly what was asked for. Ease-OUT rather than
			// ease-in-out because the item should leave the hand immediately and arrive gently —
			// the decision was made at the moment of release, and delaying the start of the motion
			// makes the input feel dropped.
			float e = 1.0f - Mathf.Pow(1.0f - u, 3.0f);
			Body.GlobalTransform = _settleFrom.InterpolateWith(_settleTo, e);
			busy = true;

			if (u >= 1.0f)
			{
				_settling = false;
				Body.GlobalTransform = _settleTo;

				// A SLOTTED ITEM IS NOT SIMULATED, AND IT STAYS ON THE KINEMATIC FREEZE TO DO IT.
				//
				// This used to switch to RigidBody3D.FreezeModeEnum.Static here, and that one line
				// is the bug Talon found by playing it (2026-09-05): there was no way to take an
				// item back off the snapping platform once it had been placed.
				//
				// MEASURED, not deduced. With the item settled and the character standing 1.06 m
				// away against a 2.0 m reach, the interactor's Area3D reported `overlaps=[]` --
				// while the body's collision layer read 3 on both the node and the physics server,
				// so the layer was right and only the pairing was missing. Flipping this one field
				// back to Kinematic, changing nothing else, made the same area report
				// `overlaps=[3_kg_jug]` on the very next tick. AN Area3D DOES NOT REPORT A
				// RigidBody3D FROZEN IN STATIC MODE, so a slotted item quietly stopped being a
				// candidate for anything: targeting, the hover cue, and the grab that takes it back.
				//
				// It survived review as a design decision because a snapped item is exactly the one
				// that NEVER MOVES AGAIN -- a tumbled item is handed back to physics with velocity,
				// so nothing about it depends on a pairing surviving, and the mode is invisible
				// there. Every case the lab self-test covered put an item DOWN; not one reached for
				// something already in a slot. 112 assertions passed over a shelf you could not
				// take anything off.
				//
				// Static bought nothing Kinematic does not also buy. `Freeze` is what stops the
				// integration; the MODE only decides whether transform writes reach the server.
				// Kinematic is already what the carry and the settle above run on, and it is what
				// this file's own freeze note recommends, for the neighbouring reason that a Static
				// body silently ignores the transform it is handed. The cost is a frozen body
				// sitting in the moving broadphase rather than the static one, which for a shelf of
				// props is not measurable; being unable to pick your things up is.
				Body.FreezeMode = RigidBody3D.FreezeModeEnum.Kinematic;
			}
		}

		if (!busy) SetProcess(false);
	}

	/// <summary>The current hover strength, for the lab readout and the self-test.</summary>
	public float HoverAmount => _hover;

	/// <summary>True while a settle animation is running.</summary>
	public bool Settling => _settling;

	public string Label => DisplayName.Length > 0 ? DisplayName : Body?.Name.ToString() ?? "?";

	/// <summary>Whether the cue is actually SUBMITTED, not merely requested. Separate from
	/// <see cref="HoverAmount"/> on purpose: the pair distinguishes "the interactor never asked" from
	/// "it asked and the hull is not drawing", which are different bugs in different files.</summary>
	public bool HullVisible => _hulls.Count > 0 && _hulls[0].Visible;

}
