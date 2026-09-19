using System;
using System.Collections.Generic;

namespace MpFoundation.Game.Round;

/// <summary>What happens, in §5's order. The ordinals are not serialised anywhere and carry no
/// wire contract; they exist so a test can assert an ORDER rather than a set of strings.</summary>
public enum StartleBeatKind
{
    /// <summary>The light dips and the intercom clicks, hider-side. Absent entirely when
    /// <see cref="StartleTuning.TellSec"/> is 0 — §5's "at 0 there is no tell at all" is a
    /// missing beat, not a zero-length one.</summary>
    Tell = 0,

    /// <summary>The leaf starts moving, the blocker's collision goes, the bang plays, the props
    /// are shoved, the hiders are kicked and the hider's hands are opened. <b>One instant</b>:
    /// everything §5 lists under "Burst" happens on the same frame, on every peer.</summary>
    Burst = 1,

    /// <summary>The leaf reaches <see cref="StartleTuning.LeafOvershootDeg"/> — the far end of
    /// the bounce.</summary>
    LeafOvershoot = 2,

    /// <summary>The camera kick has decayed to nothing.</summary>
    KickEnded = 3,

    /// <summary>The leaf has fallen back to <see cref="StartleTuning.LeafOpenDeg"/> and the whole
    /// staging is over.</summary>
    LeafSettled = 4,
}

/// <summary>One beat: what, and how long after the Found instant.</summary>
public readonly record struct StartleBeat(StartleBeatKind Kind, double AtSec);

/// <summary>
/// <b>The startle, as arithmetic.</b> Given the Found tick <c>T</c> and
/// <see cref="StartleTuning"/>, this is the whole staging: which beats happen, when, where the
/// leaf is at any instant, how hard the camera is kicked at any instant, and how hard a prop at a
/// given distance is shoved.
///
/// <para><b>Engine-free on purpose, and that is the packet's gate.</b> <c>BurstDoor</c> is a thin
/// driver over these functions — it owns nodes, tweens nothing itself, and asks this class where
/// everything should be. So the timeline can be held by <c>tests/unit/StartleTimelineTests.cs</c>
/// without a scene tree, and the only thing the scene suite is left to prove is the part a pure
/// function genuinely cannot reach: that every peer ran the same timeline at the same instant and
/// that the blocker really stopped blocking.</para>
///
/// <para><b>Everything here is relative to the Found instant, never absolute.</b> <c>T</c> is the
/// SERVER's round tick (<c>HideSeekState.Tick</c>), which a client never runs and therefore
/// cannot convert to its own clock; what every peer DOES share is the one reliable, ordered
/// message that carries T, and each peer starts its own copy of this timeline when that message
/// lands. <see cref="FoundTickOf"/> is the identity the door de-duplicates on, so one T is staged
/// exactly once however many times the wire restates it — which is also what makes a late joiner
/// (a peer that never saw the transition) able to read "Together, and T is already set" as "the
/// door is simply open".</para>
/// </summary>
public static class StartleTimeline
{
    /// <summary>
    /// The beats, in time order, as offsets from the Found instant.
    ///
    /// <para><b>The list is sorted and the ordering is asserted rather than assumed.</b> Several
    /// of these times are sums of independently tunable knobs, so an overlay can genuinely
    /// reorder them (a 0.5 s kick outlasts a 0.12 + 0.25 s leaf), and a consumer walking a list
    /// it believed was sorted would fire the settle before the overshoot.</para>
    /// </summary>
    public static IReadOnlyList<StartleBeat> Beats(in StartleTuning t)
    {
        var beats = new List<StartleBeat>(5);
        // §5: "At 0 there is no tell at all." A zero-length Tell beat would still dip the light
        // and click for one frame, which is a flicker rather than an absence.
        if (t.TellSec > 0f)
            beats.Add(new StartleBeat(StartleBeatKind.Tell, 0.0));
        double burst = Math.Max(0.0, t.TellSec);
        beats.Add(new StartleBeat(StartleBeatKind.Burst, burst));
        beats.Add(new StartleBeat(StartleBeatKind.LeafOvershoot, burst + Math.Max(0.0, t.LeafOpenSec)));
        beats.Add(new StartleBeat(StartleBeatKind.KickEnded, burst + Math.Max(0.0, t.KickSec)));
        beats.Add(new StartleBeat(StartleBeatKind.LeafSettled,
            burst + Math.Max(0.0, t.LeafOpenSec) + Math.Max(0.0, t.LeafSettleSec)));
        beats.Sort(static (a, b) => a.AtSec.CompareTo(b.AtSec));
        return beats;
    }

    /// <summary>When the burst lands, in seconds after the Found instant. The one number every
    /// other lane asks this class for.</summary>
    public static double BurstAtSec(in StartleTuning t) => Math.Max(0.0, t.TellSec);

    /// <summary>When the whole staging is over — the last beat's time.</summary>
    public static double EndsAtSec(in StartleTuning t)
    {
        IReadOnlyList<StartleBeat> beats = Beats(t);
        return beats[beats.Count - 1].AtSec;
    }

