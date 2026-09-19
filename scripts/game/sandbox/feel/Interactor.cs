using Godot;
using System.Collections.Generic;

namespace MpFoundation.Game.Sandbox.Feel;

/// <summary>
/// THE HAND. One of these per character. It decides what is being looked at, carries what it has
/// grabbed, and decides what happens when the carry ends.
///
/// GENERAL-PURPOSE AND PROJECT-NEUTRAL. It knows about <see cref="Interactable"/> and
/// <see cref="InteractionSlot"/> and about nothing else. No item names, no item types, no game.
///
/// -----------------------------------------------------------------------------------------------
/// EVERYTHING HERE RUNS IN _PhysicsProcess, ON PURPOSE, AND IT IS BOTH A CORRECTNESS AND A COST
/// DECISION
/// -----------------------------------------------------------------------------------------------
/// Correctness: the targeting pass casts a ray, and a direct space state may only be queried during
/// the physics step — asking outside one is an error at best and a stale answer at worst. The carry
/// also drives a frozen kinematic body, which the solver expects to see move on physics ticks.
///
/// Cost: it pins the whole system to the PHYSICS rate rather than the RENDER rate. On a 144 Hz
/// display a _Process implementation would do 144 scoring passes and 144 raycasts a second to
/// answer a question that changes at walking pace. Here it does 60, on any display, and the visual
/// smoothness comes from physics interpolation rather than from re-deciding more often. That is the
/// difference between a system whose cost is set by the machine it runs on and one whose cost is
/// set by the designer.
///
/// -----------------------------------------------------------------------------------------------
/// THE STEADY-STATE COST, IN FULL
/// -----------------------------------------------------------------------------------------------
///   * ZERO physics queries for proximity. The candidate set is maintained by an Area3D's
///     enter/exit signals, so the broadphase — which is already running for collision — does the
///     culling and reports only changes. The obvious alternative, a shape query every tick, asks
///     the physics server to redo work it has already done, every tick, forever.
///   * O(candidates) dot products per physics tick, where candidates means "props within reach",
///     typically nought to five.
///   * AT MOST <see cref="MaxSightChecks"/> raycasts per tick (default 2), and only when there is a
///     candidate worth checking. Standing in an empty room costs nothing.
///   * The carry itself is one spring integration and one basis slerp.
/// Nothing in that list scales with the size of the level or the number of props in it.
/// </summary>
public partial class Interactor : Node3D
{
	// ---------------------------------------------------------------------------- references

	/// <summary>The body the hand belongs to. The hold anchor is built in its frame, so the carried
	/// item follows the character's facing rather than the camera's.</summary>
	[Export] public Node3D? Carrier { get; set; }

	/// <summary>Where "looking at" is measured from. Targeting is a CAMERA question, not a character
	/// question: the player aims with the view.</summary>
	[Export] public Camera3D? Camera { get; set; }

	/// <summary>Leaned under load. Optional — the lean is feel, not function.</summary>
	[Export] public Node3D? CarrierVisual { get; set; }

	/// <summary>Optional. Null runs the whole system silently.</summary>
	public FeelAudio? Audio { get; set; }

	// ----------------------------------------------------------------------------- targeting

	/// <summary>Radius of the proximity area. Nothing outside it can ever be targeted, which is what
	/// keeps the scoring pass O(a handful) instead of O(the level).</summary>
	[Export(PropertyHint.Range, "0.5,6.0,0.05")] public float ReachRadius { get; set; } = 2.6f;

	/// <summary>How far off the view axis a prop may sit and still be targetable.
	///
	/// GENEROUS ON PURPOSE, AND THAT IS A CAMERA-DISTANCE DECISION. Under a tight shoulder-cam a
	/// narrow cone feels precise. Under the loose camera this lab is built for — farther back,
	/// showing most of the character — the same cone feels like the game is refusing to look at
	/// things that are plainly in front of you, because the character occupies a much larger share
	/// of what the cone is pointing at.</summary>
	[Export(PropertyHint.Range, "5,90,0.5")] public float AimHalfAngleDeg { get; set; } = 62.0f;

	/// <summary>Score bonus the CURRENT target keeps, so two props at similar angles do not swap the
	/// highlight back and forth every tick.
	///
	/// HYSTERESIS IS NOT POLISH HERE. A flickering highlight is not merely ugly: the player cannot
	/// tell what the button is about to do, which makes the whole cue useless at the exact moment it
	/// matters. Set it to 0 in the panel to see how bad the un-damped version is.</summary>
	[Export(PropertyHint.Range, "0.0,0.5,0.005")] public float TargetStickiness { get; set; } = 0.10f;

