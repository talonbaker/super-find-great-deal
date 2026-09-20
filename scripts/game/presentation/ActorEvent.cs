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
    // reordered — the next event appended here takes 15.

    /// <summary><b>Set down deliberately</b>, as opposed to <see cref="Dropped"/> (SFX-1,
    /// 2026-09-19). CARRY-1 split the two VERBS — <c>Carryable.OnPlaced</c> rejoins physics with
    /// no velocity and no spin, <c>OnDropped</c> tosses — but both were still reporting
    /// <see cref="Dropped"/>, so the presentation layer could not tell them apart even though the
    /// gameplay layer already could. They sound genuinely different: a can set on a shelf is a
    /// 40 ms rim tick and a can tossed on the floor is not.
    ///
    /// <para>Nothing that existed before this packet changes sound, because the one shipped
    /// profile (<c>assets/items/default/prop_presentation.tres</c>) maps neither event — a crate
    /// has always been silent on both, and still is.</para></summary>
    Placed = 14,
}
