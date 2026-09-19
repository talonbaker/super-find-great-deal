using Godot;
using MpFoundation.Game.Sandbox;

namespace Sail.Game.Bubble;

/// <summary>
/// <b>The hub's scoreboard: <c>N / TOTAL</c> in lit glyphs, on two faces, readable from across
/// the hub.</b>
///
/// <para><b>Why the total is here and not on the HUD.</b> The HUD answers "how many have we got"
/// at a glance and nothing else; this answers "how much of the level is left", which is a
/// question worth walking back to the middle of the map to ask. Splitting them is what makes the
/// pedestal a destination rather than a redundant copy of a corner readout — and the hub is where
/// six players meet, so the number that starts an argument about where to search next belongs
/// where they are standing (LEVEL-BIBLE: a landmark is a thing you navigate BY, and it earns that
/// by being the only place something can be read).</para>
///
/// <para><b>It is the one lit thing in the hub</b> (program D10). The level's night is real and
/// the aids are emissive edges, not lamps; the single exception is this, because a scoreboard
/// nobody can read after dusk is a scoreboard for half the session. The one <c>OmniLight3D</c>
/// exists to put the HOUSING in the dark hub, so the glyphs are not floating digits with no
/// object under them.</para>
///
/// <para><b>The glyphs are an emissive <c>TextMesh</c>, and the first cut got this wrong.</b> It
/// used <c>Label3D</c> with <c>shaded = false</c>, on the reasoning that "unshaded" means "full
/// brightness at any hour". A headed midnight capture disproved it: this project's night runs a
/// very low ambient floor and the tonemap takes unshaded white down with everything else — BT-0's
/// own "HUB" section label renders as dark grey at midnight for exactly that reason, which is the
/// control that makes this a measurement rather than a theory. Emission is ADDED after lighting,
/// so it is the only channel that survives, which is why the packet asked for "emission on the
/// glyph material". <c>Label3D</c> has no emission property; <c>TextMesh</c> under a
/// <c>MeshInstance3D</c> takes a full <c>StandardMaterial3D</c> and does. The material keeps
/// shading ON: an unshaded material ignores EMISSION entirely and draws only ALBEDO, a trap this
/// repo has already paid for once.</para>
///
/// <para><b>Both faces share ONE mesh.</b> Two <c>MeshInstance3D</c>s, one turned 180°, pointing
/// at the same <c>TextMesh</c> resource — so the two sides of a scoreboard cannot disagree and the
/// string is written once per change rather than once per face. A billboarded label would have
/// been one node fewer, but it spins as you walk past, which reads as a sign tracking you rather
/// than as an object in the world.</para>
///
/// <para><b>The flourish at 100/100 adds no Control to the tree</b> — packet acceptance criterion
/// 5, and the reason is the register: the level ends by the group noticing, not by the game
/// interrupting them. Three seconds of glyph, three spaced pops, nothing modal, no text.</para>
/// </summary>
public partial class BubbleCounterDisplay : Node3D
{
    /// <summary>The node name a world uses, so a log or a remote-tree dump finds it.</summary>
    public const string NodeName = "BubbleCounterDisplay";

    /// <summary>The authored scene. A file, openable in the editor — see
    /// <c>BubbleTestWorld.SetUpBubbleCounter</c> for why instancing it at runtime is not the
    /// procedural generation program D2 forbids.</summary>
    public const string ScenePath = "res://scenes/game/props/BubbleCounterDisplay.tscn";

    /// <summary>The marker BT-5 leaves on the plinth top. Named here rather than in the layout
    /// class because this is the only node that looks for it.</summary>
    public const string PedestalMountName = "PedestalMount";

    /// <summary>How long the full-house flourish runs.</summary>
    private const double FlourishSec = 3.0;

    /// <summary>Gap between the three celebration pops. Spaced, per the packet — three at once is
    /// one loud noise; three at 0.35 s is a little fanfare.</summary>
    private const double PopSpacingSec = 0.35;

    private readonly System.Collections.Generic.List<TextMesh> _faces = new();
    private readonly System.Collections.Generic.List<MeshInstance3D> _faceNodes = new();
    private BubbleCounter? _counter;
    private OmniLight3D? _light;
    private int _lastCount = int.MinValue;
    private int _lastTotal = int.MinValue;
    private bool _lastSynced;
    private bool _flourished;
    private double _popTimer = -1;
    private int _popsLeft;

    /// <summary>The string on both faces, for the suites and for anything that needs to assert on
    /// what a player can read rather than on what a field holds.</summary>
    public static string Format(int count, int total) => $"{count} / {total}";

    /// <summary>What the display reads before this peer has been told the tally — the same
    /// discipline <c>HudBubbleCount</c> uses, for the same reason: a confident zero on a peer that
    /// knows nothing is worse than an honest dash.</summary>
    /// <summary>Shown until the server's first tally arrives.
    ///
    /// <para><b>ASCII on purpose — but the em dash was never the cause. Corrected by FIX-1,
    /// 2026-08-28.</b> This doc used to say <see cref="TextMesh"/> could not triangulate the em
    /// dash "in this font". A headed night capture disproved it: "Triangulation failed" fired on
    /// the SYNCED path too, where the string is <c>"0 / 6"</c> — digits, a space and a slash — and
    /// the board rendered nothing for the whole session. The scene set no <c>font</c> at all, so
    /// TextMesh fell back to the engine's built-in default face, which this build cannot
    /// triangulate for ANY glyph. The fix is in the scene: an explicit outline TTF
    /// (<c>assets/fonts/WorkSans.ttf</c>). ASCII is kept because it costs nothing, but it is not
    /// what makes the board render.</para></summary>
    public static string UnsyncedText => "- / -";

