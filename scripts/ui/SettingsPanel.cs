using Godot;
using MpFoundation.Voice;

namespace MpFoundation.Ui;

/// <summary>
/// Shared settings panel (standalone settings screen + in-game pause overlay):
/// master volume, voice output volume, microphone input device, the current push-to-talk
/// binding, the display group (window mode, render scale, quality, world detail), the HUD
/// preferences, and the anonymous-usage report toggle.
/// </summary>
public partial class SettingsPanel : VBoxContainer
{
    [Signal]
    public delegate void BackRequestedEventHandler();

    private const int MasterBusIndex = 0;

    public override void _Ready()
    {
        // Sliders carry the prototype's mono % readout beside the track.
        var volumeValue = GetNode<Label>("VolumeRow/VolumeValue");
        var slider = GetNode<HSlider>("VolumeRow/VolumeSlider");
        slider.Value = Mathf.DbToLinear(AudioServer.GetBusVolumeDb(MasterBusIndex));
        volumeValue.Text = Percent(slider.Value);
        slider.ValueChanged += value =>
        {
            AudioServer.SetBusVolumeDb(MasterBusIndex, Mathf.LinearToDb((float)value));
            AudioSettings.SetMasterVolume((float)value);
            volumeValue.Text = Percent(value);
        };

        int voiceBus = VoiceManager.EnsureOutputBus();
        var voiceValue = GetNode<Label>("VoiceVolumeRow/VoiceVolumeValue");
        var voiceSlider = GetNode<HSlider>("VoiceVolumeRow/VoiceVolumeSlider");
        voiceSlider.Value = Mathf.DbToLinear(AudioServer.GetBusVolumeDb(voiceBus));
        voiceValue.Text = Percent(voiceSlider.Value);
        voiceSlider.ValueChanged += value =>
        {
            AudioServer.SetBusVolumeDb(AudioServer.GetBusIndex(VoiceConfig.OutputBus), Mathf.LinearToDb((float)value));
            AudioSettings.SetVoiceVolume((float)value);
            voiceValue.Text = Percent(value);
        };

        // The OS default input device is frequently wrong on Windows (webcam mics,
        // virtual devices), so the picker is a first-class setting, not a stretch goal.
        var micDropdown = GetNode<OptionButton>("MicRow/MicDropdown");
        string current = AudioServer.InputDevice;
        string[] devices = AudioServer.GetInputDeviceList();
        for (int i = 0; i < devices.Length; i++)
        {
            micDropdown.AddItem(devices[i], i);
            if (devices[i] == current)
                micDropdown.Selected = i;
        }
        micDropdown.ItemSelected += index =>
        {
            string device = micDropdown.GetItemText((int)index);
            AudioServer.InputDevice = device;
            AudioSettings.SetMicDevice(device);
        };

        GetNode<Label>("PttRow/PttKeyValue").Text = PttKeyName();
        BuildAmbientRow();
        BuildDisplayRows();
        BuildHudRows();
        BuildTelemetryRow();
        InsertRowDividers();
        GetNode<Button>("BackButton").Pressed += () => EmitSignal(SignalName.BackRequested);
    }

    private static string Percent(double linear) => $"{Mathf.RoundToInt((float)(linear * 100))}%";

    /// <summary>The prototype's hairline divider under each settings row. Runs after
    /// all rows (scene + code-built) exist; inserts a 1px rule after every row but
    /// the last, coloured from the theme's one hairline source.</summary>
    private void InsertRowDividers()
    {
        Color hair = GetThemeColor("hair", "Accent");
        var rows = new System.Collections.Generic.List<Control>();
        foreach (Node child in GetChildren())
            if (child is HBoxContainer row)
                rows.Add(row);
        for (int i = 0; i < rows.Count - 1; i++)
        {
            var rule = new ColorRect
            {
                Color = hair,
                CustomMinimumSize = new Vector2(0, 1),
                MouseFilter = MouseFilterEnum.Ignore,
            };
            AddChild(rule);
            MoveChild(rule, rows[i].GetIndex() + 1);
        }
    }

