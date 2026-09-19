using System;
using System.Collections.Generic;
using Godot;
using MpFoundation.Game;
using MpFoundation.Net;
using Sail.Game.Run;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// CORE-PROG-A2: the store's pure core (<see cref="WorldStateRegistry"/>) and the gating
/// predicate (<see cref="PlaythroughStates"/>) — registration order, fan order, per-slice
/// failure isolation, idempotent double fan, and the entry-guard's "seed state, simulate the
/// boundary WITHOUT a process restart, assert clean" contract (spec §5.4: process death is
/// non-load-bearing). The Node wrapper's engine wiring (RunDriver subscription order, the
/// band-live skip, the loud dev abort) is exercised live by tests/Run-StoreTest.ps1.
/// </summary>
public class WorldStateStoreTests
{
    private sealed class FakeSlice : IWorldStateSlice
    {
        public FakeSlice(string id, List<string>? callLog = null) { SliceId = id; _log = callLog; }
        private readonly List<string>? _log;
        public string SliceId { get; }
        public int State;      // "dirty" world state the boundary must clear
        public int ResetCalls;
        public void ResetForNewPlaythrough() { State = 0; ResetCalls++; _log?.Add(SliceId); }
    }

    private sealed class ThrowingSlice : IWorldStateSlice
    {
        public string SliceId => "broken";
        public void ResetForNewPlaythrough() => throw new InvalidOperationException("boom");
    }

    private sealed class NightSlice : INightScopedSlice
    {
        public string SliceId => "night";
        public int NightEndedRound = -1;
        public void ResetForNewPlaythrough() { }
        public void OnNightEnded(int round) => NightEndedRound = round;
    }

    private sealed class MapSlice : IMapScopedSlice
    {
        public string SliceId => "map";
        public int SnapshotRound = -1;
        public void ResetForNewPlaythrough() { }
        public void CaptureNightSnapshot(int round) => SnapshotRound = round;
    }

    // --- registration -------------------------------------------------------------------------

    [Fact]
    public void Register_OrderIsFanOrder()
    {
        var reg = new WorldStateRegistry();
        var log = new List<string>();
        reg.Register(new FakeSlice("a", log));
        reg.Register(new FakeSlice("b", log));
        reg.Register(new FakeSlice("c", log));

        reg.FanReset();

        Assert.Equal(new[] { "a", "b", "c" }, reg.Ids);
        Assert.Equal(new[] { "a", "b", "c" }, log);
    }

    [Fact]
    public void Register_DuplicateId_RefusedAndNotFanned()
    {
        var reg = new WorldStateRegistry();
        var first = new FakeSlice("dup");
        var second = new FakeSlice("dup");
        Assert.True(reg.Register(first));
        Assert.False(reg.Register(second));

        reg.FanReset();

        Assert.Equal(1, first.ResetCalls);
        Assert.Equal(0, second.ResetCalls);
        Assert.Single(reg.Ids);
    }

    // --- the boundary: seeded state clears without any process restart (spec §5.4) -------------

    [Fact]
    public void FanReset_SeededState_ClearsEverySlice_NoProcessRestartNeeded()
    {
        var reg = new WorldStateRegistry();
        var slices = new[] { new FakeSlice("props"), new FakeSlice("wallets"), new FakeSlice("water") };
        foreach (FakeSlice s in slices)
        {
            reg.Register(s);
            s.State = 42; // a playthrough's worth of dirt
        }

        var failed = reg.FanReset();

        Assert.Empty(failed);
        foreach (FakeSlice s in slices)
            Assert.Equal(0, s.State);
    }

    [Fact]
    public void FanReset_Twice_IsIdempotent_EverySliceReachedBothTimes()
    {
        var reg = new WorldStateRegistry();
        var slice = new FakeSlice("x") { State = 7 };
        reg.Register(slice);

        reg.FanReset();
        reg.FanReset(); // the documented T10 double pass, undeduped at the pure layer

        Assert.Equal(0, slice.State);
        Assert.Equal(2, slice.ResetCalls); // running twice == running once, state-wise
    }

    // --- failure isolation: one broken slice never strands the ones after it -------------------

