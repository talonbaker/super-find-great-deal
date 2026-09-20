using System;
using System.Collections.Generic;
using Godot;
using MpFoundation.Ui.Design;

namespace MpFoundation.Game.Round;

/// <summary>
/// <b>The board on the holding room's wall</b> (HOLD-1, 2026-09-19): who is in this game, what
/// they are about to do, what they have scored, and what just happened.
///
/// <para><b>It is the same object as <see cref="RoundClock"/> with more lines on it</b>, and
/// that is the packet's instruction rather than a coincidence: authored in
/// <c>scenes/game/world/supermarket/HoldingBoard.tscn</c>, painted from the one poll
/// <see cref="RoundAudio"/> already runs, coloured from <see cref="UiTokens"/> rather than from
/// the scene file, and hidden until the round is synced. Everything this class does is set
/// <c>Text</c> and <c>PixelSize</c> on labels that already exist — it adds no node, so the
/// packed-vs-live count <c>SupermarketWorldSelfTest</c> takes of this prefab stays honest
/// (<c>.claude/rules/godot-scenes.md</c>).</para>
///
/// <para><b>What it shows is decided in <see cref="HoldingBoardModel"/>, which has no engine in
/// it at all.</b> The row set, the ordering, the role wording, the header and the footer are all
/// pure functions of one <see cref="HideSeekView"/> and are tested in <c>tests/unit</c> without a
/// scene. This class is the part that cannot be tested there and therefore contains as little as
/// possible.</para>
///
/// <para><b>A late joiner is complete on its first painted frame</b>, because ROUND-1's message
/// is an absolute fold rather than a delta: every row, the header and the footer come from ONE
/// message. <c>tests/Run-HoldingBoardTest.ps1</c> asserts the late joiner's rows equal the host's
/// rather than trusting it.</para>
/// </summary>
public partial class HoldingBoard : Node3D
{
    /// <summary>Node names inside <c>HoldingBoard.tscn</c>. Spelled once, for
    /// <see cref="RoundClock.PanelNodeName"/>'s reason: a second spelling of a node is a silent
    /// miss and this repo has paid for that already.</summary>
    public const string PanelNodeName = "Panel";

    /// <inheritdoc cref="PanelNodeName"/>
    public const string HeaderNodeName = "HeaderLabel";

    /// <inheritdoc cref="PanelNodeName"/>
    public const string OverflowNodeName = "OverflowLabel";

    /// <inheritdoc cref="PanelNodeName"/>
    public const string FooterNodeName = "FooterLabel";

    /// <summary><c>Row0Label</c> .. <c>Row3Label</c>, one per
    /// <see cref="HoldingBoardModel.MaxRows"/>.</summary>
    public static string RowNodeName(int i) => $"Row{i}Label";

    /// <summary>The prefix on this board's log lines, so a suite greps for this rather than for
    /// a sentence somebody may reword. The same discipline
    /// <c>PropManager.AdoptLogPrefix</c> uses.</summary>
    public const string LogPrefix = "[board]";

    private MeshInstance3D? _panel;
    private Label3D? _header;
    private Label3D? _overflow;
    private Label3D? _footer;
    private readonly Label3D?[] _rows = new Label3D?[HoldingBoardModel.MaxRows];
    private StandardMaterial3D? _panelMaterial;

    // The authored pixel sizes, sampled once so the fit has something to shrink FROM. Read off
    // the scene rather than typed here for RoundClock's reason: the size is a level-authoring
    // decision and belongs in the file a level author opens.
    private float _headerBasePixelSize;
    private float _rowBasePixelSize;
    private float _footerBasePixelSize;

    /// <summary>
    /// The value <see cref="_lastHeader"/> and friends hold before anything has been painted,
    /// and it is NOT the empty string.
    ///
    /// <para><b>Measured, in the first capture of this board.</b> The scene authors a
    /// placeholder in every label so the file is openable in the editor and shows its own
    /// layout. <see cref="SetLine"/> early-outs when the new text equals the last text, and with
    /// <c>""</c> as the initial value an empty row NEVER GOT WRITTEN -- so the two unused row
    /// labels, the overflow line and the footer all sat there showing the authored em dash,
    /// which reads as a readout that has something to say and cannot say it. A sentinel no real
    /// line can equal makes the first paint unconditional.</para>
    /// </summary>
    private const string Unpainted = "\u0000";

