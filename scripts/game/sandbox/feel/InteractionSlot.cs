using Godot;
using System.Collections.Generic;

namespace MpFoundation.Game.Sandbox.Feel;

/// <summary>
/// A PLACE SOMETHING GOES. Drop one of these anywhere in a scene and any interactable released in
/// <c>Snap</c> mode near it will settle into it.
///
/// GENERAL-PURPOSE AND PROJECT-NEUTRAL. A slot knows a radius, an optional tag, and whether it is
/// full. It knows nothing about what kind of object the game thinks it holds.
///
/// -----------------------------------------------------------------------------------------------
/// WHY THIS IS A STATIC REGISTRY AND NOT AN Area3D
/// -----------------------------------------------------------------------------------------------
/// The obvious build gives every slot an Area3D and lets the physics broadphase report overlaps.
/// That is the right answer for HOVER TARGETING, where the candidate set changes continuously and
/// the broadphase is already running anyway (see <see cref="Interactor"/>). It is the wrong answer
/// here, and the difference is the query rate:
///
///   * hover targeting asks "what is near me" EVERY FRAME, forever
///   * slot matching asks "what is near this point" ONCE, at the instant of a release
///
/// A slot Area3D pays broadphase pair maintenance every physics tick, for a question asked a couple
/// of times a minute. A flat list of slots and a squared-distance compare costs nothing at all
/// until the moment of release, and then costs O(slots) — where "slots" is a handful in any scene
/// where this concept makes sense. On a low-end part the free option is free and the other one is
/// not, so the free one wins by default (CLAUDE.md §0).
///
/// The cost that IS real, stated rather than glossed: this is a linear scan, so a level with
/// thousands of slots would want spatial partitioning. Nothing in this design forbids that — the
/// scan lives in exactly one method.
///
/// -----------------------------------------------------------------------------------------------
/// THE SLOT OWNS ITS OWN MARKER
/// -----------------------------------------------------------------------------------------------
/// A slot that is invisible is a slot whose radius nobody can judge, and this is a lab. The marker
/// is built here rather than by the stage so that dropping a slot into ANY scene shows where it is
/// and how big it is, with no second thing to remember to add. It carries its own material because
/// it flashes independently of every other slot; that is lab furniture, not shipping geometry, and
/// a handful of small materials is not a cost worth defending against.
/// </summary>
public partial class InteractionSlot : Node3D
{
	/// <summary>
	/// Every slot currently in the tree. Registered in _EnterTree rather than _Ready so a slot is
	/// findable before its children have run, and unregistered in _ExitTree so a freed slot cannot
	/// be handed back to a caller — a stale entry here would be a use-after-free reachable from
	/// gameplay code, which is the one failure mode a static registry actually invites.
	/// </summary>
	private static readonly List<InteractionSlot> Registry = new();

	/// <summary>
	/// HORIZONTAL catch radius. Adjustable PER SLOT, per the brief — a wall hook wants a tight
	/// radius and a table wants a loose one.
	/// </summary>
	/// The setter resizes the marker. A radius slider that moved the NUMBER but not the ring drawn
	/// at it would be a control that appears to work and lies about what it did — the exact failure
	/// this repo builds its panels to make impossible.
	[Export(PropertyHint.Range, "0.05,3.0,0.01")]
	public float Radius
	{
		get => _radius;
		set { _radius = value; ResizeMarker(); }
	}
	private float _radius = 0.32f;

	/// <summary>
	/// How far ABOVE the slot an item may be released and still be caught.
	///
	/// -------------------------------------------------------------------------------------------
	/// THE CATCH VOLUME IS A VERTICAL CYLINDER, NOT A SPHERE, AND THAT IS NOT A REFINEMENT — A
	/// SPHERE DOES NOT WORK AT ALL
	/// -------------------------------------------------------------------------------------------
	/// The hand carries at roughly chest height. A slot sits on a bench at roughly waist height.
	/// Those are about 0.4 m apart VERTICALLY even when the character is standing directly over the
	/// slot with the item perfectly placed. A spherical catch volume therefore has to have a radius
	/// larger than that vertical gap before it can ever fire — by which point it is 0.5 m wide
	/// horizontally too, and every slot on a bench overlaps every other one.
	///
	/// Splitting the axes fixes it and is also the better model of the gesture: you put something
	/// down by holding it OVER the place and letting go. Vertical alignment is approximate and
	/// generous; horizontal alignment is what the player is actually aiming, and stays tight.
	///
	/// Below the slot is a different case and gets the horizontal radius instead: releasing an item
	/// from beneath a shelf and having it fly up into it is not a gesture anybody makes.
	/// </summary>
	[Export(PropertyHint.Range, "0.05,3.0,0.01")] public float VerticalReach { get; set; } = 1.1f;

