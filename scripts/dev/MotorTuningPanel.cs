using System;
using System.Collections.Generic;
using System.Text;
using Godot;
using MpFoundation.Net;

namespace MpFoundation.Dev;

/// <summary>
/// <b>The knob panel (MOVE-4d, extended by MOVE-5d).</b> A slider for every one of the
/// <see cref="MotorTuning"/> fields, grouped per MOVE-4a §2.2 and MOVE-5 §11.1, the pin that
/// constrains each row named on the row, the coupled invariants evaluated live, and the four keys
/// that make a tuning survive the window closing: save, reload, print-as-C#, reset.
///
/// <para><b>MOVE-5d added no row to this file, and that is the acceptance criterion rather than a
/// convenience.</b> Every knob the wave added appears because
/// <see cref="MotorTuningSession.Groups"/> derives the layout from <see cref="MotorTuningKnobs.All"/>
/// and nothing else — the table grew by two dozen rows across MOVE-5 with no edit here. What MOVE-5d
/// did change is everything that had a count <i>typed into it</i>: the key help and the print notice
/// read <see cref="MotorTuningKnobs.All"/><c>.Count</c> now, because a hand-typed total is a second
/// list wearing a disguise, and it was already wrong the moment the wave landed. <i>(MOVE-5g had to
/// come back and strip three more prose totals this doc block and its siblings had grown since. No
/// number of rows is written out anywhere in <c>scripts/dev</c> any more; ask the table.)</i></para>
///
/// <para><b>Built from <see cref="MotorTuningKnobs"/>, never from a second list.</b> Rows, groups,
/// ranges, steps, units, defaults and pins all come off the knob table by way of
/// <see cref="MotorTuningSession.Groups"/>. There is no hand-typed knob name anywhere in this
/// file — add a row to the table and it appears here, in its group, with its range.</para>
///
/// <para><b>It takes neither the camera nor the controls.</b> The standing repo law is that UI may
/// take the screen only when it has taken both, and this panel takes neither: it is a right-hand
/// column, the mouse stays captured by <c>SandboxCamera</c>, and every knob is reachable from the
/// keyboard — because tuning a jump you cannot perform while looking at the slider is not tuning.
/// Dragging a slider with the pointer works too, after ESC frees the mouse, which is the sandbox's
/// own pre-existing behaviour and is not changed here.</para>
///
/// <para><b>Nothing displayed is cached.</b> Every label and every grabber is rebuilt from the live
/// tuning each frame, so a refused write or a clamped value snaps back in front of the person who
/// typed it instead of leaving the panel quietly lying about the motor.</para>
/// </summary>
public partial class MotorTuningPanel : CanvasLayer
{
    // --- The keys. Raw keycodes, deliberately: this scene may not write project.godot, and an
    // InputMap action declared at runtime would be a second opinion about a binding.
    // F3 is PerfHud's, F9 is the DevScreenshot autoload's and F12 is Steam's overlay — none of
    // them are available. (In Sail this line also reserved F6 for CampfireMenu and F8 for
    // AmbientLab; the MVP extraction cut both, and MOVE-1 dropped the reservations with them.
    // The keys are free here, and are still not taken: the reserved set is the shorter one.)

    /// <summary>Show/hide the panel. The only key that works while it is hidden.</summary>
    public const Key ToggleKey = Key.F1;

    /// <summary>Force a write of <c>user://movement-tuning.json</c>. Every knob edit already writes
    /// it (§6.2); this is for the person who wants to be sure.</summary>
    public const Key SaveKey = Key.F2;

    /// <summary>Re-read the file. The A/B partner of <see cref="ResetKey"/>.</summary>
    public const Key ReloadKey = Key.F4;

    /// <summary>Print-as-C# to console, clipboard and archive. With SHIFT: every row in the
    /// table.</summary>
    public const Key PrintKey = Key.F5;

    /// <summary>Everything back to the shipped defaults. Does not touch the file.</summary>
    public const Key ResetAllKey = Key.F7;

    private const int PanelWidthPx = 640;
    private const int MarginPx = 12;
    private const int NoticesShown = 4;
    private const int LabelWidth = 26;

    private const string HoldsColor = "#7fd694";
    private const string BreachColor = "#ff7269";

