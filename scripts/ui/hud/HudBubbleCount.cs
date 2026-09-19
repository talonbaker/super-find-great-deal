using Godot;
using Sail.Game.Bubble;

namespace MpFoundation.Ui.Hud;

/// <summary>
/// <b>The group's tally, top-centre: an icon, an <c>x</c>, and one number.</b>
///
/// <para>Talon's brief: <i>"On-screen HUD: upper-center of screen — bubble icon + 'x' + current
/// count (e.g. bubble x 42)."</i> No total — the total lives on the hub pedestal, where a player
/// who wants to know how much of the level is left goes to look at it. Splitting "how many have
/// we got" from "how many are there" is what keeps the HUD a glance and the pedestal a
/// destination.</para>
///
/// <para><b>It never covers anything.</b> A top-centre readout on <see cref="GameHud"/>'s layer,
/// no panel, no input: non-diegetic UI may cover the frame only where camera <i>and</i> controls
/// have been taken, and neither is (the standing law, and <c>UiCoverageLaw</c>'s own subject).
/// It reserves its height through <c>UiColumns</c> so nothing else in the top-centre column can
/// land on it.</para>
///
/// <para><b>Hidden until the world has a counter</b>, and reading <c>x —</c> until this peer's
/// state actually came from the server. <see cref="BubbleCounter.Synced"/> exists precisely so a
/// HUD does not confidently render a zero it invented on a peer that has not been told anything
/// yet — a late joiner walking into a level at 38/100 must never flash "x 0".</para>
///
/// <para><b>Polled for the value, driven by the broadcast for the reaction, and both land on the
/// same tick.</b> The value is read on <see cref="GameHud"/>'s single 10 Hz poll like every
/// sibling — one clock for the whole HUD, so two readouts changing on one event never stagger.
/// The wobble, though, is armed from <see cref="BubbleCounter.Changed"/>, the reliable
/// <c>CallLocal</c> broadcast BT-6 already sends to every peer on every pop. Two reasons it is
/// not a diff of the polled number:
/// <list type="number">
/// <item>A diff cannot tell a pop from a reset. <c>Changed</c> can — it carries the bubble id,
/// and a reset sends −1. The lever putting a hundred bubbles back is not a collection and must
/// not read like one.</item>
/// <item>Six players popping in the same tenth of a second is one message per pop but one poll,
/// so the coalescing the brief asks for ("it never queues or jackhammers") falls out of arming a
/// flag rather than out of a queue that has to be drained.</item>
/// </list>
/// The reaction reads identically whether the popper was you or someone across the map — ruled
/// with Talon, not an oversight. Your own pop already has diegetic confirmation in front of you
/// (the bubble bursts, <c>Sfx.Pop</c>, a puff); this is doing the other job, which is <i>the
/// group total just moved</i>.</para>
/// </summary>
public partial class HudBubbleCount : PanelContainer
{
    /// <summary>Drawn size of the icon, square. Talon asked for roughly 32–40 px; 40 is the top
    /// of that range because the icon is the only thing in this readout that says WHAT is being
    /// counted, and it is competing with a 3D world behind it rather than with a page.</summary>
    public const float IconPx = 40f;

    private Label _count = null!;
    private BubbleIcon _icon = null!;

    /// <summary>The counter this widget is currently subscribed to. Re-resolved on every tick
    /// because the counter is added by the world at its <c>_Ready</c>, which is after the HUD's,
    /// and is replaced outright on a reconnect resume.</summary>
    private BubbleCounter? _bound;

    /// <summary>Set by <see cref="OnCounterChanged"/> when a POP lands; spent by the next
    /// <see cref="Tick"/>. This is the coalescing: however many pops arrive between two ticks,
    /// the flag is one bool and the icon is nudged once.</summary>
    private bool _popPending;

    private int _lastCount = int.MinValue;
    private bool _lastSynced;

