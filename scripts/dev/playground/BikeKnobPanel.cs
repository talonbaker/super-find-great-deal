using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using Godot;

namespace MpFoundation.Dev.Playground;

/// <summary>
/// <b>A live knob for every number the bike runs on</b> (BIKE-2x, 2026-09-02). The foot motor's 58
/// rows have had a panel since MOVE-4d; the bike's have not, and a value that can only be retuned
/// by editing a file and rebuilding is a value that gets judged once, badly. This is that panel, for
/// all three bike records at once: <see cref="BikeTuning"/> (the state layer's, BIKE-0's),
/// <see cref="BikeHandlingTuning"/> and <see cref="BikeCameraTuning"/> (this packet's).
///
/// <para><b>F10, and the choice is not arbitrary.</b> <c>MotorTuningPanel</c> holds F1/F2/F4/F5/F7
/// and records in its own header that F3 is <c>PerfHud</c>'s, F9 is the <c>DevScreenshot</c>
/// autoload's and F12 is Steam's overlay (in Sail that header also reserved F6 for
/// <c>CampfireMenu</c> and F8 for <c>AmbientLab</c>; the MVP extraction cut both systems and MOVE-1
/// dropped the reservations with them). F10 is
/// claimed by nothing in this repository — checked by grep over <c>scripts/</c> and against
/// <c>project.godot</c>'s autoload block, 2026-09-02. The lab's own keys (TAB, R, K, G, the digit
/// banks) and the bike's (Q, H, N, the mouse buttons) are all untouched.</para>
///
/// <para><b>Why the rows are found by reflection instead of typed out.</b> There are 84 of them
/// across three records and the first agent is still adding to <c>BikeTuning</c> on the branch this
/// one is stacked on. A hand-written table would be a second copy of a list that is still moving —
/// exactly the kind of duplicate that goes stale silently and leaves a knob nobody can reach while
/// the panel still looks complete. Reflected rows cannot go stale: a row added to any of the three
/// records appears here on the next build, and <c>--bike-handling-selftest</c> asserts the count
/// matches so a property that somehow fails to bind is a red rather than a quiet absence.</para>
///
/// <para><b>The ranges are a tuning convenience and NOT a validator.</b> They are derived from each
/// row's default and its name (a <c>*Fraction</c> row runs 0..1, a <c>*Deg</c> row 0..90, everything
/// else 0..3x its default), because the real guard is downstream and always has been:
/// <c>BikeRig.Ride</c> clamps every multiplied row inside its knob's own range and
/// <c>MotorTuning.Validate</c> refuses anything illegal. A slider here cannot seat an illegal
/// motor tuning however far it is dragged.</para>
///
/// <para><b>Every write of <see cref="BikeTuning"/> goes through
/// <see cref="BikeLayer.SetTuning"/>, whole.</b> That is the one existing writer of the ride, and it
/// re-derives the ride from the captured foot tuning on the next tick — so a row dragged
/// <i>mid-ride</i> changes the ride you are on rather than the one you get next time. Writing the
/// static directly would work while on foot and silently do nothing while mounted, which is the
/// half of the session anyone would actually be tuning in.</para>
///
/// <para>Lab-only. It writes no velocity, holds no simulation state, and is inert until F10.</para>
/// </summary>
public sealed partial class BikeKnobPanel : CanvasLayer
{
    /// <summary>Show/hide. The only key that works while the panel is hidden.</summary>
    public const Key ToggleKey = Key.F10;

    private const int PanelWidthPx = 620;
    private const int MarginPx = 12;
    private const int NoticesShown = 4;

    /// <summary>
    /// <b>One tunable number, bound to wherever it actually lives.</b> The getter and setter are
    /// closures over the live static, not a cached value: a preset pressed on the ALT row moves the
    /// record underneath this panel, and a row that had kept its own copy would then display a
    /// number the bike is not running on. The panel re-reads every row every frame for the same
    /// reason the harness readout does.
    /// </summary>
    public sealed class Row
    {
        public required string Group { get; init; }
        public required string Name { get; init; }
        public required float Min { get; init; }
        public required float Max { get; init; }
        public required float DefaultValue { get; init; }
        public required bool IsBool { get; init; }
        public required Func<float> Get { get; init; }
        public required Action<float> Set { get; init; }

