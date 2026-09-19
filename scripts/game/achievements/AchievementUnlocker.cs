using System;
using System.Collections.Generic;

namespace Sail.Game.Achievements;

/// <summary>
/// <b>The one thing allowed to move an achievement from "not earned" to "earned", and the whole
/// idempotency contract in one class</b> (MECHANICS-BIBLE §4: "events that can fire twice ...
/// double-apply unless something stops them"). Pure logic — no Godot Node, no disk, no
/// networking — the same shape <c>AimController</c> and <c>StanceController</c> use for the
/// reason their own docs give: the half worth proving is the half <c>dotnet test</c> can reach.
///
/// <para><b>The contract, exactly:</b> <see cref="TryUnlock"/> returns <c>true</c> and raises
/// <see cref="Unlocked"/> the FIRST time a given id is unlocked, and is a complete no-op —
/// returns <c>false</c>, fires nothing — every time after, including a call on the very same
/// tick as the first. Nothing here is time-based or debounced; a <see cref="HashSet{T}"/>'s own
/// <c>Add</c> is already exactly this idempotency check, which is what makes the race
/// MECHANICS-BIBLE §4 warns about ("an achievement that can fire twice on one frame") structurally
/// impossible rather than merely unlikely — there is no window between "check if earned" and
/// "mark earned" for a second caller to land in.</para>
///
/// <para><b>Per-id, not global.</b> Two different achievements unlocked on the same call each
/// fire their own <see cref="Unlocked"/> event exactly once; one being already earned never
/// blocks another.</para>
/// </summary>
public sealed class AchievementUnlocker
{
    private readonly HashSet<AchievementId> _earned;

    /// <summary>
    /// <paramref name="alreadyEarned"/> seeds the earned set directly — the persisted-state
    /// load path (<c>AchievementStore.LoadEarned</c>) — WITHOUT raising <see cref="Unlocked"/>
    /// for any of them. A boot that re-popped a toast for something earned last session would
    /// be its own idempotency bug one layer up.
    /// </summary>
    public AchievementUnlocker(IEnumerable<AchievementId>? alreadyEarned = null)
    {
        _earned = alreadyEarned is null
            ? new HashSet<AchievementId>()
            : new HashSet<AchievementId>(alreadyEarned);
    }

    /// <summary>Fires exactly once per id, the instant that id is first unlocked. Never fires
    /// for ids seeded through the constructor.</summary>
    public event Action<AchievementId>? Unlocked;

    public bool IsEarned(AchievementId id) => _earned.Contains(id);

    /// <summary>Every id earned so far, including ones seeded at construction.</summary>
    public IReadOnlyCollection<AchievementId> Earned => _earned;

    /// <summary>Unlocks <paramref name="id"/> if it is not already earned. Returns whether THIS
    /// call is the one that did it — callers that persist or toast on unlock should gate on the
    /// return value, not on <see cref="IsEarned"/> after the fact, or every repeat call would
    /// re-persist/re-toast.</summary>
    public bool TryUnlock(AchievementId id)
    {
        if (!_earned.Add(id))
            return false;
        Unlocked?.Invoke(id);
        return true;
    }
}
