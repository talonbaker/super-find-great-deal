using Godot;
using MpFoundation;

namespace Sail.Game.Water;

/// <summary>
/// The visual and haptic halves of the cold's urgency cue (lake-water contract §5.1), plus the
/// hard cut to black that the sputter-out's go-under ends in (§6). Entirely client-local
/// presentation over a value the server already owns — it writes nothing, replicates nothing,
/// and reads only <see cref="WaterService"/>'s public surface.
///
/// <b>Three redundant channels, and this class owns two of them.</b> The urgency-cue contract
/// (MECHANICS-BIBLE §10.4 via INTERACTION-BIBLE §8.2) is explicit that a player with no working
/// audio must still perceive the build — and this game deliberately severs audio, so that is a
/// hard requirement rather than a nicety. Audio is packet W4's; frost at the screen edge and an
/// irregular quickening rumble are here. Either one alone satisfies the rule.
///
/// <b>No HUD bar</b> (spec §5.1). The register does not carry meters, and a numeric chill readout
/// would flatten a slow dread into a progress bar. What the player gets is a screen that is
/// visibly closing in and a controller that will not settle.
///
/// <b>No shader, no backbuffer.</b> The frost is a radial <see cref="GradientTexture2D"/> on a
/// plain TextureRect — spec §8.2 bans backbuffer copies outright, and while that clause is aimed
/// at the water surface, a full-screen post-process here would spend the same budget from the
/// other end. A gradient texture costs one transparent quad.
/// </summary>
public partial class ChillCueOverlay : CanvasLayer
{
    /// <summary>Below <c>BedSleepFade</c>'s 95 and above every HUD layer. The sleep fade must be
    /// able to cover this — going to bed ends the night regardless of how cold anyone is.</summary>
    private const int LayerIndex = MpFoundation.Ui.Design.UiLayers.ChillCue;

    /// <summary>Peak opacity of the frost at chill 1.0. Deliberately short of opaque: this denies
    /// prospect, it does not blind. A player who cannot see the shore cannot swim to it, and an
    /// unwinnable-by-opacity lake would violate the anti-unwinnable guarantee through the
    /// presentation layer rather than the mechanic.</summary>
    private const float FrostMaxAlpha = 0.72f;

    /// <summary>
    /// Peak opacity of the dark rim that sits under the frost — R1, 2026-08-09.
    ///
    /// <b>Why a second layer.</b> The frost alone is a pale blue-white wash, and the lake it has to
    /// be seen against is a pale blue-white daylight surface under a pale blue-white sky. Captured
    /// at chill 1.0 — the instant before the cold takes you — it is indistinguishable from
    /// atmospheric haze (see the R1 contact sheets). That is not a tuning miss, it is a channel
    /// that does not exist in daylight, which is how a three-channel cue quietly became a
    /// one-channel cue for a player who was floating in the sun with the sound off.
    ///
    /// A pale rim needs a dark background to read; a dark rim needs a pale one. Carrying both means
    /// the visual channel lands on the bright day lake AND on the night water, and it means the cue
    /// is a LUMINANCE change rather than a hue change — which is what INTERACTION-BIBLE §9.4 means
    /// by a visual that does not depend on colour alone, and is also what makes it survive a
    /// colour-blind player and a badly calibrated monitor.
    /// </summary>
    private const float RimMaxAlpha = 0.55f;

    /// <summary>How much the cue breathes, as a fraction of its own opacity. The third thing that
    /// makes a peripheral signal impossible to sit through is MOTION — a static vignette is read
    /// once as "the art looks like that" and then stops being information. Small on purpose: this
    /// is a slow swell, not a flash.</summary>
    private const float BreatheDepth = 0.16f;

    /// <summary>How fast the frost follows chill, per second. Slow: the cold is a build, and a
    /// frost that snapped would read as damage rather than as creeping.</summary>
    private const float FrostFollowRate = 2.2f;

    /// <summary>Seconds to fade back in from the hard cut once the recovery lands.</summary>
    private const float RecoverFadeSec = 0.55f;

    /// <summary>Rumble floor and ceiling. Weak motor only at first; the strong motor joins in the
    /// last third, which is what makes the quickening feel like it is getting worse rather than
    /// just faster.</summary>
    private const float RumbleWeakMax = 0.55f;
    private const float RumbleStrongMax = 0.40f;

    /// <summary>Pulse period at the cue's onset and at chill 1.0, seconds. The gap between them
    /// is the "quickening"; the irregularity comes from the aperiodic modulation in
    /// <see cref="PulsePeriodFor"/>, which is a deterministic function of elapsed time rather
    /// than RNG — a random rumble reads as a loose cable, a shifting one reads as a body.</summary>
    private const float PulsePeriodSlowSec = 2.4f;
    private const float PulsePeriodFastSec = 0.55f;

    private static ChillCueOverlay? _instance;

    private TextureRect _frost = null!;
    private TextureRect _rim = null!;
    private ColorRect _veil = null!;
    private float _frostAmount;
    private float _veilAlpha;
    private float _pulseClock;
    private float _breathePhase;
    private float _elapsed;
    private bool _cutPending;
    private float _cutCountdown;