        /// <summary>One nudge of the arrow keys. A hundredth of the range, floored so a narrow row
        /// still moves: a step of zero is a knob that looks live and is not.</summary>
        public float Step => Mathf.Max((Max - Min) * 0.01f, IsBool ? 1f : 0.001f);

        /// <summary>Has this row been moved off the value the record opened on? The header counts
        /// these, so "what have I actually changed" is answerable at a glance rather than by
        /// scrolling eighty-four rows.</summary>
        public bool Moved => Mathf.Abs(Get() - DefaultValue) > 1e-6f;
    }

    private readonly List<Row> _rows = new();
    private readonly List<string> _notices = new();
    private readonly List<Label> _rowLabels = new();

    private PanelContainer _frame = null!;
    private ScrollContainer _scroll = null!;
    private Label _header = null!;
    private Label _noticeLabel = null!;
    private int _selected;
    private int _lastScrolledTo = -1;

    /// <summary>The bike layer, so a <see cref="BikeTuning"/> write goes through its single writer.
    /// Null on a scripted run that never built one; the panel then writes the static directly, which
    /// is correct precisely because there is no ride to re-derive.</summary>
    public BikeLayer? Bike { get; set; }

    /// <summary>Where a notice goes besides this panel — the harness's own notice strip, so a bike
    /// row moved with this panel hidden is still announced where the foot rows announce
    /// themselves.</summary>
    public Action<string>? Notice { get; set; }

    /// <summary>Whether the panel is up when the scene opens. Set before the node enters the
    /// tree.</summary>
    public bool StartVisible { get; set; }

    /// <summary>Every row the panel found. Exposed so the self-test can assert the count against the
    /// three records' property counts — a row that silently failed to bind is otherwise
    /// indistinguishable from a row that does not exist.</summary>
    public IReadOnlyList<Row> Rows => _rows;

    public bool PanelVisible
    {
        get => _frame.Visible;
        set
        {
            _frame.Visible = value;
            if (value)
                _lastScrolledTo = -1;
        }
    }

    public override void _Ready()
    {
        // Above the harness readout, and above MotorTuningPanel's layer 2 so the two never fight
        // over which one draws on top when both are open.
        Layer = 3;
        BuildRows();
        BuildUi();
        PanelVisible = StartVisible;
    }

    // --- Finding the rows -------------------------------------------------------------------------

    private void BuildRows()
    {
        BindRecord<BikeTuning>("BIKE", BikeTuning.Default,
            () => BikeTuning.Current,
            next =>
            {
                // Whole, through the layer's single writer — see the class doc. A mid-ride drag has
                // to re-derive the ride or it is a knob that only works while you are standing.
                if (Bike is not null)
                    Bike.SetTuning(next);
                else
                    BikeTuning.Current = next;
            });

        BindRecord<BikeHandlingTuning>("HANDLING", BikeHandlingTuning.Default,
            () => BikeHandlingTuning.Current,
            next => BikeHandlingTuning.Current = next);

        BindRecord<BikeCameraTuning>("CAMERA", BikeCameraTuning.Default,
            () => BikeCameraTuning.Current,
            next => BikeCameraTuning.Current = next);
    }

    /// <summary>
    /// <b>Bind every public float and bool property of one record.</b>
    ///
    /// <para><b>The boxing is load-bearing and worth explaining once.</b> These are
    /// <c>readonly record struct</c>s whose properties are <c>init</c>-only, so there is no ordinary
    /// way to write one field. Boxing the current value gives a mutable heap copy;
    /// <c>PropertyInfo.SetValue</c> on that box calls the init accessor (which is an ordinary setter
    /// carrying a <c>modreq</c> the reflection layer ignores); unboxing hands back a whole new record
    /// with one row changed, which is then written through the record's own single writer. The
    /// original record is never mutated in place — a <c>with</c> expression by another route, which
    /// is exactly what the panel wants.</para>
    ///
    /// <para>Only <c>float</c> and <c>bool</c> are bound. Anything else in one of these records is a
    /// row this panel cannot draw, and it is skipped LOUDLY — the self-test compares the bound count
    /// against the property count, so an unbindable row is a red rather than a knob that quietly is
    /// not there.</para>
    /// </summary>
    private void BindRecord<T>(string group, T defaults, Func<T> read, Action<T> write)
        where T : struct
    {
        foreach (PropertyInfo p in typeof(T)
                     .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(p => p.CanRead && p.CanWrite))
        {
            bool isBool = p.PropertyType == typeof(bool);
            if (!isBool && p.PropertyType != typeof(float))
                continue;

            float defaultValue = ToFloat(p.GetValue(defaults));
            (float min, float max) = RangeFor(p.Name, defaultValue, isBool);

            PropertyInfo prop = p;   // captured per iteration, deliberately
            _rows.Add(new Row
            {
                Group = group,
                Name = prop.Name,
                Min = min,
                Max = max,
                DefaultValue = defaultValue,
                IsBool = isBool,
                Get = () => ToFloat(prop.GetValue(read())),
                Set = v =>
                {
                    object boxed = read()!;
                    prop.SetValue(boxed, isBool ? v >= 0.5f : Mathf.Clamp(v, min, max));
                    write((T)boxed);
                },
            });
        }
    }

