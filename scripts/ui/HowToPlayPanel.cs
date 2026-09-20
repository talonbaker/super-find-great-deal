using System.Collections.Generic;
using Godot;
using MpFoundation.Ui.Design;

namespace MpFoundation.Ui;

/// <summary>
/// The single How-to-Play control graphic (spec §3), built once and shown from two doors:
/// MainMenu's button and PauseOverlay's entry. A self-contained <see cref="CanvasLayer"/>
/// (layer 11 — one above PauseOverlay's 10) so it reliably draws on top of whichever of the two
/// hosts adds it, nested CanvasLayer or not.
///
/// <para><b>There used to be a third door and there is not any more</b> (2026-09-04, Talon's
/// direction). The level opened this panel unprompted on first entry, behind a persisted "don't
/// show this again" tick. What the player meets on entry now is one transient line naming the
/// level's objective (<see cref="PhaseToastText.BubbleGoalToast"/>); this screen is something
/// they ASK for, from the pause menu, and the HUD advertises where — see
/// <c>Hud.HudHowToPlayHint</c>. Everything the first-run door needed and no other door did left
/// with it: the "don't show this again" row, its persisted flag, and the mouse-capture gate that
/// existed only because that door was the one door that could open over a live avatar.</para>
///
/// <para><b>HOWTO-2, 2026-08-29 — rebuilt around real key and mouse icons.</b> Talon (note 2):
/// <i>"'HOW TO PLAY' section needs better UI. Needs icons for right mouse button, left mouse
/// buttons, shift, etc. these should have keys, icons which go with the keys… please make the
/// overall UI bigger so the text is bigger and the icons go along with this."</i> UI-3 already
/// raised the type scale 15% as a set, so the half of that sentence left here is the icons: they
/// are drawn by <see cref="ControlIcon"/> at a size derived from the resolved <c>Body</c> font,
/// which means they grew with the type and will keep tracking it. Nothing on this screen picks a
/// pixel height.</para>
///
/// <para><b>The layout is code, not scene.</b> The scene is now the scrim and nothing else. Every
/// row, every section and every gap is built from the data table, so appending a verb cannot
/// require moving a node — which is the property note 3 needs (see <see cref="HowToPlayContent"/>)
/// and the property the old hand-placed VBox did not have.</para>
///
/// <para>Rows come from <see cref="ControlGlyphs"/> — one data table, glyphs resolved live from
/// the real InputMap, never hardcoded letters (spec §8 scenario 6). Sections whose verbs this
/// build does not declare vanish with their headings.</para>
///
/// <para>Escape/pause is handled in <see cref="_Input"/> (not _UnhandledInput) specifically so it
/// consumes the key BEFORE PauseOverlay's own _UnhandledInput handler ever sees it — opened
/// from the pause menu, this panel is a second CanvasLayer stacked on top of PauseOverlay's,
/// and without this a stray Escape would toggle PauseOverlay.Close() (capturing the mouse)
/// while this panel stayed fullscreen-visible on top of it: unreachable by mouse, a real
/// interrupt-handling bug (INTERACTION-BIBLE §7). Consuming Escape here first means it always
/// just closes the topmost open modal, regardless of which of the three doors opened it.</para>
/// </summary>
public partial class HowToPlayPanel : CanvasLayer
{
    [Signal]
    public delegate void ClosedEventHandler();

    private PanelContainer _card = null!;
    private ScrollContainer _scroll = null!;
    private VBoxContainer _scrollBody = null!;

    /// <summary>Every icon cluster, grouped by the column it was placed in. Clusters are padded
    /// out to a common width per column AFTER the tree exists, because a cap's width is a string
    /// measured in a resolved font and is not knowable at construction. Aligning them is what
    /// turns a list of rows into a table.</summary>
    private readonly List<List<Control>> _clustersByColumn = new();

    public override void _Ready()
    {
        // The one scrim. Its colour lives nowhere else — see UiThemeService.BindScrim.
        UiThemeService.BindScrim(this);
        Layer = UiLayers.PauseChildPanel;

        Build();

        GetViewport().SizeChanged += ClampToViewport;
        CallDeferred(MethodName.AfterLayout);
    }

