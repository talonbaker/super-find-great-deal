using System;
using System.IO;
using Godot;
using MpFoundation.Game.Honk;
using MpFoundation.Game.Sandbox;
using MpFoundation.Voice;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// The goose honk's decision core and its waveform (HONK-1, 2026-09-04). Pure: no Godot runtime,
/// no network, no scene, no audio device — everything here is arithmetic on floats and a
/// dictionary. The live end-to-end behaviour over a real ENet session (does peer B hear peer A's
/// honk at peer A's position, does a peer across the map stay silent, is a forged honk refused) is
/// proven separately by <c>tests/Run-HonkTest.ps1</c>, which needs real peers and a real relay and
/// therefore cannot live here.
///
/// <para>The invariants are written as behaviours rather than as line coverage, because what is
/// being defended is a player-visible fact in every case: mashing does not machine-gun, a friend
/// in the next room hears you, a stranger across the map does not, and the range never quietly
/// forks away from voice's.</para>
/// </summary>
public class HonkTests
{
    private const int PeerA = 11;
    private const int PeerB = 22;

    private static Vector3 At(float x) => new(x, 0, 0);

    // --- Anti-spam: the cooldown ---------------------------------------------------------------

    [Fact]
    public void FirstHonk_IsAlwaysGranted()
    {
        var gate = new HonkGate();
        Assert.True(gate.TryHonk(PeerA, 0.0));
    }

    /// <summary>The packet's acceptance criterion 6, as arithmetic: N rapid presses must produce
    /// fewer than N honks. Six presses spread across half a second — a plausible mash — against a
    /// 0.6 s cooldown must yield exactly one.</summary>
    [Fact]
    public void SixPressesInsideOneCooldown_ProduceExactlyOneHonk()
    {
        var gate = new HonkGate();
        int granted = 0;
        for (int i = 0; i < 6; i++)
        {
            if (gate.TryHonk(PeerA, i * 0.1))
                granted++;
        }
        Assert.Equal(1, granted);
        Assert.Equal((1L, 5L), gate.Counters);
    }

    /// <summary>A key held down for three seconds honks at the cadence the cooldown sets and not
    /// one press faster. 180 frames at 60 Hz over 3.0 s at a 0.6 s cooldown is 5 honks
    /// (t = 0.0, 0.6, 1.2, 1.8, 2.4 — 3.0 is the 181st frame and is never reached).</summary>
    [Fact]
    public void HeldKeyForThreeSeconds_HonksAtTheCooldownCadence()
    {
        var gate = new HonkGate();
        int granted = 0;
        for (int frame = 0; frame < 180; frame++)
        {
            if (gate.TryHonk(PeerA, frame / 60.0))
                granted++;
        }
        Assert.Equal(5, granted);
    }

    /// <summary>MECHANICS-BIBLE §1: the cooldown bound is pinned EXCLUSIVE — a press at exactly
    /// the cooldown is granted. Pinned in a test because the alternative eats a press on a
    /// floating-point tie, and the failure a player notices is the swallowed honk.</summary>
    [Fact]
    public void CooldownBoundary_ExactlyAtTheBound_IsGranted()
    {
        var gate = new HonkGate();
        Assert.True(gate.TryHonk(PeerA, 10.0));
        Assert.False(gate.TryHonk(PeerA, 10.0 + HonkConfig.CooldownSec - 0.001));
        Assert.True(gate.TryHonk(PeerA, 10.0 + HonkConfig.CooldownSec));
    }

    // --- The server's jitter tolerance ----------------------------------------------------------
    //
    // The client spaces its sends at exactly CooldownSec; the server judges them on ARRIVAL, so the
    // gap it sees is CooldownSec plus the difference between two network latencies — negative half
    // the time. With both latches strict, every "early" arrival is refused, and because a refusal
    // does not advance the latch the next honk lands a FULL cooldown later: a held key honks at
    // about half the intended cadence. HonkConfig.ServerJitterToleranceSec is what stops that.

    /// <summary>The default gate is strict, so nothing that does not ask for tolerance gets any —
    /// the client's copy included.</summary>
    [Fact]
    public void DefaultGate_HasNoTolerance()
    {
        Assert.Equal(0.0, new HonkGate().ToleranceSec);
    }

