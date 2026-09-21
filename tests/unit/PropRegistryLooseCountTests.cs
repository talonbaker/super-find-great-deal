using System.Collections.Generic;
using Godot;
using MpFoundation.Net;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// PROBE-1 (2026-09-20): <see cref="PropRegistry.LooseCount"/> against the honest answer.
///
/// <para><b>Why a cached count needs a test and a computed one does not.</b> The server's
/// per-tick loop now early-outs on this number instead of walking every prop to discover there
/// is nothing loose. If it ever under-counts, the server stops simulating a loose prop — the
/// exact failure DOOR-1 paid a session for, where the server's own body was shoved across the
/// room while every client's copy sat still and the log said it had worked. So every verb, on
/// every mode, including the ones the store REFUSES, is compared against a filter over
/// <see cref="PropRegistry.All"/>, which is the definition the count is standing in for.</para>
/// </summary>
public class PropRegistryLooseCountTests
{
    private static readonly Transform3D At = new(Basis.Identity, new Vector3(1f, 2f, 3f));

    private static int Honest(PropRegistry r)
    {
        int n = 0;
        foreach (PropState s in r.All)
            if (s.Mode == PropMode.Loose)
                n++;
        return n;
    }

    private static void Agrees(PropRegistry r, int expected)
    {
        Assert.Equal(expected, Honest(r));
        Assert.Equal(expected, r.LooseCount);
    }

    [Fact]
    public void AFreshStoreHasNothingLoose() => Agrees(new PropRegistry(), 0);

    [Fact]
    public void APropIsBornResting()
    {
        var r = new PropRegistry();
        r.Register(PropKind.Can, At);
        r.RegisterAt(1000, PropKind.Box, At);
        Agrees(r, 0);
    }

    [Fact]
    public void GrabThenReleaseThenSettleWalksTheCountUpAndBackDown()
    {
        var r = new PropRegistry();
        int id = r.Register(PropKind.Can, At);
        Agrees(r, 0);
        Assert.True(r.SetHolder(id, 7));
        Agrees(r, 0);
        Assert.True(r.Release(id, At, PropRelease.Thrown));
        Agrees(r, 1);
        Assert.True(r.SetResting(id, At));
        Agrees(r, 0);
    }

    [Fact]
    public void WakeAndSetLooseBothCountAndAreIdempotentFromLoose()
    {
        var r = new PropRegistry();
        int a = r.Register(PropKind.Can, At);
        int b = r.Register(PropKind.Box, At);
        Assert.True(r.Wake(a, At));
        Agrees(r, 1);
        Assert.True(r.Wake(a, At));          // already loose: a second blast on the same crate
        Agrees(r, 1);
        Assert.True(r.SetLoose(b, At));
        Agrees(r, 2);
        Assert.True(r.SetLoose(b, At));
        Agrees(r, 2);
    }

    /// <summary><b>A REFUSED verb must not move the count.</b> This is the half a naive
    /// increment-at-the-call-site gets wrong, and every refusal in the store is here.</summary>
    [Fact]
    public void RefusedVerbsLeaveTheCountAlone()
    {
        var r = new PropRegistry();
        int id = r.Register(PropKind.Can, At);

        Assert.False(r.Release(id, At));            // not held
        Agrees(r, 0);
        Assert.False(r.SetHolder(id, 0));           // the no-holder sentinel
        Agrees(r, 0);
        Assert.False(r.SetLooseTransform(id, At));  // not loose
        Agrees(r, 0);
        Assert.False(r.Wake(9999, At));             // unknown id
        Agrees(r, 0);
        Assert.False(r.SetLoose(9999, At));
        Agrees(r, 0);
        Assert.False(r.Remove(9999));
        Agrees(r, 0);

        Assert.True(r.SetHolder(id, 7));
        Assert.False(r.SetHolder(id, 8));           // contested grab: first-grab-wins
        Agrees(r, 0);
        Assert.False(r.Wake(id, At));               // waking something in a hand
        Agrees(r, 0);
        Assert.False(r.RegisterAt(id, PropKind.Box, At));  // adoption collision
        Agrees(r, 0);
    }

    [Fact]
    public void StreamingALoosePropsTransformDoesNotChangeTheCount()
    {
        var r = new PropRegistry();
        int id = r.Register(PropKind.Can, At);
        Assert.True(r.Wake(id, At));
        Agrees(r, 1);
        for (int i = 0; i < 20; i++)
            Assert.True(r.SetLooseTransform(id, new Transform3D(Basis.Identity, new Vector3(i, 0f, 0f))));
        Agrees(r, 1);
    }

    [Fact]
    public void RemovingALoosePropDecrementsAndRemovingARestingOneDoesNot()
    {
        var r = new PropRegistry();
        int loose = r.Register(PropKind.Can, At);
        int resting = r.Register(PropKind.Box, At);
        Assert.True(r.Wake(loose, At));
        Agrees(r, 1);
        Assert.True(r.Remove(resting));
        Agrees(r, 1);
        Assert.True(r.Remove(loose));
        Agrees(r, 0);
    }

    [Fact]
    public void ADisconnectingHolderDroppingEverythingLeavesNothingLoose()
    {
        var r = new PropRegistry();
        var ids = new List<int>();
        for (int i = 0; i < 5; i++)
            ids.Add(r.Register(PropKind.Can, At));
        foreach (int id in ids)
            Assert.True(r.SetHolder(id, 7));
        Agrees(r, 0);
        Assert.Equal(5, r.ReleaseAllHeldBy(7, At));
        Agrees(r, 0);   // ReleaseAllHeldBy latches to RESTING, not Loose
    }

    /// <summary>A long random walk over every verb, compared against the honest answer after
    /// every single step. A cached count that drifts drifts under sequences nobody wrote a
    /// named test for.</summary>
    [Fact]
    public void ARandomWalkOverEveryVerbNeverDrifts()
    {
        var r = new PropRegistry();
        var ids = new List<int>();
        for (int i = 0; i < 12; i++)
            ids.Add(r.Register(i % 2 == 0 ? PropKind.Can : PropKind.Box, At));
        var rng = new System.Random(20260920);
        for (int step = 0; step < 4000; step++)
        {
            int id = ids[rng.Next(ids.Count)];
            switch (rng.Next(7))
            {
                case 0: r.SetHolder(id, 1 + rng.Next(3)); break;
                case 1: r.Release(id, At, PropRelease.Dropped); break;
                case 2: r.Wake(id, At); break;
                case 3: r.SetLoose(id, At); break;
                case 4: r.SetResting(id, At); break;
                case 5: r.SetLooseTransform(id, At); break;
                case 6: r.ReleaseAllHeldBy(1 + rng.Next(3), At); break;
            }
            Assert.Equal(Honest(r), r.LooseCount);
        }
    }

    /// <summary><see cref="PropRegistry.AllValues"/> is the same collection as
    /// <see cref="PropRegistry.All"/> — the only difference is the static type, which is what
    /// keeps the per-tick <c>foreach</c> off the heap.</summary>
    [Fact]
    public void AllValuesEnumeratesExactlyWhatAllDoes()
    {
        var r = new PropRegistry();
        for (int i = 0; i < 40; i++)
            r.Register(PropKind.Produce, At);
        r.Wake(3, At);
        var viaAll = new List<int>();
        foreach (PropState s in r.All)
            viaAll.Add(s.Id);
        var viaValues = new List<int>();
        foreach (PropState s in r.AllValues)
            viaValues.Add(s.Id);
        viaAll.Sort();
        viaValues.Sort();
        Assert.Equal(viaAll, viaValues);
        Assert.Equal(r.Count, viaValues.Count);
    }
}
