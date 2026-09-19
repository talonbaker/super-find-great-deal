using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Sail.Game.Bubble;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// <b>BT-6's shared tally, its idle math, and the rule that a client cannot move either.</b>
///
/// <para>Everything that decides whether the bubble counter is CORRECT lives in
/// <see cref="BubbleCounterState"/> and <see cref="BubbleOscillation"/>, both engine-free by
/// design, so the rules that matter — a bubble pops once, a late joiner is told which ones, a
/// reset really resets, a bob cannot drift the collider — are proved here rather than inferred
/// from a green scene suite. The three-peer convergence that only a real session can show is
/// <c>tests/Run-BubbleSyncTest.ps1</c>'s job; these are the assertions that suite would otherwise
/// have to guess at.</para>
/// </summary>
public class BubbleCounterStateTests
{
    // --- the tally ------------------------------------------------------------------------

    [Fact]
    public void FreshState_HasNothingPoppedAndCountsZero()
    {
        var state = new BubbleCounterState();
        Assert.Equal(0, state.Count);
        Assert.Empty(state.PoppedIds());
        for (int id = 0; id < BubbleCounterState.Capacity; id++)
            Assert.False(state.IsPopped(id));
    }

    [Fact]
    public void TryPop_FirstTouch_PopsTheBubbleAndMovesTheTallyByOne()
    {
        var state = new BubbleCounterState();
        Assert.True(state.TryPop(7));
        Assert.True(state.IsPopped(7));
        Assert.Equal(1, state.Count);
        Assert.False(state.IsPopped(6));
        Assert.False(state.IsPopped(8));
    }

    /// <summary>Acceptance criterion 2, verbatim: an already-popped id returns false and leaves
    /// the count unchanged.</summary>
    [Fact]
    public void TryPop_OnAnAlreadyPoppedId_ReturnsFalseAndLeavesTheCountUnchanged()
    {
        var state = new BubbleCounterState();
        Assert.True(state.TryPop(3));
        Assert.Equal(1, state.Count);

        for (int repeat = 0; repeat < 5; repeat++)
            Assert.False(state.TryPop(3));

        Assert.Equal(1, state.Count);
        Assert.Equal(new[] { 3 }, state.PoppedIds());
    }