	/// <summary>
	/// Empty accepts anything. Otherwise the item's <see cref="Interactable.Tag"/> must match.
	///
	/// A STRING, DELIBERATELY, rather than an enum or a type check. An enum would have to enumerate
	/// a specific game's item kinds, which is exactly the coupling this lab is forbidden to have.
	/// </summary>
	[Export] public string AcceptTag { get; set; } = "";

	/// <summary>Whether a settled item takes the slot's rotation as well as its position. Off is
	/// for slots where the object's own facing carries meaning and only its place is being tidied
	/// — a shelf that accepts a book either way up.</summary>
	[Export] public bool SnapRotation { get; set; } = true;

	/// <summary>Vertical offset applied to the settled pose, so a slot node can sit ON a surface
	/// while the object it holds sits above it by half its own height.</summary>
	[Export(PropertyHint.Range, "-1.0,1.0,0.005")] public float RestHeight { get; set; } = 0.0f;

	/// <summary>Null when free. Set by <see cref="Interactor"/> at the moment a settle begins, not
	/// when it completes: a slot that is still free during the settle animation could be claimed by
	/// a second item mid-flight, and two items would settle into the same pose.</summary>
	public Interactable? Occupant { get; private set; }

	public bool IsFree => Occupant == null || !GodotObject.IsInstanceValid(Occupant);

	/// <summary>Where an item settles to. Public so the interactor can drive the animation without
	/// knowing anything about how the slot is built.</summary>
	public Transform3D RestPose(Transform3D releasePose)
	{
		var basis = SnapRotation ? GlobalBasis : releasePose.Basis;
		return new Transform3D(basis, GlobalPosition + Vector3.Up * RestHeight);
	}

	public enum Verdict
	{
		/// <summary>Nothing within radius. The item should fall back to a physics release.</summary>
		NoSlotInRange,
		/// <summary>A slot is in range and will take it.</summary>
		Accepted,
		/// <summary>A slot is in range but already holds something.</summary>
		Occupied,
		/// <summary>A slot is in range and free, but the tags disagree.</summary>
		WrongTag,
	}

	/// <summary>
	/// The whole matching rule, in one place.
	///
	/// IT REPORTS WHY IT SAID NO, and that is not politeness — the brief asks for occupied-slot
	/// feedback, and "the item tumbled onto the floor" is indistinguishable from "there was no slot
	/// there" unless the near-miss is reported. So the scan tracks the nearest slot that REJECTED
	/// as well as the nearest that accepted, and hands the rejection back when nothing accepted.
	/// </summary>
	public static Verdict FindBest(Vector3 point, Interactable item,
		out InteractionSlot? accepted, out InteractionSlot? rejected)
	{
		accepted = null;
		rejected = null;
		float bestAccept = float.MaxValue;
		float bestReject = float.MaxValue;
		var worst = Verdict.NoSlotInRange;

		foreach (var s in Registry)
		{
			if (!GodotObject.IsInstanceValid(s)) continue;

			Vector3 d = point - s.GlobalPosition;
			// Squared horizontal distance against squared radius: no sqrt in the scan. It is one
			// release, so this is not where the money is — but a scan that is obviously free needs
			// no argument about how often it runs.
			float d2 = d.X * d.X + d.Z * d.Z;
			if (d2 > s.Radius * s.Radius) continue;
			// See VerticalReach: generous above, tight below.
			if (d.Y > s.VerticalReach || d.Y < -s.Radius) continue;

			// RANKED ON HORIZONTAL DISTANCE ALONE. Ranking on the full 3D distance would let a
			// slot the player is standing directly over lose to a neighbouring one simply because
			// the item happened to be held lower — which is the axis the design has just declared
			// approximate.

			bool free = s.IsFree;
			bool tagOk = s.AcceptTag.Length == 0 || s.AcceptTag == item.Tag;

			if (free && tagOk)
			{
				if (d2 < bestAccept) { bestAccept = d2; accepted = s; }
			}
			else if (d2 < bestReject)
			{
				bestReject = d2;
				rejected = s;
				worst = free ? Verdict.WrongTag : Verdict.Occupied;
			}
		}

		return accepted != null ? Verdict.Accepted : worst;
	}

	public void Claim(Interactable item) => Occupant = item;

	public void Release(Interactable item)
	{
		if (Occupant == item) Occupant = null;
	}

	/// <summary>Every slot in the tree, for panels and self-tests. A copy, so a caller iterating it
	/// cannot be tripped by a slot freeing itself mid-loop.</summary>
	public static IReadOnlyList<InteractionSlot> All => Registry.ToArray();

	// --------------------------------------------------------------------------------- marker

	private MeshInstance3D? _marker;
	private StandardMaterial3D? _markerMat;
	private float _flash;
	private Color _flashColor;

	private static readonly Color FreeColor = new(0.42f, 0.62f, 0.52f);
	private static readonly Color FullColor = new(0.40f, 0.44f, 0.52f);
	private static readonly Color RejectColor = new(0.86f, 0.34f, 0.26f);