    // What is currently painted, so a poll that changes nothing costs a handful of string
    // comparisons. The same early-out every HUD widget's Tick makes.
    private string _lastHeader = Unpainted;
    private string _lastFooter = Unpainted;
    private string _lastOverflow = Unpainted;
    private readonly string[] _lastRows = new string[HoldingBoardModel.MaxRows];

    public override void _Ready()
    {
        _panel = GetNodeOrNull<MeshInstance3D>(PanelNodeName);
        _header = GetNodeOrNull<Label3D>(HeaderNodeName);
        _overflow = GetNodeOrNull<Label3D>(OverflowNodeName);
        _footer = GetNodeOrNull<Label3D>(FooterNodeName);
        for (int i = 0; i < _rows.Length; i++)
        {
            _rows[i] = GetNodeOrNull<Label3D>(RowNodeName(i));
            _lastRows[i] = Unpainted;
        }

        if (_panel == null || _header == null || _overflow == null || _footer == null
            || Array.Exists(_rows, r => r == null))
        {
            // Loud, and then inert — RoundClock's rule. A board missing a label looks exactly
            // like a board the round never reached, and the two have completely different fixes.
            GD.PushError($"{LogPrefix} HoldingBoard '{Name}' is missing one of "
                         + $"{PanelNodeName}/{HeaderNodeName}/Row0..{HoldingBoardModel.MaxRows - 1}Label/"
                         + $"{OverflowNodeName}/{FooterNodeName} — check HoldingBoard.tscn.");
            return;
        }

        _headerBasePixelSize = _header.PixelSize;
        _rowBasePixelSize = _rows[0]!.PixelSize;
        _footerBasePixelSize = _footer.PixelSize;

        // Local to the scene (see HoldingBoard.tscn), duplicated defensively anyway: a shared
        // material here would mean the last board to _Ready decides the colour of all of them,
        // which is invisible until somebody changes a token.
        _panelMaterial = _panel.GetActiveMaterial(0) as StandardMaterial3D;
        if (_panelMaterial != null && !_panelMaterial.ResourceLocalToScene)
        {
            _panelMaterial = (StandardMaterial3D)_panelMaterial.Duplicate();
            _panel.SetSurfaceOverrideMaterial(0, _panelMaterial);
        }

        UiThemeService.Bind(this, ApplyTokens);

        // A board that shows a plausible default is worse than a blank wall — RoundClock's class
        // doc has the argument. RoundAudio turns it on when the round is synced.
        Visible = false;

        RoundAudio.RegisterBoard(this);
    }

    public override void _ExitTree() => RoundAudio.UnregisterBoard(this);

    /// <summary>Repaints from the live token set, on bind and on every temperature change.
    /// The header and the footer are quieter than the rows on purpose: the rows are the thing
    /// being scanned and the other two are context.</summary>
    private void ApplyTokens(UiTokens tokens)
    {
        if (_panelMaterial != null)
            _panelMaterial.AlbedoColor = tokens.SurfaceSunken;
        if (_header != null)
            _header.Modulate = tokens.InkRank3;
        if (_overflow != null)
            _overflow.Modulate = tokens.InkRank3;
        if (_footer != null)
            _footer.Modulate = tokens.InkRank2;
        foreach (Label3D? row in _rows)
            if (row != null)
                row.Modulate = tokens.InkRank1;
    }