    /// <summary>The bug, reproduced: two presses spaced at exactly the cooldown by the client, the
    /// second arriving 20 ms early through jitter. A strict server refuses it — a tolerant one does
    /// not.</summary>
    [Fact]
    public void JitteredArrival_IsRefusedByAStrictGate_AndGrantedByTheServersGate()
    {
        const double jitterSec = 0.02;

        var strict = new HonkGate();
        Assert.True(strict.TryHonk(PeerA, 0.0));
        Assert.False(strict.TryHonk(PeerA, HonkConfig.CooldownSec - jitterSec));

        var server = new HonkGate { ToleranceSec = HonkConfig.ServerJitterToleranceSec };
        Assert.True(server.TryHonk(PeerA, 0.0));
        Assert.True(server.TryHonk(PeerA, HonkConfig.CooldownSec - jitterSec));
    }

    /// <summary>Tolerance is a margin, not a hole: a flood is still capped just outside it.</summary>
    [Fact]
    public void Tolerance_DoesNotOpenTheGateToAFlood()
    {
        var server = new HonkGate { ToleranceSec = HonkConfig.ServerJitterToleranceSec };
        Assert.True(server.TryHonk(PeerA, 0.0));
        double justOutside = HonkConfig.CooldownSec - HonkConfig.ServerJitterToleranceSec - 0.001;
        Assert.False(server.TryHonk(PeerA, justOutside));
        Assert.False(server.TryHonk(PeerA, 0.001));
    }

    /// <summary><b>What the tolerance costs on the abuse side, measured rather than argued.</b>
    ///
    /// <para>A client pressing every frame lands its next press on the first frame at or after
    /// <c>next − tolerance</c>, so its cadence settles at <c>Cooldown − tolerance</c>, not at
    /// <c>Cooldown</c> — the tolerance IS spendable by a flooder. Written down here because the
    /// first version of this test asserted the opposite ("it cannot compound") and went red: over
    /// ten seconds the gate granted <b>19</b> honks, which is the 0.55 s rate, not the 0.6 s one.
    /// The design is unchanged by that — <see cref="HonkConfig.ServerJitterToleranceSec"/> already
    /// states the cap as 1.82/s rather than 1.67/s — but the bound is now pinned instead of
    /// assumed.</para>
    ///
    /// <para>The claim that matters is that it is BOUNDED and small: a 9% loosening of the
    /// anti-spam rule, bought to remove a 50%-loss bug from every honest player holding the key.
    /// A tolerance that compounded would show up here as a runaway count, not as 19.</para></summary>
    [Fact]
    public void Tolerance_CostsABoundedNinePercent_AgainstAFlood()
    {
        const double floodSec = 10.0;

        var server = new HonkGate { ToleranceSec = HonkConfig.ServerJitterToleranceSec };
        var strict = new HonkGate();
        for (int frame = 0; frame * (1 / 60.0) < floodSec; frame++)
        {
            server.TryHonk(PeerA, frame / 60.0);
            strict.TryHonk(PeerA, frame / 60.0);
        }

        long tolerant = server.Counters.Granted;
        long strictCount = strict.Counters.Granted;

        // The tolerant rate, +1 for the press at t=0. Runaway compounding would blow past this.
        long ceiling = (long)System.Math.Ceiling(floodSec / (HonkConfig.CooldownSec - HonkConfig.ServerJitterToleranceSec)) + 1;
        Assert.InRange(tolerant, strictCount, ceiling);
        // ...and the loosening is single-digit percent, not a hole.
        Assert.True(tolerant <= strictCount + 3,
            $"tolerance granted {tolerant} against the strict gate's {strictCount} over {floodSec}s");
        Assert.True(server.Counters.Throttled > 500, "the flood must mostly have been refused");
    }

    /// <summary>The cooldown is PER PLAYER (INTERACTION-BIBLE §5, contention: "both allowed" with a
    /// per-sender cooldown, not a shared one). Six players honking in the same tick is six honks.</summary>
    [Fact]
    public void CooldownIsPerPeer_OneHonkerNeverGatesAnother()
    {
        var gate = new HonkGate();
        Assert.True(gate.TryHonk(PeerA, 0.0));
        Assert.True(gate.TryHonk(PeerB, 0.0));
        Assert.False(gate.TryHonk(PeerA, 0.1));
        Assert.False(gate.TryHonk(PeerB, 0.1));
    }

