namespace MpFoundation.Game.Presentation;

/// <summary>The semantic gameplay-event vocabulary — describes WHAT HAPPENED, never
/// what plays. One shared vocabulary for creatures and items (spec decision): items
/// simply use a subset, and a profile that doesn't map an event stays silent for it.
/// `Step` means "primary locomotion beat" — a footed creature's footfall, a
/// hover-creature's bob cycle, a slitherer's pulse. Event GENERATION (thresholds,
/// cooldowns, debounce) stays in controller code; profiles own only the response.
/// Ordinals are a .tres serialization contract — append new events, never reorder.</summary>
public enum ActorEvent
{
    Step = 0,
    Jump = 1,
    Land = 2,
    Bump = 3,
    // 4, 5 formerly Death/Recovered (removed with the pop death path — foundation-reset
    // Task 4). Ordinals are a .tres serialization contract: never reused, never reordered.
    PickedUp = 6,
    Dropped = 7,
    Thrown = 8,
    Impact = 9,
    ScrapSorted = 10,
    Bark = 11,

    // 12 formerly Flash (a since-removed camera's flash beacon, removed with it). 13 is
    // likewise untaken. Ordinals are a .tres serialization contract: never reused, never
    // reordered — the next event appended here takes 14.
}
