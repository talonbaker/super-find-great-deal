using System;
using Godot;

namespace Sail.Game.Water;

/// <summary>
/// The three states a player can be in with respect to the lake, chosen purely from
/// submersion depth (<c>WATER_Y − feetY</c>) inside the lake region. There are no trigger
/// volumes and no Area nodes anywhere in this system — depth is the only input, which is
/// what lets the same resolution run identically on the server, on the owning client's
/// prediction, and inside a reconciliation replay.
///
/// <b>The ordinals ride the wire</b> (packed into the snapshot flags byte by
/// <c>NetCodec.PackSnapshot</c>, and into <see cref="WaterDump"/>), so this enum is
/// append-only exactly like <c>PropKind</c> — reordering it would silently turn a swimming
/// player into a dry one on a mixed-version session.
/// </summary>
public enum WaterState : byte
{
    /// <summary>Not in the lake at all, or standing in less than the wade depth. Normal movement.</summary>
    Dry = 0,

    /// <summary>Feet on the bed, water somewhere between shin and chest. Move speed scaled,
    /// sprint denied, camera unchanged. Chill does not accumulate here — it recovers.</summary>
    Wading = 1,

    /// <summary>Off the bed. Horizontal-only movement, the eye drops to the waterline, sprint
    /// and jump are denied, and the camcorder is stowed and unusable. The only state in which
    /// chill accumulates.</summary>
    Swimming = 2,
}

/// <summary>
/// One water event, as broadcast by <see cref="WaterService"/>. This is the fixed W2↔W4
/// contract from the lake-water design spec §13.1 — packet W4 (splash VFX and audio) builds
/// its listener against this exact shape and owns nothing inside W2's files.
/// </summary>
/// <param name="PeerId">The player the event happened to.</param>
/// <param name="Position">World-space point the effect plays at — the avatar's feet at the
/// moment of the transition, which is where a splash actually breaks the surface.</param>
/// <param name="From">State before the transition. Equal to <paramref name="To"/> for the
/// throttled <see cref="WaterService.Splash"/> stream, which is not a transition.</param>
/// <param name="To">State after the transition.</param>
/// <param name="Speed">Horizontal speed at the moment, m/s — scales burst size, so a running
/// jump into the lake throws more water than a step does.</param>
public readonly record struct WaterEvent(
    int PeerId,
    Vector3 Position,
    WaterState From,
    WaterState To,
    float Speed);

/// <summary>
/// Which of the five events a replicated <see cref="WaterEvent"/> is. Server-assigned and
/// sent on the wire so every client raises the identical event in the same instant rather
/// than each guessing locally from its own interpolated view (spec §9.1). Append-only.
/// </summary>
public enum WaterEventKind : byte
{
    Entered = 0,
    Exited = 1,
    Splash = 2,
    WentUnder = 3,
    Sputtered = 4,
}

/// <summary>
/// Where a player is in the sputter-out sequence (spec §6). Not a <see cref="WaterState"/>
/// — a player is simultaneously <see cref="WaterState.Swimming"/> and
/// <see cref="GoingUnder"/>, and simultaneously <see cref="WaterState.Dry"/> and
/// <see cref="Recovering"/>. The two axes are orthogonal on purpose; collapsing them would
/// make "went under" and "swimming" mutually exclusive, which they are not.
///
/// <b>Control is locked for every phase except <see cref="None"/></b>, and it returns at
/// exactly one moment: the end of <see cref="Recovering"/>. There is no intermediate
/// half-steering state — MECHANICS-BIBLE §2 is explicit, and this repo has already shipped
/// the ragdoll-you-still-steer bug once.
/// </summary>
public enum SputterPhase : byte
{
    /// <summary>Normal. Chill accumulates and recovers; the player steers.</summary>
    None = 0,

    /// <summary>The swallow. Control locked, the body sinks, the view goes under. Ends after
    /// <see cref="WaterGeometry.GoUnderSec"/> with the hard cut and the shore teleport.</summary>
    GoingUnder = 1,

    /// <summary>Prone at the nearest shoreline point, coughing. Control still locked. Ends
    /// after <see cref="WaterGeometry.RecoverSec"/>, which is the single moment control
    /// returns and the single moment <see cref="WaterService.Sputtered"/> fires.</summary>
    Recovering = 2,
}

/// <summary>
/// One player's complete water state as it crosses the wire — the unit of the late-join dump
/// (<see cref="WaterDump"/>) and of the throttled live broadcast. Kept as a plain readonly
/// record struct with no Godot node dependency so the round-trip is xUnit-testable with no
/// engine running.
/// </summary>
public readonly record struct WaterPeerSnapshot(
    int PeerId,
    WaterState State,
    float Chill,
    bool Soaked,
    SputterPhase Phase)
{
    /// <summary>Total-by-construction sanitiser for anything arriving off the wire: a
    /// malformed packet must produce a boring, legal state, never a poisoned simulation or an
    /// exception inside a network callback.</summary>
    public WaterPeerSnapshot Sanitized() => this with
    {
        State = State > WaterState.Swimming ? WaterState.Dry : State,
        Chill = float.IsFinite(Chill) ? Math.Clamp(Chill, 0f, 1f) : 0f,
        Phase = Phase > SputterPhase.Recovering ? SputterPhase.None : Phase,
    };
}