    [Fact]
    public void RemainingSec_CountsDownAndFloorsAtZero()
    {
        var gate = new HonkGate();
        gate.TryHonk(PeerA, 5.0);
        Assert.Equal(HonkConfig.CooldownSec, gate.RemainingSec(PeerA, 5.0), 6);
        Assert.Equal(0.0, gate.RemainingSec(PeerA, 5.0 + HonkConfig.CooldownSec), 6);
        Assert.Equal(0.0, gate.RemainingSec(PeerA, 99.0), 6);
        Assert.Equal(0.0, gate.RemainingSec(PeerB, 5.0), 6); // never honked
    }

    /// <summary>ENet recycles peer ids. A latch left behind by a departed player would make the
    /// next player handed that id silently unable to honk for up to a cooldown after joining —
    /// exactly the class of bug VoiceManager fixes for its avatar cache and its mute entries.</summary>
    [Fact]
    public void RecycledPeerId_AfterForgetPeer_HonksImmediately()
    {
        var gate = new HonkGate();
        gate.TryHonk(PeerA, 0.0);
        Assert.False(gate.TryHonk(PeerA, 0.1)); // still the old occupant, still cooling
        gate.ForgetPeer(PeerA);                 // they left; the id is free again
        Assert.True(gate.TryHonk(PeerA, 0.1));  // a NEW player got it — no inherited latch
    }

    [Fact]
    public void Clear_ResetsBothLatchesAndCounters()
    {
        var gate = new HonkGate();
        gate.TryHonk(PeerA, 0.0);
        gate.TryHonk(PeerA, 0.1);
        Assert.Equal((1L, 1L), gate.Counters);
        gate.Clear();
        Assert.Equal((0L, 0L), gate.Counters);
        Assert.True(gate.TryHonk(PeerA, 0.1));
    }

    [Fact]
    public void InvalidPeerId_IsRefusedAndCountsAsNeither()
    {
        var gate = new HonkGate();
        Assert.False(gate.TryHonk(0, 0.0));
        Assert.False(gate.TryHonk(-3, 0.0));
        Assert.Equal((0L, 0L), gate.Counters);
    }

    // --- Proximity: earshot --------------------------------------------------------------------

    [Fact]
    public void APeerInConversationRange_Hears()
    {
        Assert.True(HonkGate.InEarshot(At(0), At(4)));
    }

    [Fact]
    public void APeerAcrossTheMap_DoesNotHear()
    {
        Assert.False(HonkGate.InEarshot(At(0), At(200)));
    }

    /// <summary>MECHANICS-BIBLE §1 again: the range bound is pinned INCLUSIVE, matching
    /// VoiceRelayDecider's own <c>dist &lt;= radius</c> rather than inventing a second
    /// convention.</summary>
    [Fact]
    public void RangeBoundary_ExactlyAtTheBound_IsInEarshot()
    {
        Assert.True(HonkGate.InEarshot(At(0), At(HonkConfig.AudibleRangeM)));
        Assert.False(HonkGate.InEarshot(At(0), At(HonkConfig.AudibleRangeM + 0.01f)));
    }

    /// <summary>Distance is 3D, matching AudioStreamPlayer3D's own 3D cutoff: a listener directly
    /// above a honker is as far away to this gate as they are to the mixer.</summary>
    [Fact]
    public void Earshot_IsThreeDimensional()
    {
        var high = new Vector3(0, HonkConfig.AudibleRangeM + 1f, 0);
        Assert.False(HonkGate.InEarshot(Vector3.Zero, high));
    }

    /// <summary>An unresolvable avatar (the spawn/despawn race) FAILS OPEN. The failure a player
    /// reports is "nobody could hear me", so the safe direction is loud — and the mixer's own
    /// MaxDistance is still behind this, so a fail-open cannot produce an audible honk from across
    /// the map.</summary>
    [Fact]
    public void UnknownPosition_FailsOpen_OnEitherSide()
    {
        Assert.True(HonkGate.InEarshot(null, At(200)));
        Assert.True(HonkGate.InEarshot(At(0), null));
        Assert.True(HonkGate.InEarshot(null, null));
    }

    // --- The range is voice's range, not a fork -------------------------------------------------

