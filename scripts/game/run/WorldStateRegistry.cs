using System;
using System.Collections.Generic;

namespace Sail.Game.Run;

/// <summary>
/// The store's pure core (core-spine spec §5.3, CORE-PROG-A2) — registration list, fan-out
/// order, and per-slice failure isolation, with no Godot types, so the boundary contract is
/// walkable by <c>dotnet test</c> (the PlaythroughMachine/PlaythroughDriver split, one system
/// over). <see cref="WorldStateStore"/> is the Node wrapper that wires it to the scene tree,
/// RunDriver's events, and the engine log.
///
/// Registration order = construction order = fan-out order (spec §5.3): slices register from
/// their own Setup, and Gameplay constructs the store before every stateful manager, so the
/// list is the construction order by construction. Duplicate slice ids are refused loudly —
/// two slices sharing an id would make the VER completeness probe lie.
/// </summary>
public sealed class WorldStateRegistry
{
    private readonly List<IWorldStateSlice> _slices = new();
    private readonly List<string> _ids = new();

    public IReadOnlyList<string> Ids => _ids;

    public int Count => _slices.Count;

    /// <summary>Registers a slice at the end of the fan-out order. Returns false (not
    /// registered) on a duplicate id — the caller reports that loudly; a silent second
    /// registration would double-reset one system and corrupt the completeness probe.</summary>
    public bool Register(IWorldStateSlice slice)
    {
        if (slice == null || _ids.Contains(slice.SliceId))
            return false;
        _slices.Add(slice);
        _ids.Add(slice.SliceId);
        return true;
    }

    /// <summary>The playthrough-boundary fan (spec §5.4): every registered slice's
    /// <see cref="IWorldStateSlice.ResetForNewPlaythrough"/>, in registration order. A slice
    /// that throws is isolated — the fan continues so one broken system cannot leave every
    /// LATER slice carrying stale state into the new playthrough — and its id is reported via
    /// <paramref name="onSliceFailed"/> and the returned list, from which the Node layer
    /// builds the loud dev abort (spec §5.4: "a registered slice that fails to reset aborts
    /// loudly in dev"). Idempotent because slices are required to be (spec §5.3).</summary>
    public IReadOnlyList<string> FanReset(Action<string, Exception>? onSliceFailed = null)
    {
        List<string>? failed = null;
        foreach (IWorldStateSlice slice in _slices)
        {
            try
            {
                slice.ResetForNewPlaythrough();
            }
            catch (Exception e)
            {
                (failed ??= new List<string>()).Add(slice.SliceId);
                onSliceFailed?.Invoke(slice.SliceId, e);
            }
        }
        return (IReadOnlyList<string>?)failed ?? Array.Empty<string>();
    }

    /// <summary>The dawn cutoff fan (spec §5.3): every <see cref="INightScopedSlice"/>'s
    /// <c>OnNightEnded</c>, registration order, same failure isolation as the reset fan.</summary>
    public IReadOnlyList<string> FanNightEnded(int round, Action<string, Exception>? onSliceFailed = null)
    {
        List<string>? failed = null;
        foreach (IWorldStateSlice slice in _slices)
        {
            if (slice is not INightScopedSlice night)
                continue;
            try
            {
                night.OnNightEnded(round);
            }
            catch (Exception e)
            {
                (failed ??= new List<string>()).Add(slice.SliceId);
                onSliceFailed?.Invoke(slice.SliceId, e);
            }
        }
        return (IReadOnlyList<string>?)failed ?? Array.Empty<string>();
    }

    /// <summary>The reserved across-nights write path (spec §5.3/§5.5): every
    /// <see cref="IMapScopedSlice"/>'s <c>CaptureNightSnapshot</c>, registration order. No-op
    /// bodies are the expected present tense — this fan exists so disk persistence lands
    /// without re-plumbing a single manager.</summary>
    public IReadOnlyList<string> FanCaptureSnapshot(int round, Action<string, Exception>? onSliceFailed = null)
    {
        List<string>? failed = null;
        foreach (IWorldStateSlice slice in _slices)
        {
            if (slice is not IMapScopedSlice map)
                continue;
            try
            {
                map.CaptureNightSnapshot(round);
            }
            catch (Exception e)
            {
                (failed ??= new List<string>()).Add(slice.SliceId);
                onSliceFailed?.Invoke(slice.SliceId, e);
            }
        }
        return (IReadOnlyList<string>?)failed ?? Array.Empty<string>();
    }
}
