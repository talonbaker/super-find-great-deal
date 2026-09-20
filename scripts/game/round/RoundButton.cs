using Godot;
using MpFoundation.Game.Sandbox;

namespace MpFoundation.Game.Round;

/// <summary>
/// <b>One of the three round buttons</b> — a base, a cap that physically goes down when you press
/// it, a lamp that tells you whether pressing it will work, and a label that says what it is.
/// One class, three instances, every one of them authored into its room's scene file.
///
/// <para><b>Why this file is as small as it is.</b> Talon's ruling behind the packet: prior
/// in-game buttons did not work well, and this one has to be basic and actually work. So the
/// button holds no game logic at all. <see cref="Press"/> asks the server; the server latches a
/// fact; <see cref="HideSeekLoop"/> answers on the next tick; the answer comes back as one
/// broadcast that every peer plays. Everything this class decides for itself is cosmetic, and
/// the one thing it derives — the lamp — is pure arithmetic in
/// <see cref="RoundButtonRules.Lamp"/>, tested without an engine.</para>
///
/// <para><b>The three playtest failures it is built against</b>
/// (<c>docs/INTERACTION-BIBLE.md</c> §2, §3, §5, §7):</para>
/// <list type="bullet">
/// <item><b>A debounce mistaken for a confirmation.</b> There is no debounce anywhere in this
/// lane. Every press produces a result message and a cap animation, accepted or refused; the
/// loop's one-shot fact reading is the idempotence.</item>
/// <item><b>A sign hidden behind the player's own body.</b> First person removes it, and the
/// label is a <c>Label3D</c> on the button's own face rather than a world sign.</item>
/// <item><b>A prompt that never said which key.</b> Nothing here ever writes a key. The glyph is
/// <c>InteractPrompt</c>'s, resolved live from the <c>InputMap</c>, drawn over whichever
/// interactable <c>InteractHighlighter</c> has picked — which is this button whenever it is the
/// nearest candidate, because it joins <see cref="IPressable.Group"/>.</item>
/// </list>
///
/// <para><b>The kind comes from a GROUP, not an <c>[Export]</c>.</b> Nested-scene exported script
/// properties read back as their C# defaults on this project's Godot/Mono build (measured;
/// <c>PropManager.AuthoredKindOf</c>'s note), and while a node scripted directly in a room scene
/// is not that case, a level-authored fact that CAN silently read as a default is not worth the
/// risk when a group cannot. Groups live in the <c>[node]</c> header
/// (<c>.claude/rules/godot-scenes.md</c>) and a misspelled one fails loudly here rather than
/// silently at the far end.</para>
/// </summary>
public partial class RoundButton : StaticBody3D, IPressable
{
    /// <summary>Every round button joins this, so <c>RoundControls</c> can find all three without
    /// knowing where a level author put them.</summary>
    public const string Group = "round_button";

    /// <summary>The per-kind groups. One of these, exactly, on every instance.</summary>
    public const string GroupStart = "round_button_start";

    /// <inheritdoc cref="GroupStart"/>
    public const string GroupConfirm = "round_button_confirm";

    /// <inheritdoc cref="GroupStart"/>
    public const string GroupEnd = "round_button_end";

    /// <summary>How close the avatar has to be, in metres. The packet's 1.6 m: comfortably more
    /// than an arm's length so a player standing at the rack can reach Start without shuffling,
    /// and well inside the room so two buttons could never both be in range.</summary>
    public const float PressRadius = 1.6f;

    /// <summary>How far the cap travels. The packet's 4 cm — big enough to read at first-person
    /// eye height and on a capture, small enough that it still looks like a button.
    ///
    /// <para><b>It travels along the button's own local −Z</b>, which is the direction the button
    /// FACES. Every instance is authored with +Z pointing out of the surface it is mounted on, so
    /// one rule covers a button on the −Z wall, a button on the −X wall and a button on the +X
    /// wall without any of them needing a special case — and a cap that pressed along world −Y
    /// would slide DOWN the wall instead of going into it.</para></summary>
    public const float CapTravelM = 0.04f;

