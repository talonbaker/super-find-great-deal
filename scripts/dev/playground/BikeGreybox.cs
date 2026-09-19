using Godot;
using MpFoundation.Game.Sandbox;

namespace MpFoundation.Dev.Playground;

/// <summary>
/// <b>A bike you can see, and a mount you can see</b> (BIKE-0, 2026-09-01). Two rings and a bar.
/// Folded flat on the body's back like a backpack until it is summoned; then it comes off the
/// back, arcs forward and down, <b>springs open</b> with an overshoot, and lands under the body
/// as the body hops onto it (<see cref="BikeLayer"/> supplies the hop). On a dismount it snaps
/// shut and flies back onto the back. On a landing it squashes and recovers. The wheels spin with
/// the ground speed.
///
/// <para>Talon: <i>"a folding, travel bike worn on the back like a backpack. This would come off
/// the back and then spring open and the player character would jump onto it and begin riding.
/// Smooth and fluid... really nice feeling nice juice."</i> Greybox on purpose — no asset is
/// wanted; the read is the motion, not the mesh.</para>
///
/// <para>It is a sibling of the avatar in the scene, never its child: it follows the body's
/// transform every frame rather than living inside a hierarchy another system also writes. Every
/// number it animates on is a <see cref="BikeTuning"/> juice row and changes nothing about where
/// the body goes.</para>
/// </summary>
public partial class BikeGreybox : Node3D
{
    private const float WheelRadiusM = 0.34f;
    private const float WheelbaseM = 1.05f;
    private const float FoldedWheelbaseM = 0.10f;
    private const float BackScale = 0.62f;

    private Node3D _rig = null!;
    private Node3D _front = null!;
    private Node3D _rear = null!;
    private MeshInstance3D _frame = null!;
    private MeshInstance3D _post = null!;
    private MeshInstance3D _bars = null!;
    private float _pitch;
    private float _spin;
    private float _open;   // 0 folded, 1 open, may overshoot

    /// <summary>The wheels' disc normal in the greybox's own frame — the self-test's instrument
    /// for "which way do the coins face". The tori stand in the rig's local YZ plane (their ring
    /// is rotated 90° about Z in <see cref="Wheel"/>), so the disc normal is the rig's local ±X,
    /// pushed through the rig's rotation. Riding, it is the greybox's ±X (wheels upright, rolling
    /// along the frame); stowed, it must be the greybox's ±Z — faces toward the wearer's back —
    /// or the stack sits edge-on (the BIKE-3A defect).</summary>
    public Vector3 DiscNormalLocal => (_rig.Basis * new Vector3(1f, 0f, 0f)).Normalized();

    public override void _Ready()
    {
        _rig = new Node3D { Name = "Rig" };
        AddChild(_rig);

        var wheelMat = new StandardMaterial3D { AlbedoColor = new Color(0.12f, 0.12f, 0.13f) };
        var frameMat = new StandardMaterial3D { AlbedoColor = new Color(0.86f, 0.42f, 0.16f) };

        _front = Wheel("Front", wheelMat);
        _rear = Wheel("Rear", wheelMat);

        _frame = new MeshInstance3D
        {
            Name = "Frame",
            Mesh = new BoxMesh { Size = new Vector3(0.06f, 0.06f, 1f) },
            MaterialOverride = frameMat,
            Position = new Vector3(0f, WheelRadiusM + 0.22f, 0f),
        };
        _rig.AddChild(_frame);

        _post = new MeshInstance3D
        {
            Name = "SeatPost",
            Mesh = new BoxMesh { Size = new Vector3(0.06f, 0.36f, 0.06f) },
            MaterialOverride = frameMat,
        };
        _rig.AddChild(_post);

        _bars = new MeshInstance3D
        {
            Name = "Handlebar",
            Mesh = new BoxMesh { Size = new Vector3(0.50f, 0.05f, 0.05f) },
            MaterialOverride = frameMat,
        };
        _rig.AddChild(_bars);

        SetOpen(0f);
    }

