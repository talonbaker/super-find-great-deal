using Godot;

namespace Sail.Game.World.PuffinLab;

/// <remarks>PORTED from the sibling repo <c>mp-foundation</c> @ <c>8d07fed</c>
/// (<c>scripts/game/world/Lurker.cs</c>) by packet EGG-1, 2026-09-02. The ONLY diff
/// against the source is the namespace line above and this remark: no behaviour, no
/// tuning value and no comment was touched, so the two files stay diffable in both
/// directions. Do not "improve" it in place — drift makes the next re-port unreadable.
/// It is presentation-only (no collision, no player awareness, no consequence logic) and
/// is driven from <see cref="PuffinLabRoom"/>, which owns every schedule it obeys.</remarks>

/// <summary>
/// The wrong Puffling. A dark, slightly-too-tall silhouette with faintly glowing,
/// asymmetric eyes — deliberately its own handful of dumb dark blobs, not AvatarVisual,
/// so nothing about the real avatar is touched.
///
/// Two operating modes:
///   - Scripted beats (the original behaviour): LabEnvironment (or LurkerDirector)
///     positions it and calls Appear()/AppearWalking(); it shows itself after a short
///     delay and hides again before the lights steady.
///   - Directed (<see cref="Directed"/> = true): a LurkerDirector owns position,
///     facing, and visibility outright via <see cref="DirectedUpdate"/> — used for the
///     server-authoritative wander, where one shared schedule drives every peer's copy
///     instead of each client's own random timer. This node stays purely presentational
///     either way: it has no player-awareness and no consequence logic (that future
///     behaviour belongs in LurkerDirector — see its SEAM note).
/// </summary>
public partial class Lurker : Node3D
{
    /// <summary>Scene-tree group, so hosts/tests/harnesses can find the lurker without
    /// hard-coded node paths.</summary>
    public const string Group = "lurker";

    /// <summary>While true, the timed Appear/hide logic in _Process stands down and a
    /// director drives this node via <see cref="DirectedUpdate"/>. The scripted beats
    /// (Appear/AppearWalking) temporarily clear it for their own duration.</summary>
    public bool Directed { get; set; }
    public enum Guise
    {
        /// <summary>Full silhouette pressed close to the door glass.</summary>
        AtGlass,
        /// <summary>Just the eyes in the black.</summary>
        EyesOnly,
        /// <summary>A shape far down the hallway, barely against the office glow.</summary>
        FarShadow,
    }

    private Node3D _body = null!;
    private StandardMaterial3D _eyeMat = null!;
    private float _showDelay = -1f;
    private float _remaining;
    private Vector3 _walkVel;
    private bool _walking;
    private double _bobPhase;

    /// <summary>Director-driven pose: place, face, show/hide, and (when moving) run the
    /// shamble bob. The director owns the timing — nothing here counts down or hides
    /// itself. Facing uses the walk direction the director derived, so every peer sees
    /// the same shape leaning the same way.</summary>
    public void DirectedUpdate(Vector3 position, float yawRadians, bool visible, bool moving, float dt)
    {
        Directed = true;
        _walking = false; // the scripted-walk integrator must not also move us
        Position = position;
        Rotation = new Vector3(0, yawRadians, 0);
        if (visible && !Visible)
        {
            _body.Visible = true;
            _eyeMat.EmissionEnergyMultiplier = 0.9f;
        }
        Visible = visible;
        if (visible && moving)
        {
            _bobPhase += dt * 7.0; // the same slow, wrong shamble as the walk-by
            _body.Position = new Vector3(0, Mathf.Sin((float)_bobPhase) * 0.04f, 0);
        }
    }

