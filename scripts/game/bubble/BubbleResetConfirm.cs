using System.Collections.Generic;

namespace Sail.Game.Bubble;

/// <summary>What a press did, once <see cref="BubbleResetConfirm"/> has looked at it.</summary>
public enum ResetPress
{
    /// <summary>The first press of a pair. Nothing was reset; the lever is now warning, and this
    /// peer has <see cref="BubbleResetConfirm.ConfirmWindowSec"/> to mean it.</summary>
    Armed,

    /// <summary>A press that arrived so soon after the arming press that it cannot have been a
    /// reading of the warning — a double-tap, not a decision. The arm is left standing and
    /// nothing else happens. See <see cref="BubbleResetConfirm.MinDwellSec"/>.</summary>
    Ignored,

    /// <summary>The second, deliberate press inside the window. The caller may now reset — after
    /// its own debounce has had its separate say.</summary>
    Confirmed,
}

/// <summary>
/// <b>"Did this player mean it?" — the whole of the lever's warning, with no Godot in it.</b>
///
/// <para><b>Why this exists at all (Talon, 2026-08-29, playtest note 4):</b> <i>"There needs to
/// be a sign at least that it will reset the bubble counter and a warning ... because I would be
/// upset if I spent hours collecting all the bubbles and then someone reset my progress."</i> A
/// player could walk up to an unlabelled lever and erase everyone's board with one press. This
/// class is the second press.</para>
///
/// <para><b>It is NOT the debounce, and the separation is deliberate.</b>
/// <see cref="BubbleResetGate"/> answers <i>"did two people press at once?"</i> and collapses
/// them into one reset. This answers <i>"did one person mean it?"</i> They are different
/// questions with different right answers, and folding them into one timer would make both
/// untestable — a single window could not simultaneously be short enough to collapse a
/// double-tap and long enough to let somebody read a sign. So a press passes through this first,
/// and only a <see cref="ResetPress.Confirmed"/> one is ever offered to the gate.</para>
///
/// <para><b>Armed state is per peer, not one slot.</b> Six players can reach this lever. A single
/// shared arm would mean A's press arms, B's press steals the arm, A's second press steals it
/// back — nobody ever confirming, the lever permanently unusable whenever two people stand at it.
/// Worse, the other resolution (B's press confirms A's arm) is precisely the defect Talon named:
/// two people who each pressed once, neither of whom decided anything, and the board gone. Per
/// peer, both go away: your second press confirms <i>your</i> arm and nobody else's, and a
/// stranger's stray press can only ever arm the stranger.</para>
///
/// <para><b>Server-side, exactly as the gate is</b> (MECHANICS-BIBLE §3: the authority
/// adjudicates a race, never the machine that got there first). The client requests; this decides.
/// A client cannot forge an arm it does not have, because arms live here and are keyed by the
/// sender id the multiplayer layer reports.</para>
///
/// <para><b>Cancellation is real</b> (the packet: <i>a warning you cannot back out of is not a
/// warning</i>) and has two shapes, both of which land here: <see cref="Cancel"/>, sent by a peer
/// that walks away from the lever, and simple expiry — an arm older than
/// <see cref="ConfirmWindowSec"/> is dead and a later press inherits nothing from it.</para>
/// </summary>
public sealed class BubbleResetConfirm
{
    /// <summary>How long an armed warning stands before it lapses, in seconds.
    ///
    /// <para>4 s is chosen against the two things it has to sit between. The floor is reading:
    /// the sign's warning line is a dozen words and a number, which is a beat to see, a beat to
    /// read and a beat to act on — call it two seconds, and do not make a player race it. The
    /// ceiling is meaning: the window exists to establish that the two presses were one decision,
    /// so it must be shorter than "I pulled it, wandered off, came back and pulled it again". At
    /// a walk this is a couple of metres of travel — comfortably inside
    /// <see cref="BubbleResetLever.PressRadius"/>, which is the other half of the argument: leave
    /// the lever and <see cref="Cancel"/> fires anyway.</para></summary>
    public const double ConfirmWindowSec = 4.0;

    /// <summary>How soon after arming a press is treated as a double-tap rather than a decision.
    ///
    /// <para><b>Without this the warning is defeatable by mashing.</b> Two presses in the same
    /// half-second would arm and confirm, and a player who never looked at the sign would wipe
    /// the board exactly as before — the warning would be a formality that only slowed down the
    /// people already being careful. 0.5 s is below any plausible read-and-decide and above the
    /// interval a key held or double-tapped produces, so it separates "I meant that" from "my
    /// finger bounced" without ever standing in a deliberate player's way.</para>
    ///
    /// <para>Note this is NOT the debounce wearing a second hat: the debounce suppresses a second
    /// <i>reset</i> for 1.2 s after one lands, and never sees a press that did not confirm. This
    /// suppresses a second <i>confirm</i> from the peer that just armed. Neither can do the
    /// other's job.</para></summary>
    public const double MinDwellSec = 0.5;

