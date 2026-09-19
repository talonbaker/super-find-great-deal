namespace Sail.Game.Achievements;

/// <summary>
/// The four achievements Talon asked for (playtest note 12, 2026-08-30), and nothing else — the
/// packet's explicit scope is these four, not an achievements framework or a browser screen.
/// A byte enum with explicit ordinals for the same reason every other replicated/persisted
/// state enum in this repo carries one (<c>AimStance</c>, <c>BodyStance</c>, <c>MoveVerb</c>):
/// the persisted key in <c>AchievementStore</c>'s save file is a string, not this ordinal, so
/// renumbering is actually safe here — but the discipline costs nothing and keeps this enum
/// consistent with its neighbours.
/// </summary>
public enum AchievementId : byte
{
    DuckWalk = 0,
    ToughGuy = 1,
    ToughGuyDuckWalk = 2,
    Bubblholic = 3,
}

/// <summary>
/// Names and toast copy for the four achievements — one place, so the enum, the display name
/// and the persisted key can never drift apart silently. <b>"Bubblholic" is Talon's own
/// spelling</b> (not "Bubbleholic") and is shipped exactly as he wrote it: playtest note 12 says
/// so explicitly, and the packet repeats it as a hard instruction. It is a joke name, not a
/// typo to correct.
/// </summary>
public static class AchievementCatalog
{
    public const string DuckWalkName = "Duck walk";
    public const string ToughGuyName = "Tough guy";
    public const string ToughGuyDuckWalkName = "Tough guy duck walk";
    public const string BubblholicName = "Bubblholic";

    /// <summary>Every achievement id, in the order Talon listed them. The one place a new
    /// achievement would need to be added for the rest of the catalog, the store and the
    /// tracker's tests to see it.</summary>
    public static readonly AchievementId[] All =
    {
        AchievementId.DuckWalk,
        AchievementId.ToughGuy,
        AchievementId.ToughGuyDuckWalk,
        AchievementId.Bubblholic,
    };

    public static string NameFor(AchievementId id) => id switch
    {
        AchievementId.DuckWalk => DuckWalkName,
        AchievementId.ToughGuy => ToughGuyName,
        AchievementId.ToughGuyDuckWalk => ToughGuyDuckWalkName,
        AchievementId.Bubblholic => BubblholicName,
        _ => id.ToString(),
    };

    /// <summary>The one-line toast copy shown on unlock (packet scope: a toast, not a browser —
    /// "if earning one needs a toast, build the toast; stop there").</summary>
    public static string ToastTextFor(AchievementId id) => $"Achievement unlocked: {NameFor(id)}";
}