	/// <summary>Weight on distance in the score, per metre. Small: angle should dominate, because the
	/// player aims by looking. Raise it for a system where the nearest thing always wins.</summary>
	[Export(PropertyHint.Range, "0.0,1.0,0.005")] public float DistanceWeight { get; set; } = 0.06f;

	/// <summary>A prop you cannot see is a prop you cannot grab.
	///
	/// This is the rule that makes the depth-respecting hover outline correct rather than limited:
	/// the cue is hidden in exactly the cases the interactor would refuse. See
	/// hover_outline.gdshader.</summary>
	[Export] public bool RequireLineOfSight { get; set; } = true;

	/// <summary>Ceiling on raycasts per tick. When the best candidate is occluded the pass tries the
	/// next best, and then stops. BOUNDED RATHER THAN EXHAUSTIVE: a cluttered corner could otherwise
	/// turn one tick's targeting into a dozen queries, and the failure mode of stopping early is
	/// "you have to step round the crate", which nobody files a bug about.</summary>
	[Export(PropertyHint.Range, "1,6,1")] public int MaxSightChecks { get; set; } = 2;

	/// <summary>Hover cue fade, seconds each way. Fast: this is a state readout, not a transition.
	/// Above about 0.2 s the cue arrives after the player has already decided.</summary>
	[Export(PropertyHint.Range, "0.0,0.6,0.005")] public float HoverFadeTime { get; set; } = 0.10f;

	/// <summary>Layer bit the proximity area listens on. Props sit on it; walls do not, so the area
	/// never reports a wall it would only have to discard.</summary>
	[Export(PropertyHint.Layers3DPhysics)] public uint InteractableLayer { get; set; } = 2;

	/// <summary>What can block sight. World and props both, or a prop behind a prop is grabbable
	/// through it.</summary>
	[Export(PropertyHint.Layers3DPhysics)] public uint SightMask { get; set; } = 3;

	// -------------------------------------------------------------------------------- carry

	/// <summary>The hand's resting place, in the carrier's frame: X right, Y up, Z BACK (so forward
	/// is negative, Godot's convention).</summary>
	[Export] public Vector3 HandOffset { get; set; } = new(0.36f, 1.12f, -0.52f);

	/// <summary>
	/// The mass at which an item counts as fully heavy. Everything about weight in this system is
	/// <c>heft = clamp(item mass * item HeftScale / MassReference, 0, 1)</c>, and every other weight
	/// dial is a pair of endpoints that heft interpolates between.
	///
	/// ONE NORMALISATION, IN ONE PLACE. The alternative — every dial deciding for itself what heavy
	/// means — is how a system ends up with an item that lags like a boulder and throws like a
	/// pebble.
	/// </summary>
	[Export(PropertyHint.Range, "0.5,60.0,0.1")] public float MassReference { get; set; } = 10.0f;

	/// <summary>Spring frequency for a weightless item, radians/second. High is snappy.</summary>
	[Export(PropertyHint.Range, "2.0,40.0,0.1")] public float LightResponse { get; set; } = 20.0f;

	/// <summary>
	/// Spring frequency at full heft. LOW IS THE ENTIRE WEIGHT EFFECT.
	///
	/// A critically damped spring's frequency is exactly the knob that reads as mass, because it is
	/// what mass does in the real equation: the same hand motion produces a visible lag on a heavy
	/// item and none on a light one, with no animation, no per-item authoring and no branch. Below
	/// about 4 the item stops reading as heavy and starts reading as attached to the character by
	/// elastic, which is a different and much worse thing.
	/// </summary>
	[Export(PropertyHint.Range, "1.0,20.0,0.1")] public float HeavyResponse { get; set; } = 6.5f;

	/// <summary>Rotation chases the carry pose at this multiple of the position frequency. Below 1
	/// the item's facing trails its position, which is what real held objects do.</summary>
	[Export(PropertyHint.Range, "0.1,3.0,0.01")] public float RotationResponse { get; set; } = 0.7f;

	/// <summary>Peak trailing tilt, degrees, at full heft and <see cref="SwingReferenceSpeed"/>.
	/// This is the visible "it is pulling on me" — the item leans away from the direction of
	/// travel, like something being carried rather than something glued to a socket.</summary>
	[Export(PropertyHint.Range, "0.0,45.0,0.1")] public float SwingDegrees { get; set; } = 16.0f;

