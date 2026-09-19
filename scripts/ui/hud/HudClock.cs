using Godot;
using MpFoundation.Game.World;

namespace MpFoundation.Ui.Hud;

/// <summary>
/// Turns the run's abstract cycle phase into the day/phase line the top-centre readout prints.
/// Pure static math over <see cref="CycleBands"/> — no <see cref="Node"/>, no scene tree, no
/// engine state — so every rule below is directly xUnit-testable (same posture as
/// <see cref="CyclePhase"/>).
///
/// <b>Band-anchored, not linear.</b> The readout names the band the clock is in rather than
/// deriving anything from raw phase, because the run's night is not a fixed slice of the cycle:
/// <see cref="CycleBands.Boundaries"/> moves dusk from phase 0.550 on day 1 to 0.350 on day 5 and
/// pushes dawn later, so night widens from a quarter of the cycle to half of it (the escalation
/// design's whole point). Reading the band table's output — never re-deriving it — is what keeps
/// this readout and the sky agreeing on every day of the run.
/// </summary>
public static class HudClock
{
    /// <summary>The player-facing name of a band. These are the four the rest of the game already
    /// uses (<see cref="PhaseEventKind"/>'s transitions are named for exactly these), so the
    /// top-centre readout can never disagree with the phase toast that announced it.
    ///
    /// The two sweeps get their own names rather than being folded into Day and Night. They are
    /// short, but they are the windows the game asks the player to ACT in — dusk is "get back to
    /// the light" and dawn is "you survived" — and a readout that called dusk "DAY" would be
    /// telling the player they had more time than they do.</summary>
    public static string BandName(CycleBands.Band band) => band switch
    {
        CycleBands.Band.Day => "DAY",
        CycleBands.Band.DuskSweep => "DUSK",
        CycleBands.Band.Night => "NIGHT",
        _ => "DAWN",
    };

    /// <summary>The top-centre line: which day of the run, and what part of it. Reads
    /// "DAY 2 · NIGHT".
    ///
    /// The day number is 1-based for display (<paramref name="cyclesElapsed"/> is 0-based) —
    /// the first day of a run is "DAY 1", never "DAY 0". It is deliberately NOT clamped to
    /// <see cref="CycleBands.MaxDayIndex"/>: the escalation TABLE clamps at day 5 and keeps
    /// looping, but the readout should keep counting, because a group that has survived to day 7
    /// has done something the HUD should not quietly re-label as day 5.</summary>
    public static string DayPhaseText(float phase, int cyclesElapsed)
    {
        CycleBands.Band band = CycleBands.GetBand(phase, cyclesElapsed, out _);
        int dayNumber = (cyclesElapsed < 0 ? 0 : cyclesElapsed) + 1;
        return $"DAY {dayNumber} · {BandName(band)}";
    }

    /// <summary>True when the given band is a night-side one, for widgets that shift emphasis
    /// once the light starts going (the panel opacity step in <see cref="DayPhaseWidget"/>). Dusk
    /// counts as night-side: by the time the sweep starts the light has already gone warm and
    /// low, and the HUD should turn with it rather than a beat later.</summary>
    public static bool IsNightSide(CycleBands.Band band) =>
        band is CycleBands.Band.DuskSweep or CycleBands.Band.Night;
}
