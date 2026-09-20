using System;
using System.Globalization;
using Godot;
using MpFoundation.Net;

namespace MpFoundation.Dev;

/// <summary>
/// <b>One row of the MOVE-4d knob panel</b>: the knob's name and index, its live value and unit,
/// the shipped default it is being compared against, a slider whose track paints the legal window
/// as a band, and — for every row a test pins — a badge naming the test that pins it.
///
/// <para><b>Nothing here is a display copy.</b> <see cref="Sync"/> is handed the live tuning every
/// frame and rebuilds every label and the slider position from it, so a value shown here cannot
/// disagree with what the motor is running on. In particular the slider is re-seated with
/// <c>SetValueNoSignal</c> rather than trusted: when the parity guard refuses a drag, or the
/// validator clamps one, the grabber snaps back on the next frame because the tuning did not
/// move.</para>
///
/// <para><b>Pinned rows warn; they do not lock</b> (spec §3.4). Most of the table is pinned by some
/// test and a handful are frozen to within ±0.1%, so locking would leave a small minority of the
/// sliders working and freeze the game at exactly the values the lab exists to question. What the
/// badge states instead is the true and actionable thing: moving this outside its window costs you
/// <i>that</i> test. <i>(No count is written out here, and the historical tally that used to be —
/// MOVE-4d's, MOVE-4f's, MOVE-5's — has been deleted along with it, because it went stale again the
/// very next wave. The live figures are <c>MotorTuningKnobs.All.Count</c> for the table and
/// <c>MotorTuningSession.Rows.Count(k =&gt; k.IsPinned)</c> for the pinned rows, and
/// <c>MotorTuningSessionTests</c> is where they are asserted — a prose total in a doc comment has
/// been wrong every wave it has been written.)</i></para>
///
/// <para><b>A discrete row draws its stops</b> (spec §11.5). Five rows — the two toggles, the two
/// wire widths and the mode switch — ride as floats with <c>Step = 1</c> and an integer range,
/// which is correct for <c>MotorTuning</c> and reads as broken on a continuous track: a grabber
/// that can only rest in three places, on a bar that looks like it has a thousand. They get a tick
/// per legal value painted on the track, derived from <c>Min</c>, <c>Max</c> and <c>Step</c> like
/// everything else here — <b>no row is named</b>, so a future <c>Step = 1</c> knob is discrete
/// without an edit, and <see cref="HSlider"/>'s own <c>TickCount</c> is deliberately not used
/// because it renders through a theme icon this panel does not ship.</para>
/// </summary>
public partial class MotorKnobRow : VBoxContainer
{
    /// <summary>How far the slider's usable track is inset from its own rect by the grabber. Godot
    /// does not expose the figure, so the band is drawn against an approximation — it is an
    /// at-a-glance affordance, and the exact numbers are on the badge beside it.</summary>
    private const float GrabInsetPx = 7f;

    /// <summary>Most stops a discrete row may paint. Above this the ticks stop being stops and start
    /// being hatching — <c>TurnAcceleration</c> has <c>Step = 1</c> over a span of 78 and is a
    /// continuous knob by any reading, so the shape finding §11.5 makes is bounded by a legal stop
    /// count rather than applied to every integer step in the table.</summary>
    private const int MaxDrawnStops = 12;

    private static readonly Color BandHolds = new(0.35f, 0.78f, 0.45f, 0.28f);
    private static readonly Color BandBreached = new(0.92f, 0.33f, 0.30f, 0.30f);
    private static readonly Color DefaultTick = new(1f, 1f, 1f, 0.55f);
    private static readonly Color StopTick = new(1f, 1f, 1f, 0.22f);
    private static readonly Color SelectedRow = new(0.40f, 0.62f, 1.00f, 0.16f);

    private static readonly Color TextNormal = new(0.92f, 0.93f, 0.95f);
    private static readonly Color TextMoved = new(1.00f, 0.85f, 0.42f);
    private static readonly Color TextMuted = new(0.62f, 0.65f, 0.70f);
    private static readonly Color TextBreached = new(1.00f, 0.45f, 0.42f);