    /// <summary>
    /// Acceptance criterion 4 at the state layer: two bodies entering one bubble on the same
    /// server tick produce exactly one increment — and therefore exactly one
    /// <c>PopBroadcast</c>, because <c>BubbleCounter.ServerPop</c> returns before the
    /// <c>Rpc</c> call whenever <see cref="BubbleCounterState.TryPop"/> is false.
    /// </summary>
    [Fact]
    public void TwoBodiesOnOneTick_ProduceExactlyOneIncrementAndOneBroadcast()
    {
        var state = new BubbleCounterState();
        int broadcasts = 0;

        // The exact shape of ServerPop: adjudicate, and only then broadcast.
        void ServerPop(int id)
        {
            if (!state.TryPop(id))
                return;
            broadcasts++;
        }

        ServerPop(11); // player A's body
        ServerPop(11); // player B's body, same tick

        Assert.Equal(1, state.Count);
        Assert.Equal(1, broadcasts);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-999)]
    [InlineData(BubbleCounterState.Capacity)]
    [InlineData(BubbleCounterState.Capacity + 1)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void TryPop_OutOfRangeId_IsRefusedRatherThanThrowing(int id)
    {
        var state = new BubbleCounterState();
        // −1 is not a hypothetical: it is what every un-adopted Bubble carries, so this path is
        // reached by any bubble dropped into a world with no counter.
        Assert.False(BubbleCounterState.IsValidId(id));
        Assert.False(state.TryPop(id));
        Assert.False(state.IsPopped(id));
        Assert.Equal(0, state.Count);
    }

    [Fact]
    public void EncodeThenDecode_RoundTripsThePoppedSetAndTheCount()
    {
        var source = new BubbleCounterState();
        int[] popped = { 0, 1, 7, 8, 63, 64, 100, 255, 256, BubbleCounterState.Capacity - 1 };
        foreach (int id in popped)
            Assert.True(source.TryPop(id));

        byte[] wire = source.Encode();
        Assert.Equal(BubbleCounterState.EncodedBytes, wire.Length);

        var target = new BubbleCounterState();
        Assert.True(target.Decode(wire, source.Count));
        Assert.Equal(source.Count, target.Count);
        Assert.Equal(source.PoppedIds(), target.PoppedIds());
        // Byte boundaries are where a bitset goes wrong, so they are named explicitly above and
        // checked explicitly here.
        foreach (int id in popped)
            Assert.True(target.IsPopped(id));
        Assert.False(target.IsPopped(2));
        Assert.False(target.IsPopped(62));
        Assert.False(target.IsPopped(257));
    }

    [Fact]
    public void Decode_RefusesAPayloadThatCannotBeTrue_AndChangesNothing()
    {
        var state = new BubbleCounterState();
        Assert.True(state.TryPop(5));

        byte[] wire = new byte[BubbleCounterState.EncodedBytes];
        wire[0] = 0b0000_0011; // two bits set

        Assert.False(state.Decode(wire, 3));            // count disagrees with the bits
        Assert.False(state.Decode(null, 0));            // no payload at all
        Assert.False(state.Decode(new byte[8], 0));     // wrong length
        Assert.False(state.Decode(new byte[BubbleCounterState.EncodedBytes + 1], 0));

        // Untouched by every refusal — a rejected sync must not half-apply.
        Assert.Equal(1, state.Count);
        Assert.Equal(new[] { 5 }, state.PoppedIds());

        // ...and the well-formed version of the same payload is accepted, so the refusals above
        // are about the payload rather than about Decode being broken.
        Assert.True(state.Decode(wire, 2));
        Assert.Equal(new[] { 0, 1 }, state.PoppedIds());
    }

    [Fact]
    public void Encode_IsASnapshot_NotAWindowOntoLiveState()
    {
        var state = new BubbleCounterState();
        state.TryPop(1);
        byte[] wire = state.Encode();
        state.TryPop(2); // happens after the message was built

        var target = new BubbleCounterState();
        Assert.True(target.Decode(wire, 1));
        Assert.Equal(new[] { 1 }, target.PoppedIds());
    }

    [Fact]
    public void Reset_PutsEveryBubbleBack_AndIsIdempotent()
    {
        var state = new BubbleCounterState();
        for (int id = 0; id < 40; id++)
            state.TryPop(id);
        Assert.Equal(40, state.Count);

        state.Reset();
        Assert.Equal(0, state.Count);
        Assert.Empty(state.PoppedIds());

        state.Reset(); // the lever pulled twice
        Assert.Equal(0, state.Count);

        // And the same bubble can be popped again afterwards — a reset that left the bits set
        // would read as "reset worked" from the count alone.
        Assert.True(state.TryPop(0));
        Assert.Equal(1, state.Count);
    }

    [Fact]
    public void PoppedIds_AreAscendingAndComplete_AcrossTheWholeCapacity()
    {
        var state = new BubbleCounterState();
        var expected = new List<int>();
        for (int id = 0; id < BubbleCounterState.Capacity; id += 3)
        {
            Assert.True(state.TryPop(id));
            expected.Add(id);
        }
        Assert.Equal(expected, state.PoppedIds());
        Assert.Equal(expected.Count, state.Count);
    }

    // --- the idle bob ---------------------------------------------------------------------

    /// <summary>
    /// Acceptance criterion 6 as arithmetic: no id, at any time, can put a bubble further than
    /// 0.15 m from where it was authored. The self-test measures this over 10 s of one real
    /// session; this sweeps every id the bitset can hold over 60 s at 60 Hz, which is the part a
    /// session cannot cover.
    /// </summary>
    [Fact]
    public void OffsetAt_NeverLeavesTheAuthoredPositionByMoreThanTheBound()
    {
        float worst = 0f;
        for (int id = 0; id < BubbleCounterState.Capacity; id++)
        {
            for (int step = 0; step < 600; step++)
            {
                float len = BubbleOscillation.OffsetAt(id, step / 10.0).Length();
                if (len > worst)
                    worst = len;
            }
        }
        Assert.True(worst <= BubbleOscillation.MaxOffsetM + 1e-4f,
            $"worst offset {worst:F4} m exceeds the {BubbleOscillation.MaxOffsetM} m bound");
        // And it must actually MOVE — a bob clamped to nothing would pass the bound trivially.
        Assert.True(worst > BubbleOscillation.AmplitudeM * 0.9f,
            $"worst offset {worst:F4} m — the bubbles are barely moving");
    }

    [Fact]
    public void OffsetAt_IsAPureFunctionOfIdAndTime_SoEveryPeerAgrees()
    {
        for (int id = 0; id < 32; id++)
        {
            Vector3 first = BubbleOscillation.OffsetAt(id, 12.345);
            Vector3 second = BubbleOscillation.OffsetAt(id, 12.345);
            Assert.Equal(first, second);
        }
    }

    [Fact]
    public void OffsetAt_OnAnUnassignedBubble_LeavesItExactlyWhereItWasAuthored()
    {
        Assert.Equal(Vector3.Zero, BubbleOscillation.OffsetAt(-1, 3.0));
        Assert.Equal(Vector3.Zero, BubbleOscillation.OffsetAt(BubbleCounterState.Capacity, 3.0));
    }

    [Fact]
    public void BobRates_StayInRange_AndDoNotMoveInUnisonAcrossNeighbouringIds()
    {
        var seen = new HashSet<float>();
        for (int id = 0; id < 64; id++)
        {
            float w = BubbleOscillation.OmegaFor(id);
            Assert.InRange(w, BubbleOscillation.OmegaMinRadPerSec, BubbleOscillation.OmegaMaxRadPerSec);
            Assert.InRange(BubbleOscillation.PhaseFor(id), 0f, Mathf.Tau);
            seen.Add(w);
        }
        // A placement pass lays bubbles down in consecutive ids; if a modulus had been used here,
        // this set would be tiny and a row of bubbles would breathe as one object.
        Assert.True(seen.Count > 56, $"only {seen.Count} distinct bob rates across 64 ids");
    }

    /// <summary>Program §7's wander is flagged, not built. Pinned so "the hook exists" cannot
    /// quietly become "the hook does something nobody bounded".</summary>
    [Fact]
    public void WanderHook_IsDeliberatelyZeroToday()
    {
        for (int id = 0; id < 16; id++)
            Assert.Equal(Vector3.Zero, BubbleOscillation.WanderOffsetAt(id, 7.5));
    }

    // --- the night glow -------------------------------------------------------------------

    /// <summary>
    /// Acceptance criterion 7: emission energy is 0 at noon and above 0.5 at deep night. Checked
    /// on every day of the escalation table, because the night band moves (0.250 wide on day 1,
    /// 0.500 by day 5) and a glow keyed to a fixed phase number would pass on day 1 and be wrong
    /// by day 5.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(9)] // past the table: clamps to day 5
    public void EmissionEnergy_IsDarkAtNoonAndLitAtDeepNight(int cyclesElapsed)
    {
        float[] keys = MpFoundation.Game.World.CycleBands.AtmosphereBreakpoints(cyclesElapsed);
        float noon = keys[1];
        float deepNight = keys[6];

        Assert.Equal(0f, BubbleOscillation.EmissionEnergy(noon, cyclesElapsed));
        Assert.True(BubbleOscillation.EmissionEnergy(deepNight, cyclesElapsed) > 0.5f,
            $"deep night on day {cyclesElapsed + 1} glows at "
            + $"{BubbleOscillation.EmissionEnergy(deepNight, cyclesElapsed):F3}");
        Assert.Equal(BubbleOscillation.MaxEmissionEnergy,
            BubbleOscillation.EmissionEnergy(deepNight, cyclesElapsed), 1e-4f);
    }

    [Fact]
    public void Darkness_IsBoundedEverywhere_AndRisesThroughDuskBeforeItPeaks()
    {
        for (int day = 0; day <= 5; day++)
        {
            for (int i = 0; i <= 1000; i++)
            {
                float d = BubbleOscillation.Darkness(i / 1000f, day);
                Assert.InRange(d, 0f, 1f);
            }
        }

        (float duskStart, float nightStart, float dawnStart) =
            MpFoundation.Game.World.CycleBands.Boundaries(0);
        float early = BubbleOscillation.Darkness(duskStart + (nightStart - duskStart) * 0.25f, 0);
        float late = BubbleOscillation.Darkness(duskStart + (nightStart - duskStart) * 0.75f, 0);
        Assert.True(early < late, "the glow must come up WITH the dusk sweep, not after it");
        Assert.Equal(1f, BubbleOscillation.Darkness(nightStart + 0.01f, 0), 1e-3f);
        // ...and go back down through dawn rather than staying lit into the morning.
        Assert.True(BubbleOscillation.Darkness(dawnStart + (1f - dawnStart) * 0.9f, 0) < 0.2f);
    }

    // --- the scene file ---------------------------------------------------------------------

    /// <summary>
    /// Acceptance criterion 8, read off the packed file rather than off a running scene: exactly
    /// one <c>MeshInstance3D</c> and exactly one <c>CollisionShape3D</c>. A second mesh is how a
    /// hundred-instance prop quietly doubles its draw calls, and a second collider is how a
    /// bubble becomes pop-able from somewhere its visual never reaches.
    ///
    /// <para>The radius pair is checked in the same place because it is the same rule from the
    /// other side (program §6.11): the collider must be strictly smaller than the visual, or a
    /// bubble hidden behind a corner can be popped through the wall it was hidden behind.</para>
    /// </summary>
    [Fact]
    public void BubbleScene_HasExactlyOneMeshAndOneCollider_AndTheColliderIsTheSmallerOfThePair()
    {
        string root = FindRepoRoot();
        string path = Path.Combine(root, "scenes", "game", "props", "Bubble.tscn");
        Assert.True(File.Exists(path), $"{path} is missing");
        string text = File.ReadAllText(path);

        Assert.Equal(1, CountOccurrences(text, "type=\"MeshInstance3D\""));
        Assert.Equal(1, CountOccurrences(text, "type=\"CollisionShape3D\""));
        Assert.Equal(1, CountOccurrences(text, "type=\"Area3D\""));

        // The film path the layout contract (§4 rule 7) reserves for BT-9.
        Assert.Contains("res://resources/materials/bubbletest/bubble_film.tres", text);
        Assert.True(File.Exists(Path.Combine(
            root, "resources", "materials", "bubbletest", "bubble_film.tres")));

        // Authored radii, against the constants the entity states.
        Assert.Contains($"radius = {Bubble.VisualRadiusM}", text);
        Assert.Contains($"radius = {Bubble.ColliderRadiusM}", text);
        Assert.True(Bubble.ColliderRadiusM < Bubble.VisualRadiusM,
            "the collider must be strictly smaller than the visual (program 6.11)");

        // Monitorable off: a bubble is something that detects, never something detected.
        Assert.Contains("monitorable = false", text);
        Assert.Contains("collision_mask = 1", text);
    }

    // --- acceptance criterion 5: no client path pops -------------------------------------

    /// <summary>
    /// <b>The absence, checked instead of asserted.</b> Acceptance criterion 5 is a claim that no
    /// client code path can pop a bubble. Three structural facts make that true, and all three are
    /// swept out of the shipped sources here:
    ///
    /// <list type="number">
    /// <item>the tally is only ever moved through <c>BubbleCounterState.TryPop</c>, and the only
    /// file that calls it is <c>BubbleCounter.cs</c>;</item>
    /// <item>every bubble RPC is <c>RpcMode.Authority</c> — an <c>AnyPeer</c> mode is what would
    /// let a client call the broadcast directly, and it appears nowhere;</item>
    /// <item><c>BodyEntered</c> is subscribed exactly once in the whole bubble tree, inside the
    /// <c>isServer</c> branch of <c>Bubble.InitAuthored</c>.</item>
    /// </list>
    ///
    /// <para>Per the standing verification law, an absence check is worth nothing until it can
    /// prove a presence — so the matcher is fed a planted counterexample and the file set is
    /// required to contain a token that genuinely lives there today.</para>
    /// </summary>
    [Fact]
    public void PositiveControl_TheSweepReadsRealBubbleSourcesAndItsMatcherFires()
    {
        string root = FindRepoRoot();
        var files = new List<string>(BubbleSources(root));
        Assert.True(files.Count >= 4, $"only {files.Count} bubble sources found — wrong tree");
        Assert.Contains(files, f => f.EndsWith("BubbleCounter.cs", StringComparison.Ordinal));
        Assert.Contains(files, f => f.EndsWith("Bubble.cs", StringComparison.Ordinal));

        // The matcher can see what it is looking for.
        Assert.Contains("AnyPeer", "MultiplayerApi.RpcMode." + "AnyPeer", StringComparison.Ordinal);
        // ...and the file set is genuinely being read: a token that is definitely in it.
        int sentinel = 0;
        foreach (string file in files)
            if (File.ReadAllText(file).Contains("PopBroadcast", StringComparison.Ordinal))
                sentinel++;
        Assert.True(sentinel > 0, "the sweep is not actually reading these files");
    }

    [Fact]
    public void NoClientPathCanPopABubble()
    {
        string root = FindRepoRoot();
        var offences = new List<string>();
        int tryPopCallers = 0;
        int bodyEnteredSubscriptions = 0;

        foreach (string file in Directory.EnumerateFiles(
            Path.Combine(root, "scripts"), "*.cs", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(file);
            string name = Path.GetFileName(file);
            string rel = Path.GetRelativePath(root, file);

            if (text.Contains("TryPop(", StringComparison.Ordinal)
                && name != "BubbleCounterState.cs")
            {
                tryPopCallers++;
                if (name != "BubbleCounter.cs")
                    offences.Add($"{rel} calls TryPop outside the server-adjudicated counter");
            }
            if (text.Contains("ServerPop(", StringComparison.Ordinal)
                && name != "BubbleCounter.cs" && name != "Bubble.cs")
            {
                offences.Add($"{rel} calls ServerPop from outside the bubble entity");
            }
            if (name is "Bubble.cs" or "BubbleCounter.cs" or "BubbleSelfTest.cs")
            {
                if (text.Contains("RpcMode." + "AnyPeer", StringComparison.Ordinal))
                    offences.Add($"{rel} declares an AnyPeer RPC — a client could call it directly");
                bodyEnteredSubscriptions += CountOccurrences(text, "BodyEntered +=");
            }
        }

        Assert.True(offences.Count == 0,
            "a client-reachable pop path exists: " + string.Join("; ", offences));
        Assert.Equal(1, tryPopCallers);            // BubbleCounter.cs, and nothing else
        Assert.Equal(1, bodyEnteredSubscriptions); // Bubble.InitAuthored's isServer branch
    }

    /// <summary>
    /// <b>The bulk-pop surface is swept too, not merely permitted</b> (CELEBRATE-1, 2026-09-04).
    ///
    /// <para><c>BubbleCounter.ServerPopAllForTest</c> exists so CELEBRATE-1's harness can complete
    /// a level on a schedule without a second file calling <c>ServerPop</c> — which
    /// <see cref="NoClientPathCanPopABubble"/> above would rightly refuse. That put the loop on the
    /// counter, where the server check and the per-bubble adjudication are the same ones a real
    /// collider gets. <b>But a method that satisfies an audit by moving is a method that has to be
    /// audited in its own right</b>, or the sweep has been dodged rather than passed. So: exactly
    /// one caller in <c>scripts/**</c>, and it is the schedule node.</para>
    /// </summary>
    [Fact]
    public void OnlyTheCompletionScheduleDrivesTheBulkPop()
    {
        string root = FindRepoRoot();
        var callers = new List<string>();
        foreach (string file in Directory.EnumerateFiles(
            Path.Combine(root, "scripts"), "*.cs", SearchOption.AllDirectories))
        {
            string name = Path.GetFileName(file);
            if (name == "BubbleCounter.cs")
                continue; // the declaration itself
            if (File.ReadAllText(file).Contains("ServerPopAllForTest(", StringComparison.Ordinal))
                callers.Add(name);
        }
        Assert.Equal(new[] { "BubbleCompletionSchedule.cs" }, callers.ToArray());
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0;
        int at = haystack.IndexOf(needle, StringComparison.Ordinal);
        while (at >= 0)
        {
            count++;
            at = haystack.IndexOf(needle, at + needle.Length, StringComparison.Ordinal);
        }
        return count;
    }

    private static IEnumerable<string> BubbleSources(string root) =>
        Directory.EnumerateFiles(Path.Combine(root, "scripts", "game", "bubble"), "*.cs",
            SearchOption.AllDirectories);

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "project.godot")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