    /// <summary>The two numbers agree today. <b>This is the weak half of criterion 5 and it is
    /// labelled as such:</b> a hand-typed <c>24.0f</c> in HonkConfig passes it — measured, by
    /// planting exactly that literal and watching this test stay green. The half that actually
    /// bites is <see cref="HonkRange_IsAnAlias_NotACopiedLiteral"/> below.</summary>
    [Fact]
    public void HonkRange_AgreesWithVoiceRange()
    {
        Assert.Equal(VoiceConfig.ProximityMaxDistance, HonkConfig.AudibleRangeM);
        Assert.Equal(VoiceConfig.ProximityUnitSize, HonkConfig.UnitSizeM);
    }

    /// <summary>The packet's acceptance criterion 5, structurally: <i>"reuse the voice proximity
    /// gate's own numbers rather than typing new ones, so a change to voice range moves the honk
    /// with it."</i> A value comparison cannot express that — two numbers that agree today agree
    /// whether one is an alias or a copy, and the copy only reveals itself on the day somebody
    /// retunes voice and the honk silently does not follow. So this reads the SOURCE and requires
    /// both constants to be defined as references to <c>VoiceConfig</c>.
    ///
    /// <para><b>With a positive control, because a scanner that has stopped reading files reports
    /// clean.</b> The control asserts the same file DOES contain a bare float literal somewhere
    /// (<c>VolumeDb</c>, <c>PitchJitter</c> and <c>CooldownSec</c> are all honestly local values) —
    /// so a regex that matched nothing, or a path that resolved to an empty string, fails here
    /// instead of passing everything. Same discipline as <c>DeadNameAuditTests</c>.</para></summary>
    [Fact]
    public void HonkRange_IsAnAlias_NotACopiedLiteral()
    {
        string source = File.ReadAllText(
            Path.Combine(FindRepoRoot(), "scripts", "game", "honk", "HonkConfig.cs"));

        // Positive control FIRST: prove the scanner is actually reading a populated file that it
        // is capable of finding literals in, before any absence below is believed.
        Assert.Matches(@"public const float VolumeDb\s*=\s*-?[0-9]", source);

        Assert.Matches(@"AudibleRangeM\s*=\s*VoiceConfig\.ProximityMaxDistance\s*;", source);
        Assert.Matches(@"UnitSizeM\s*=\s*VoiceConfig\.ProximityUnitSize\s*;", source);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "project.godot")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    /// <summary>The voice relay gate's radii are deliberately NOT reused: they carry a jitter-buffer
    /// hysteresis a one-shot has no use for, and relaying at 30 m would post honks 6 m past the
    /// distance the receiving mixer throws them away at. Pinned so the "why is this number
    /// different" question has an answer that fails if the answer stops being true.</summary>
    [Fact]
    public void HonkRange_IsTheAudibilityCutoff_NotTheRelayGatesEnterRadius()
    {
        Assert.True(HonkConfig.AudibleRangeM < MpFoundation.Net.VoiceProximityGate.EnterRadiusM);
        Assert.True(HonkConfig.AudibleRangeM < MpFoundation.Net.VoiceProximityGate.ExitRadiusM);
    }

    // --- The sound itself -----------------------------------------------------------------------
    //
    // SfxLab.GooseHonkPcm returns raw float PCM and uses only Mathf's pure static math, so the
    // waveform is reachable from a Godot-free suite. What is asserted is exactly the set of
    // properties the DECISION-LOG claims and no more: Talon owns the taste, this owns the facts
    // that would make the taste unhearable.

    [Fact]
    public void GooseHonk_IsTheDocumentedLength()
    {
        float[] pcm = SfxLab.GooseHonkPcm();
        // 48 kHz is the project's pinned mix rate; 0.42 s of it.
        Assert.InRange(pcm.Length, 20_000, 20_500);
    }

    /// <summary>Headroom. Render() clamps to ±1, so a recipe that overshoots does not error — it
    /// hard-clips, which is an audible buzz and is not the roughness the goose wants. The gain was
    /// picked to land here; this is what stops a retune from silently clipping.</summary>
    [Fact]
    public void GooseHonk_NeverClips()
    {
        float peak = 0f;
        foreach (float s in SfxLab.GooseHonkPcm())
            peak = Math.Max(peak, Math.Abs(s));
        Assert.InRange(peak, 0.2f, 0.95f);
    }

    [Fact]
    public void GooseHonk_StartsAndEndsAtSilence()
    {
        float[] pcm = SfxLab.GooseHonkPcm();
        Assert.InRange(Math.Abs(pcm[0]), 0f, 0.001f);
        Assert.InRange(Math.Abs(pcm[^1]), 0f, 0.02f); // the fall lands on silence, not a click
    }