    [Fact]
    public void FanReset_ThrowingSlice_IsReported_AndLaterSlicesStillReset()
    {
        var reg = new WorldStateRegistry();
        var before = new FakeSlice("before") { State = 1 };
        var after = new FakeSlice("after") { State = 1 };
        reg.Register(before);
        reg.Register(new ThrowingSlice());
        reg.Register(after);
        var reported = new List<string>();

        var failed = reg.FanReset((id, _) => reported.Add(id));

        Assert.Equal(new[] { "broken" }, failed);
        Assert.Equal(new[] { "broken" }, reported);
        Assert.Equal(0, before.State);
        Assert.Equal(0, after.State); // the loud-abort contract aborts AFTER the fan, never mid-fan
    }

    // --- the typed fans reach only their own kind ----------------------------------------------

    [Fact]
    public void FanNightEnded_ReachesNightSlicesOnly_WithTheRound()
    {
        var reg = new WorldStateRegistry();
        var plain = new FakeSlice("plain");
        var night = new NightSlice();
        var map = new MapSlice();
        reg.Register(plain);
        reg.Register(night);
        reg.Register(map);

        reg.FanNightEnded(3);

        Assert.Equal(3, night.NightEndedRound);
        Assert.Equal(-1, map.SnapshotRound);
        Assert.Equal(0, plain.ResetCalls);
    }

    [Fact]
    public void FanCaptureSnapshot_ReachesMapSlicesOnly()
    {
        var reg = new WorldStateRegistry();
        var night = new NightSlice();
        var map = new MapSlice();
        reg.Register(night);
        reg.Register(map);

        reg.FanCaptureSnapshot(2);

        Assert.Equal(2, map.SnapshotRound);
        Assert.Equal(-1, night.NightEndedRound);
    }
}

/// <summary>CORE-PROG-A2 scope 6: the gating predicate is spec §1.2's (state × band) table's
/// "gameplay-live" column, verbatim — every consumer's one-line guard calls this.</summary>
public class PlaythroughStatesTests
{
    [Theory]
    [InlineData(PlaythroughState.Boot, false)]
    [InlineData(PlaythroughState.RoundIntro, true)]
    [InlineData(PlaythroughState.InRound, true)]
    [InlineData(PlaythroughState.RoundEnd, false)]
    [InlineData(PlaythroughState.UpgradeLobby, false)]
    [InlineData(PlaythroughState.Loss, false)]
    public void IsBandLive_MatchesSpecTable(PlaythroughState state, bool live) =>
        Assert.Equal(live, PlaythroughStates.IsBandLive(state));
}

/// <summary>CORE-PROG-A2 spec §6 case 3: the cross-reset resume exploit closes (a pre-boundary
/// ticket is unconsumable after the boundary) while the legitimate same-run window keeps
/// working (the positive control the acceptance criterion demands).</summary>
public class ReconnectRegistrySliceTests
{
    [Fact]
    public void ResetForNewPlaythrough_KillsAPendingTicket_ExploitClosed()
    {
        var reg = new ReconnectRegistry();
        reg.Capture(steamId: 76561198000000001, new Vector3(5, 0, 5), new[] { 3, 7 }, colorIndex: 2, nowSec: 100);

        reg.ResetForNewPlaythrough(); // the playthrough boundary (Play Again)

        // Well inside the 60 s window — before this packet, this consumed and resumed stale
        // position + stale props into a freshly wiped world.
        Assert.False(reg.TryConsume(76561198000000001, nowSec: 110, out ReconnectRegistry.ResumeData data));
        Assert.Empty(data.HeldPropIds);
        Assert.Equal(0, reg.Count);
    }

    [Fact]
    public void SameRunResume_WithinWindow_StillWorks_PositiveControl()
    {
        var reg = new ReconnectRegistry();
        reg.Capture(steamId: 76561198000000002, new Vector3(1, 2, 3), new[] { 9 }, colorIndex: 4, nowSec: 100);

        // No boundary between capture and resume — the legitimate 60 s path is untouched.
        Assert.True(reg.TryConsume(76561198000000002, nowSec: 159, out ReconnectRegistry.ResumeData data));
        Assert.Equal(new Vector3(1, 2, 3), data.Position);
        Assert.Equal(new[] { 9 }, data.HeldPropIds);
        Assert.Equal(4, data.ColorIndex);
    }

    [Fact]
    public void ResetForNewPlaythrough_Twice_Idempotent()
    {
        var reg = new ReconnectRegistry();
        reg.Capture(1UL, Vector3.Zero, Array.Empty<int>(), 0, nowSec: 0);
        reg.ResetForNewPlaythrough();
        reg.ResetForNewPlaythrough();
        Assert.Equal(0, reg.Count);
    }
}