    /// <summary>The current face text, for tests and captures.</summary>
    public string FaceText => _faces.Count > 0 ? _faces[0].Text : string.Empty;

    public override void _Ready()
    {
        CollectFaces(this);
        _light = GetNodeOrNull<OmniLight3D>("Light");
        if (_faces.Count == 0)
            GD.PushWarning($"[bubbletest] {NodeName} has no TextMesh faces — nothing to read.");
        foreach (TextMesh face in _faces)
            face.Text = UnsyncedText;
    }

    public override void _ExitTree()
    {
        if (_counter != null && GodotObject.IsInstanceValid(_counter))
            _counter.Changed -= OnCounterChanged;
        _counter = null;
    }

    public override void _Process(double delta)
    {
        if (_popTimer >= 0)
        {
            _popTimer -= delta;
            if (_popTimer <= 0 && _popsLeft > 0)
            {
                _popsLeft--;
                SfxLab.PlayStream3D(this, GlobalPosition, SfxLab.Get(Sfx.Pop), volumeDb: -4f,
                    pitchJitter: 0.10f, maxDistance: 40f);
                _popTimer = _popsLeft > 0 ? PopSpacingSec : -1;
            }
        }

        if (_counter == null || !GodotObject.IsInstanceValid(_counter))
        {
            // Resolved lazily rather than captured: the world adds the counter and this display in
            // the same pass, and a fixture can add the counter later still.
            BubbleCounter? found = BubbleCounter.Instance;
            if (found == null)
                return;
            _counter = found;
            _counter.Changed += OnCounterChanged;
        }

        // Polled as well as subscribed, deliberately. The subscription is what makes a change
        // land on the frame it happened; the poll is what makes the FIRST render correct on a
        // peer whose state arrived before this node was in the tree, which is exactly the late
        // joiner BT-6's sync exists for. Both write the same string, so they cannot disagree.
        Render(_counter);
    }

    private void OnCounterChanged(int id, int count, int byPeer)
    {
        if (_counter != null)
            Render(_counter);
    }

    private void Render(BubbleCounter counter)
    {
        int count = counter.Count;
        int total = counter.BubbleCount;
        bool synced = counter.Synced;
        // Synced is part of what is RENDERED, so it has to be part of what counts as a change.
        // Without it, a peer that joins an untouched level writes the dash once at count 0 and
        // then never rewrites, because the tally it is finally told about is also 0 — a headed
        // capture caught the board stuck on "— / —" for a whole session.
        if (count == _lastCount && total == _lastTotal && synced == _lastSynced)
            return;
        _lastCount = count;
        _lastTotal = total;
        _lastSynced = synced;

        string text = synced ? Format(count, total) : UnsyncedText;
        foreach (TextMesh face in _faces)
            face.Text = text;

        if (total > 0 && count >= total)
        {
            if (!_flourished)
            {
                _flourished = true;
                Flourish();
            }
        }
        else
        {
            // Re-arms on a reset, so a second run gets its own ending.
            _flourished = false;
        }
    }

    /// <summary>Three seconds of glyph, three spaced pops, and nothing else. No <c>Control</c> is
    /// created here and none may be — see the class doc.</summary>
    private void Flourish()
    {
        _popsLeft = 3;
        _popTimer = 0;
        // Pulsed on the SHARED glyph material's emission energy rather than on each face node: it
        // is one property write for both faces, it is the same channel that makes the glyphs
        // readable at night in the first place, and a Tween on a resource needs a Node to own it —
        // this one, which outlives the flourish.
        if (GlyphMaterial() is { } glyph)
        {
            float rest = glyph.EmissionEnergyMultiplier;
            Tween tween = CreateTween();
            tween.SetLoops(3);
            tween.TweenProperty(glyph, "emission_energy_multiplier", rest * 2.2f, FlourishSec / 6.0)
                .SetTrans(Tween.TransitionType.Sine);
            tween.TweenProperty(glyph, "emission_energy_multiplier", rest, FlourishSec / 6.0)
                .SetTrans(Tween.TransitionType.Sine);
        }
        if (_light == null)
            return;
        float restEnergy = _light.LightEnergy;
        Tween lightTween = _light.CreateTween();
        lightTween.SetLoops(3);
        lightTween.TweenProperty(_light, "light_energy", restEnergy * 2.0f, FlourishSec / 6.0);
        lightTween.TweenProperty(_light, "light_energy", restEnergy, FlourishSec / 6.0);
    }

    /// <summary>The flourish colour. Soap-film cool, matching BT-6's puff and BT-9's film, so the
    /// ending reads as "the bubbles" rather than as a generic UI success green.</summary>
    private static readonly Color FlourishColour = new(0.72f, 0.93f, 1.0f);

    /// <summary>The material both faces draw with. Read off the first face rather than loaded by
    /// path, so the scene stays the single place the look is authored.</summary>
    private StandardMaterial3D? GlyphMaterial() =>
        _faceNodes.Count > 0 ? _faceNodes[0].MaterialOverride as StandardMaterial3D : null;

    /// <summary>Walks for <c>MeshInstance3D</c>s carrying a <c>TextMesh</c>. The housing's boxes
    /// are <c>MeshInstance3D</c>s too, so the mesh TYPE is what selects a face — a name-based
    /// match would silently pick up a future decorative mesh named FaceSomething.</summary>
    private void CollectFaces(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            if (child is MeshInstance3D mesh && mesh.Mesh is TextMesh text)
            {
                _faceNodes.Add(mesh);
                if (!_faces.Contains(text))
                    _faces.Add(text);   // both faces share one resource: write it once.
            }
            CollectFaces(child);
        }
    }
}