    private Node3D Wheel(string name, Material mat)
    {
        // The pivot spins about X (the axle); the torus inside it stands in YZ.
        var pivot = new Node3D { Name = name, Position = new Vector3(0f, WheelRadiusM, 0f) };
        var ring = new MeshInstance3D
        {
            Name = "Ring",
            Mesh = new TorusMesh { InnerRadius = WheelRadiusM - 0.05f, OuterRadius = WheelRadiusM },
            MaterialOverride = mat,
            RotationDegrees = new Vector3(0f, 0f, 90f),
        };
        pivot.AddChild(ring);
        // One spoke so the spin can be seen.
        var spoke = new MeshInstance3D
        {
            Name = "Spoke",
            Mesh = new BoxMesh { Size = new Vector3(0.03f, WheelRadiusM * 1.9f, 0.03f) },
            MaterialOverride = mat,
        };
        pivot.AddChild(spoke);
        _rig.AddChild(pivot);
        return pivot;
    }

    /// <summary>Lay the parts out for a given openness. 0 is the folded backpack: wheels stacked,
    /// no frame length. 1 is the bike. Above 1 is the spring's overshoot.</summary>
    private void SetOpen(float open)
    {
        _open = open;
        float wb = Mathf.Lerp(FoldedWheelbaseM, WheelbaseM, Mathf.Max(open, 0f));
        _front.Position = new Vector3(0f, WheelRadiusM, -wb * 0.5f);
        _rear.Position = new Vector3(0f, WheelRadiusM, wb * 0.5f);
        _frame.Scale = new Vector3(1f, 1f, Mathf.Max(wb, 0.05f));
        float rise = Mathf.Clamp(open, 0f, 1f);
        _post.Position = new Vector3(0f, WheelRadiusM + 0.22f + 0.18f * rise, wb * 0.25f);
        _post.Scale = new Vector3(1f, Mathf.Max(rise, 0.08f), 1f);
        _bars.Position = new Vector3(0f, WheelRadiusM + 0.22f + 0.36f * rise, -wb * 0.42f);
        _bars.Scale = new Vector3(Mathf.Max(rise, 0.08f), 1f, 1f);
    }