    /// <summary>Attach to a scene root. No-op headless (there is no screen to frost and no
    /// controller to shake) and no-op if one already exists — the same guard
    /// <c>BedSleepFade.Attach</c> uses, and for the same reason: a reconnect re-runs the
    /// gameplay setup path.</summary>
    public static void Attach(Node sceneRoot)
    {
        if (NetworkManager.Instance is { IsHeadless: true })
            return;
        if (_instance != null && IsInstanceValid(_instance))
            return;
        sceneRoot.AddChild(new ChillCueOverlay { Name = "ChillCueOverlay" });
    }

    public override void _Ready()
    {
        _instance = this;
        Layer = LayerIndex;

        // The dark rim goes in FIRST so it sits under the frost: the frost is the thing the player
        // is meant to read as ice, the rim is the thing that makes it visible at all.
        _rim = new TextureRect
        {
            Texture = BuildRimTexture(),
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Modulate = new Color(1, 1, 1, 0),
        };
        _rim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_rim);

        _frost = new TextureRect
        {
            Texture = BuildFrostTexture(),
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Modulate = new Color(1, 1, 1, 0),
        };
        _frost.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_frost);

        _veil = new ColorRect
        {
            Color = new Color(0, 0, 0, 0),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _veil.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_veil);

        if (WaterService.Instance is { } water)
        {
            water.WentUnder += OnWentUnder;
            water.Sputtered += OnSputtered;
        }
    }

    public override void _ExitTree()
    {
        if (WaterService.Instance is { } water)
        {
            water.WentUnder -= OnWentUnder;
            water.Sputtered -= OnSputtered;
        }
        // Never leave a controller buzzing after the overlay is gone. This is the one piece of
        // global hardware state this class touches, so it is also the one it must hand back.
        StopRumble();
        if (_instance == this)
            _instance = null;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (!float.IsFinite(dt) || dt <= 0f)
            return;
        _elapsed += dt;

        float target = 0f;
        if (WaterService.Instance is { Synced: true } water)
        {
            int me = Multiplayer?.MultiplayerPeer != null ? Multiplayer.GetUniqueId() : 1;
            target = ChillClock.CueIntensity(water.ChillOf(me));
        }

        // Frost. Eased rather than snapped, and eased in BOTH directions: climbing out of the
        // lake should visibly thaw, not cut.
        _frostAmount = Mathf.Lerp(_frostAmount, target, 1f - Mathf.Exp(-FrostFollowRate * dt));
        if (_frostAmount < 0.001f)
            _frostAmount = 0f;

        // The swell. Phase advances by a fraction of the CURRENT period, so the breath quickens
        // with the cold in step with the rumble and the chatter rather than running to its own
        // clock — the three channels are one signal and must never tell different stories about
        // the same value. Advancing the phase (not sampling absolute time) is what lets the period
        // change without the wave jumping.
        // Wrapped into one cycle rather than left to climb: a session-long float fed to Sin loses
        // precision until the swell visibly stutters, and this node lives for the whole night.
        _breathePhase = (_breathePhase + dt / PulsePeriodFor(_frostAmount, _elapsed)) % 1f;
        float breathe = 1f + BreatheDepth * Mathf.Sin(_breathePhase * Mathf.Tau);

        // Square root, not linear — R1. Alpha is not perceived linearly, and a straight mapping
        // spends almost all of the channel's range on the last third of the swim: captured at
        // intensity 0.50, half way to being taken, the linear version was a barely-there darkening
        // at the corners. That is the SAME mistake the old 0.35 onset threshold made, one layer
        // up — a cue that is technically present the whole time and only legible near the end.
        // The peak is unchanged, so nothing about the crisis reads differently; what changes is
        // that the middle of the swim now looks like the middle of the swim.
        float eased = Mathf.Sqrt(Mathf.Clamp(_frostAmount, 0f, 1f));
        float shown = Mathf.Clamp(eased * breathe, 0f, 1f);

        _frost.Modulate = new Color(0.80f, 0.90f, 1.0f, shown * FrostMaxAlpha);
        _rim.Modulate = new Color(1f, 1f, 1f, shown * RimMaxAlpha);

        UpdateVeil(dt);
        UpdateRumble(_frostAmount, dt);
    }

    private void UpdateVeil(float dt)
    {
        if (_cutPending)
        {
            _cutCountdown -= dt;
            if (_cutCountdown <= 0f)
            {
                // The hard cut (spec §6.2). Timed locally from the WentUnder event rather than
                // waiting for a second server message, so it lands on the exact frame the go-under
                // ends even under loss — and if the Sputtered message is late the veil simply
                // holds, which is the failure mode you want.
                _cutPending = false;
                _veilAlpha = 1f;
            }
        }
        else if (_veilAlpha > 0f)
        {
            _veilAlpha = Mathf.MoveToward(_veilAlpha, 0f, dt / RecoverFadeSec);
        }
        _veil.Color = new Color(0, 0, 0, _veilAlpha);
    }

    private void OnWentUnder(WaterEvent evt)
    {
        if (!IsForLocalPeer(evt.PeerId))
            return;
        _cutPending = true;
        _cutCountdown = WaterGeometry.GoUnderSec;
    }

    private void OnSputtered(WaterEvent evt)
    {
        if (!IsForLocalPeer(evt.PeerId))
            return;
        // Control has returned; start the fade-in. If the go-under's local timer somehow never
        // fired (a client that joined mid-episode), clear it so no cut lands after the recovery.
        _cutPending = false;
        _frostAmount = 0f;
        StopRumble();
    }

    private bool IsForLocalPeer(int peerId)
    {
        int me = Multiplayer?.MultiplayerPeer != null ? Multiplayer.GetUniqueId() : 1;
        return peerId == me;
    }

    // --- Haptics ---------------------------------------------------------------------------------

    /// <summary>
    /// Irregular and quickening (spec §5.1). The period shortens with intensity, and a slow
    /// second sine wobbles it so consecutive pulses are never the same length — deterministic,
    /// not random, because a random interval reads as a fault and a shifting one reads as
    /// something breathing.
    /// </summary>
    private static float PulsePeriodFor(float intensity, float elapsed)
    {
        float basePeriod = Mathf.Lerp(PulsePeriodSlowSec, PulsePeriodFastSec, intensity);
        float wobble = 1f + 0.28f * Mathf.Sin(elapsed * 0.83f) * (1f - intensity * 0.5f);
        return Mathf.Max(0.2f, basePeriod * wobble);
    }

    private void UpdateRumble(float intensity, float dt)
    {
        if (intensity <= 0.001f)
        {
            _pulseClock = 0f;
            return;
        }
        if (Input.GetConnectedJoypads().Count == 0)
            return;

        _pulseClock -= dt;
        if (_pulseClock > 0f)
            return;
        float period = PulsePeriodFor(intensity, _elapsed);
        _pulseClock = period;

        float weak = RumbleWeakMax * intensity;
        // The strong motor only joins in the last third — the point where the cold stops being
        // atmosphere and starts being a deadline.
        float strong = intensity <= 0.66f ? 0f : RumbleStrongMax * ((intensity - 0.66f) / 0.34f);
        float duration = Mathf.Min(0.35f, period * 0.45f);
        foreach (int device in Input.GetConnectedJoypads())
            Input.StartJoyVibration(device, weak, strong, duration);
    }

    private static void StopRumble()
    {
        foreach (int device in Input.GetConnectedJoypads())
            Input.StopJoyVibration(device);
    }

    // --- The frost texture -------------------------------------------------------------------------

    /// <summary>
    /// A radial gradient, transparent in the middle and frost-white at the corners, generated once
    /// at 256x256 and stretched. Radial fill from the centre out is exactly the shape "creeping in
    /// at the screen edge" describes, and <see cref="GradientTexture2D"/> gives it with no shader
    /// and no image work.
    /// </summary>
    private static GradientTexture2D BuildFrostTexture()
    {
        // Reaches further in than it used to (the ramp starts at 0.34 rather than 0.48) — R1. The
        // old profile only carried real opacity in the extreme corners, which on a 16:9 screen is
        // a few hundred pixels of a place nobody looks. The centre is still perfectly clear, so
        // the anti-unwinnable argument in FrostMaxAlpha's doc is untouched: what is denied is
        // peripheral prospect, never the shore you are swimming at.
        var gradient = new Gradient
        {
            Offsets = new[] { 0.0f, 0.34f, 0.66f, 1.0f },
            Colors = new[]
            {
                new Color(0.85f, 0.93f, 1.0f, 0.0f),
                new Color(0.85f, 0.93f, 1.0f, 0.0f),
                new Color(0.88f, 0.95f, 1.0f, 0.50f),
                new Color(0.94f, 0.98f, 1.0f, 1.0f),
            },
        };
        return RadialFrom(gradient);
    }

    /// <summary>
    /// The dark rim under the frost. Same radial shape, starting a little further out so it reads
    /// as an outer band rather than as a second frost, and very slightly blue so it looks like cold
    /// rather than like a damage vignette.
    /// </summary>
    private static GradientTexture2D BuildRimTexture()
    {
        var gradient = new Gradient
        {
            Offsets = new[] { 0.0f, 0.42f, 0.74f, 1.0f },
            Colors = new[]
            {
                new Color(0.02f, 0.05f, 0.10f, 0.0f),
                new Color(0.02f, 0.05f, 0.10f, 0.0f),
                new Color(0.02f, 0.05f, 0.10f, 0.45f),
                new Color(0.01f, 0.03f, 0.07f, 1.0f),
            },
        };
        return RadialFrom(gradient);
    }

    private static GradientTexture2D RadialFrom(Gradient gradient) => new()
    {
        Gradient = gradient,
        Width = 256,
        Height = 256,
        Fill = GradientTexture2D.FillEnum.Radial,
        FillFrom = new Vector2(0.5f, 0.5f),
        FillTo = new Vector2(1.0f, 0.5f),
    };
}