    /// <summary>How long the cap takes to come back up.</summary>
    public const double CapReturnSec = 0.18;

    /// <summary>The refusal shake, and its amplitude. §3 of the bible: state changing with
    /// nothing moving is the shortcut that gets reported as "it didn't work" — so a REFUSAL moves
    /// too, differently from an acceptance.</summary>
    public const double ShakeSec = 0.15;

    /// <inheritdoc cref="ShakeSec"/>
    public const float ShakeAmplitudeM = 0.012f;

    /// <summary>How long the lamp holds its pressed/refused flash.</summary>
    public const double FlashSec = 0.18;

    /// <summary>The lamp is re-derived on this cadence, matching <c>InteractHighlighter</c>'s own
    /// 0.1 s proximity poll. The packet asks for 100 ms explicitly; a per-frame derivation would
    /// be three dictionary lookups a frame for a value that changes a handful of times a round.</summary>
    private const double LampPollSec = 0.1;

    private static readonly Color LampLit = new(0.30f, 0.92f, 0.38f);
    private static readonly Color LampDark = new(0.13f, 0.14f, 0.13f);
    private static readonly Color LampPressed = new(0.96f, 0.96f, 0.92f);
    private static readonly Color LampRefused = new(0.92f, 0.24f, 0.18f);

    /// <summary>Which button this is, read from this node's groups in <see cref="_Ready"/>.</summary>
    public RoundButtonKind Kind { get; private set; }

    /// <summary>The lamp's current derived state — <c>Pressed</c> only while a flash is playing.
    /// Public so a suite, a capture script or a reviewer can read the affordance rather than
    /// infer it from a colour.</summary>
    public RoundLamp Lamp { get; private set; } = RoundLamp.Dark;

    /// <inheritdoc/>
    public float PressRadiusM => PressRadius;

    /// <inheritdoc/>
    /// <remarks>Drives the cap's own emission rather than a separate shell: a button is already
    /// a small bright thing and an inverted-hull outline on it reads as a graphical fault.</remarks>
    public bool Highlighted { get; set; }

    private MeshInstance3D? _cap;
    private MeshInstance3D? _lamp;
    private Label3D? _label;
    private StandardMaterial3D? _lampMaterial;
    private StandardMaterial3D? _capMaterial;

    private Vector3 _capRest;
    private float _capBaseEmission;
    private double _pressedLeft;
    private double _shakeLeft;
    private double _sinceLampPoll;
    private bool _lastRefused;
    private RoundLamp _lastLoggedLamp = RoundLamp.Pressed;   // forces one line on the first poll

    public override void _Ready()
    {
        Kind = ResolveKind();
        AddToGroup(IPressable.Group);   // belt and braces: the scene authors it too, and a button
                                        // that is not in this group is invisible to the prompt,
                                        // the shimmer and the key — the silent failure the rules
                                        // file names.

        _cap = GetNodeOrNull<MeshInstance3D>("Cap");
        _lamp = GetNodeOrNull<MeshInstance3D>("Lamp");
        _label = GetNodeOrNull<Label3D>("Label");

        if (_cap is null || _lamp is null || _label is null)
        {
            // Loud: a button with no cap is a state change with nothing moving, which is exactly
            // the defect this whole packet exists to remove, and it would look in-game like a
            // button that "did not work".
            GD.PushError($"[button] {Name}: expects authored children Cap, Lamp and Label "
                         + $"(cap={_cap != null} lamp={_lamp != null} label={_label != null}). "
                         + "See HoldingRoom.tscn's StartButton for the shape.");
            return;
        }

        _capRest = _cap.Position;

        // The lamp and the cap are TINTED at runtime, so each instance needs its own material —
        // a SubResource authored in a room scene is shared by every instantiation of that scene,
        // and the world self-test instantiates each room twice. Duplicating a resource creates no
        // nodes, so the packed-vs-live node count is untouched.
        _lampMaterial = (_lamp.MaterialOverride as StandardMaterial3D)?.Duplicate() as StandardMaterial3D;
        if (_lampMaterial != null)
            _lamp.MaterialOverride = _lampMaterial;
        _capMaterial = (_cap.MaterialOverride as StandardMaterial3D)?.Duplicate() as StandardMaterial3D;
        if (_capMaterial != null)
        {
            _cap.MaterialOverride = _capMaterial;
            _capBaseEmission = _capMaterial.EmissionEnergyMultiplier;
        }

        // The face copy is AUTHORED (so the scene can be opened and read) and CHECKED (so it
        // cannot drift from the tested copy table). One sentence in two places is fine when one
        // of them fails loudly.
        string want = RoundButtonText.Label(Kind);
        if (_label.Text != want)
        {
            GD.PushWarning($"[button] {Name}: label reads \"{_label.Text}\" but "
                           + $"RoundButtonText.Label({Kind}) is \"{want}\" — using the code's.");
            _label.Text = want;
        }

        ApplyLamp(RoundLamp.Dark, force: true);
    }

