using Godot;
using MpFoundation.Net;
using MpFoundation.Voice;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// The voice relay proximity gate's decision core (VoiceRelayDecider). Pure state machine:
/// no Godot runtime, no network, no scene — the live end-to-end behaviour over a real ENet
/// session is proven separately by tests/Run-NetStatsTest.ps1 (which needs real peers and a
/// real relay, and therefore cannot live here).
///
/// The invariants under test are the ones that decide whether a player can hear their friends,
/// so they are written as behaviours rather than as line coverage: near hears, far does not,
/// the boundary does not chatter, an unknown position never silences anyone, and the PA is
/// never culled.
/// </summary>
public class VoiceProximityGateTests
{
    private const int Talker = 11;
    private const int Listener = 22;

    private static Vector3 At(float x) => new(x, 0, 0);

    private static bool Relay(VoiceRelayDecider gate, float distanceM, bool pa = false) =>
        gate.ShouldRelay(Talker, Listener, At(0), At(distanceM), pa);

    // --- The two facts the whole gate exists to produce -------------------------------------

    [Fact]
    public void PeerInsideAudibleRange_Relays()
    {
        var gate = new VoiceRelayDecider();
        Assert.True(Relay(gate, VoiceConfig.ProximityMaxDistance - 1f));
    }

    [Fact]
    public void PeerFarOutsideAudibleRange_IsGated()
    {
        var gate = new VoiceRelayDecider();
        Assert.False(Relay(gate, 200f));
    }

    [Fact]
    public void EnterRadiusSitsOutsideTheHardAudibilityCutoff()
    {
        // The margin is the whole reason a listener never hears the relay start: by the time
        // they are close enough for AudioStreamPlayer3D to produce a single audible sample,
        // the stream has already been flowing (and the jitter buffer already filled) for
        // metres of approach. If someone ever tightens EnterRadiusM below the cutoff, this
        // fails rather than shipping a gate that clips the first word of every sentence.
        Assert.True(VoiceProximityGate.EnterRadiusM > VoiceConfig.ProximityMaxDistance);
        Assert.True(VoiceProximityGate.ExitRadiusM > VoiceProximityGate.EnterRadiusM);
    }

    // --- Hysteresis: the boundary must not chatter -------------------------------------------

    [Fact]
    public void ApproachingFromFar_StaysGatedUntilInsideEnterRadius()
    {
        var gate = new VoiceRelayDecider();
        Assert.False(Relay(gate, 100f));
        // Inside the EXIT radius but outside ENTER: a closed pair is judged on ENTER, so this
        // is still silence. Without that asymmetry the pair would open at 40 m and close at
        // 40 m — a coin flip on every footstep.
        float between = (VoiceProximityGate.EnterRadiusM + VoiceProximityGate.ExitRadiusM) * 0.5f;
        Assert.False(Relay(gate, between));
        Assert.True(Relay(gate, VoiceProximityGate.EnterRadiusM - 0.5f));
    }

    [Fact]
    public void RecedingFromNear_StaysOpenUntilPastExitRadius()
    {
        var gate = new VoiceRelayDecider();
        Assert.True(Relay(gate, 1f));
        float between = (VoiceProximityGate.EnterRadiusM + VoiceProximityGate.ExitRadiusM) * 0.5f;
        Assert.True(Relay(gate, between));
        Assert.True(Relay(gate, VoiceProximityGate.ExitRadiusM - 0.5f));
        Assert.False(Relay(gate, VoiceProximityGate.ExitRadiusM + 0.5f));
    }

    [Fact]
    public void OscillatingInsideTheHysteresisBand_NeverFlips()
    {
        // A player pacing back and forth across the ENTER radius is the stutter case. Once
        // open, nothing inside the band closes the pair, so the relay flips exactly zero times
        // across the whole walk and VoiceSpeaker's jitter buffer is never torn down.
        var gate = new VoiceRelayDecider();
        Assert.True(Relay(gate, 1f));
        for (int i = 0; i < 50; i++)
        {
            Assert.True(Relay(gate, VoiceProximityGate.EnterRadiusM - 1f));
            Assert.True(Relay(gate, VoiceProximityGate.EnterRadiusM + 1f));
        }
        Assert.Equal(1, gate.OpenPairCount);
    }

    // --- Fail-open: an unknown position must never silence anyone ----------------------------

    [Fact]
    public void UnknownTalkerPosition_FailsOpen()
    {
        var gate = new VoiceRelayDecider();
        Assert.True(gate.ShouldRelay(Talker, Listener, null, At(500f), paExempt: false));
    }

    [Fact]
    public void UnknownListenerPosition_FailsOpen()
    {
        var gate = new VoiceRelayDecider();
        Assert.True(gate.ShouldRelay(Talker, Listener, At(0), null, paExempt: false));
    }