	/// <summary>Hand speed, m/s, at which the swing reaches its full angle.</summary>
	[Export(PropertyHint.Range, "0.5,10.0,0.05")] public float SwingReferenceSpeed { get; set; } = 3.2f;

	/// <summary>Smoothing on the velocity the swing reads, seconds. Unsmoothed, the swing snaps the
	/// instant the input changes and reads as a twitch rather than as momentum.</summary>
	[Export(PropertyHint.Range, "0.0,0.6,0.005")] public float SwingSmoothing { get; set; } = 0.12f;

	/// <summary>How far a full-heft item sags below the hand anchor, metres.</summary>
	[Export(PropertyHint.Range, "0.0,0.5,0.005")] public float CarryDroop { get; set; } = 0.11f;

	/// <summary>Counter-lean applied to the carrier under load, degrees at full heft. The character
	/// leans AWAY from the side it is carrying on — which is what a person does, and what makes a
	/// heavy item read as heavy even in the frames where the hand is not moving.</summary>
	[Export(PropertyHint.Range, "0.0,20.0,0.1")] public float CarryLeanDegrees { get; set; } = 6.0f;

	/// <summary>See <see cref="Interactable.OnGrabbed"/> for why this defaults off.</summary>
	[Export] public bool CollideWhileHeld { get; set; } = false;

	// -------------------------------------------------------------------------------- squash

	/// <summary>Peak scale deviation on grab. 0.25 is a clear reaction; past ~0.5 a rigid prop reads
	/// as rubber, which is a style claim this lab is not allowed to make by default.</summary>
	[Export(PropertyHint.Range, "0.0,0.8,0.005")] public float GrabSquash { get; set; } = 0.26f;

	/// <summary>Ring frequency, Hz.</summary>
	[Export(PropertyHint.Range, "1.0,20.0,0.1")] public float GrabSquashHz { get; set; } = 7.5f;

	/// <summary>Decay rate. High is a click, low is a jelly.</summary>
	[Export(PropertyHint.Range, "1.0,30.0,0.1")] public float GrabSquashDecay { get; set; } = 9.0f;

	/// <summary>Heavier items ring SLOWER and LONGER. Same argument as the spring frequency: it is
	/// what mass does, so it needs no per-item authoring. 0 makes every item ring identically, which
	/// is the honest A/B for whether this term is doing anything.</summary>
	[Export(PropertyHint.Range, "0.0,1.0,0.01")] public float SquashMassResponse { get; set; } = 0.6f;

	// ------------------------------------------------------------------------------- release

	/// <summary>How much of the carried item's own velocity it keeps. 1.0 means letting go while
	/// running throws it forward, which is the physically honest answer and the one that makes
	/// tumble mode feel connected to the carry rather than bolted on after it.</summary>
	[Export(PropertyHint.Range, "0.0,2.0,0.01")] public float ThrowScale { get; set; } = 1.0f;

	/// <summary>A constant nudge along the view direction, m/s. Small: without it a standing release
	/// drops the prop onto the character's own feet, where it is both invisible and in the way.</summary>
	[Export(PropertyHint.Range, "0.0,8.0,0.05")] public float ThrowForward { get; set; } = 0.9f;

	/// <summary>Speed ceiling on a release, m/s. THIS IS WHY CONTINUOUS COLLISION IS NOT NEEDED: a
	/// clamp costs one comparison and removes the tunnelling case that CCD costs a swept test per
	/// substep to survive. The brief asks for lightweight rigid bodies; this is how that is paid
	/// for rather than merely asserted.</summary>
	[Export(PropertyHint.Range, "1.0,30.0,0.1")] public float MaxThrowSpeed { get; set; } = 8.0f;

	/// <summary>Spin imparted on a tumble release, scaled by hand speed. The tumble is the point of
	/// the mode — a prop that lets go and drops perfectly flat reads as a snap that missed.</summary>
	[Export(PropertyHint.Range, "0.0,6.0,0.01")] public float ThrowSpin { get; set; } = 1.6f;

	/// <summary>Baseline spin, rad/s, so even a stationary drop turns over a little.</summary>
	[Export(PropertyHint.Range, "0.0,6.0,0.01")] public float ReleaseSpin { get; set; } = 1.1f;