    private Label _name = null!;
    private Label _value = null!;
    private Label _shipped = null!;
    private HSlider _slider = null!;
    private Label _badge = null!;

    private Action<MotorKnob, float>? _onChanged;
    private bool _selected;
    private bool _hasBand;
    private bool _bandBreached;
    private float _bandLo;
    private float _bandHi;

    /// <summary>The knob-table row this widget is. Set once, by <see cref="Configure"/>.</summary>
    public MotorKnob Knob { get; private set; } = null!;

    /// <summary>Builds the row from <paramref name="knob"/> — every range, step, unit, default and
    /// pin comes off the table row, so this widget carries no knowledge of its own about any
    /// particular knob.</summary>
    public void Configure(MotorKnob knob, Action<MotorKnob, float> onChanged)
    {
        Knob = knob;
        _onChanged = onChanged;
        AddThemeConstantOverride("separation", 1);

        var head = new HBoxContainer();
        AddChild(head);

        _name = Small(14, TextNormal);
        _name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(_name);

        _value = Small(14, TextNormal);
        _value.CustomMinimumSize = new Vector2(150, 0);
        _value.HorizontalAlignment = HorizontalAlignment.Right;
        head.AddChild(_value);

        _shipped = Small(12, TextMuted);
        _shipped.CustomMinimumSize = new Vector2(92, 0);
        _shipped.HorizontalAlignment = HorizontalAlignment.Right;
        head.AddChild(_shipped);

        _slider = new HSlider
        {
            MinValue = knob.Min,
            MaxValue = knob.Max,
            Step = knob.Step,
            Value = knob.Default,
            CustomMinimumSize = new Vector2(0, 16),
            // Never focusable: a focused slider swallows the arrow keys, and the arrow keys are how
            // a knob is nudged while the mouse is still captured by the camera.
            FocusMode = FocusModeEnum.None,
        };
        _slider.ValueChanged += OnSliderValueChanged;
        AddChild(_slider);

        _badge = Small(11, TextMuted);
        _badge.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        AddChild(_badge);
    }

