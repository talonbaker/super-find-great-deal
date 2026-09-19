using System.Collections.Generic;
using Godot;

namespace Sail.Game.Achievements;

/// <summary>
/// Persisted earned-achievement state, following the <c>DisplaySettings.cs</c> /
/// <c>OnboardingSettings.cs</c> / <c>GraphicsSettings.cs</c> pattern exactly: a static class
/// backed by its own section of the same "user://settings.cfg" every other per-player persisted
/// fact in this repo already uses. Like those classes, the <see cref="Godot.ConfigFile"/>
/// read/write half is exercised at runtime rather than under the Godot-free
/// <c>dotnet test</c> suite — see <c>AchievementTests.cs</c>'s class doc for why, and
/// <c>GraphicsSettingsTests.cs</c> / (absent) <c>OnboardingSettingsTests.cs</c> for the
/// precedent that neither of this pattern's existing users tests Save/Load either.
///
/// <para><b>In-game only</b> (packet W7-5's explicit scope): this file, like every other file
/// this packet touches, contains no Steamworks call, achievement definition or partner-site
/// configuration — see the packet's acceptance criterion 6.</para>
///
/// <para>A fresh or corrupt config degrades to "nothing earned yet" rather than throwing — the
/// same defensive stance <c>OnboardingSettings.Load</c> documents for itself, and for the same
/// reason: a settings read must never be the thing that crashes boot.</para>
/// </summary>
public static class AchievementStore
{
    private const string Path = "user://settings.cfg";
    private const string Section = "achievements";

    /// <summary>Every id this profile has already earned, read off disk. Feed straight into
    /// <see cref="AchievementUnlocker"/>'s constructor — that path seeds the earned set without
    /// firing <see cref="AchievementUnlocker.Unlocked"/>, so loading a past session's
    /// achievements on boot never re-pops a toast for them.</summary>
    public static List<AchievementId> LoadEarned()
    {
        var earned = new List<AchievementId>();
        var cfg = new ConfigFile();
        cfg.Load(Path); // Ok or not — a fresh/corrupt file just means nothing was earned yet.
        foreach (AchievementId id in AchievementCatalog.All)
        {
            Variant raw = cfg.GetValue(Section, KeyFor(id), false);
            if (raw.VariantType == Variant.Type.Bool && raw.AsBool())
                earned.Add(id);
        }
        return earned;
    }

    /// <summary>Records one achievement as earned, permanently. Call ONLY from
    /// <see cref="AchievementUnlocker.Unlocked"/> (i.e. only on the call that actually unlocked
    /// it) — calling this on every repeat trigger would still be harmless (the write is already
    /// idempotent: setting the same key to <c>true</c> twice changes nothing on disk) but would
    /// cost a needless file write per repeat, which the event-gated call site avoids for free.</summary>
    public static void Persist(AchievementId id)
    {
        var cfg = new ConfigFile();
        cfg.Load(Path); // keep every other section (display/graphics/onboarding/...)
        cfg.SetValue(Section, KeyFor(id), true);
        cfg.Save(Path);
    }

    private static string KeyFor(AchievementId id) => id switch
    {
        AchievementId.DuckWalk => "duck_walk",
        AchievementId.ToughGuy => "tough_guy",
        AchievementId.ToughGuyDuckWalk => "tough_guy_duck_walk",
        AchievementId.Bubblholic => "bubblholic",
        _ => id.ToString(),
    };
}