	// ------------------------------------------------------------------ the game's throw key
	//
	// THE LAB HAD NO THROW. Releasing while moving carried the item's own velocity out of the hand
	// (ThrowScale above) and that was the whole of it, which is right for a lab where the only
	// verbs are grab and put down. The game has a dedicated throw on its own key, and a feel system
	// judged without it would be judged without the one action that puts the carry under load.
	//
	// These three are deliberately SEPARATE dials from ThrowForward/MaxThrowSpeed rather than a
	// reuse of them: those govern what letting go does, and a release and a throw are different
	// events. Sharing one ceiling is how a throw ends up clamped to a drop.

	/// <summary>Forward speed the throw key adds, m/s, before heft scales it. Matches the shipped
	/// avatar's own throw so the verb is the one the player already knows.</summary>
	[Export(PropertyHint.Range, "0.0,20.0,0.1")] public float ThrowKeyForward { get; set; } = 7.5f;

	/// <summary>Upward speed the throw key adds, m/s, before heft scales it.</summary>
	[Export(PropertyHint.Range, "0.0,12.0,0.1")] public float ThrowKeyUp { get; set; } = 3.2f;

	/// <summary>Speed ceiling for a thrown item. Higher than <see cref="MaxThrowSpeed"/> because a
	/// throw is allowed to be faster than letting go; still a clamp, for the same reason
	/// MaxThrowSpeed is one -- it is what buys the right to leave continuous collision off.</summary>
	[Export(PropertyHint.Range, "1.0,40.0,0.1")] public float MaxKeyThrowSpeed { get; set; } = 14.0f;

	/// <summary>Settle duration for a snap release, seconds. Short — see the ease note in
	/// Interactable._Process. Past about 0.4 s the item is visibly flying to its slot on rails.</summary>
	[Export(PropertyHint.Range, "0.02,1.0,0.005")] public float SettleTime { get; set; } = 0.22f;

	/// <summary>
	/// What a Snap item does when released nowhere near a slot.
	///
	/// TRUE IS THE ONLY SANE DEFAULT AND IT IS WORTH SAYING WHY. The alternatives are to refuse the
	/// release (the player presses the button and nothing happens, which reads as a dropped input)
	/// or to leave the item frozen in mid-air (which reads as a bug and IS one). Falling back to a
	/// physics release means the mode is a preference about where things like to end up, not a cage.
	/// </summary>
	[Export] public bool SnapFallsBackToTumble { get; set; } = true;

	// --------------------------------------------------------------------------------- state

	private Area3D _reach = null!;
	private readonly HashSet<Interactable> _near = new();

	// ---- reused per-tick scratch ---------------------------------------------------------------
	//
	// ALLOCATED ONCE, NOT PER TICK. The first version built a scoring List, a
	// PhysicsRayQueryParameters3D and a Godot.Collections.Array<Rid> every physics step. Two of
	// those three are RefCounted ENGINE objects, so that is 120 native allocations a second, every
	// second, for a system whose entire pitch is that it costs nothing while you stand still —
	// and it showed up as objects still un-collected at process exit.
	//
	// Reusing them costs three fields and removes the garbage completely. The query object is
	// mutable; the only thing that has to be rebuilt is the exclude list, and only when the carrier
	// changes, which is never in practice.
	private readonly List<(float score, Interactable it)> _scored = new();
	private PhysicsRayQueryParameters3D _sightQuery = null!;
	private Godot.Collections.Array<Rid> _sightExclude = new();
	private Node3D? _excludeFor;

	public Interactable? Target { get; private set; }
	public Interactable? Carried { get; private set; }

	/// <summary>Why the last release ended where it did. For the lab readout, and for the self-test
	/// to assert against — a system whose decisions are not inspectable cannot be tested, only
	/// looked at.</summary>
	public string LastVerdict { get; private set; } = "—";

	private Vector3 _carryPos;
	private Vector3 _carryVel;
	private Basis _carryBasis = Basis.Identity;
	private Vector3 _anchorPrev;
	private Vector3 _anchorVelSmooth;
	private Vector3 _itemPrev;
	private Vector3 _itemVelSmooth;
	private float _lean;