	/// <summary>Set false for a slot whose presence should not be advertised. On in the lab,
	/// because a lab that hides its instrument is not one.</summary>
	[Export] public bool ShowMarker { get; set; } = true;

	public override void _EnterTree() => Registry.Add(this);

	public override void _ExitTree() => Registry.Remove(this);

	/// <summary>The marker's node name. A LEVEL may author a child of this name and this class
	/// will adopt it rather than building one — see <see cref="_Ready"/>.</summary>
	public const string MarkerNodeName = "SlotMarker";

	public override void _Ready()
	{
		if (!ShowMarker) { SetProcess(false); return; }

		// A LEVEL MAY BRING ITS OWN MARKER, and if it has, this class MUST NOT build a second
		// one (HOLD-1, 2026-09-19). SupermarketWorldSelfTest counts a room's PACKED nodes against
		// its LIVE ones and fails on any difference, so a slot dropped into a level scene adds a
		// node the .tscn does not declare and turns the suite red — measured here, exactly that,
		// 87 packed / 88 live on HoldingRoom.tscn.
		//
		// ShowMarker = false is NOT a way out, and that is the finding worth keeping: the export
		// was authored on an INLINE node (script set directly, not a nested PackedScene instance)
		// and it still read back as its C# default. This project's Godot/Mono build drops an
		// exported C# property set in a .tscn in BOTH shapes, not only the nested-instance one
		// that PropManager.AuthoredKindOf, Carryable.LoadLiftM and RoundClock.Room recorded.
		//
		// So the level authors a child called SlotMarker and this adopts it: same flash, same
		// state colours, no node created. The alternative would have been a fourth payment for
		// the same trap.
		var authored = GetNodeOrNull<MeshInstance3D>(MarkerNodeName);
		if (authored != null)
		{
			_marker = authored;
			_markerMat = authored.MaterialOverride as StandardMaterial3D;
			if (_markerMat != null && !_markerMat.ResourceLocalToScene)
			{
				// Duplicated for RoundClock's reason: a shared material means the last slot to
				// _Ready decides the colour of all of them, and these flash independently.
				_markerMat = (StandardMaterial3D)_markerMat.Duplicate();
				authored.MaterialOverride = _markerMat;
			}
			RefreshMarker();
			SetProcess(false);
			return;
		}

		// A THIN FLAT RING AT THE CATCH RADIUS. It was a filled disc first, and the first capture of
		// the stage settled it: three translucent discs overlapping on a bench read as spilled
		// liquid, and — worse for an instrument — they covered the props sitting in them. A ring
		// states the same radius, leaves the middle clear, and stays legible where two of them
		// overlap.
		var ring = new TorusMesh
		{
			InnerRadius = Radius * 0.88f,
			OuterRadius = Radius,
			Rings = 28,
			RingSegments = 5,
		};
		_markerMat = new StandardMaterial3D
		{
			AlbedoColor = FreeColor,
			// Unshaded so the marker's colour is the STATE and nothing else. A lit marker changes
			// colour as the character walks past the lamp, which is a state change that is not one.
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
		};
		_markerMat.AlbedoColor = new Color(FreeColor, 0.75f);
		_marker = new MeshInstance3D
		{
			Name = "SlotMarker",
			Mesh = ring,
			MaterialOverride = _markerMat,
			Position = new Vector3(0, 0.012f, 0),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		AddChild(_marker);
		SetProcess(false);
	}

	/// <summary>A short colour flash. The interactor calls this on a rejected release; the slot owns
	/// the animation so that every rejection looks the same wherever it was triggered from.</summary>
	public void Flash(bool rejected)
	{
		_flash = 1.0f;
		_flashColor = rejected ? RejectColor : FreeColor;
		// Processing is enabled only while there is something to animate. A hundred idle slots cost
		// exactly nothing per frame, which is the point of turning it back off at the end.
		SetProcess(_marker != null);
	}

	public override void _Process(double delta)
	{
		if (_markerMat == null) return;

		_flash = Mathf.Max(0.0f, _flash - (float)delta * 2.6f);
		var basis = IsFree ? FreeColor : FullColor;
		// Squared falloff: a linear fade reads as a slow dimming, a squared one reads as a flash.
		var c = basis.Lerp(_flashColor, _flash * _flash);
		_markerMat.AlbedoColor = new Color(c, Mathf.Lerp(0.6f, 1.0f, _flash));

		if (_flash <= 0.0f) SetProcess(false);
	}

	private void ResizeMarker()
	{
		if (_marker?.Mesh is TorusMesh t)
		{
			t.InnerRadius = _radius * 0.88f;
			t.OuterRadius = _radius;
		}
	}

	/// <summary>Repaint once without animating — for the occupied/free colour changing while
	/// nothing is flashing.</summary>
	public void RefreshMarker()
	{
		if (_markerMat == null) return;
		_markerMat.AlbedoColor = new Color(IsFree ? FreeColor : FullColor, 0.75f);
	}
}