    /// <summary>
    /// Paint one poll's worth of round. Driven by <see cref="RoundAudio"/> at the HUD's cadence;
    /// there is no <c>_Process</c> on this node.
    /// </summary>
    /// <param name="view">This peer's own folded view — the same value the HUD strip and the
    /// wall clock read, from the same message, which is what makes the three readouts agree by
    /// construction rather than by all three being careful.</param>
    /// <param name="selfPeerId">Who is reading. Changes exactly one thing: the pronoun in this
    /// peer's own row during Holding (<see cref="HoldingBoardModel.RoleCell"/>).</param>
    /// <param name="nameOf">The session's display-name resolver. Null is legal and falls back
    /// through <see cref="HideSeekText.PlayerName"/>, as everywhere else.</param>
    /// <param name="tuning">The DRIVER's tuning, not <c>HideSeekTuning.Current</c> — MATCH-1's
    /// rule, so a readout can never be stepping to a different object from the driver.</param>
    public void Apply(in HideSeekView view, int selfPeerId, Func<int, string>? nameOf,
        in HideSeekTuning tuning)
    {
        if (_header == null || _footer == null || _overflow == null)
            return;

        Visible = true;

        SetLine(_header, ref _lastHeader, HoldingBoardModel.Header(view, tuning),
            _headerBasePixelSize, HoldingBoardLayout.SmallChars);

        IReadOnlyList<HoldingBoardRow> rows = HoldingBoardModel.Rows(view, selfPeerId, nameOf);
        for (int i = 0; i < _rows.Length; i++)
        {
            Label3D? label = _rows[i];
            if (label == null)
                continue;
            // A row with nobody in it is BLANKED, never removed: the labels are authored and a
            // level that frees one of them is a level that fails the packed-vs-live count.
            string line = i < rows.Count ? HoldingBoardModel.RowLine(rows[i]) : "";
            SetLine(label, ref _lastRows[i], line,
                _rowBasePixelSize, HoldingBoardLayout.RowChars);
        }

        int overflow = HoldingBoardModel.Overflow(view);
        SetLine(_overflow, ref _lastOverflow, overflow > 0 ? $"+{overflow} MORE" : "",
            _headerBasePixelSize, HoldingBoardLayout.SmallChars);

        SetLine(_footer, ref _lastFooter, HideSeekText.BoardFooter(view, nameOf, tuning),
            _footerBasePixelSize, HoldingBoardLayout.SmallChars);
    }

    /// <summary>Take the board off the wall — no round to show. Not a fade, for
    /// <see cref="RoundClock.Blank"/>'s reason.</summary>
    public void Blank()
    {
        Visible = false;
        _lastHeader = Unpainted;
        _lastFooter = Unpainted;
        _lastOverflow = Unpainted;
        for (int i = 0; i < _lastRows.Length; i++)
            _lastRows[i] = Unpainted;
    }

    /// <summary>What this board currently reads, for <c>--log-clock</c> and for the smoke. Read
    /// off the LABELS rather than recomputed from the view — CLOCK-1's rule, and the whole
    /// reason the late-joiner assertion means anything: a line built from <c>driver.View</c>
    /// would still compare equal with a blank panel on the wall.</summary>
    public (string Header, IReadOnlyList<string> Rows, string Overflow, string Footer) CurrentText()
    {
        var rows = new List<string>(_lastRows.Length);
        foreach (string r in _lastRows)
            if (r.Length > 0 && r != Unpainted)
                rows.Add(r);
        return (_lastHeader, rows, _lastOverflow, _lastFooter);
    }

    /// <summary>One machine-readable line per log interval, emitted by
    /// <see cref="RoundAudio"/> when <c>--log-clock</c> is on. It shares CLOCK-1's flag rather
    /// than taking one of its own: the two readouts are painted by one poll from one view, and
    /// a suite that wants to see what the walls say wants both. Rows are separated by <c>" | "</c>
    /// because the rows themselves already contain the project's <c>" · "</c>.</summary>
    public string LogLine(long wallMs)
    {
        (string header, IReadOnlyList<string> rows, string overflow, string footer) = CurrentText();
        if (header == Unpainted) header = "";
        if (overflow == Unpainted) overflow = "";
        if (footer == Unpainted) footer = "";
        string joined = rows.Count == 0 ? "(none)" : string.Join(" | ", rows);
        return $"{LogPrefix} wall={wallMs} header=\"{header}\" rows={rows.Count} "
               + $"[{joined}] overflow=\"{overflow}\" footer=\"{footer}\"";
    }

    private static void SetLine(Label3D label, ref string last, string text,
        float basePixelSize, int referenceChars)
    {
        if (text == last)
            return;
        last = text;
        label.Text = text;
        label.PixelSize = HoldingBoardLayout.FitPixelSize(
            basePixelSize, text.Length, referenceChars);
    }
}
