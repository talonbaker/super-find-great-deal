using Godot;

namespace MpFoundation.Ui.Design;

/// <summary>
/// <b>The specimen board.</b> Every component in the kit, in all five states, at whichever
/// temperature is showing — with SPACE to cross to the other one and watch the page lose its
/// light, and number keys to re-cut every corner in the game live.
///
/// <para>This exists because the substrate's whole claim is "change one thing, see it
/// everywhere". A claim like that needs somewhere to be checked in one launch, or it quietly
/// stops being true. Run it after any token edit:</para>
///
/// <code>godot --path . -- --ui-theme-demo</code>
///
/// <list type="bullet">
/// <item>SPACE — cross to the other temperature (the real ~2s crossfade, not a cut)</item>
/// <item>1/2/3/4 — square / soft / rounded / pill, applied to every surface in the game</item>
/// <item>5 — cycle the fill treatment: outline, translucent, solid</item>
/// <item>R — restore the shipped recipes</item>
/// </list>
/// </summary>
public partial class UiThemeDemo : Node
{
    private static readonly (string Label, string Variation)[] ButtonSpecimens =
    {
        ("Primary", "PrimaryAction"),
        ("Secondary", "SecondaryAction"),
        ("Ghost", "GhostAction"),
        ("Tertiary", "TertiaryAction"),
        ("Danger", "DangerAction"),
        ("MENU ITEM", "MenuAction"),
        ("QUIT", "MenuDanger"),
    };

    // The sizes are INTERPOLATED from UiScale, never typed. They used to be literals reading
    // "Hero 40", "Body 15" and so on, and UI-3's 15% lift (2026-08-29) turned every one of them
    // into a lie the same afternoon: a board whose whole job is to report what size things are was
    // rendering 46 px under a label that said 40. A dev instrument that can go stale is worse than
    // no instrument, because it is believed.
    private static readonly (string Label, string Variation)[] TypeSpecimens =
    {
        ($"Hero {UiScale.SizeHero}", "Hero"),
        ($"Display {UiScale.SizeDisplay}", "Display"),
        ($"Title {UiScale.SizeTitle}", "Title"),
        ($"Body {UiScale.SizeBody} — what a player reads.", "Body"),
        ($"CAPTION {UiScale.SizeCaption}", "Caption"),
        ($"MICRO {UiScale.SizeMicro}", "Micro"),
        ($"MONO {UiScale.SizeBody}", "Mono"),
    };

    private SurfaceFill _fillCycle = SurfaceFill.Solid;
    private Label _status = null!;
    private CanvasLayer _layer = null!;

    public override void _Ready()
    {
        _layer = new CanvasLayer { Name = "Board" };
        AddChild(_layer);
        Rebuild();
        UiThemeService.TokensChanged += OnTokensChanged;
    }

    public override void _ExitTree() => UiThemeService.TokensChanged -= OnTokensChanged;

    private void OnTokensChanged(UiTokens tokens)
    {
        if (_ground != null && GodotObject.IsInstanceValid(_ground))
            _ground.Color = tokens.PageGround;
        if (_status != null && GodotObject.IsInstanceValid(_status))
            _status.Text = StatusLine();
    }

    private ColorRect _ground = null!;

    private void Rebuild()
    {
        foreach (Node child in _layer.GetChildren())
            child.QueueFree();

        _ground = new ColorRect { Color = UiThemeService.Tokens.PageGround };
        _ground.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _layer.AddChild(_ground);

        var margin = new MarginContainer();
        margin.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        foreach (string side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" })
            margin.AddThemeConstantOverride(side, UiScale.ScreenMargin);
        _ground.AddChild(margin);

        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", UiScale.SpaceWide);
        margin.AddChild(columns);

        columns.AddChild(BuildStateGrid());
        columns.AddChild(BuildTypeColumn());
        columns.AddChild(BuildSurfaceColumn());

        _status = new Label { Text = StatusLine(), ThemeTypeVariation = "Caption" };
        _status.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        _status.OffsetLeft = UiScale.ScreenMargin;
        _status.OffsetTop = -UiScale.SpaceWide;
        _ground.AddChild(_status);
    }

    /// <summary>Every button variation crossed with every state — the grid that makes a missing
    /// state impossible to miss. Disabled and focus are posed, not waited for.</summary>
    private Control BuildStateGrid()
    {
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", UiScale.SpaceNormal);
        column.AddChild(new Label { Text = "COMPONENTS × STATES", ThemeTypeVariation = "Crumb" });

        foreach ((string label, string variation) in ButtonSpecimens)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", UiScale.SpaceSnug);

            // Normal, then a focused one, then a disabled one. Hover and pressed are live —
            // the pointer proves those, and posing them would prove only that a stylebox exists.
            row.AddChild(new Button { Text = label, ThemeTypeVariation = variation });

            var focused = new Button { Text = label + " ⟵ focus", ThemeTypeVariation = variation };
            focused.Ready += focused.GrabFocus;
            row.AddChild(focused);

            row.AddChild(new Button { Text = label, ThemeTypeVariation = variation, Disabled = true });
            column.AddChild(row);
        }

        var field = new LineEdit { PlaceholderText = "room code", CustomMinimumSize = new Vector2(220, 0) };
        column.AddChild(field);

        var slider = new HSlider { CustomMinimumSize = new Vector2(220, 0), Value = 60 };
        column.AddChild(slider);

        column.AddChild(new CheckButton { Text = "a toggle" });
        return column;
    }