    /// <summary>Peer id -> the clock reading at which that peer armed. At most one entry per
    /// connected peer, and entries are pruned as they lapse (see <see cref="Prune"/>), so this
    /// cannot grow with session length.</summary>
    private readonly Dictionary<int, double> _armedAt = new();

    /// <summary>Session-total arms — instrumentation for the suites, which assert on exact
    /// counts rather than on "a warning happened".</summary>
    public int Armed { get; private set; }

    /// <summary>Session-total confirmations offered to the caller.</summary>
    public int Confirmed { get; private set; }

    /// <summary>Session-total presses swallowed by <see cref="MinDwellSec"/>.</summary>
    public int Ignored { get; private set; }

    /// <summary>Session-total arms ended by <see cref="Cancel"/> or by lapsing. Counted because a
    /// cancel that is not observed is indistinguishable from an arm that never existed.</summary>
    public int Cancelled { get; private set; }

    /// <summary>True when any peer's warning is currently standing — what the lever's sign and
    /// its shimmer are driven from.</summary>
    public bool AnyArmed(double nowSec)
    {
        Prune(nowSec);
        return _armedAt.Count > 0;
    }

    /// <summary>
    /// The peer whose warning armed most recently and is still standing, or 0 for none.
    ///
    /// <para>Exists because the lever shows ONE sign and six peers may arm it. Showing the newest
    /// is the only choice that keeps the sign honest as arms come and go: the oldest would leave
    /// the plate quiet while a fresh warning was live, and "whichever the dictionary yields" is
    /// the non-deterministic answer MECHANICS-BIBLE §3 is about. Ties are impossible in practice
    /// (two arms in one frame share a clock reading) and resolved by peer id when they are not,
    /// so two machines asked the same question give the same answer.</para>
    /// </summary>
    public int NewestArmed(double nowSec)
    {
        Prune(nowSec);
        int best = 0;
        double bestAt = double.NegativeInfinity;
        foreach (KeyValuePair<int, double> kv in _armedAt)
        {
            if (kv.Value > bestAt || (kv.Value == bestAt && kv.Key > best))
            {
                bestAt = kv.Value;
                best = kv.Key;
            }
        }
        return best;
    }

    /// <summary>Seconds left on this peer's warning; 0 when it has none. Boundary, stated rather
    /// than left to fall out (MECHANICS-BIBLE §1): the window is INCLUSIVE — a confirm landing at
    /// exactly <see cref="ConfirmWindowSec"/> is honoured, and the arm is dead only after it.
    /// </summary>
    public double RemainingSec(int peer, double nowSec) =>
        _armedAt.TryGetValue(peer, out double at)
            ? System.Math.Max(0, at + ConfirmWindowSec - nowSec)
            : 0;

    /// <summary>
    /// Take a press from <paramref name="peer"/> at <paramref name="nowSec"/>.
    ///
    /// <para>The three outcomes are exhaustive and ordered by what the peer's own arm is doing:
    /// no live arm arms one; a live arm younger than <see cref="MinDwellSec"/> ignores the press
    /// and leaves the arm alone; a live arm past the dwell confirms and CLEARS itself, so one
    /// confirmation can never be spent twice (MECHANICS-BIBLE §4: an event that can fire twice
    /// double-applies unless something stops it).</para>
    /// </summary>
    public ResetPress Press(int peer, double nowSec)
    {
        Prune(nowSec);
        if (!_armedAt.TryGetValue(peer, out double at))
        {
            _armedAt[peer] = nowSec;
            Armed++;
            return ResetPress.Armed;
        }
        if (nowSec - at < MinDwellSec)
        {
            Ignored++;
            return ResetPress.Ignored;
        }
        _armedAt.Remove(peer);
        Confirmed++;
        return ResetPress.Confirmed;
    }

    /// <summary>Back out. True when there was in fact a warning to withdraw — the caller only
    /// broadcasts a change when something changed.</summary>
    public bool Cancel(int peer)
    {
        if (!_armedAt.Remove(peer))
            return false;
        Cancelled++;
        return true;
    }

    /// <summary>Drop every peer whose warning has lapsed, counting each as a cancellation. True
    /// when at least one went, so a caller ticking this can broadcast the sign going quiet
    /// without polling for it.</summary>
    public bool Prune(double nowSec)
    {
        List<int>? dead = null;
        foreach (KeyValuePair<int, double> kv in _armedAt)
        {
            if (nowSec - kv.Value > ConfirmWindowSec)
                (dead ??= new List<int>()).Add(kv.Key);
        }
        if (dead == null)
            return false;
        foreach (int peer in dead)
        {
            _armedAt.Remove(peer);
            Cancelled++;
        }
        return true;
    }
}