    public HudBubbleCount() => MouseFilter = MouseFilterEnum.Ignore;

    /// <summary>The rendered string for a tally. One label, not an "x" and a number in two ranks:
    /// the readout is three glyph groups wide and its acceptance criterion is stated on the text
    /// of a single node, so splitting it would buy a typographic nicety at the cost of making the
    /// thing under test a concatenation. Mono (<see cref="HudTheme.RoleTimer"/>) so a digit
    /// rolling over does not re-flow the string.</summary>
    public static string Format(int count) => $"x {count}";

    /// <summary>What the readout says on a peer that has not yet been told the tally. An em dash
    /// rather than a zero, and rather than an empty box: "we do not know yet" is information, and
    /// a widget that vanishes for a second on join reads as a bug.</summary>
    public const string UnsyncedText = "x —";

    public override void _Ready()
    {
        HudTheme.BindPanel(this);

        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        // Space.Tight is the scale's own "an icon and its number" rung — the proximity rule: the
        // gap inside this readout must be smaller than the gap around it, which is the column's.
        row.AddThemeConstantOverride("separation", Design.UiScale.Px(Design.Space.Tight));
        AddChild(row);

        _icon = new BubbleIcon
        {
            CustomMinimumSize = new Vector2(IconPx, IconPx),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        row.AddChild(_icon);

        _count = HudTheme.MakeLabel(UnsyncedText, HudTheme.RoleTimer);
        row.AddChild(_count);

        // Absent until there is something to count — see the class doc. GameHud publishes this
        // widget's occupied height into the shared top-centre column, and a hidden occupant must
        // report zero, so the column closes up rather than reserving space for nothing.
        Visible = false;
    }

    public override void _ExitTree() => Unbind();

    /// <summary>Driven by <see cref="GameHud"/>'s single poll. See the class doc for why the
    /// value is polled and the reaction is not.</summary>
    public void Tick()
    {
        BubbleCounter? counter = BubbleCounter.Instance;
        if (counter != null && !GodotObject.IsInstanceValid(counter))
            counter = null;

        if (!ReferenceEquals(counter, _bound))
            Bind(counter);

        if (counter == null)
        {
            Visible = false;
            return;
        }

        Visible = true;

        bool synced = counter.Synced;
        int count = counter.Count;
        bool pop = _popPending;
        _popPending = false;

        if (count != _lastCount || synced != _lastSynced)
        {
            if (!synced)
            {
                _count.Text = UnsyncedText;
            }
            else if (_lastSynced && _lastCount >= 0 && pop)
            {
                // A pop rolls the digits from where they were; anything else (first sync, a late
                // joiner's state arriving, the lever putting everything back) SNAPS, because a
                // roll is an acknowledgement of something the group just did and none of those
                // are that.
                HudMotion.CountTo(_count, _lastCount, count, Format);
            }
            else
            {
                _count.Text = Format(count);
            }
            _lastCount = count;
            _lastSynced = synced;
        }

        if (pop)
            _icon.Pop();
    }

    private void Bind(BubbleCounter? counter)
    {
        Unbind();
        _bound = counter;
        if (counter != null)
            counter.Changed += OnCounterChanged;
        // A rebind is a new session's state arriving, not a pop: drop any armed reaction and
        // force the next tick to rewrite the label rather than compare against a dead tally.
        _popPending = false;
        _lastCount = int.MinValue;
        _lastSynced = false;
    }

    private void Unbind()
    {
        if (_bound != null && GodotObject.IsInstanceValid(_bound))
            _bound.Changed -= OnCounterChanged;
        _bound = null;
    }

    /// <summary>BT-6's broadcast, on every peer. <c>id &lt; 0</c> is a reset or a late-join sync,
    /// which moves the number without anyone having collected anything — see the class doc.
    /// </summary>
    private void OnCounterChanged(int id, int count, int byPeer)
    {
        if (id >= 0)
            _popPending = true;
    }
}