	public override void _Ready()
	{
		// Proximity by broadphase, not by query. See the class header.
		_reach = new Area3D
		{
			Name = "Reach",
			Monitoring = true,
			// Nothing needs to detect the reach volume itself, and a monitorable area is a second
			// entry in every other area's pair list for no reason.
			Monitorable = false,
			CollisionLayer = 0,
			CollisionMask = InteractableLayer,
		};
		var shape = new CollisionShape3D { Shape = new SphereShape3D { Radius = ReachRadius } };
		_reach.AddChild(shape);
		AddChild(_reach);

		_reach.BodyEntered += OnBodyEntered;
		_reach.BodyExited += OnBodyExited;

		_sightQuery = new PhysicsRayQueryParameters3D { CollideWithAreas = false };
	}

	/// <summary>Resize the reach volume live. The SHAPE has to be rewritten, not just the export:
	/// the sphere was handed to the physics server at build time and a stale radius there is a
	/// slider that appears to work and changes nothing — the failure mode this repo's panels are
	/// specifically built to avoid.</summary>
	public void SetReachRadius(float r)
	{
		ReachRadius = r;
		foreach (var c in _reach.GetChildren())
			if (c is CollisionShape3D cs && cs.Shape is SphereShape3D sphere) sphere.Radius = r;
	}

	private void OnBodyEntered(Node3D body)
	{
		var it = Interactable.For(body);
		if (it != null) _near.Add(it);
	}

	private void OnBodyExited(Node3D body)
	{
		var it = Interactable.For(body);
		if (it != null)
		{
			_near.Remove(it);
			// Fade it out explicitly. An item that leaves the reach volume while highlighted would
			// otherwise keep its cue forever, because nothing else is left holding a reference to
			// it — the classic leak in every "highlight the nearest thing" implementation.
			//
			// THE CARRIED ITEM IS EXEMPT, and finding out why cost a capture. Grabbing sets the
			// held body's collision layer to 0 so it stops fighting the world (see
			// Interactable.OnGrabbed) — and a body that leaves every layer the reach area watches
			// fires `body_exited` immediately. So the act of picking something up reports it as
			// having left, and this handler dutifully faded the cue off the object the player was
			// holding, one tick after they grabbed it. The state readout said `cue=0.00/NOT DRAWN`
			// while `carrying=22 kg anvil`, which is the pair of facts that gives it away.
			if (it != Target && it != Carried) it.SetHover(false, HoverFadeTime);
		}
	}

	/// <summary>
	/// Drop every reference to the world without touching any of it.
	///
	/// FOR A CALLER THAT IS ABOUT TO FREE THE THINGS THIS IS HOLDING. The ordinary paths — SetTarget,
	/// TryRelease — all call back INTO the interactable to fade its cue or hand it to physics, and
	/// calling into an object that is queued for deletion is a crash rather than a no-op. So this
	/// assigns nulls directly and calls nothing.
	/// </summary>
	public void ForgetAll()
	{
		Carried = null;
		Target = null;
		_near.Clear();
		LastVerdict = "—";
	}

	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;
		// The area follows the carrier without being parented to it, so the carrier's own rotation
		// cannot skew a sphere that has no orientation to skew.
		if (Carrier != null) GlobalPosition = Carrier.GlobalPosition;

		if (Carried == null) UpdateTargeting();
		else UpdateCarry(dt);