    public override void _Input(InputEvent @event)
    {
        if (@event.IsActionPressed("pause") || @event.IsActionPressed("ui_cancel"))
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    // --- the screen ---------------------------------------------------------------------------

    private void Build()
    {
        var frame = new MarginContainer { Name = "Frame", MouseFilter = Control.MouseFilterEnum.Ignore };
        frame.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        Margins(frame, UiScale.ScreenMargin);
        AddChild(frame);

        var centre = new CenterContainer { Name = "Center", MouseFilter = Control.MouseFilterEnum.Ignore };
        frame.AddChild(centre);

        _card = new PanelContainer { Name = "Card" };
        centre.AddChild(_card);

        var pad = new MarginContainer { Name = "Pad" };
        Margins(pad, UiScale.Px(Space.Loose));
        _card.AddChild(pad);

        var column = new VBoxContainer { Name = "Column" };
        column.AddThemeConstantOverride("separation", UiScale.Px(Space.Loose));
        pad.AddChild(column);

        column.AddChild(BuildHeader());

        // The screen's one accent instance. It has no primary action to carry the accent, so the
        // keyline is where moonlight arrives — the same component the flow screens use, so this
        // panel and the menu that opened it are accented by the same object (note 5).
        ColorRect keyline = UiKit.Keyline();
        keyline.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        column.AddChild(keyline);

        _scroll = new ScrollContainer
        {
            Name = "Scroll",
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            FollowFocus = true,
        };
        column.AddChild(_scroll);

        _scrollBody = new VBoxContainer { Name = "Body", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _scrollBody.AddThemeConstantOverride("separation", UiScale.Px(Space.Wide));
        _scroll.AddChild(_scrollBody);

        _scrollBody.AddChild(BuildColumns());

        Control? notes = BuildNotes();
        if (notes != null)
            _scrollBody.AddChild(notes);

        column.AddChild(BuildEscHint());

        UiMotion.StaggerIn(column);
    }

    private static void Margins(MarginContainer box, int px)
    {
        box.AddThemeConstantOverride("margin_left", px);
        box.AddThemeConstantOverride("margin_right", px);
        box.AddThemeConstantOverride("margin_top", px);
        box.AddThemeConstantOverride("margin_bottom", px);
    }

    private Control BuildHeader()
    {
        var header = new HBoxContainer { Name = "Header" };
        header.AddThemeConstantOverride("separation", UiScale.Px(Space.Normal));

        header.AddChild(new Label
        {
            Name = "Title",
            Text = "HOW TO PLAY",
            ThemeTypeVariation = "Title",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center,
        });

        var close = new MenuButton
        {
            Name = "CloseButton",
            Text = "X",
            ThemeTypeVariation = "TertiaryAction",
        };
        close.Pressed += Close;
        header.AddChild(close);
        close.CallDeferred(Control.MethodName.GrabFocus);

        return header;
    }

    /// <summary>
    /// The control table, flowed into two columns.
    ///
    /// <para><b>Two columns is a 720p requirement, not a preference.</b> The smallest window the
    /// project supports is 1280x720 (project.godot, display/window/size) and the table is ten rows
    /// of a 33 px cap at Body 17; one column plus header, hint and card padding does not fit
    /// inside 688 px of usable height. Splitting by <i>weight</i> rather than by section count
    /// means a section appended tomorrow rebalances itself instead of overflowing one side.</para>
    /// </summary>
    private Control BuildColumns()
    {
        var sections = new List<ControlGlyphs.Section>(ControlGlyphs.DeclaredSections);

        int total = 0;
        foreach (ControlGlyphs.Section section in sections)
            total += Weight(section);

        var columns = new HBoxContainer { Name = "Columns" };
        columns.AddThemeConstantOverride("separation", UiScale.Px(Space.Wide));

        VBoxContainer left = NewColumn("ColumnA");
        VBoxContainer right = NewColumn("ColumnB");
        columns.AddChild(left);
        columns.AddChild(right);

        _clustersByColumn.Clear();
        var leftClusters = new List<Control>();
        var rightClusters = new List<Control>();
        _clustersByColumn.Add(leftClusters);
        _clustersByColumn.Add(rightClusters);

        int placed = 0;
        int half = (total + 1) / 2;
        foreach (ControlGlyphs.Section section in sections)
        {
            bool goLeft = placed < half;
            VBoxContainer host = goLeft ? left : right;
            host.AddChild(BuildSection(section, goLeft ? leftClusters : rightClusters));
            placed += Weight(section);
        }

        // A single-section build would otherwise leave an empty column claiming half the card.
        right.Visible = right.GetChildCount() > 0;
        return columns;
    }

    private static VBoxContainer NewColumn(string name)
    {
        var box = new VBoxContainer { Name = name, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", UiScale.Px(Space.Loose));
        return box;
    }

    /// <summary>A section's height in rows: one for its heading, plus one per row — two for a row
    /// laid out as the WASD cross, which is two caps tall.</summary>
    private static int Weight(ControlGlyphs.Section section)
    {
        int weight = 1;
        foreach (ControlGlyphs.Row row in section.Rows)
            weight += IsCross(ControlGlyphs.BindingsFor(row)) ? 2 : 1;
        return weight;
    }

    private static Control BuildSection(ControlGlyphs.Section section, List<Control> clusters)
    {
        var box = new VBoxContainer { Name = section.Title };
        box.AddThemeConstantOverride("separation", UiScale.Px(Space.Snug));

        box.AddChild(new Label
        {
            Name = "SectionTitle",
            Text = section.Title,
            ThemeTypeVariation = "SectionLabel",
        });

        foreach (ControlGlyphs.Row row in section.Rows)
        {
            var line = new HBoxContainer();
            line.AddThemeConstantOverride("separation", UiScale.Px(Space.Normal));

            Control cluster = IconCluster(row);
            clusters.Add(cluster);
            line.AddChild(cluster);

            line.AddChild(new Label
            {
                Text = row.Label,
                ThemeTypeVariation = "Body",
                VerticalAlignment = VerticalAlignment.Center,
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            });

            box.AddChild(line);
        }

        return box;
    }

    /// <summary>The icons for one row. Four single-character keys lay out as the physical cross
    /// they are on the keyboard — W over A S D — because that is the shape a player's hand already
    /// knows; anything else is a straight run of caps.</summary>
    private static Control IconCluster(ControlGlyphs.Row row)
    {
        IReadOnlyList<ControlGlyphs.Binding> bindings = ControlGlyphs.BindingsFor(row);

        if (IsCross(bindings))
        {
            var cross = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
            cross.AddThemeConstantOverride("separation", UiScale.Px(Space.Tight));

            var top = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
            top.AddThemeConstantOverride("separation", UiScale.Px(Space.Tight));
            top.AddChild(ControlIcon.For(bindings[0]));
            cross.AddChild(top);

            var bottom = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
            bottom.AddThemeConstantOverride("separation", UiScale.Px(Space.Tight));
            for (int i = 1; i < bindings.Count; i++)
                bottom.AddChild(ControlIcon.For(bindings[i]));
            cross.AddChild(bottom);

            return cross;
        }

        var run = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        run.AddThemeConstantOverride("separation", UiScale.Px(Space.Tight));
        foreach (ControlGlyphs.Binding binding in bindings)
            run.AddChild(ControlIcon.For(binding));
        return run;
    }

    /// <summary>Four bindings, all single-character keys: the WASD shape. Asked of the resolved
    /// bindings rather than of the action names, so a remap to arrow keys stops being drawn as a
    /// cross the moment it stops being one.</summary>
    private static bool IsCross(IReadOnlyList<ControlGlyphs.Binding> bindings)
    {
        if (bindings.Count != 4)
            return false;

        foreach (ControlGlyphs.Binding binding in bindings)
        {
            if (binding.Kind != ControlGlyphs.GlyphKind.Key || binding.Label.Length != 1)
                return false;
        }

        return true;
    }

    /// <summary>Talon's note-3 content, if it has arrived. Returns null while
    /// <see cref="HowToPlayContent.Lines"/> is empty — no heading, no gap, no evidence a block was
    /// ever planned there. See that file for why the list is missing and what filling it costs
    /// (an array literal, and nothing else).</summary>
    private static Control? BuildNotes()
    {
        if (HowToPlayContent.Lines.Length == 0)
            return null;

        var box = new VBoxContainer { Name = "Notes" };
        box.AddThemeConstantOverride("separation", UiScale.Px(Space.Snug));

        box.AddChild(new Label
        {
            Name = "NotesTitle",
            Text = HowToPlayContent.NotesTitle,
            ThemeTypeVariation = "SectionLabel",
        });

        foreach (string line in HowToPlayContent.Lines)
        {
            box.AddChild(new Label
            {
                Text = line,
                ThemeTypeVariation = "Body",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            });
        }

        return box;
    }

    /// <summary>Discoverability (Talon hit this live): ESC already closed the panel before this
    /// hint was added, it just wasn't advertised anywhere. The key is drawn as a cap like every
    /// other key on the screen and resolved live from the <c>pause</c> binding — same rule as
    /// spec §8 scenario 6, so a rebind carries the hint with it.</summary>
    private static Control BuildEscHint()
    {
        var hint = new HBoxContainer { Name = "EscHint", Alignment = BoxContainer.AlignmentMode.Center };
        hint.AddThemeConstantOverride("separation", UiScale.Px(Space.Snug));

        hint.AddChild(new Label
        {
            Text = "Press",
            ThemeTypeVariation = "Caption",
            VerticalAlignment = VerticalAlignment.Center,
        });
        // Caption-ranked, so the cap matches the sentence it sits inside instead of out-sizing it.
        hint.AddChild(new KeycapIcon(ControlGlyphs.BindingFor("pause").Label, "Caption")
        {
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        });
        hint.AddChild(new Label
        {
            Text = "to close",
            ThemeTypeVariation = "Caption",
            VerticalAlignment = VerticalAlignment.Center,
        });

        return hint;
    }

    // --- post-layout: alignment and the height clamp -------------------------------------------

    private void AfterLayout()
    {
        AlignIconColumns();
        ClampToViewport();
    }

    /// <summary>Pad every icon cluster in a column out to the widest one, so the labels beside
    /// them share a left edge. Measured rather than constant: a cap's width is a string in a
    /// resolved font, and a hardcoded gutter would be wrong the first time a binding changed
    /// length.</summary>
    private void AlignIconColumns()
    {
        foreach (List<Control> column in _clustersByColumn)
        {
            float widest = 0f;
            foreach (Control cluster in column)
                widest = Mathf.Max(widest, cluster.GetCombinedMinimumSize().X);

            foreach (Control cluster in column)
                cluster.CustomMinimumSize = new Vector2(widest, cluster.CustomMinimumSize.Y);
        }
    }

    /// <summary>
    /// Keep the card inside the window.
    ///
    /// <para>The table fits 720p as it stands — measured, not assumed. The clamp exists for what
    /// comes next: <see cref="HowToPlayContent"/> is an open insertion point, and a screen built
    /// to be appended to must not be one edit away from running off the bottom of the smallest
    /// supported window. Chrome (header, keyline, toggle row, hint) always keeps its height; only
    /// the table scrolls, and only once there is no room left for it.</para>
    /// </summary>
    private void ClampToViewport()
    {
        Viewport? viewport = GetViewport();
        if (viewport == null)
            return;

        float available = viewport.GetVisibleRect().Size.Y - (UiScale.ScreenMargin * 2);

        // Measure the card with the scroll region collapsed: what is left is the chrome, and the
        // difference is the room the table may have.
        _scroll.CustomMinimumSize = Vector2.Zero;
        float chrome = _card.GetCombinedMinimumSize().Y;
        float wanted = _scrollBody.GetCombinedMinimumSize().Y;
        float room = Mathf.Max(UiScale.TargetHeight, available - chrome);

        _scroll.CustomMinimumSize = new Vector2(0, Mathf.Min(wanted, room));
    }

    private void Close() => EmitSignal(SignalName.Closed);
}
