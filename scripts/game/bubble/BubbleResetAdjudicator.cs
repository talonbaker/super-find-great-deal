namespace Sail.Game.Bubble;

/// <summary>What the server decided about one pull of the lever.</summary>
public enum ResetVerdict
{
    /// <summary>First press of a pair. The warning is now up; nothing was reset.</summary>
    Armed,

    /// <summary>A press too soon after the arm to be a decision. The warning stands; nothing
    /// else happened, and there is nothing to tell the player that the standing warning is not
    /// already telling them.</summary>
    Ignored,

    /// <summary>A confirmed press that the debounce then swallowed — someone else's confirm
    /// landed inside the last <see cref="BubbleResetGate.CooldownSec"/>. The board is already
    /// back; this press changed nothing and the presser is told so.</summary>
    Debounced,

    /// <summary>A confirmed press that got through. Reset the board.</summary>
    Reset,
}

/// <summary>
/// <b>The whole of the lever's server-side decision, with no Godot in it</b> — the warning and
/// the debounce composed in the order that matters.
///
/// <para><b>Why a third class rather than two.</b> <see cref="BubbleResetConfirm"/> and
/// <see cref="BubbleResetGate"/> are each exhaustively testable alone, and each was. What was not
/// testable outside a scene tree and three processes is the thing most likely to be got wrong:
/// the ORDER they run in. Composing them here means the test that encodes Talon's sentence —
/// <i>a full board, a stray press from a peer who never confirmed, and the tally still
/// standing</i> — drives the real decision rather than a paraphrase of it in a test file.</para>
///
/// <para><b>The order, and why it is not interchangeable.</b> Confirm first. If a first press
/// spent the debounce, the confirming press half a second later would be refused as a double-tap
/// and the lever would be impossible to use at all. So the debounce never sees a press that did
/// not confirm, and goes on doing exactly the job it has always done — collapsing two people
/// confirming in the same second into one reset rather than two.</para>
///
/// <para><b>Server-side.</b> Every method here is the authority's, called with a peer id the
/// multiplayer layer reported and a clock the server owns. Nothing a client sends is trusted
/// beyond "this peer pulled"; see <see cref="BubbleResetLever"/>.</para>
/// </summary>
public sealed class BubbleResetAdjudicator
{
    private readonly BubbleResetConfirm _confirm = new();
    private readonly BubbleResetGate _gate = new();

    /// <summary>Resets that actually landed this session.</summary>
    public int Accepted => _gate.Accepted;

    /// <summary>Confirmed presses the debounce swallowed.</summary>
    public int Refused => _gate.Refused;

    /// <summary>Warnings raised.</summary>
    public int Armed => _confirm.Armed;

    /// <summary>Confirmations the warning stage honoured — note this counts presses that reached
    /// the debounce, not resets that landed. The difference IS <see cref="Refused"/>.</summary>
    public int Confirmed => _confirm.Confirmed;

    /// <summary>Presses swallowed by the minimum dwell.</summary>
    public int Ignored => _confirm.Ignored;

    /// <summary>Warnings withdrawn — cancelled or lapsed.</summary>
    public int Cancelled => _confirm.Cancelled;

    /// <summary>Seconds left on the debounce, for the refusal message.</summary>
    public double CooldownRemainingSec(double nowSec) => _gate.RemainingSec(nowSec);

    /// <summary>The peer whose warning the sign should be naming right now, or 0 for a quiet
    /// sign. Prunes lapsed warnings as a side effect, which is what makes expiry reach the sign
    /// without anything having to press anything.</summary>
    public int WarningPeer(double nowSec) => _confirm.NewestArmed(nowSec);

    /// <summary>Withdraw this peer's warning. True when there was one.</summary>
    public bool Cancel(int peer) => _confirm.Cancel(peer);

    /// <summary>One pull of the lever, by <paramref name="peer"/>, at
    /// <paramref name="nowSec"/> on the server's clock.</summary>
    public ResetVerdict Press(int peer, double nowSec) => _confirm.Press(peer, nowSec) switch
    {
        ResetPress.Armed => ResetVerdict.Armed,
        ResetPress.Ignored => ResetVerdict.Ignored,
        _ => _gate.TryPress(nowSec) ? ResetVerdict.Reset : ResetVerdict.Debounced,
    };
}