		UpdateLean(dt);
	}

	// ----------------------------------------------------------------------------- targeting

	private void UpdateTargeting()
	{
		if (Camera == null) { SetTarget(null); return; }

		Vector3 eye = Camera.GlobalPosition;
		Vector3 fwd = -Camera.GlobalBasis.Z;
		float cosLimit = Mathf.Cos(Mathf.DegToRad(AimHalfAngleDeg));

		// One pass to score, then up to MaxSightChecks line-of-sight tests on the best few. Scoring
		// is arithmetic; sight tests are physics queries. Doing all the free work first is what
		// keeps the query count bounded regardless of how many props are in reach.
		_scored.Clear();
		foreach (var it in _near)
		{
			if (!GodotObject.IsInstanceValid(it) || it.Body == null) continue;
			Vector3 to = it.Body.GlobalPosition - eye;
			float d = to.Length();
			if (d < 0.001f) continue;
			float cos = fwd.Dot(to / d);
			if (cos < cosLimit) continue;

			float score = cos - d * DistanceWeight;
			if (it == Target) score += TargetStickiness;
			_scored.Add((score, it));
		}

		if (_scored.Count == 0) { SetTarget(null); return; }
		_scored.Sort((a, b) => b.score.CompareTo(a.score));

		int checks = Mathf.Min(MaxSightChecks, _scored.Count);
		for (int i = 0; i < checks; i++)
		{
			if (!RequireLineOfSight || HasLineOfSight(eye, _scored[i].it))
			{
				SetTarget(_scored[i].it);
				return;
			}
		}
		SetTarget(null);
	}

	private bool HasLineOfSight(Vector3 eye, Interactable it)
	{
		var space = GetWorld3D().DirectSpaceState;

		// THE CARRIER IS EXCLUDED because the character stands between the camera and everything it
		// is about to pick up. Without this the system is unusable at every camera pitch that puts
		// the capsule in front of the prop, which under a loose over-the-shoulder camera is most of
		// them — and the symptom is "targeting randomly stops working when I turn", which sends you
		// looking at the scoring code rather than at one missing exclusion.
		//
		// Rebuilt only when the carrier changes, so the steady state allocates nothing.
		if (!ReferenceEquals(_excludeFor, Carrier))
		{
			_excludeFor = Carrier;
			_sightExclude = new Godot.Collections.Array<Rid>();
			if (Carrier is CollisionObject3D co) _sightExclude.Add(co.GetRid());
			_sightQuery.Exclude = _sightExclude;
		}

		_sightQuery.From = eye;
		_sightQuery.To = it.Body.GlobalPosition;
		_sightQuery.CollisionMask = SightMask;

		var hit = space.IntersectRay(_sightQuery);
		if (hit.Count == 0) return true;
		// A ray to an object's CENTRE hits the object itself. Anything else in the way means the
		// object is behind something.
		return hit["collider"].As<GodotObject>() is Node3D n && Interactable.For(n) == it;
	}

	private void SetTarget(Interactable? next)
	{
		if (next == Target) return;
		Target?.SetHover(false, HoverFadeTime);
		Target = next;
		Target?.SetHover(true, HoverFadeTime);
	}

	// --------------------------------------------------------------------------------- carry

	/// <summary>0 for weightless, 1 at or above <see cref="MassReference"/>. Every weight dial in
	/// the system reads this and nothing else.</summary>
	public float HeftOf(Interactable it) => Mathf.Clamp(it.HeftKg / MassReference, 0.0f, 1.0f);

	public bool TryGrab()
	{
		if (Carried != null || Target == null || Carrier == null) return false;

		var it = Target;
		float heft = HeftOf(it);

		it.OnGrabbed(CollideWhileHeld);
		Carried = it;
		SetTarget(null);
		// The cue stays lit on the item you are holding. It is still the object the button acts on,
		// so turning the highlight off at the moment of grab would be the cue lying about state.
		it.SetHover(true, HoverFadeTime);

		// Seed the spring AT THE ITEM'S CURRENT POSE, not at the hand. Seeding at the target makes
		// the item teleport into the hand on frame one, which throws away the one frame in which
		// the grab is most readable.
		_carryPos = it.Body.GlobalPosition;
		_carryBasis = it.Body.GlobalBasis.Orthonormalized();
		_carryVel = Vector3.Zero;
		_itemPrev = _carryPos;
		_itemVelSmooth = Vector3.Zero;
		_anchorPrev = HandAnchor(it);
		_anchorVelSmooth = Vector3.Zero;

		// Heavier things ring slower and for longer, from the same heft the spring reads.
		float hzScale = Mathf.Lerp(1.0f, 0.55f, heft * SquashMassResponse);
		float decayScale = Mathf.Lerp(1.0f, 0.65f, heft * SquashMassResponse);
		it.Kick(GrabSquash, GrabSquashHz * hzScale, GrabSquashDecay * decayScale);

		Audio?.Grab(heft);
		LastVerdict = $"grabbed {it.Label} ({it.HeftKg:0.##} kg, heft {heft:0.00})";
		return true;
	}

	private Vector3 HandAnchor(Interactable it)
	{
		if (Carrier == null) return GlobalPosition;
		var b = Carrier.GlobalBasis;
		float heft = HeftOf(it);
		return Carrier.GlobalPosition + b * (HandOffset + it.HoldOffset) - Vector3.Up * (CarryDroop * heft);
	}

	private void UpdateCarry(float dt)
	{
		var it = Carried!;
		if (!GodotObject.IsInstanceValid(it) || Carrier == null) { Carried = null; return; }

		float heft = HeftOf(it);
		float omega = Mathf.Lerp(LightResponse, HeavyResponse, heft);

		Vector3 anchor = HandAnchor(it);
		Vector3 anchorVel = dt > 0.0f ? (anchor - _anchorPrev) / dt : Vector3.Zero;
		_anchorPrev = anchor;
		_anchorVelSmooth = _anchorVelSmooth.Lerp(anchorVel, Smoothing(SwingSmoothing, dt));

		// ---- position: one critically damped spring ----------------------------------------
		Spring(ref _carryPos, ref _carryVel, anchor, omega, dt);

		// ---- rotation: the carry pose, plus a trailing tilt ---------------------------------
		Basis target = Carrier.GlobalBasis * Basis.FromEuler(it.HoldRotationDegrees * (Mathf.Pi / 180.0f));

		Vector3 v = _anchorVelSmooth;
		float speed = v.Length();
		if (speed > 0.01f)
		{
			// AXIS = velocity CROSS up, so the tilt trails the motion instead of leading it. The
			// opposite order leans the item INTO its direction of travel, which reads as the item
			// dragging the character along and is one sign flip away from correct — the kind of
			// mistake that looks like a tuning problem rather than a bug.
			Vector3 axis = v.Cross(Vector3.Up);
			if (axis.LengthSquared() > 1e-6f)
			{
				float amt = Mathf.Clamp(speed / SwingReferenceSpeed, 0.0f, 1.0f) * heft;
				float ang = Mathf.DegToRad(SwingDegrees) * amt;
				target = new Basis(axis.Normalized(), ang) * target;
			}
		}

		_carryBasis = _carryBasis.Orthonormalized()
			.Slerp(target.Orthonormalized(), Smoothing(1.0f / (omega * RotationResponse), dt))
			.Orthonormalized();

		it.Body.GlobalTransform = new Transform3D(_carryBasis, _carryPos);

		// The item's OWN velocity, not the hand's, is what a release inherits. A heavy item that
		// lagged behind the hand should leave at the speed it was actually travelling — which is
		// the whole reason the lag is worth simulating rather than faking with an offset.
		Vector3 itemVel = dt > 0.0f ? (_carryPos - _itemPrev) / dt : Vector3.Zero;
		_itemPrev = _carryPos;
		_itemVelSmooth = _itemVelSmooth.Lerp(itemVel, Smoothing(0.06f, dt));
	}

	private void UpdateLean(float dt)
	{
		if (CarrierVisual == null) return;
		float want = 0.0f;
		if (Carried != null && GodotObject.IsInstanceValid(Carried))
		{
			// Lean AWAY from the carried side: the sign follows the hand offset, so moving the hand
			// across the body flips the lean without a second dial to keep in sync.
			want = -Mathf.Sign(HandOffset.X + Carried.HoldOffset.X) * CarryLeanDegrees * HeftOf(Carried);
		}
		_lean = Mathf.Lerp(_lean, want, Smoothing(0.28f, dt));
		CarrierVisual.RotationDegrees = new Vector3(0, 0, _lean);
	}

	// ------------------------------------------------------------------------------- release

	public bool TryRelease()
	{
		if (Carried == null) return false;
		var it = Carried;
		Carried = null;
		it.SetHover(false, HoverFadeTime);

		float heft = HeftOf(it);

		if (it.Release == Interactable.ReleaseMode.Snap)
		{
			var verdict = InteractionSlot.FindBest(it.Body.GlobalPosition, it,
				out var accepted, out var rejected);

			if (verdict == InteractionSlot.Verdict.Accepted && accepted != null)
			{
				it.OnSnapReleased(accepted, it.Body.GlobalTransform, SettleTime);
				accepted.Flash(false);
				Audio?.Place(heft);
				LastVerdict = $"SNAP {it.Label} -> {accepted.Name}";
				return true;
			}

			// Rejected, or nothing in range. Either way the item has to go somewhere real.
			if (rejected != null)
			{
				rejected.Flash(true);
				Audio?.Reject();
				LastVerdict = verdict == InteractionSlot.Verdict.Occupied
					? $"REJECTED {it.Label}: {rejected.Name} occupied -> tumble"
					: $"REJECTED {it.Label}: tag '{it.Tag}' != '{rejected.AcceptTag}' -> tumble";
			}
			else
			{
				LastVerdict = $"SNAP {it.Label}: no slot in range -> tumble";
			}

			if (!SnapFallsBackToTumble)
			{
				// Even with the fallback disabled the item is handed back to physics. See the
				// SnapFallsBackToTumble note: the alternative is a frozen prop in mid-air, and
				// there is no configuration in which that is the behaviour somebody wanted.
				it.OnTumbleReleased(Vector3.Zero, Vector3.Zero);
				return true;
			}
		}

		Tumble(it, heft);
		return true;
	}

	/// <summary>
	/// The throw key. ALWAYS TUMBLES, even for an item in Snap mode.
	///
	/// A throw is a decision to send the thing somewhere physics chooses, so a snap that swallowed
	/// one -- item leaves the hand, clicks neatly into the slot at the player's elbow -- would read
	/// as the key not working, and the player would press it again.
	///
	/// HEFT GOVERNS THE THROW, because heft governs everything else (see MassReference) and the
	/// throw is this system's loudest single action. A 22 kg anvil leaves the hand at about a third
	/// of the 0.4 kg block's speed. Without this scaling the one verb that most obviously ought to
	/// know what it is holding would be the one verb that does not.
	/// </summary>
	public bool TryThrow()
	{
		if (Carried == null) return false;

		var it = Carried;
		Carried = null;
		it.SetHover(false, HoverFadeTime);

		float heft = HeftOf(it);
		Vector3 fwd = Camera != null ? -Camera.GlobalBasis.Z : -GlobalBasis.Z;
		fwd = new Vector3(fwd.X, 0, fwd.Z).Normalized();

		float scale = Mathf.Lerp(1.0f, 0.35f, heft);
		Vector3 impulse = (fwd * ThrowKeyForward + Vector3.Up * ThrowKeyUp) * scale;

		Tumble(it, heft, impulse, MaxKeyThrowSpeed, "THROW");
		return true;
	}

	private void Tumble(Interactable it, float heft, Vector3 extraImpulse = default,
		float speedCap = -1.0f, string verb = "TUMBLE")
	{
		Vector3 fwd = Camera != null ? -Camera.GlobalBasis.Z : -GlobalBasis.Z;
		fwd = new Vector3(fwd.X, 0, fwd.Z).Normalized();

		Vector3 linear = _itemVelSmooth * ThrowScale + fwd * ThrowForward + extraImpulse;
		float cap = speedCap > 0.0f ? speedCap : MaxThrowSpeed;
		if (linear.Length() > cap) linear = linear.Normalized() * cap;

		// Spin about the axis the swing was already tilting on, so the tumble continues the motion
		// the carry established rather than starting a new one. Falling back to the item's own
		// right axis when it was stationary keeps a standing drop from landing perfectly flat.
		Vector3 v = _itemVelSmooth;
		Vector3 axis = v.LengthSquared() > 0.01f ? v.Cross(Vector3.Up).Normalized() : it.Body.GlobalBasis.X;
		Vector3 angular = axis * (ReleaseSpin + v.Length() * ThrowSpin);

		it.OnTumbleReleased(linear, angular);
		Audio?.Place(heft);
		if (!LastVerdict.StartsWith("REJECTED") && !LastVerdict.Contains("no slot"))
			LastVerdict = $"{verb} {it.Label} at {linear.Length():0.00} m/s";
	}

	// --------------------------------------------------------------------------------- maths

	/// <summary>
	/// A critically damped spring, in the closed form that is unconditionally stable.
	///
	/// The naive integration (<c>v += (k*x - c*v) * dt</c>) blows up as soon as <c>omega * dt</c>
	/// approaches 1, which for a snappy light item at 60 Hz is a value the tuning range reaches. It
	/// does not blow up quietly: the item flies to infinity and the frame after that the transform
	/// is NaN and the object is gone. This form is exact enough at any dt and any omega, which is
	/// what lets the response dials be exposed at all.
	/// </summary>
	private static void Spring(ref Vector3 x, ref Vector3 v, Vector3 target, float omega, float dt)
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
	/// A bare <c>Lerp(a, b, 0.1f)</c> smooths TWICE AS FAST at 120 Hz as at 60, so every feel dial
	/// in a system that uses one is secretly a function of the player's monitor. This is the
	/// exponential form, and it costs one <c>exp</c>.
	/// </summary>
	private static float Smoothing(float tau, float dt) =>
		tau <= 0.0f ? 1.0f : 1.0f - Mathf.Exp(-dt / tau);

	/// <summary>For the readout: how far the carried item is currently trailing its hand.</summary>
	public float CarryLag => Carried != null && GodotObject.IsInstanceValid(Carried)
		? _carryPos.DistanceTo(HandAnchor(Carried)) : 0.0f;

	public int NearCount => _near.Count;
}