    /// <summary>The world-ambience slider, on the <c>Ambient</c> GROUP bus so one control covers
    /// the continuous bed and the scenery one-shots together (five sliders on five lanes just
    /// lets a player carve the mix into mud). Built in code, like the display and telemetry rows,
    /// so the shared scene file — used by both the standalone settings screen and the in-game
    /// pause overlay — stays untouched.
    ///
    /// This is an obligation rather than a nicety: the bed plays continuously for a whole
    /// session, and before it landed every sound in the game was a one-shot that stopped on its
    /// own. Master is not a substitute, because turning the world down should not also turn a
    /// teammate's voice down.</summary>
    private void BuildAmbientRow()
    {
        int ambientBus = Game.World.AudioBuses.EnsureBus(
            Game.World.AudioBuses.Ambient, "Master");

        var row = new HBoxContainer { Name = "AmbientVolumeRow" };
        row.AddThemeConstantOverride("separation", Design.UiScale.SpaceLoose);
        row.AddChild(new Label { Text = "WORLD AMBIENCE", ThemeTypeVariation = "FieldLabel" });
        var slider = new HSlider
        {
            Name = "AmbientVolumeSlider",
            CustomMinimumSize = new Vector2(180, 0),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            MinValue = 0,
            MaxValue = 1,
            Step = 0.01,
            Value = Mathf.DbToLinear(AudioServer.GetBusVolumeDb(ambientBus)),
        };
        var value = new Label
        {
            Name = "AmbientVolumeValue",
            Text = Percent(slider.Value),
            ThemeTypeVariation = "Mono",
            CustomMinimumSize = new Vector2(44, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        slider.ValueChanged += v =>
        {
            // Re-resolve the index rather than closing over it: buses are created lazily by
            // several owners and an index captured now can name a different bus later.
            AudioServer.SetBusVolumeDb(
                AudioServer.GetBusIndex(Game.World.AudioBuses.Ambient), Mathf.LinearToDb((float)v));
            AudioSettings.SetAmbientVolume((float)v);
            value.Text = Percent(v);
        };
        row.AddChild(slider);
        row.AddChild(value);
        AddChild(row);
        // Directly under the voice row: the three audio sliders read as one group.
        MoveChild(row, GetNode<Control>("VoiceVolumeRow").GetIndex() + 1);
    }

    // ============================================================================================
    // ToggleMode BEFORE ButtonPressed — every code-built PillToggle on this panel, in that order,
    // in the initializer.
    //
    // Godot's BaseButton::set_pressed returns immediately when toggle_mode is false. PillToggle
    // sets ToggleMode in _Ready, which does not run until the node enters the tree — so an object
    // initializer that assigns only ButtonPressed is assigning it to a button that is not yet a
    // toggle, and the assignment is silently dropped. Every pill built that way opened UNCHECKED
    // no matter what the setting behind it said. A .tscn-instantiated pill is immune (a child's
    // _Ready runs before its parent's), which is why this only ever bit the three rows built here
    // in code.
    //
    // Found 2026-09-04 (COPY-1) from Talon's report of the display pill reading unchecked over a
    // fullscreen game, and it was never only that row: REDUCE HUD MOTION and ANONYMOUS USAGE
    // REPORT had it too. The usage row is the one that mattered — its own comment argued that an
    // OFF pill over a session that is in fact reporting would be "a settings screen lying about
    // its own setting", and this trap made it do exactly that to every player who never touched it.
    // ============================================================================================

    /// <summary>The low-spec preset (Bible/audit Flag 5), inserted after the audio rows:
    /// 3D resolution scale + a floor/high quality tier. Built in code so the scene file
    /// stays untouched and both settings apply live to the running viewport.</summary>
    private void BuildDisplayRows()
    {
        int insertAt = GetNode<Control>("PttRow").GetIndex() + 1;

        // WINDOW MODE (Talon, 2026-08-30 playtest note 2: "Please set default to full screen on
        // start. If needed, please put a setting in the settings which can toggle / enable
        // windowed mode.") It leads the display group because it is the one setting on this panel
        // a player goes looking for while the window is in their way.
        //
        // STATED AS "WINDOWED", NOT "FULLSCREEN" (Talon, 2026-09-04, COPY-1). It used to read
        // FULLSCREEN on the argument that the pill's ON state should match the shipped default;
        // Talon asked for the opposite wording, so the row now names the thing the player is
        // asking FOR — an unchecked pill they tick to get out of fullscreen — and it is unchecked
        // by default, which still matches the shipped fullscreen default. His call, not a
        // rediscovered principle.
        //
        // THE BINDING IS INVERTED, THE SETTING IS NOT. DisplaySettings.Fullscreen and
        // SetFullscreen keep their meaning everywhere (Boot, settings.cfg, DisplaySettings' own
        // doc); only this row's label, its initial ButtonPressed and its Toggled handler negate.
        // The node names stay FullscreenRow/FullscreenToggle for the same reason: they name the
        // SETTING, which did not change, and renaming them would have been a second, silent edit.
        var fullscreenRow = new HBoxContainer { Name = "FullscreenRow" };
        fullscreenRow.AddThemeConstantOverride("separation", Design.UiScale.SpaceLoose);
        fullscreenRow.AddChild(new Label
        {
            Text = "WINDOWED",
            ThemeTypeVariation = "FieldLabel",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        });
        var fullscreenToggle = new PillToggle
        {
            Name = "FullscreenToggle",
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            // ToggleMode BEFORE ButtonPressed, and that ordering is load-bearing — see the
            // ToggleMode note above BuildDisplayRows. Without it this pill read UNCHECKED over a
            // fullscreen game, which is exactly the mismatch Talon reported on 2026-09-04.
            ToggleMode = true,
            ButtonPressed = !DisplaySettings.Fullscreen,
        };
        // Applies live and persists in the same call: a window-mode setting that only takes effect
        // on the next launch is the setting a player toggles three times looking for the one that
        // works. `!on` is the whole inversion — checked means windowed.
        fullscreenToggle.Toggled += on => DisplaySettings.SetFullscreen(!on);
        fullscreenRow.AddChild(fullscreenToggle);
        AddChild(fullscreenRow);
        MoveChild(fullscreenRow, insertAt);
        insertAt++;

        var scaleRow = new HBoxContainer { Name = "ScaleRow" };
        scaleRow.AddThemeConstantOverride("separation", Design.UiScale.SpaceLoose);
        scaleRow.AddChild(new Label { Text = "RENDER SCALE", ThemeTypeVariation = "FieldLabel" });
        var scaleSlider = new HSlider
        {
            Name = "ScaleSlider",
            CustomMinimumSize = new Vector2(180, 0),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            MinValue = 0.6,
            MaxValue = 1.0,
            Step = 0.05,
            Value = DisplaySettings.ResolutionScale,
        };
        // The prototype renders this one slider in the secondary (orchid) accent.
        Color orange = GetThemeColor("orange", "Accent");
        var grabArea = new StyleBoxFlat { BgColor = new Color(orange, 0.5f) };
        grabArea.SetCornerRadiusAll(2);
        var grabAreaHi = new StyleBoxFlat { BgColor = orange };
        grabAreaHi.SetCornerRadiusAll(2);
        scaleSlider.AddThemeStyleboxOverride("grabber_area", grabArea);
        scaleSlider.AddThemeStyleboxOverride("grabber_area_highlight", grabAreaHi);
        var scaleValue = new Label
        {
            Name = "ScaleValue",
            Text = Percent(scaleSlider.Value),
            ThemeTypeVariation = "Mono",
            CustomMinimumSize = new Vector2(44, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        scaleSlider.ValueChanged += value =>
        {
            DisplaySettings.SetResolutionScale((float)value);
            DisplaySettings.Apply(GetViewport());
            scaleValue.Text = Percent(value);
        };
        scaleRow.AddChild(scaleSlider);
        scaleRow.AddChild(scaleValue);
        AddChild(scaleRow);
        MoveChild(scaleRow, insertAt);

        var qualityRow = new HBoxContainer { Name = "QualityRow" };
        qualityRow.AddThemeConstantOverride("separation", Design.UiScale.SpaceLoose);
        qualityRow.AddChild(new Label { Text = "QUALITY", ThemeTypeVariation = "FieldLabel" });
        var quality = new OptionButton
        {
            Name = "QualityDropdown",
            CustomMinimumSize = new Vector2(180, 0),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        quality.AddItem("Smooth (default)", 0); // the doctrine floor path — 60fps first
        quality.AddItem("Fancy (strong PCs)", 1);
        quality.Selected = DisplaySettings.HighQuality ? 1 : 0;
        quality.ItemSelected += index =>
        {
            DisplaySettings.SetHighQuality(index == 1);
            DisplaySettings.Apply(GetViewport());
        };
        qualityRow.AddChild(quality);
        AddChild(qualityRow);
        MoveChild(qualityRow, insertAt + 1);

        // The GraphicsQuality tier (Issue #201): a DIFFERENT axis from the quality dropdown
        // above. That one buys viewport extras (MSAA) on top of the baseline; this one sizes
        // the world's own work — grass density and reach, ground-shader detail distance. It is
        // the user-facing half of the tier system whose absence meant every session ever played
        // ran the compiled-in default. Persisted via GraphicsSettings (same settings.cfg), and
        // applied live to the extent the system supports: shader parameters re-apply to the
        // running world through GraphicsSettings.TierChanged; grass geometry rebuilds at the
        // next world build (the ground-detail contract — hence the row's honest label suffix).
        var detailRow = new HBoxContainer { Name = "WorldDetailRow" };
        detailRow.AddThemeConstantOverride("separation", Design.UiScale.SpaceLoose);
        detailRow.AddChild(new Label { Text = "WORLD DETAIL", ThemeTypeVariation = "FieldLabel" });
        var detail = new OptionButton
        {
            Name = "WorldDetailDropdown",
            CustomMinimumSize = new Vector2(180, 0),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        // Item ids ARE the enum values (Low=0, Medium=1, High=2), so no mapping table can drift.
        detail.AddItem("Low", (int)World.GraphicsQuality.Tier.Low);
        detail.AddItem("Medium", (int)World.GraphicsQuality.Tier.Medium);
        detail.AddItem("High (default)", (int)World.GraphicsQuality.Tier.High);
        detail.Selected = (int)World.GraphicsQuality.Current;
        detail.ItemSelected += index =>
            GraphicsSettings.SetTier((World.GraphicsQuality.Tier)(int)index);
        detailRow.AddChild(detail);
        AddChild(detailRow);
        MoveChild(detailRow, insertAt + 2);
    }

    /// <summary>HUD preferences. Built in code for the same reason the display and telemetry rows
    /// are — the shared scene file serves both the standalone Settings screen and the in-game
    /// pause overlay, and neither should need editing to gain a row. The toggle applies live
    /// (<see cref="Hud.HudSettings"/> commits on set): a preference that appears to do nothing
    /// until the menu closes reads as broken.</summary>
    private void BuildHudRows()
    {
        Hud.HudSettings.Load();

        // Reduced motion. An accessibility requirement rather than a preference — moving and
        // scaling interface elements is a common vestibular trigger. With it on, every HUD
        // animation still applies its END state instantly, so the information is unchanged and
        // only the movement is dropped; it must never make a change invisible.
        var motionRow = new HBoxContainer { Name = "ReducedMotionRow" };
        motionRow.AddThemeConstantOverride("separation", Design.UiScale.SpaceLoose);
        motionRow.AddChild(new Label
        {
            Text = "REDUCE HUD MOTION",
            ThemeTypeVariation = "FieldLabel",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        });
        var motionToggle = new PillToggle
        {
            Name = "ReducedMotionToggle",
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            ToggleMode = true, // before ButtonPressed — see the ToggleMode block above BuildDisplayRows
            ButtonPressed = Hud.HudSettings.ReducedMotion,
        };
        motionToggle.Toggled += on => Hud.HudSettings.SetReducedMotion(on);
        motionRow.AddChild(motionToggle);
        AddChild(motionRow);
        MoveChild(motionRow, GetNode<Control>("BackGap").GetIndex()); // keep it above the Back button
    }

    /// <summary>The persistent usage-report toggle. <b>This is the only writer of the consent
    /// state in shipping code</b> (Talon, 2026-08-30 note 15: <i>"I would like this to be
    /// something that the player themselves has to go into settings and disable"</i>) — the
    /// first-run notice tells and never asks, and nothing at boot decides on the player's behalf.
    /// Turning it off writes a second, durable witness so a lost or corrupted settings.cfg cannot
    /// undo the decision; see <c>TelemetryPaths.OptOutMarkerFile</c>. Built in code so the shared
    /// scene file (standalone Settings + in-game pause overlay) stays untouched.</summary>
    private void BuildTelemetryRow()
    {
        var row = new HBoxContainer { Name = "UsageReportRow" };
        row.AddThemeConstantOverride("separation", Design.UiScale.SpaceLoose);
        var caption = new Label
        {
            Text = "ANONYMOUS USAGE REPORT",
            ThemeTypeVariation = "FieldLabel",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        row.AddChild(caption);

        MpFoundation.Telemetry.TelemetryStore.Load();
        var toggle = new PillToggle
        {
            Name = "UsageReportToggle",
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            // UsageReportingEnabled, not "== Granted": since 2026-08-30 reporting is ON by
            // default, so a player who has never touched this must find the pill ON. Reading the
            // raw tri-state here would have shown every new player an OFF pill over a session
            // that was in fact reporting — a settings screen lying about its own setting. It did
            // exactly that anyway until 2026-09-04: without ToggleMode set first, the assignment
            // below was dropped and the pill opened OFF for everyone. See the ToggleMode block
            // above BuildDisplayRows.
            ToggleMode = true, // before ButtonPressed, always
            ButtonPressed = MpFoundation.Telemetry.TelemetryStore.UsageReportingEnabled,
        };
        toggle.Toggled += on => MpFoundation.Telemetry.TelemetryStore.SetUsageConsent(on);
        row.AddChild(toggle);
        AddChild(row);
        MoveChild(row, GetNode<Control>("BackGap").GetIndex()); // keep it above the Back button
    }

    private static string PttKeyName()
    {
        foreach (InputEvent e in InputMap.ActionGetEvents(VoiceConfig.PttAction))
        {
            if (e is InputEventKey key)
                return key.AsText().Replace(" (Physical)", "");
        }
        return "unbound";
    }
}