    private static Label Small(int size, Color color)
    {
        var label = new Label();
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    private void OnSliderValueChanged(double value) => _onChanged?.Invoke(Knob, (float)value);

    /// <summary>
    /// Rebuilds the whole row from the live tuning. Called every frame; there is no dirty check,
    /// because a dirty check is a cached copy wearing a hat.
    /// </summary>
    public void Sync(in MotorTuning tuning, bool selected)
    {
        _selected = selected;
        float value = Knob.Get(tuning);
        bool moved = value != Knob.Default;
        bool breached = Knob.Breaches(tuning, out float pinned, out float? lo, out float? hi);

        _name.Text = $"{(selected ? "▸" : " ")} {Knob.Index,2}  {Knob.Name}";
        _name.AddThemeColorOverride("font_color", selected ? TextMoved : TextNormal);

        // Invariant culture, deliberately: this number is the one that ends up in the printed C#,
        // and a panel reading "21,50" beside a literal reading "21.5f" is a panel arguing with the
        // printer on any machine whose locale uses a comma.
        string format = "F" + MotorTuningSession.DecimalsFor(Knob.Step);
        _value.Text = $"{value.ToString(format, CultureInfo.InvariantCulture)} {Knob.Unit}";
        _value.AddThemeColorOverride("font_color",
            breached ? TextBreached : moved ? TextMoved : TextNormal);

        _shipped.Text = moved
            ? $"was {Knob.Default.ToString(format, CultureInfo.InvariantCulture)}"
            : "default";

        _slider.SetValueNoSignal(value);

        _badge.Text = BadgeText(pinned, lo, hi, breached);
        _badge.AddThemeColorOverride("font_color", breached ? TextBreached : TextMuted);

        _hasBand = Knob.IsPinned;
        _bandBreached = breached;
        _bandLo = lo ?? Knob.Min;
        _bandHi = hi ?? Knob.Max;

        QueueRedraw();
    }

    private string BadgeText(float pinned, float? lo, float? hi, bool breached)
    {
        string prefix = "";
        if (Knob.PinQuantityLabel.Length > 0)
            prefix = $"{Knob.PinQuantityLabel} {pinned:0.###}   ";

        if (!Knob.IsPinned)
            return prefix + "free — no test pins this row.";

        return prefix
             + $"{(breached ? "PINNED — OUT OF BOUNDS" : "PINNED")} {Window(lo, hi)} — "
             + $"{Knob.PinTest} ({Knob.PinSource})";
    }

    private static string Window(float? lo, float? hi) => (lo, hi) switch
    {
        (float l, float h) when l == h => $"exactly {l:0.####}",
        (float l, float h) => $"[{l:0.####}, {h:0.####}]",
        (float l, null) => $"> {l:0.####}",
        (null, float h) => $"<= {h:0.####}",
        _ => "(unbounded)",
    };

    /// <summary>Paints the selection highlight, the legal-window band and the shipped-default tick
    /// <i>behind</i> the children — so "how far can I move this before it costs me" is answered by
    /// looking at the track rather than by reading the badge (§3.4).</summary>
    public override void _Draw()
    {
        if (_selected)
            DrawRect(new Rect2(Vector2.Zero, Size), SelectedRow);

        if (Knob is null || _slider is null)
            return;

        float span = Knob.Max - Knob.Min;
        if (span <= 0f)
            return;

        Vector2 at = _slider.Position;
        Vector2 size = _slider.Size;
        float usable = Mathf.Max(1f, size.X - 2f * GrabInsetPx);

        float Map(float v) =>
            at.X + GrabInsetPx + Mathf.Clamp((v - Knob.Min) / span, 0f, 1f) * usable;

        if (_hasBand)
        {
            float x0 = Map(_bandLo);
            float x1 = Map(_bandHi);
            DrawRect(new Rect2(x0, at.Y, Mathf.Max(2f, x1 - x0), size.Y),
                _bandBreached ? BandBreached : BandHolds);
        }

        // §11.5's discrete rows: one faint tick per legal value, UNDER the shipped-default tick so
        // the default still reads as the brighter of the two when they coincide (which they always
        // do on a discrete row, since every legal value is a stop).
        int stops = DiscreteStops(Knob);
        for (int i = 0; i < stops; i++)
        {
            float x = Map(Knob.Min + i * Knob.Step);
            DrawLine(new Vector2(x, at.Y + 3f), new Vector2(x, at.Y + size.Y - 3f), StopTick, 1f);
        }

        float tick = Map(Knob.Default);
        DrawLine(new Vector2(tick, at.Y), new Vector2(tick, at.Y + size.Y), DefaultTick, 2f);
    }

    /// <summary>
    /// <b>How many stops a row has, or 0 if it is not a discrete row</b> (spec §11.5). Discrete
    /// means: a whole-number step, whole-number bounds, and few enough stops that painting them all
    /// is information rather than texture.
    ///
    /// <para>Pure and static so <c>MotorTuningSessionTests</c> can assert which rows the panel
    /// treats as discrete without instantiating a <c>Control</c> — the same split MOVE-4d gave
    /// every other decision in this panel.</para>
    /// </summary>
    public static int DiscreteStops(MotorKnob knob)
    {
        if (knob.Step != Mathf.Round(knob.Step) || knob.Step <= 0f)
            return 0;
        if (knob.Min != Mathf.Round(knob.Min) || knob.Max != Mathf.Round(knob.Max))
            return 0;
        int stops = (int)Mathf.Round((knob.Max - knob.Min) / knob.Step) + 1;
        return stops >= 2 && stops <= MaxDrawnStops ? stops : 0;
    }
}