    private static float ToFloat(object? value) => value switch
    {
        float f => f,
        bool b => b ? 1f : 0f,
        _ => 0f,
    };

    /// <summary>
    /// <b>A slider's range, derived from its name and its default.</b> Name-driven rather than
    /// tabulated for the same reason the rows are reflected: a table of 84 ranges beside a list of
    /// 84 rows is a second copy that drifts. A row whose name ends in <c>Fraction</c> is a 0..1
    /// quantity; one ending in <c>Deg</c> is an angle; everything else gets three times its own
    /// default, which is generous enough to find the far end of any of these numbers and tight
    /// enough that one arrow press is a nudge rather than a jump.
    ///
    /// <para>A default of exactly zero would otherwise produce a zero-width range — a knob that
    /// looks live and cannot move — so those get 0..1 and are named here rather than discovered in
    /// a session.</para>
    /// </summary>
    private static (float Min, float Max) RangeFor(string name, float defaultValue, bool isBool)
    {
        if (isBool)
            return (0f, 1f);
        if (name.EndsWith("Fraction", StringComparison.Ordinal))
            return (0f, 1f);
        if (name.EndsWith("Deg", StringComparison.Ordinal))
        {
            // 0..90 for an angle, EXCEPT when the row's own default is so small that a 90-degree
            // slider makes it untunable. The step is one per cent of the range, so a 2.5-degree row
            // on a 0..90 slider nudges by 0.9 degrees — better than a third of its own value per
            // press, and nine degrees on SHIFT. BIKE-4B's WobbleAmplitudeDeg is that row.
            //
            // The guard is deliberately narrow: only rows whose three-times-default span is under
            // 20 degrees are affected, which is why every angle row that existed before this line
            // was written keeps the range it had — FovBaseDeg (75), LeanMaxDeg (38) and FovAtCapDeg
            // (18) all span 20 or more and still get 0..90. The self-test asserts exactly that,
            // by name, so a future row cannot quietly move one of them.
            float degSpan = Mathf.Abs(defaultValue) * 3f;
            return (0f, degSpan >= 20f ? 90f : Mathf.Max(degSpan, 1f));
        }
        if (Mathf.Abs(defaultValue) < 1e-6f)
            return (0f, 1f);
        float span = Mathf.Abs(defaultValue) * 3f;
        return defaultValue < 0f ? (-span, 0f) : (0f, span);
    }

    // --- The panel ---------------------------------------------------------------------------------

    private void BuildUi()
    {
        _frame = new PanelContainer { Name = "BikeKnobFrame" };
        // The LEFT edge, deliberately: MotorTuningPanel owns the right-hand column, and two panels
        // stacked on the same side is one panel nobody can read.
        _frame.SetAnchorsPreset(Control.LayoutPreset.LeftWide);
        _frame.OffsetLeft = MarginPx;
        _frame.OffsetRight = PanelWidthPx;
        _frame.OffsetTop = MarginPx;
        _frame.OffsetBottom = -MarginPx;
        _frame.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.08f, 0.07f, 0.90f),
            BorderColor = new Color(0.34f, 0.44f, 0.38f, 0.9f),
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
        column.AddThemeConstantOverride("separation", 5);
        _frame.AddChild(column);

        _header = Text(15, new Color(1f, 1f, 1f));
        column.AddChild(_header);

        _noticeLabel = Text(11, new Color(0.98f, 0.80f, 0.45f));
        _noticeLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(_noticeLabel);

        column.AddChild(new HSeparator());