    private readonly List<MotorKnobRow> _rows = new();

    private MotorTuningSession _session = null!;
    private PanelContainer _frame = null!;
    private ScrollContainer _scroll = null!;
    private Label _header = null!;
    private Label _path = null!;
    private RichTextLabel _invariants = null!;
    private Label _notices = null!;
    private int _selected;
    private int _lastScrolledTo = -1;

    /// <summary>
    /// <b>False disables the tuning file entirely</b> — no load at startup, no write on apply. The
    /// scripted capture run sets it: a measurement run must read the shipped defaults or its apex
    /// figures mean nothing, and it must never overwrite a tuning a human left in that file.
    /// Set before the node enters the tree.
    /// </summary>
    public bool FileIo { get; set; } = true;

    /// <summary>Whether the panel is up when the scene opens. Set before the node enters the tree.</summary>
    public bool StartVisible { get; set; } = true;

    /// <summary>The session this panel drives. Exposed so the harness's readout and its capture log
    /// can read the same state the panel shows rather than keeping their own.</summary>
    public MotorTuningSession Session => _session;

    public bool PanelVisible
    {
        get => _frame.Visible;
        set
        {
            _frame.Visible = value;
            if (value)
                _lastScrolledTo = -1;   // re-centre on the selected row when it comes back
        }
    }

    /// <summary>The selected row, as a knob-table row.</summary>
    public MotorKnob SelectedKnob => MotorTuningSession.Rows[_selected];

    public override void _Ready()
    {
        // Above the harness readout, below the DevScreenshot toast.
        Layer = 2;

        _session = new MotorTuningSession(FileIo ? MotorTuningFile.AbsolutePath() : "")
        {
            // §6.3 wants every warning on the readout rather than only on stdout — so it gets both:
            // the panel shows the newest few, and the console keeps all of them, including any
            // raised while the panel was hidden.
            NoticeSink = line => GD.Print("[motor-tuning] " + line),
        };
        BuildUi();
        PanelVisible = StartVisible;

        // Startup load, through MOVE-4b's single writer. If the guard refuses — a live network
        // session — Current stays where it is, the refusal is on the readout, and every slider
        // below is inert until the session ends. That is the guard doing its job, not a bug.
        _session.LoadFromFile();
    }

    // --- Construction ---------------------------------------------------------------------------

