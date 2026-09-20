using Godot;

namespace MpFoundation;

/// <summary>
/// The low-spec preset (Performance Bible, audit Flag 5): persisted display settings —
/// 3D resolution scale and a quality tier — applied at boot and adjustable from the
/// settings panel. The philosophy is the Art Bible's §3.3: ONE lean baseline that hits
/// the floor ("low" IS the doctrine floor path, nothing about the game's look depends
/// on anything above it), and stronger machines may turn things UP. The floor never
/// pays for the ceiling.
/// </summary>
public static class DisplaySettings
{
    private const string Path = "user://settings.cfg";
    private const string Section = "display";

    /// <summary>3D render scale (0.6–1.0 of window size). 1080p windows on weak iGPUs
    /// gain real frame time at 0.75–0.85 with little visible cost in this art style.</summary>
    public static float ResolutionScale { get; private set; } = 1.0f;

    /// <summary>false = floor tier (the doctrine path — default); true = extras for
    /// strong machines (today: 4× MSAA; the tier is where glow/SSAO would hang later).</summary>
    public static bool HighQuality { get; private set; }

    /// <summary><b>Fullscreen on start, default true</b> (Talon, 2026-08-30 playtest note 2:
    /// <i>"Please set default to full screen on start. If needed, please put a setting in the
    /// settings which can toggle / enable windowed mode."</i>).
    ///
    /// <para><b>The default lives here, not in <c>project.godot</c>, and that is the whole safety
    /// argument.</b> <c>display/window/size/mode</c> would put every process this repo launches
    /// into fullscreen — the headed capture bots, the UI capture lab, the screen demo, every
    /// windowed self-test — and a capture rig that steals the display is how the verification of
    /// every other packet in a wave breaks at once. As a runtime value it is applied by exactly
    /// one call site (<see cref="MpFoundation.Boot"/>'s real-client splash branch), which no
    /// server, bot, practice, self-test or capture launch ever reaches.</para></summary>
    public static bool Fullscreen { get; private set; } = true;

    public static void Load()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(Path) == Error.Ok)
        {
            // Type-checked reads: a hand-edited cfg with e.g. resolution_scale = "high"
            // survives Load() fine, but a direct Variant cast would throw in Boot._Ready
            // before any scene shows. Wrong-typed values fall back to defaults instead.
            Variant scale = cfg.GetValue(Section, "resolution_scale", 1.0f);
            if (scale.VariantType is Variant.Type.Float or Variant.Type.Int)
                ResolutionScale = Mathf.Clamp((float)scale.AsDouble(), 0.6f, 1.0f);
            Variant high = cfg.GetValue(Section, "high_quality", false);
            if (high.VariantType == Variant.Type.Bool)
                HighQuality = high.AsBool();
            // Missing key => the shipped default (fullscreen). Type-checked like its neighbours:
            // a hand-edited cfg must not be able to throw in Boot._Ready before any scene shows.
            Variant full = cfg.GetValue(Section, "fullscreen", true);
            if (full.VariantType == Variant.Type.Bool)
                Fullscreen = full.AsBool();
        }
    }

    public static void SetResolutionScale(float scale)
    {
        ResolutionScale = Mathf.Clamp(scale, 0.6f, 1.0f);
        Save();
    }

    public static void SetHighQuality(bool high)
    {
        HighQuality = high;
        Save();
    }

    /// <summary>Records the player's window-mode choice and applies it to the live window, so the
    /// setting is not a promise about the next launch. Persisted immediately — a preference the
    /// player has to remember to confirm is a preference that gets silently lost.</summary>
    public static void SetFullscreen(bool fullscreen)
    {
        Fullscreen = fullscreen;
        Save();
        ApplyWindowMode();
    }

    /// <summary>Puts the OS window into the persisted mode. <b>Call only from the real windowed
    /// client path</b> — see <see cref="Fullscreen"/> for why this is not a project setting.
    ///
    /// <para>Inert without a real display server, so a headless process that somehow reaches a
    /// settings toggle changes nothing. <see cref="DisplayServer.WindowMode.Fullscreen"/> rather
    /// than <c>ExclusiveFullscreen</c>: borderless keeps alt-tab, the overlay and a second monitor
    /// behaving, and this game is played in voice chat with a browser open.</para></summary>
    public static void ApplyWindowMode()
    {
        if (DisplayServer.GetName() == "headless")
            return;
        DisplayServer.WindowMode want = Fullscreen
            ? DisplayServer.WindowMode.Fullscreen
            : DisplayServer.WindowMode.Windowed;
        if (DisplayServer.WindowGetMode() != want)
            DisplayServer.WindowSetMode(want);
    }

    /// <summary>Applies the current values to a viewport. Call at boot and on change.</summary>
    public static void Apply(Viewport viewport)
    {
        viewport.Scaling3DMode = Viewport.Scaling3DModeEnum.Bilinear;
        viewport.Scaling3DScale = ResolutionScale;
        viewport.Msaa3D = HighQuality ? Viewport.Msaa.Msaa4X : Viewport.Msaa.Disabled;
    }

    private static void Save()
    {
        var cfg = new ConfigFile();
        cfg.Load(Path); // keep any other sections
        cfg.SetValue(Section, "resolution_scale", ResolutionScale);
        cfg.SetValue(Section, "high_quality", HighQuality);
        cfg.SetValue(Section, "fullscreen", Fullscreen);
        cfg.Save(Path);
    }
}