    /// <summary>The kind this node's groups declare. Falls back to <see cref="RoundButtonKind.Start"/>
    /// with a loud error rather than throwing: a level authoring slip should be a red self-test
    /// and a shouting log, not a crash on every peer that loads the world.</summary>
    private RoundButtonKind ResolveKind()
    {
        bool start = IsInGroup(GroupStart);
        bool confirm = IsInGroup(GroupConfirm);
        bool end = IsInGroup(GroupEnd);
        int declared = (start ? 1 : 0) + (confirm ? 1 : 0) + (end ? 1 : 0);
        if (declared == 1)
            return start ? RoundButtonKind.Start
                : confirm ? RoundButtonKind.Confirm
                : RoundButtonKind.End;

        GD.PushError($"[button] {Name}: declares {declared} of the three kind groups "
                     + $"({GroupStart} / {GroupConfirm} / {GroupEnd}); exactly one is required. "
                     + "Groups go in the [node] header (.claude/rules/godot-scenes.md).");
        return RoundButtonKind.Start;
    }

    // ------------------------------------------------------------------------------------
    // The press
    // ------------------------------------------------------------------------------------

    /// <summary>
    /// <b>The player pressed Interact with this button as the winning candidate.</b> Runs on the
    /// pressing client (<see cref="IPressable"/>'s contract) and does exactly one thing: asks.
    ///
    /// <para><b>No local prediction, not even the cap.</b> The cap goes down when the SERVER says
    /// a press happened, on every peer including this one, so what the presser sees is the same
    /// event the other player sees rather than a local animation that a refused press would then
    /// have to take back. At localhost and LAN latencies that is a frame or two; the alternative
    /// is a button that appears to work and then silently does not, which is the failure
    /// mode.</para>
    /// </summary>
    public void Press() => RoundControls.Instance?.ClientRequestPress(Kind);

    /// <summary>
    /// <b>The server's answer, played on every peer.</b> Called from
    /// <c>RoundControls.ApplyPressResult</c>, which is reliable + <c>CallLocal</c>, so the server
    /// runs this too and the log line below exists on the one process a headless suite reads.
    /// </summary>
    /// <param name="accepted">Did the round act on it.</param>
    /// <param name="reason">Why not, if not.</param>
    /// <param name="mine">Is this peer the presser — the only one that gets the sound, the shake
    /// and the sentence.</param>
    public void PlayPressResult(bool accepted, PressRefusal reason, bool mine)
    {
        // THE CAP MOVES ON EVERY PEER, ACCEPTED OR REFUSED. Bible §3: a thing described with a
        // physical verb reads best if it visibly does that, and a refusal that did not move the
        // cap would be indistinguishable from a press the game never received.
        _pressedLeft = FlashSec;
        _lastRefused = !accepted;
        if (_cap != null)
            _cap.Position = _capRest + Vector3.Forward * CapTravelM;

        GD.Print($"[button] {Kind} {(accepted ? "ACCEPTED" : "REFUSED")} "
                 + $"reason={reason} cap -{CapTravelM * 100f:0} cm"
                 + (mine ? " (this peer pressed it)" : string.Empty));

        if (!mine)
            return;

        // THE PRESSER, AND ONLY THE PRESSER, GETS AN ANSWER IN THREE CHANNELS. Bible §2 —
        // feedback at the exact moment of the trigger, or the player presses again.
        if (accepted)
        {
            SfxLab.PlayStream3D(this, GlobalPosition, SfxLab.Get(Sfx.Pop),
                volumeDb: -4f, pitchJitter: 0.03f, maxDistance: 12f);
        }
        else
        {
            _shakeLeft = ShakeSec;
            SfxLab.PlayStream3D(this, GlobalPosition, SfxLab.Get(Sfx.Thunk),
                volumeDb: -4f, pitchJitter: 0.03f, maxDistance: 12f);
            string sentence = RoundButtonText.Sentence(reason, Kind);
            if (sentence.Length > 0)
                Ui.Hud.RoundStripWidget.SayLocal(sentence);
        }
    }