    /// <summary>Follow the body. Called from the playground's <c>_Process</c>.</summary>
    public void Track(BikeLayer? bike, SandboxAvatar avatar, float dt)
    {
        BikeTuning b = BikeTuning.Current;

        // The body's real facing: AvatarMotor.Step writes the CharacterBody3D's yaw every tick
        // (see BikeLayer.FacingOf, remeasured 2026-09-02 — its old "never turns" claim was false
        // and froze this greybox at world -Z, Talon's compass needle).
        Vector3 forward = BikeLayer.FacingOf(avatar);
        if (forward.LengthSquared() < 1e-6f)
            forward = Vector3.Forward;
        forward = forward.Normalized();
        float yaw = Mathf.Atan2(-forward.X, -forward.Z);

        bool mounted = bike?.Mounted ?? false;
        float since = bike?.SinceToggleSec ?? 99f;

        // Two anchors, in the body's frame: on the back, and under the body.
        Vector3 backPos = avatar.GlobalPosition - forward * 0.32f + new Vector3(0f, 0.75f, 0f);
        Vector3 groundPos = avatar.GlobalPosition;

        // Floor pitch along the heading, smoothed so a box collider's normal jitter does not shake
        // the frame.
        float targetPitch = 0f;
        if (mounted && avatar.IsOnFloor())
        {
            Vector3 n = avatar.GetFloorNormal();
            if (n.IsFinite() && n.LengthSquared() > 0.5f)
                targetPitch = Mathf.Asin(Mathf.Clamp(n.Dot(forward), -1f, 1f));
        }
        _pitch = Mathf.Lerp(_pitch, targetPitch, Mathf.Clamp(dt * 12f, 0f, 1f));

        Rotation = new Vector3(0f, yaw, 0f);

        float swing = bike?.SwingFraction ?? 0f;
        if (!mounted && swing > 0f)
        {
            // THE SWING (BIKE-1c): off the back, one full turn round the body at hip height,
            // reaching out to SwingReachM across the front, and back onto the back. Open while it
            // is out - a swung bike is a swung bike, not a folded one - and rolled on its side so
            // the wheels face the target. The hit cast fires at the half-way point, which is where
            // the reach peaks.
            Vector2 off = BikeRig.SwingOffset(swing, b);
            Vector3 right = new Vector3(forward.Z, 0f, -forward.X);
            Vector3 pos = avatar.GlobalPosition + right * off.X + forward * off.Y + new Vector3(0f, 0.95f, 0f);
            GlobalPosition = pos;
            SetOpen(Mathf.Clamp(Mathf.Sin(swing * Mathf.Pi) * 1.6f, 0f, 1f));
            // Point the frame along the sweep's tangent, lying on its side.
            float a = swing * Mathf.Tau;
            _rig.Rotation = new Vector3(0f, -a, Mathf.DegToRad(75f));
            _rig.Scale = Vector3.One * Mathf.Lerp(BackScale, 1f, Mathf.Sin(swing * Mathf.Pi));
            _spin += 25f * dt;
            _front.Rotation = new Vector3(_spin, 0f, 0f);
            _rear.Rotation = new Vector3(_spin, 0f, 0f);
            return;
        }

        if (mounted)
        {
            // Off the back, over an arc, springing open, landing under the body.
            float t = b.UnfoldSec <= 0f ? 1f : Mathf.Clamp(since / b.UnfoldSec, 0f, 1f);
            float travel = 1f - (1f - t) * (1f - t);                   // ease-out on the flight
            Vector3 pos = backPos.Lerp(groundPos, travel);
            pos.Y += Mathf.Sin(t * Mathf.Pi) * b.UnfoldArcM;             // the arc
            GlobalPosition = pos;

            SetOpen(BikeRig.UnfoldSpring(t, b));

            // On the back it is stood on its end AND yawed a quarter turn, so the folded stack's
            // coin faces press flat against the back (the wheels' disc normals are the rig's
            // local X; the yaw carries them onto the greybox's forward axis — with Godot's YXZ
            // Euler order the yaw is applied outside the pitch, and the X axis is invariant under
            // the pitch, so the normals land exactly on ±Z at stow). Without the yaw they stay on
            // ±X and the stack sits edge-on, "fed into a piggy bank slot" (BIKE-3A, 2026-09-02).
            // Both angles ride the same travel scalar, so the unfold flight unwinds them together.
            float lay = Mathf.DegToRad(90f) * (1f - travel);
            _rig.Rotation = new Vector3(_pitch + lay, lay, 0f);
            float scale = Mathf.Lerp(BackScale, 1f, travel);

            // Landing squash: down and wide on touchdown, back to round over 0.18 s.
            float sinceLand = bike?.SinceTouchdownSec ?? 99f;
            if (sinceLand < 0.18f && t >= 1f)
            {
                float k = sinceLand / 0.18f;
                float y = Mathf.Lerp(b.LandSquash, 1f, k);
                float xz = 1f / Mathf.Sqrt(Mathf.Max(y, 0.1f));
                _rig.Scale = new Vector3(scale * xz, scale * y, scale * xz);
            }
            else
            {
                _rig.Scale = Vector3.One * scale;
            }

            // Wheels spin with the ground speed.
            float speed = new Vector2(avatar.Velocity.X, avatar.Velocity.Z).Length();
            _spin += speed / WheelRadiusM * dt;
            _front.Rotation = new Vector3(_spin, 0f, 0f);
            _rear.Rotation = new Vector3(_spin, 0f, 0f);
        }
        else
        {
            // Snap shut and fly back onto the back.
            float t = b.FoldSec <= 0f ? 1f : Mathf.Clamp(since / b.FoldSec, 0f, 1f);
            float travel = t * t;                                        // ease-in on the return
            Vector3 pos = groundPos.Lerp(backPos, travel);
            pos.Y += Mathf.Sin(t * Mathf.Pi) * b.UnfoldArcM * 0.6f;
            GlobalPosition = since < b.FoldSec ? pos : backPos;

            // Shut fast, then settle.
            float open = 1f - Mathf.Clamp(t * 1.6f, 0f, 1f);
            SetOpen(since < b.FoldSec ? open : 0f);

            // The mirror of the unfold: the stow yaw winds IN with the same travel scalar the
            // pitch does, landing the coin faces flat against the back (see the mounted branch).
            float lay = Mathf.DegToRad(90f) * travel;
            _rig.Rotation = new Vector3(Mathf.Lerp(_pitch, 0f, travel) + lay, lay, 0f);
            _rig.Scale = Vector3.One * Mathf.Lerp(1f, BackScale, travel);
            _pitch = Mathf.Lerp(_pitch, 0f, Mathf.Clamp(dt * 12f, 0f, 1f));
        }
    }
}