    /// <summary><b>The one property that makes it a goose and not a car horn:</b> the pitch ARC.
    /// A horn holds one frequency flat for its whole length; the goose rises into a shout and
    /// falls away from it ("ah-RONK"). Measured on this tip via <see cref="Fundamental"/>:
    /// <b>opening 411 Hz -> shout 476 Hz -> tail 406 Hz</b>. The assertion is the SHAPE with a 5%
    /// margin, not those numbers — retuning HonkStartHz/HonkPeakHz/HonkEndHz keeps it green, and
    /// flattening the arc is the one edit that does not.
    ///
    /// <para>The estimator is a zero-crossing count on a three-pole-lowpassed copy rather than the
    /// raw signal, and that is not fussiness: a plain zero-crossing count on this waveform reads
    /// the reed stack's harmonics rather than its fundamental, and because the timbre DARKENS
    /// across the call it reported the pitch going DOWN through the rise. Autocorrelation was
    /// tried second and octave-errored on the same harmonics. Both were measured before this
    /// one.</para></summary>
    [Fact]
    public void GooseHonk_RisesIntoTheShoutAndFallsAwayFromIt()
    {
        float[] pcm = SfxLab.GooseHonkPcm();
        int n = pcm.Length;
        double opening = Fundamental(pcm, (int)(n * 0.005), (int)(n * 0.06));
        double shout = Fundamental(pcm, (int)(n * 0.20), (int)(n * 0.40));
        double tail = Fundamental(pcm, (int)(n * 0.60), (int)(n * 0.82));
        Assert.True(shout > opening * 1.05,
            $"the call must rise into the shout (opening {opening:F0} Hz -> shout {shout:F0} Hz)");
        Assert.True(shout > tail * 1.05,
            $"the call must fall away from the shout (shout {shout:F0} Hz -> tail {tail:F0} Hz)");
    }

    /// <summary>The shout sits in a goose's band. Wide bounds on purpose — this is the guard
    /// against a retune landing on a foghorn (too low) or a toy squeaker (too high), not a pin on
    /// Talon's taste, which owns everything inside them.</summary>
    [Fact]
    public void GooseHonk_ShoutsInAGoosesBand()
    {
        float[] pcm = SfxLab.GooseHonkPcm();
        double shout = Fundamental(pcm, (int)(pcm.Length * 0.20), (int)(pcm.Length * 0.40));
        Assert.InRange(shout, 250.0, 800.0);
    }

    /// <summary>Fundamental frequency of <c>pcm[from..to)</c>, in Hz.</summary>
    private static double Fundamental(float[] pcm, int from, int to)
    {
        const int rate = 48000;
        // Three cascaded one-pole lowpasses at ~500 Hz (18 dB/oct). The reed stack's harmonics
        // are what defeat a raw zero-crossing count and an autocorrelation alike; with them
        // 18-28 dB down the sign of the signal is the fundamental's alone, and a zero-crossing
        // count on that IS the fundamental.
        const double a = 0.0633;
        double y1 = 0, y2 = 0, y3 = 0;
        int warm = Math.Max(0, from - 2000);
        int crossings = 0;
        double prev = 0;
        for (int i = warm; i < to; i++)
        {
            y1 += a * (pcm[i] - y1);
            y2 += a * (y1 - y2);
            y3 += a * (y2 - y3);
            if (i > from && ((prev < 0 && y3 >= 0) || (prev >= 0 && y3 < 0)))
                crossings++;
            prev = y3;
        }
        return crossings * rate / (2.0 * (to - from));
    }

    /// <summary>Deterministic: the honk is seeded, so it is the same honk every session and on
    /// every peer. Per-shot variation is the playback pitch jitter's job, not the recipe's — a
    /// recipe that re-randomised would make a cached stream disagree with a freshly rendered
    /// one.</summary>
    [Fact]
    public void GooseHonk_IsTheSameHonkEveryTime()
    {
        float[] a = SfxLab.GooseHonkPcm();
        float[] b = SfxLab.GooseHonkPcm();
        Assert.Equal(a.Length, b.Length);
        for (int i = 0; i < a.Length; i += 97)
            Assert.Equal(a[i], b[i], 0.000001f);
    }

}