    /// <summary>
    /// The leaf's hinge angle, degrees, at <paramref name="sinceFoundSec"/> after the Found
    /// instant. Shut (0) through the tell, up to <see cref="StartleTuning.LeafOvershootDeg"/>
    /// over <see cref="StartleTuning.LeafOpenSec"/>, back to
    /// <see cref="StartleTuning.LeafOpenDeg"/> over <see cref="StartleTuning.LeafSettleSec"/>,
    /// and there forever after.
    ///
    /// <para><b>The open leg eases OUT and the settle leg eases IN-OUT</b>, which is the whole
    /// difference between a door that is thrown and a door that is animated. The open leg is a
    /// quartic ease-out: it leaves the frame at maximum speed on frame one and is already most of
    /// the way across by the time a player's eye has found it. A linear open reads as a gate.</para>
    ///
    /// <para><b>Defined for a negative argument</b> (returns 0), because a peer whose first
    /// sample of the timeline lands before its own start — which a clock adjustment can do —
    /// should see a shut door rather than an extrapolated one.</para>
    /// </summary>
    public static float LeafAngleDegAt(double sinceFoundSec, in StartleTuning t)
    {
        double burst = BurstAtSec(t);
        if (sinceFoundSec <= burst)
            return 0f;

        double open = Math.Max(0.0, t.LeafOpenSec);
        double intoOpen = sinceFoundSec - burst;
        if (intoOpen < open)
        {
            double u = open <= 0.0 ? 1.0 : intoOpen / open;
            return (float)(t.LeafOvershootDeg * EaseOutQuart(u));
        }

        double settle = Math.Max(0.0, t.LeafSettleSec);
        double intoSettle = intoOpen - open;
        if (intoSettle >= settle)
            return t.LeafOpenDeg;
        double s = settle <= 0.0 ? 1.0 : intoSettle / settle;
        return (float)Lerp(t.LeafOvershootDeg, t.LeafOpenDeg, EaseInOut(s));
    }

    /// <summary>The leaf's angle while it swings SHUT on the reset, degrees, at
    /// <paramref name="sinceResetSec"/> after <c>ResetRequested</c>. From wherever it was
    /// (<paramref name="fromDeg"/>) to 0 over <see cref="StartleTuning.LeafCloseSec"/>, eased in
    /// and out — the close is housekeeping and must not read as a second event.</summary>
    public static float ClosingAngleDegAt(double sinceResetSec, float fromDeg, in StartleTuning t)
    {
        double close = Math.Max(0.0, t.LeafCloseSec);
        if (sinceResetSec <= 0.0)
            return fromDeg;
        if (close <= 0.0 || sinceResetSec >= close)
            return 0f;
        return (float)Lerp(fromDeg, 0.0, EaseInOut(sinceResetSec / close));
    }

    /// <summary>
    /// The transient PITCH offset on a hider's first-person camera, degrees, at
    /// <paramref name="sinceBurstSec"/> after the burst. One full sine cycle over
    /// <see cref="StartleTuning.KickSec"/>, linearly decaying, so it starts at zero, throws the
    /// view up by <see cref="StartleTuning.KickDegrees"/>, comes back through zero, undershoots
    /// by less, and is exactly zero at the end.
    ///
    /// <para><b>The frequency is derived from <see cref="StartleTuning.KickSec"/> rather than
    /// being a knob of its own</b> — the packet lists the dials this staging has, and a second
    /// number that only ever means "how many wobbles fit in the window" is a dial nobody would
    /// turn without turning the first one too.</para>
    ///
    /// <para><b>Pitch only, and no roll.</b> A roll on a first-person lens is the motion-sickness
    /// channel FP-1's whole camera is built to refuse, and one axis is enough to read as a
    /// flinch. It is a TRANSIENT OFFSET and never touches <c>FirstPersonCamera.Yaw</c> or
    /// <c>Pitch</c>, so the replicated aim — and therefore the server's aim ray and everything E
    /// resolves against — is untouched, and the player does not lose a frame of input.</para>
    /// </summary>
    public static float KickPitchDegAt(double sinceBurstSec, in StartleTuning t)
    {
        double dur = Math.Max(0.0, t.KickSec);
        if (dur <= 0.0 || sinceBurstSec < 0.0 || sinceBurstSec >= dur)
            return 0f;
        double u = sinceBurstSec / dur;
        return (float)(t.KickDegrees * Math.Sin(2.0 * Math.PI * u) * (1.0 - u));
    }

    /// <summary>
    /// The outward impulse, newton-seconds, on a prop whose centre is
    /// <paramref name="distanceM"/> from the doorway. <b>Linear falloff</b>, per §5 and the
    /// packet: full <see cref="StartleTuning.BurstImpulseNs"/> at the doorway itself, zero at
    /// <see cref="StartleTuning.BurstRadiusM"/>, and zero beyond it — never negative, and never
    /// an inverse-square that would fling whatever happens to be nearest the hinge across the
    /// room.
    /// </summary>
    public static float ImpulseNsAt(double distanceM, in StartleTuning t)
    {
        float radius = t.BurstRadiusM;
        if (!(radius > 0f) || distanceM >= radius)
            return 0f;
        double d = Math.Max(0.0, distanceM);
        return (float)(t.BurstImpulseNs * (1.0 - d / radius));
    }

    /// <summary>The sentinel the wire uses for "no find this round"
    /// (<c>HideSeekWire.NoFoundTick</c>) restated as a predicate, so the door reads one rule
    /// rather than comparing against a magic number in three places.</summary>
    public static bool IsRealFoundTick(long foundTick) => foundTick >= 0;

    /// <summary>The identity a staged burst is de-duplicated on. Trivial today — it IS the tick —
    /// and named so the de-duplication in <c>BurstDoor</c> reads as a deliberate rule rather than
    /// as an equality test somebody happened to write.</summary>
    public static long FoundTickOf(long foundTick) => foundTick;

    private static double Lerp(double a, double b, double u) => a + (b - a) * u;

    private static double EaseOutQuart(double u)
    {
        double c = 1.0 - Math.Clamp(u, 0.0, 1.0);
        return 1.0 - c * c * c * c;
    }

    private static double EaseInOut(double u)
    {
        double c = Math.Clamp(u, 0.0, 1.0);
        return c < 0.5 ? 2.0 * c * c : 1.0 - Math.Pow(-2.0 * c + 2.0, 2) / 2.0;
    }
}