        _scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            FocusMode = Control.FocusModeEnum.None,
        };
        column.AddChild(_scroll);

        var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 2);
        _scroll.AddChild(list);

        string group = "";
        foreach (Row row in _rows)
        {
            if (row.Group != group)
            {
                group = row.Group;
                Label heading = Text(13, new Color(0.55f, 0.90f, 0.74f));
                heading.Text = $"── {group} ──────────────────────────────────";
                list.AddChild(heading);
                // Headings are NOT added to _rowLabels: that list is index-parallel to _rows, and
                // one extra entry in it would shift every label by one from the first heading
                // onward — a panel showing every value against the wrong name, which reads as
                // plausible nonsense rather than as a bug.
            }

            Label label = Text(12, new Color(0.86f, 0.90f, 0.94f));
            list.AddChild(label);
            _rowLabels.Add(label);
        }

        column.AddChild(new HSeparator());

        Label keys = Text(11, new Color(0.66f, 0.74f, 0.70f));
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
        "F10 hide    UP/DOWN select    LEFT/RIGHT nudge (SHIFT x10)    PGUP/PGDN group    "
      + "HOME reset this row\n"
      + "SHIFT+F10 reset ALL bike rows    CTRL+F10 print C# to the console    "
      + "ALT+F10 save user://bike-tuning.txt\n"
      + "CTRL+ALT+0-6 HANDLING PRESETS (1 = no turn curve, 3 = wiggle charge, 6 = handling off)\n"
      + "While this panel is up it owns the arrow keys; F10 hands them back to F1's motor panel.";

    // --- Per-frame sync ------------------------------------------------------------------------------

    public override void _Process(double delta)
    {
        if (!_frame.Visible)
            return;

        int moved = 0;
        foreach (Row row in _rows)
            if (row.Moved)
                moved++;

        _header.Text = $"BIKE KNOBS   {_rows.Count} rows across 3 records   {moved} moved";
        _noticeLabel.Text = _notices.Count == 0
            ? "(nothing moved yet)"
            : string.Join("\n", _notices);

        for (int i = 0; i < _rows.Count && i < _rowLabels.Count; i++)
        {
            Row row = _rows[i];
            float value = row.Get();
            string shown = row.IsBool
                ? (value >= 0.5f ? "ON " : "off")
                : value.ToString("0.###", CultureInfo.InvariantCulture).PadLeft(8);
            string marker = i == _selected ? ">" : " ";
            string movedMark = row.Moved ? "*" : " ";
            _rowLabels[i].Text = $"{marker}{movedMark} {row.Name,-26} {shown}   {Bar(row, value)}";
            _rowLabels[i].AddThemeColorOverride("font_color", i == _selected
                ? new Color(1f, 0.95f, 0.60f)
                : row.Moved ? new Color(0.62f, 0.93f, 0.72f) : new Color(0.80f, 0.84f, 0.88f));
        }

        if (_lastScrolledTo != _selected && _selected < _rowLabels.Count)
        {
            _lastScrolledTo = _selected;
            _scroll.EnsureControlVisible(_rowLabels[_selected]);
        }
    }

    /// <summary>A twenty-cell text bar. Text rather than a real <c>HSlider</c> because eighty-four
    /// sliders is eighty-four focusable controls competing with WASD for the keyboard, and the
    /// standing law in this lab is that a panel may take the screen only when it has taken neither
    /// the camera nor the controls.</summary>
    private static string Bar(Row row, float value)
    {
        const int cells = 20;
        float span = Mathf.Max(row.Max - row.Min, 1e-6f);
        int filled = Mathf.Clamp(Mathf.RoundToInt((value - row.Min) / span * cells), 0, cells);
        return "[" + new string('#', filled) + new string('.', cells - filled) + "]";
    }

    // --- Input ----------------------------------------------------------------------------------------

    /// <summary>
    /// <b>Handled in <c>_ShortcutInput</c>, and that is what keeps this panel out of the harness's
    /// file.</b> Godot dispatches shortcut input before unhandled input, and
    /// <c>MovementPlayground._UnhandledInput</c> is where <c>MotorTuningPanel</c> is offered its
    /// keys — so consuming an event here takes it cleanly, without a line being added to anyone
    /// else's key handler.
    ///
    /// <para><b>It claims the arrows only while it is visible.</b> Hidden, the only key it takes is
    /// F10, and the motor panel's navigation is byte-identical to what it was before this file
    /// existed. Visible, it owns them, and the key line says so — two panels silently splitting the
    /// arrow keys is worse than one panel owning them and saying it does.</para>
    /// </summary>
    public override void _ShortcutInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true } key)
            return;
        // Echo is wanted on the six navigation keys (holding RIGHT to walk a row up its range is the
        // whole ergonomics of a keyboard slider) and never on a toggle.
        if (key.Echo && !IsRepeatable(key.Keycode))
            return;

        if (key.Keycode == ToggleKey && !key.Echo)
        {
            if (key.ShiftPressed) ResetAll();
            else if (key.CtrlPressed) PrintBlock();
            else if (key.AltPressed) SaveBlock();
            else PanelVisible = !PanelVisible;
            GetViewport().SetInputAsHandled();
            return;
        }

        // CTRL+ALT+digit is the HANDLING preset row (BikeHandlingTuning.Presets). It is claimed
        // here rather than in the harness's key handler for the same reason everything else in this
        // packet is: that file is not this packet's to edit, and _ShortcutInput runs before the
        // handler that would otherwise see it. CTRL+ALT is free — MovementPlayground's own bike row
        // is ALT-without-CTRL, and its CTRL bank is plain CTRL — but consuming the event here is
        // what makes that true rather than merely likely.
        if (!key.Echo && key.CtrlPressed && key.AltPressed && DigitOf(key.Keycode) is int d and >= 0)
        {
            if (BikeHandlingTuning.ForKey(d) is { } preset)
            {
                BikeHandlingTuning.Current = preset.Tuning;
                Announce($"handling preset {preset.Key} — {preset.Name}");
            }
            GetViewport().SetInputAsHandled();
            return;
        }

        if (!PanelVisible || _rows.Count == 0)
            return;

        switch (key.Keycode)
        {
            case Key.Down: Select(_selected + 1); break;
            case Key.Up: Select(_selected - 1); break;
            case Key.Right: Nudge(key.ShiftPressed ? 10f : 1f); break;
            case Key.Left: Nudge(key.ShiftPressed ? -10f : -1f); break;
            case Key.Pagedown: Select(NextGroupRow(1)); break;
            case Key.Pageup: Select(NextGroupRow(-1)); break;
            case Key.Home: ResetSelected(); break;
            default: return;
        }
        GetViewport().SetInputAsHandled();
    }

    private static bool IsRepeatable(Key key)
        => key is Key.Up or Key.Down or Key.Left or Key.Right or Key.Pageup or Key.Pagedown;

    /// <summary>The digit a key event names, or -1. Number row and keypad both, the same shape
    /// <c>MovementPlayground.DigitOf</c> has — a preset you cannot reach from the pad you happen to
    /// be resting on is one you will not press.</summary>
    private static int DigitOf(Key key) => key switch
    {
        >= Key.Key0 and <= Key.Key9 => (int)(key - Key.Key0),
        >= Key.Kp0 and <= Key.Kp9 => (int)(key - Key.Kp0),
        _ => -1,
    };

    private void Select(int index)
    {
        if (_rows.Count == 0)
            return;
        _selected = Mathf.PosMod(index, _rows.Count);
    }

    /// <summary>The first row of the next (or previous) group. Eighty-four rows is more than an
    /// arrow key wants to walk, and the three records are the natural chapters.</summary>
    private int NextGroupRow(int direction)
    {
        if (_rows.Count == 0)
            return 0;
        string here = _rows[_selected].Group;
        for (int i = 1; i <= _rows.Count; i++)
        {
            int index = Mathf.PosMod(_selected + i * direction, _rows.Count);
            if (_rows[index].Group != here)
            {
                // Walk back to the FIRST row of that group when moving forward, so PGDN lands on a
                // heading rather than in the middle of the previous chapter.
                if (direction > 0)
                    return index;
                while (index > 0 && _rows[index - 1].Group == _rows[index].Group)
                    index--;
                return index;
            }
        }
        return _selected;
    }

    private void Nudge(float steps)
    {
        Row row = _rows[_selected];
        float next = row.IsBool
            ? (row.Get() >= 0.5f ? 0f : 1f)
            : Mathf.Clamp(row.Get() + row.Step * steps, row.Min, row.Max);
        row.Set(next);
        Announce($"{row.Group}.{row.Name} = {Format(row, next)}");
    }

    private void ResetSelected()
    {
        Row row = _rows[_selected];
        row.Set(row.DefaultValue);
        Announce($"{row.Group}.{row.Name} reset to {Format(row, row.DefaultValue)}");
    }

    /// <summary>Every bike row back to the record's own default. Whole records rather than row by
    /// row: eighty-four individual writes would fire eighty-four ride re-derives, and the last of
    /// them would be the only one that mattered anyway.</summary>
    private void ResetAll()
    {
        if (Bike is not null)
            Bike.SetTuning(BikeTuning.Default);
        else
            BikeTuning.Current = BikeTuning.Default;
        BikeHandlingTuning.Current = BikeHandlingTuning.Default;
        BikeCameraTuning.Current = BikeCameraTuning.Default;
        Announce("ALL bike rows reset to their record defaults");
    }

    /// <summary>
    /// <b>The paste block.</b> Same workflow the motor panel's F5 has and for the same reason: a
    /// number found by feel is worthless unless it can get back into the source, and making anyone
    /// transcribe eighty-four floats off a screen is how that round trip quietly stops happening.
    /// Rows that have not moved are omitted, so what comes out is a <c>with</c> expression naming
    /// exactly what the session changed.
    /// </summary>
    public string RenderBlock()
    {
        var sb = new StringBuilder();
        sb.AppendLine("// Bike tuning, printed from the F10 panel — "
                    + DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        string group = "";
        int moved = 0;
        foreach (Row row in _rows)
        {
            if (!row.Moved)
                continue;
            if (row.Group != group)
            {
                group = row.Group;
                sb.AppendLine();
                sb.AppendLine($"// --- {group} ---");
            }
            sb.AppendLine($"    {row.Name} = {Format(row, row.Get())},");
            moved++;
        }
        if (moved == 0)
            sb.AppendLine("// nothing moved this session.");
        return sb.ToString();
    }

    private static string Format(Row row, float value) => row.IsBool
        ? (value >= 0.5f ? "true" : "false")
        : value.ToString("0.####", CultureInfo.InvariantCulture) + "f";

    private void PrintBlock()
    {
        GD.Print(RenderBlock());
        Announce("printed the moved rows to the console");
    }

    /// <summary>
    /// Write the same block to <c>user://bike-tuning.txt</c>.
    ///
    /// <para><b>A plain text file next to the block, not a load-on-start tuning file</b>, and that is
    /// deliberate: <c>MotorTuningPanel</c>'s JSON is loaded at startup, and a bike file that did the
    /// same would mean a lab session silently opening on someone else's numbers — which is exactly
    /// the failure the motor panel's <c>FileIo</c> flag exists to prevent under capture. The bike is
    /// a prototype whose whole value is that every run starts from a known record; this file is a
    /// notebook, not a state.</para>
    ///
    /// <para>A failure to write is announced and swallowed. <c>user://</c> on this project resolves
    /// to a shared real profile directory, and a lab that will not open because a notebook could not
    /// be saved is worse than a missing notebook.</para>
    /// </summary>
    private void SaveBlock()
    {
        const string path = "user://bike-tuning.txt";
        try
        {
            // Godot.FileAccess, spelled out: System.IO.FileAccess is an enum of the same name and
            // the ambiguity is a compile error rather than a subtle one, but naming it here also
            // says which filesystem this is — `user://` resolves through Godot, not the OS.
            using Godot.FileAccess? file =
                Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Write);
            if (file is null)
            {
                Announce($"save FAILED — {path} ({Godot.FileAccess.GetOpenError()})");
                return;
            }
            file.StoreString(RenderBlock());
            Announce($"saved {path}");
        }
        catch (Exception e)
        {
            Announce($"save FAILED — {e.Message}");
        }
    }

    private void Announce(string line)
    {
        _notices.Insert(0, line);
        while (_notices.Count > NoticesShown)
            _notices.RemoveAt(_notices.Count - 1);
        Notice?.Invoke("bike knobs: " + line);
    }

    /// <summary>One terse line for the harness readout, in the same voice as
    /// <c>BikeLayer.Line()</c>.</summary>
    public string Line()
    {
        int moved = 0;
        foreach (Row row in _rows)
            if (row.Moved)
                moved++;
        return $"KNOBS      F10 panel {(PanelVisible ? "UP" : "hidden")}   {_rows.Count} bike rows"
             + $"   {moved} moved   selected {_rows[Mathf.Min(_selected, _rows.Count - 1)].Name}";
    }
}