    public override void _Ready()
    {
        AddToGroup(Group);
        Visible = false;

        var bodyMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.045f, 0.040f, 0.050f),
            Roughness = 1f,
        };
        _eyeMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0, 0, 0),
            EmissionEnabled = true,
            Emission = new Color(0.62f, 0.95f, 0.55f), // pale sickly green
            EmissionEnergyMultiplier = 0.8f,
        };

        _body = new Node3D { Name = "Body" };
        AddChild(_body);

        // Puffling-shaped, but stretched and hunched: same bottom-heavy read, wrong proportions.
        AddBlob(_body, bodyMat, 0.40f, new Vector3(0, 0.36f, 0), new Vector3(1.05f, 0.90f, 1.00f));
        AddBlob(_body, bodyMat, 0.42f, new Vector3(0, 0.52f, 0), new Vector3(0.96f, 1.30f, 0.92f));
        AddBlob(_body, bodyMat, 0.20f, new Vector3(0, 0.88f, -0.05f), new Vector3(1f, 1f, 1f));
        // Head-sprout bent hard sideways — the healthy Pufflings' proudest feature, broken.
        AddBlob(_body, bodyMat, 0.060f, new Vector3(0.06f, 1.05f, 0.02f), Vector3.One);
        AddBlob(_body, bodyMat, 0.050f, new Vector3(0.13f, 1.13f, 0.05f), Vector3.One);
        AddBlob(_body, bodyMat, 0.040f, new Vector3(0.20f, 1.17f, 0.08f), Vector3.One);

        // Eyes: mismatched heights and sizes. Children of the root (not _body) so
        // EyesOnly can hide the body and leave points of light in the dark.
        AddEye(0.048f, new Vector3(-0.11f, 0.70f, -0.34f));
        AddEye(0.038f, new Vector3(0.12f, 0.64f, -0.35f));
        // Light body-horror pass: a too-large low eye and a small stray high one (too many
        // eyes, wrong places), plus a sagging asymmetric growth — still reads "was a Puffling."
        AddEye(0.056f, new Vector3(-0.02f, 0.50f, -0.33f));
        AddEye(0.026f, new Vector3(0.23f, 0.81f, -0.29f));
        AddBlob(_body, bodyMat, 0.22f, new Vector3(-0.27f, 0.44f, 0.03f), new Vector3(1.15f, 0.72f, 1.0f));
    }

    /// <summary>Shows the lurker after <paramref name="delay"/> seconds (so it is never
    /// seen arriving — the dark comes first), for <paramref name="seconds"/>.</summary>
    public void Appear(Guise guise, float delay, float seconds)
    {
        Directed = false;
        _walking = false;
        _body.Visible = guise != Guise.EyesOnly;
        _eyeMat.EmissionEnergyMultiplier = guise switch
        {
            Guise.AtGlass => 1.3f,
            Guise.EyesOnly => 1.4f, // two pixels of glint at gameplay distance
            _ => 0.3f,
        };
        _showDelay = Mathf.Max(0f, delay);
        _remaining = seconds;
    }

    /// <summary>Walk-by: emerges from black, shambles sideways past the observation windows
    /// (facing the glass so its eyes catch the player), and is gone before the lights return.
    /// The blackout is timed to this crossing.</summary>
    public void AppearWalking(Vector3 from, Vector3 walkDir, float speed, float delay, float seconds)
    {
        Directed = false;
        Position = from;
        RotationDegrees = Vector3.Zero;          // face -Z: toward the lab, staring in as it passes
        _walkVel = walkDir.Normalized() * speed;
        _walking = true;
        _body.Visible = true;
        _eyeMat.EmissionEnergyMultiplier = 1.0f;
        _showDelay = Mathf.Max(0f, delay);
        _remaining = seconds;
    }

    public override void _Process(double delta)
    {
        if (Directed)
            return; // a director owns position/visibility outright (DirectedUpdate)
        float dt = (float)delta;
        if (_showDelay >= 0f)
        {
            _showDelay -= dt;
            if (_showDelay < 0f)
                Visible = true;
            return;
        }
        if (!Visible)
            return;
        if (_walking)
        {
            Position += _walkVel * dt;
            _bobPhase += dt * 7.0;               // a slow, wrong shamble
            _body.Position = new Vector3(0, Mathf.Sin((float)_bobPhase) * 0.04f, 0);
        }
        _remaining -= dt;
        if (_remaining <= 0f)
        {
            Visible = false;
            _walking = false;
        }
    }

    private static void AddBlob(Node3D parent, Material mat, float radius, Vector3 pos, Vector3 scale)
    {
        parent.AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = radius, Height = radius * 2f },
            MaterialOverride = mat,
            Position = pos,
            Scale = scale,
        });
    }

    private void AddEye(float radius, Vector3 pos)
    {
        AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = radius, Height = radius * 2f },
            MaterialOverride = _eyeMat,
            Position = pos,
        });
    }
}
