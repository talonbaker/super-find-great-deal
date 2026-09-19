using Godot;
using MpFoundation.Ui.Achievements;

namespace Sail.Game.Achievements;

/// <summary>
/// Wires the pure <see cref="AchievementUnlocker"/>/<see cref="AchievementTracker"/> logic to
/// disk persistence (<see cref="AchievementStore"/>) and the on-screen toast
/// (<see cref="AchievementToastLayer"/>) — the Godot-touching half this packet's tests
/// deliberately do not cover (see <c>AchievementTests.cs</c>'s class doc).
///
/// <para><b>One instance per local player session, owned by that player's own avatar.</b>
/// <c>SandboxAvatar</c> constructs exactly one of these, only for the tick path that represents
/// this client's own controlled body (<c>OwnerTick</c>, <c>NetRole.PredictedOwner</c>) — never
/// for a remote proxy and never on a headless dedicated server, so an achievement is only ever
/// tracked, persisted and toasted on the machine of the player who actually earned it.</para>
/// </summary>
public sealed class AchievementRuntime
{
    private readonly AchievementUnlocker _unlocker;
    private readonly Node _hostForToast;
    private AchievementToastLayer? _toast;

    /// <summary>Drives achievements 1-3 off this tick's resolved player state. See its own
    /// class doc for exactly what each of the three reads.</summary>
    public AchievementTracker Tracker { get; }

    /// <param name="hostForToast">Any live node in the scene tree — used only to reach
    /// <c>GetTree().Root</c> the first time a toast is actually needed. <c>SandboxAvatar</c>
    /// passes itself.</param>
    public AchievementRuntime(Node hostForToast)
    {
        _hostForToast = hostForToast;
        _unlocker = new AchievementUnlocker(AchievementStore.LoadEarned());
        Tracker = new AchievementTracker(_unlocker);
        _unlocker.Unlocked += OnUnlocked;
    }

    public bool IsEarned(AchievementId id) => _unlocker.IsEarned(id);

    /// <summary>
    /// "Bubblholic" (note 12.4) lives outside <see cref="AchievementTracker"/> on purpose: its
    /// inputs come from <c>Sail.Game.Bubble.BubbleCounter</c>, a different subsystem entirely,
    /// and folding it into the per-tick movement tracker would make that tracker depend on the
    /// bubble world existing. <paramref name="count"/>/<paramref name="total"/> should be
    /// <c>BubbleCounter.Count</c>/<c>BubbleCounter.BubbleCount</c> — see
    /// <see cref="BubblholicRule"/>'s own doc for why those two values are safe to poll directly
    /// (server-authoritative and scene-derived respectively) rather than a defect waiting to
    /// happen.
    /// </summary>
    public void CheckBubblholic(int count, int total)
    {
        if (BubblholicRule.AllPopped(count, total))
            _unlocker.TryUnlock(AchievementId.Bubblholic);
    }

    private void OnUnlocked(AchievementId id)
    {
        AchievementStore.Persist(id);
        GD.Print($"[achievements] unlocked: {AchievementCatalog.NameFor(id)}");
        EnsureToast().Show(AchievementCatalog.ToastTextFor(id));
    }

    private AchievementToastLayer EnsureToast()
    {
        if (_toast != null && GodotObject.IsInstanceValid(_toast))
            return _toast;
        _toast = new AchievementToastLayer { Name = "AchievementToastLayer" };
        _hostForToast.GetTree().Root.AddChild(_toast);
        return _toast;
    }
}