    private Control BuildTypeColumn()
    {
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", UiScale.SpaceNormal);
        column.AddChild(new Label { Text = "TYPE RANKS", ThemeTypeVariation = "Crumb" });
        foreach ((string label, string variation) in TypeSpecimens)
            column.AddChild(new Label { Text = label, ThemeTypeVariation = variation });
        column.AddChild(new Label { Text = "Danger ink", ThemeTypeVariation = "Danger" });
        return column;
    }

    private Control BuildSurfaceColumn()
    {
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", UiScale.SpaceNormal);
        column.AddChild(new Label { Text = "SURFACES", ThemeTypeVariation = "Crumb" });

        var card = new PanelContainer();
        var cardBody = new VBoxContainer();
        cardBody.AddThemeConstantOverride("separation", UiScale.SpaceSnug);
        cardBody.AddChild(new Label { Text = "A card", ThemeTypeVariation = "Title" });
        cardBody.AddChild(new Label
        {
            Text = "Body text on a card, at whichever\ntemperature is currently lit.",
            ThemeTypeVariation = "Body",
        });
        card.AddChild(cardBody);
        column.AddChild(card);

        var scrap = new PanelContainer { ThemeTypeVariation = "HudScrap" };
        var scrapBody = new VBoxContainer();
        scrapBody.AddChild(new Label { Text = "HUD SCRAP", ThemeTypeVariation = "Caption" });
        scrapBody.AddChild(new Label { Text = "12:04", ThemeTypeVariation = "MonoTimer" });
        scrap.AddChild(scrapBody);
        column.AddChild(scrap);

        var chip = new PanelContainer { ThemeTypeVariation = "Chip" };
        chip.AddChild(new Label { Text = "a chip", ThemeTypeVariation = "Caption" });
        column.AddChild(chip);

        return column;
    }

    private static string StatusLine() =>
        $"{UiThemeService.Tokens.Temperature}  ·  shape {UiRecipes.Current.Action.Shape}  ·  " +
        "SPACE cross  ·  1-4 shape  ·  5 fill  ·  R restore";

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
            return;

        switch (key.Keycode)
        {
            case Key.Space:
                UiThemeService.Instance?.SetTemperature(
                    UiThemeService.Tokens.Temperature == UiTemperature.Day ? UiTemperature.Night : UiTemperature.Day);
                return;
            case Key.Key1: ApplyShape(SurfaceShape.Square); return;
            case Key.Key2: ApplyShape(SurfaceShape.Soft); return;
            case Key.Key3: ApplyShape(SurfaceShape.Rounded); return;
            case Key.Key4: ApplyShape(SurfaceShape.Pill); return;
            case Key.Key5: CycleFill(); return;
            case Key.R:
                UiRecipes.Current = UiRecipeSet.Default;
                _fillCycle = SurfaceFill.Solid;
                Refresh();
                return;
            case Key.Escape:
                GetTree().Quit();
                return;
        }
    }

    private void ApplyShape(SurfaceShape shape)
    {
        UiRecipes.Current = UiRecipes.Current.WithShape(shape);
        Refresh();
    }

    private void CycleFill()
    {
        _fillCycle = _fillCycle switch
        {
            SurfaceFill.Solid => SurfaceFill.Translucent,
            SurfaceFill.Translucent => SurfaceFill.Outline,
            _ => SurfaceFill.Solid,
        };
        UiRecipes.Current = UiRecipes.Current.WithFill(_fillCycle);
        Refresh();
    }

    /// <summary>A recipe change is a theme change, so the service rebuilds and every specimen
    /// on the board follows without being rebuilt itself — which is the property being
    /// demonstrated. The board is re-laid-out only because padding may have changed.</summary>
    private void Refresh()
    {
        UiThemeService.Instance?.Rebuild();
        Rebuild();
    }
}