    [Fact]
    public void FailOpen_DoesNotLatchThePairOpen()
    {
        // The spawn-race hazard: if an "I don't know" wrote the latch, one un-spawned frame
        // would hold a 500 m pair open until it drifted past the EXIT radius — which, being
        // 500 m away, it never would.
        var gate = new VoiceRelayDecider();
        Assert.True(gate.ShouldRelay(Talker, Listener, null, At(500f), paExempt: false));
        Assert.Equal(0, gate.OpenPairCount);
        Assert.False(Relay(gate, 500f));
    }

    // --- PA: exempt, and it does not contaminate the latch ------------------------------------

    [Fact]
    public void PaBroadcast_ReachesEveryoneRegardlessOfDistance()
    {
        var gate = new VoiceRelayDecider();
        Assert.True(Relay(gate, 5000f, pa: true));
    }

    [Fact]
    public void PaBroadcast_DoesNotLeaveThePairLatchedOpen()
    {
        // Switching the PA off mid-sentence must drop the listener back to their real distance,
        // not inherit an "open" the broadcast put there.
        var gate = new VoiceRelayDecider();
        Assert.True(Relay(gate, 5000f, pa: true));
        Assert.Equal(0, gate.OpenPairCount);
        Assert.False(Relay(gate, 5000f));
    }

    // --- Peer lifecycle -----------------------------------------------------------------------

    [Fact]
    public void ForgetPeer_ClearsLatchesInBothRoles()
    {
        // ENet recycles peer ids. A recycled id inheriting a departed player's open pair would
        // relay voice to (or from) someone standing 200 m away, in either direction.
        var gate = new VoiceRelayDecider();
        Assert.True(gate.ShouldRelay(Talker, Listener, At(0), At(1f), false));
        Assert.True(gate.ShouldRelay(Listener, Talker, At(0), At(1f), false));
        Assert.Equal(2, gate.OpenPairCount);

        gate.ForgetPeer(Listener);
        Assert.Equal(0, gate.OpenPairCount);
    }

    [Fact]
    public void ForgetPeer_LeavesUnrelatedPairsAlone()
    {
        var gate = new VoiceRelayDecider();
        Assert.True(gate.ShouldRelay(1, 2, At(0), At(1f), false));
        Assert.True(gate.ShouldRelay(3, 4, At(0), At(1f), false));
        gate.ForgetPeer(2);
        Assert.Equal(1, gate.OpenPairCount);
        // and the survivor is genuinely still latched — receding into the hysteresis band
        // keeps relaying, which only an OPEN pair does.
        float between = (VoiceProximityGate.EnterRadiusM + VoiceProximityGate.ExitRadiusM) * 0.5f;
        Assert.True(gate.ShouldRelay(3, 4, At(0), At(between), false));
    }

    [Fact]
    public void Clear_DropsEveryLatchAndZeroesCounters()
    {
        var gate = new VoiceRelayDecider();
        Relay(gate, 1f);
        Relay(gate, 500f);
        gate.Clear();
        Assert.Equal(0, gate.OpenPairCount);
        (long relayed, long gated, long pa) = gate.TakeCounters();
        Assert.Equal(0, relayed);
        Assert.Equal(0, gated);
        Assert.Equal(0, pa);
    }

    // --- Counters: what the bandwidth instrumentation reads -----------------------------------

    [Fact]
    public void Counters_SeparateRelayedGatedAndPaExempt()
    {
        var gate = new VoiceRelayDecider();
        Relay(gate, 1f);              // relayed
        gate.Clear();
        Relay(gate, 1f);              // relayed
        Relay(gate, 500f);            // ...the pair is open, 500 > exit, so gated
        gate.ShouldRelay(7, 8, At(0), At(9000f), paExempt: true); // pa-exempt (counts as relayed too)

        (long relayed, long gated, long pa) = gate.TakeCounters();
        Assert.Equal(2, relayed);     // the near one and the PA one
        Assert.Equal(1, gated);
        Assert.Equal(1, pa);
    }

    [Fact]
    public void TakeCounters_ResetsSoEachIntervalIsADelta()
    {
        // NetStatsLogger pairs these with ENetConnection.PopStatistic, which is read-and-reset.
        // If these accumulated instead, one line's voice counts would cover the whole run while
        // its byte counts covered one second — and every derived per-packet number would be
        // silently wrong.
        var gate = new VoiceRelayDecider();
        Relay(gate, 1f);
        gate.TakeCounters();
        (long relayed, long gated, long pa) = gate.TakeCounters();
        Assert.Equal(0, relayed);
        Assert.Equal(0, gated);
        Assert.Equal(0, pa);
    }
}