    // ------------------------------------------------------------------------------------
    // Presentation
    // ------------------------------------------------------------------------------------

    public override void _Process(double delta)
    {
        if (_cap is null)
            return;

        // The cap's return, and the refusal shake, both as a plain ease — a Tween per press would
        // allocate three nodes a round for two eighths of a second of motion.
        Vector3 offset = Vector3.Zero;
        if (_pressedLeft > 0.0)
        {
            _pressedLeft -= delta;
            float t = (float)Mathf.Clamp(1.0 - (_pressedLeft / CapReturnSec), 0.0, 1.0);
            offset += Vector3.Forward * CapTravelM * (1f - t);
        }
        if (_shakeLeft > 0.0)
        {
            _shakeLeft -= delta;
            // A fixed 26 Hz wobble on the cap's local X. Deterministic (no RNG) so a capture
            // taken at a known moment looks the same every run.
            float phase = (float)((ShakeSec - _shakeLeft) * Mathf.Tau * 26.0);
            float decay = (float)Mathf.Clamp(_shakeLeft / ShakeSec, 0.0, 1.0);
            offset += Vector3.Right * (Mathf.Sin(phase) * ShakeAmplitudeM * decay);
        }
        _cap.Position = _capRest + offset;

        _sinceLampPoll += delta;
        if (_sinceLampPoll < LampPollSec)
            return;
        _sinceLampPoll = 0.0;
        PollLamp();
    }

    /// <summary>Re-derives the lamp from this peer's own view of the round. Pure arithmetic in
    /// <see cref="RoundButtonRules.Lamp"/>; everything here is gathering its inputs.</summary>
    private void PollLamp()
    {
        RoundLamp want = _pressedLeft > 0.0
            ? RoundLamp.Pressed
            : RoundButtonRules.Lamp(Kind, RoundControls.LampFactsFor((int)Multiplayer.GetUniqueId()));

        if (want != _lastLoggedLamp)
        {
            // One line per CHANGE, not per poll: three buttons times a handful of changes a round
            // is a log a human can read, and it is what a headless suite has instead of a screen.
            GD.Print($"[button] {Kind} lamp {_lastLoggedLamp} -> {want}");
            _lastLoggedLamp = want;
        }
        ApplyLamp(want, force: false);
    }

    private void ApplyLamp(RoundLamp state, bool force)
    {
        if (!force && state == Lamp && _capMaterial == null)
            return;
        Lamp = state;

        if (_lampMaterial != null)
        {
            Color c = state switch
            {
                RoundLamp.Lit => LampLit,
                RoundLamp.Pressed => _lastRefused ? LampRefused : LampPressed,
                _ => LampDark,
            };
            _lampMaterial.AlbedoColor = c;
            _lampMaterial.EmissionEnabled = true;
            _lampMaterial.Emission = c;
            // Value, not hue alone (ART-BIBLE §3.4 / the repo's standing colour rule): a dark
            // lamp is dark as well as grey, so the affordance survives a colour-blind player and
            // a greyscale capture.
            _lampMaterial.EmissionEnergyMultiplier = state == RoundLamp.Dark ? 0.05f : 1.6f;
        }

        if (_capMaterial != null)
            _capMaterial.EmissionEnergyMultiplier =
                Highlighted ? _capBaseEmission * 2.2f : _capBaseEmission;
    }
}