    private void BuildUi()
    {
        _frame = new PanelContainer { Name = "Frame" };
        _frame.SetAnchorsPreset(Control.LayoutPreset.RightWide);
        _frame.OffsetLeft = -PanelWidthPx;
        _frame.OffsetRight = -MarginPx;
        _frame.OffsetTop = MarginPx;
        _frame.OffsetBottom = -MarginPx;
        _frame.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.06f, 0.07f, 0.09f, 0.88f),
            BorderColor = new Color(0.30f, 0.34f, 0.40f, 0.9f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 4,
            CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4,
            CornerRadiusBottomRight = 4,
            ContentMarginLeft = 12,
            ContentMarginRight = 12,
            ContentMarginTop = 10,
            ContentMarginBottom = 10,
        });
        AddChild(_frame);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 6);
        _frame.AddChild(column);

        _header = Text(16, new Color(1f, 1f, 1f));
        column.AddChild(_header);

        _path = Text(11, new Color(0.60f, 0.64f, 0.70f));
        _path.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(_path);

        _invariants = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.Off,
        };
        _invariants.AddThemeFontSizeOverride("normal_font_size", 12);
        column.AddChild(_invariants);

        _notices = Text(11, new Color(0.98f, 0.80f, 0.45f));
        _notices.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(_notices);

        column.AddChild(new HSeparator());

        _scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            FocusMode = Control.FocusModeEnum.None,
        };
        column.AddChild(_scroll);

        var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 7);
        _scroll.AddChild(list);

        foreach (MotorTuningSession.KnobGroup group in MotorTuningSession.Groups)
        {
            Label heading = Text(13, new Color(0.55f, 0.74f, 1.00f));
            heading.Text = $"── {group.Name.ToUpperInvariant()} ──────────────────────────────";
            list.AddChild(heading);

            foreach (MotorKnob knob in group.Knobs)
            {
                var row = new MotorKnobRow { Name = knob.Name };
                list.AddChild(row);
                row.Configure(knob, OnKnobDragged);
                _rows.Add(row);
            }
        }

        column.AddChild(new HSeparator());

        Label keys = Text(11, new Color(0.66f, 0.70f, 0.76f));
        keys.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        keys.Text = KeyHelp;
        column.AddChild(keys);
    }

    private static Label Text(int size, Color color)
    {
        var label = new Label();
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    /// <summary>The panel's keys, in one place so the harness readout can quote them rather than
    /// keep a second copy that goes stale.</summary>
    public static string KeyHelp =>
        "F1 hide panel    UP/DOWN select    LEFT/RIGHT nudge one step (SHIFT x10)    "
      + "PGUP/PGDN group    HOME reset this knob\n"
      + $"F2 save file    F4 reload file    F5 print C# to clipboard (SHIFT = all {MotorTuningKnobs.All.Count})    "
      + "F7 reset all to shipped (file untouched)";

    // --- Per-frame sync -------------------------------------------------------------------------

    public override void _Process(double delta)
    {
        if (!_frame.Visible)
            return;

        MotorTuning t = _session.Live;

        int changed = MotorTuningSession.ChangedCount(t);
        int breachedKnobs = MotorTuningSession.BreachedKnobCount(t);
        _header.Text = $"MOTOR TUNING   {changed} of {MotorTuningKnobs.All.Count} moved   "
                     + $"{breachedKnobs} pin{(breachedKnobs == 1 ? "" : "s")} out of bounds";

        _path.Text = _session.FileIoEnabled
            ? $"{MotorTuningFile.UserPath}  —  {_session.SaveCount} write(s), "
            + $"{_session.LoadCount} read(s) this session   [{_session.FilePath}]"
            : "file I/O OFF for this run (scripted capture) — nothing is loaded and nothing is written";

        _invariants.Text = InvariantMarkup(t);
        _notices.Text = NoticeText();

        for (int i = 0; i < _rows.Count; i++)
            _rows[i].Sync(t, i == _selected);

        if (_lastScrolledTo != _selected)
        {
            _lastScrolledTo = _selected;
            _scroll.EnsureControlVisible(_rows[_selected]);
        }
    }

    /// <summary>§3.4's live coupled-constraint readout: several pins constrain a <i>product</i>
    /// rather than a value, so a panel that marked each knob individually still could not say that
    /// a combination had just walked out of bounds.</summary>
    private static string InvariantMarkup(in MotorTuning t)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[b]INVARIANTS[/b] — coupled, evaluated live");
        foreach (MotorInvariant inv in MotorTuningInvariants.Evaluate(t))
        {
            string label = inv.Name.Length >= LabelWidth
                ? inv.Name
                : inv.Name + " " + new string('.', LabelWidth - inv.Name.Length - 1);
            string measured = inv.BoundSpeaksForItself
                ? inv.Bound
                : inv.Bound.StartsWith("(", StringComparison.Ordinal)
                    ? $"{inv.Value:0.000} {inv.Bound}"
                    : $"{inv.Value:0.000} / {inv.Bound}";
            string verdict = inv.Holds
                ? $"[color={HoldsColor}]holds[/color]"
                : $"[color={BreachColor}]BREACHED[/color]";
            sb.AppendLine($"  {label} {measured,-24} {verdict}");
        }
        return sb.ToString().TrimEnd();
    }

    private string NoticeText()
    {
        IReadOnlyList<string> notices = _session.Notices;
        if (notices.Count == 0)
            return "";

        var sb = new StringBuilder();
        int from = Math.Max(0, notices.Count - NoticesShown);
        if (from > 0)
            sb.AppendLine($"({from} earlier notice(s) — all of them are in the console)");
        for (int i = from; i < notices.Count; i++)
            sb.AppendLine("• " + notices[i]);
        return sb.ToString().TrimEnd();
    }

    // --- Input ----------------------------------------------------------------------------------

    /// <summary>
    /// Handles one key press for the panel. Called from <c>MovementPlayground._UnhandledInput</c>
    /// so that every key this scene owns is dispatched from one place; returns <c>true</c> when the
    /// event was consumed.
    ///
    /// <para><b>Only the toggle works while the panel is hidden.</b> A hidden panel that still
    /// silently rewrote the tuning on a stray F7 would be the worst of both.</para>
    /// </summary>
    public bool HandleKey(InputEventKey key)
    {
        if (key.Keycode == ToggleKey)
        {
            PanelVisible = !PanelVisible;
            return true;
        }
        if (!PanelVisible)
            return false;

        bool shift = key.ShiftPressed;
        MotorKnob knob = SelectedKnob;

        switch (key.Keycode)
        {
            case Key.Down:
                Select(_selected + 1);
                return true;
            case Key.Up:
                Select(_selected - 1);
                return true;
            case Key.Pagedown:
                Select(NextGroupRow(+1));
                return true;
            case Key.Pageup:
                Select(NextGroupRow(-1));
                return true;
            case Key.Right:
                _session.Nudge(knob, shift ? 10 : 1);
                return true;
            case Key.Left:
                _session.Nudge(knob, shift ? -10 : -1);
                return true;
            case Key.Home:
                _session.ResetKnob(knob);
                return true;
            case SaveKey:
                SaveNow();
                return true;
            case ReloadKey:
                _session.ReloadFromFile();
                return true;
            case PrintKey:
                Print(shift);
                return true;
            case ResetAllKey:
                _session.ResetAll();
                return true;
            default:
                return false;
        }
    }

    private void Select(int index)
    {
        int count = MotorTuningSession.Rows.Count;
        _selected = ((index % count) + count) % count;
    }

    /// <summary>First row of the next (or previous) group, so the far end of the table is a handful
    /// of keystrokes deep rather than <see cref="MotorTuningKnobs.All"/><c>.Count - 1</c>.
    /// <b>MOVE-5 is what made this load-bearing:</b> at the thirty-one rows MOVE-4f closed with, a
    /// held arrow key reached anything; at the table's present size, across nine groups, it does
    /// not.</summary>
    private int NextGroupRow(int direction)
    {
        IReadOnlyList<MotorKnob> rows = MotorTuningSession.Rows;
        string here = rows[_selected].Group;

        if (direction > 0)
        {
            for (int i = _selected + 1; i < rows.Count; i++)
                if (rows[i].Group != here)
                    return i;
            return 0;
        }

        int startOfThis = _selected;
        while (startOfThis > 0 && rows[startOfThis - 1].Group == here)
            startOfThis--;
        if (startOfThis == 0)
            return rows.Count - 1;

        string previous = rows[startOfThis - 1].Group;
        int startOfPrevious = startOfThis - 1;
        while (startOfPrevious > 0 && rows[startOfPrevious - 1].Group == previous)
            startOfPrevious--;
        return startOfPrevious;
    }

    private void OnKnobDragged(MotorKnob knob, float value) => _session.SetKnob(knob, value);

    private void SaveNow()
    {
        if (!_session.FileIoEnabled)
        {
            _session.Note("file I/O is off for this run — nothing was written.");
            return;
        }
        int before = _session.SaveCount;
        _session.Save();
        _session.Note(_session.SaveCount > before
            ? $"saved {_session.FilePath}"
            : $"save FAILED — {_session.FilePath}");
    }

    /// <summary>
    /// §6.4's paste block to the console, the OS clipboard and a timestamped archive.
    /// <b>The clipboard is not optional</b> — "paste it back" is the workflow, and making anyone
    /// hunt a console line for it is how the round trip quietly stops happening.
    /// </summary>
    private void Print(bool all)
    {
        DateTimeOffset stamp = DateTimeOffset.Now;
        string block = _session.Render(all, stamp);
        try
        {
            string archive = MotorTuningPrint.Publish(block, stamp);
            string what = all ? $"all {MotorTuningKnobs.All.Count} rows" : "what moved";
            _session.Note(archive.Length > 0
                ? $"printed {what} to the console and the clipboard; archived at {archive}"
                : $"printed {what} to the console and the clipboard; "
                + "the archive copy could not be written");
        }
        catch (Exception e)
        {
            // A headless or clipboard-less host must not take the panel down with it; the block is
            // still on the console, which is the copy that matters least often and never nothing.
            GD.Print(block);
            _session.Note($"print partly failed ({e.Message}) — the block is on the console.");
        }
    }
}
