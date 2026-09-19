using Godot;

namespace Sail.Game.World.PuffinLab;

/// <remarks>PORTED from the sibling repo <c>mp-foundation</c> @ <c>8d07fed</c>
/// (<c>scripts/game/world/FlickerLight.cs</c>) by packet EGG-1, 2026-09-02. The ONLY diff
/// against the source is the namespace line above and this remark: no behaviour, no
/// tuning value and no comment was touched, so the two files stay diffable in both
/// directions. Do not "improve" it in place — drift makes the next re-port unreadable.
/// It is presentation-only (no collision, no player awareness, no consequence logic) and
/// is driven from <see cref="PuffinLabRoom"/>, which owns every schedule it obeys.</remarks>

/// <summary>
/// One ceiling fluorescent fixture: metal housing, emissive tube, and an unfussy
/// OmniLight3D driven by a nervy flicker model. Each fixture jitters and micro-dips on
/// its own; the room-wide "dark beat" (the whole bank drops out at once, staggered by a
/// few frames per fixture so it reads as one dying circuit) is scheduled centrally by
/// LabEnvironment via BeginDark. Relighting stutters briefly — real tubes never come
/// back clean.
/// </summary>
public partial class FlickerLight : Node3D
{
    private static readonly Color TubeColor = new(0.85f, 0.94f, 0.89f); // cold fluorescent green-white

    private static readonly StandardMaterial3D HousingMat = new()
    {
        AlbedoColor = new Color(0.30f, 0.31f, 0.30f),
        Metallic = 0.6f,
        Roughness = 0.55f,
    };

    /// <summary>Steady-state light energy; the flicker model scales around this.
    /// [Export] so the authored-scene dump round-trips per-fixture tuning (the lab's
    /// fixtures don't all share one energy) instead of every instance reverting to the
    /// default on scene load.</summary>
    [Export] public float BaseEnergy { get; set; } = 2.1f;

    /// <summary>False = an emissive-only fixture: the tube still glows, dips, and joins
    /// dark beats, but carries no OmniLight3D. This is how a room reads as "banked with
    /// fluorescents" while staying inside the ≤8-unshadowed-lights budget (Bible §4 +
    /// audit Flag 1) — only the budgeted fixtures set this true.</summary>
    [Export] public bool LightEnabled { get; set; } = true;

    /// <summary>Faulty tubes dip harder, more often, and buzz about it.</summary>
    [Export] public bool Faulty { get; set; }

    private readonly RandomNumberGenerator _rng = new();
    private OmniLight3D? _light;
    private StandardMaterial3D _tubeMat = null!; // per-fixture: emission animates independently

    private float _darkDelay = -1f;   // countdown to a scheduled dark beat; <0 = none pending
    private float _darkRemaining;
    private bool _dark;
    private float _stutterRemaining;  // relight "brrt" window after a dark beat
    private float _dipRemaining;
    private float _nextDipIn = 2f;

    public override void _Ready()
    {
        AddChild(new MeshInstance3D
        {
            Name = "Housing",
            Mesh = new BoxMesh { Size = new Vector3(1.5f, 0.09f, 0.30f) },
            MaterialOverride = HousingMat,
        });

        _tubeMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.78f, 0.80f, 0.78f),
            Roughness = 0.4f,
            EmissionEnabled = true,
            Emission = TubeColor,
            EmissionEnergyMultiplier = 2.4f,
        };
        AddChild(new MeshInstance3D
        {
            Name = "Tube",
            Mesh = new BoxMesh { Size = new Vector3(1.30f, 0.05f, 0.10f) },
            Position = new Vector3(0, -0.07f, 0),
            MaterialOverride = _tubeMat,
        });

        // Never a shadow caster (Bible §4: zero, non-negotiable).
        if (LightEnabled)
        {
            _light = new OmniLight3D
            {
                Position = new Vector3(0, -0.28f, 0),
                LightColor = TubeColor,
                LightEnergy = BaseEnergy,
                OmniRange = 6.5f,
                ShadowEnabled = false,
            };
            AddChild(_light);
        }
    }

    /// <summary>Schedules a full blackout: off after <paramref name="delay"/>, dark for
    /// <paramref name="duration"/>, then a stuttering relight.</summary>
    public void BeginDark(float delay, float duration)
    {
        _darkDelay = Mathf.Max(0f, delay);
        _darkRemaining = Mathf.Max(0.15f, duration);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        if (_darkDelay >= 0f)
        {
            _darkDelay -= dt;
            if (_darkDelay < 0f)
                _dark = true;
        }

        if (_dark)
        {
            _darkRemaining -= dt;
            if (_darkRemaining <= 0f)
            {
                _dark = false;
                _stutterRemaining = _rng.RandfRange(0.08f, 0.22f);
            }
        }

        float f;
        if (_dark)
        {
            f = 0f;
        }
        else if (_stutterRemaining > 0f)
        {
            _stutterRemaining -= dt;
            f = _rng.Randf() < 0.45f ? 0f : 1.15f; // relight overshoot between blinks
        }
        else
        {
            _nextDipIn -= dt;
            if (_nextDipIn <= 0f)
            {
                _dipRemaining = Faulty ? _rng.RandfRange(0.08f, 0.26f) : _rng.RandfRange(0.03f, 0.09f);
                // Toned down: dips are noticeably less frequent so the flicker reads as
                // "occasionally uneasy" rather than "constantly strobing."
                _nextDipIn = Faulty ? _rng.RandfRange(3.5f, 9f) : _rng.RandfRange(9f, 20f);
                if (Faulty && _rng.Randf() < 0.4f)
                    LabAmbience.PlayBuzzPop(GetParent(), GlobalPosition);
            }

            if (_dipRemaining > 0f)
            {
                _dipRemaining -= dt;
                f = 0.28f;
            }
            else
            {
                f = 1f + (_rng.Randf() - 0.5f) * 0.05f; // gentle idle breathing (was 0.12 — too busy)
            }
        }

        if (_light != null)
            _light.LightEnergy = BaseEnergy * f;
        _tubeMat.EmissionEnergyMultiplier = 2.4f * f + 0.02f;
    }
}
