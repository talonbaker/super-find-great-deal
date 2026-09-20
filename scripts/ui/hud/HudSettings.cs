using Godot;

namespace MpFoundation.Ui.Hud;

/// <summary>
/// Persisted HUD preferences, in the same <c>user://settings.cfg</c> every other settings class
/// writes to (<see cref="OnboardingSettings"/>, DisplaySettings, TelemetryStore) under its own
/// section, so no class can clobber another's keys.
///
/// Holds the reduced-motion preference — a real persisted preference with a settings-menu
/// control, not a compile-time constant.
///
/// <b>Corruption degrades to the default, never to a crash</b> — same discipline
/// <see cref="OnboardingSettings"/> documents: a failed <see cref="ConfigFile.Load"/> is an
/// error return rather than an exception, and the type-checked read below rejects a wrong-typed
/// value instead of throwing on the cast.
/// </summary>
public static class HudSettings
{
    private const string Path = "user://settings.cfg";
    private const string Section = "hud";

    /// <summary>True to suppress HUD animation. Default false.
    ///
    /// <b>Not a nicety.</b> The UI/UX skill lists honouring a reduced-motion preference among its
    /// non-negotiables: vestibular disorders are real, and moving or scaling interface elements is
    /// a common trigger. <see cref="HudMotion"/> honours this by applying every END state
    /// instantly — the information a player gets is identical, only the movement is dropped. It
    /// must never mean a change becomes invisible.</summary>
    public static bool ReducedMotion { get; private set; }

    public static void Load()
    {
        var cfg = new ConfigFile();
        cfg.Load(Path); // Ok or not — the defaults apply either way (see class doc).
        Variant motion = cfg.GetValue(Section, "reduced_motion", false);
        ReducedMotion = motion.VariantType == Variant.Type.Bool && motion.AsBool();
    }

    /// <summary>Records the reduced-motion preference. Written on every change rather than at
    /// menu close, the same commit-on-toggle rule
    /// <see cref="OnboardingSettings.SetPlaytestForewordDismissed"/> uses — a player who alt-F4s out of
    /// the settings menu keeps the preference they just set. No change event: motion is read at
    /// the moment each animation starts, so the next one already respects a new setting without
    /// anything having to be rebuilt.</summary>
    public static void SetReducedMotion(bool enabled)
    {
        if (ReducedMotion == enabled)
            return;
        ReducedMotion = enabled;
        var cfg = new ConfigFile();
        cfg.Load(Path);
        cfg.SetValue(Section, "reduced_motion", enabled);
        cfg.Save(Path);
    }
}
