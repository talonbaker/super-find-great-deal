namespace Sail.Game.Bubble;

/// <summary>
/// <b>"Has enough time passed since the last reset?" — the whole of the lever's rule, with no
/// Godot in it.</b>
///
/// <para>Separated from <see cref="BubbleResetLever"/> for the reason
/// <c>BubbleCounterState</c> is separated from <c>BubbleCounter</c>: the interesting failure is
/// arithmetic (two presses inside the window producing two resets), and arithmetic is worth
/// testing without a scene tree, a server and three processes. The scene suite then proves the
/// same rule end-to-end; the unit test proves it exhaustively.</para>
///
/// <para><b>Server-side, and that is the point.</b> Six players can reach the same lever. The
/// client that pressed does not decide whether the press counts — it asks, and this gate is what
/// the ask is measured against, so a spammed or forged request buys nothing (MECHANICS-BIBLE:
/// the authority adjudicates; the client only ever requests).</para>
/// </summary>
public sealed class BubbleResetGate
{
    /// <summary>How long the lever ignores further presses after honouring one.
    ///
    /// <para>1.2 s is the packet's number, and it is doing two jobs at once: it debounces a
    /// double-tap from one player, and it collapses "everybody grabbed the lever at once" into a
    /// single reset. It is deliberately longer than the lever's own animation, so a second player
    /// arriving mid-swing sees the swing rather than a lever that has already snapped back.</para>
    /// </summary>
    public const double CooldownSec = 1.2;

    /// <summary>Never-fired, in the same clock the caller uses. Negative rather than 0 so a press
    /// at t = 0 (a reset scheduled at session start, which the fixture does) is honoured.</summary>
    private double _lastAcceptedAt = double.NegativeInfinity;

    /// <summary>Session-total accepted resets — instrumentation for the suites, which assert on
    /// "exactly one" rather than on "a reset happened".</summary>
    public int Accepted { get; private set; }

    /// <summary>Session-total refusals. Counted, because a refusal that is not observed is
    /// indistinguishable from a press that never arrived.</summary>
    public int Refused { get; private set; }

    /// <summary>Seconds until the next press would be honoured; 0 when it would be honoured now.
    /// </summary>
    public double RemainingSec(double nowSec) =>
        double.IsNegativeInfinity(_lastAcceptedAt)
            ? 0
            : System.Math.Max(0, _lastAcceptedAt + CooldownSec - nowSec);

    /// <summary>Take a press at <paramref name="nowSec"/>. True exactly once per cooldown window;
    /// the caller resets only when this returns true.</summary>
    public bool TryPress(double nowSec)
    {
        if (RemainingSec(nowSec) > 0)
        {
            Refused++;
            return false;
        }
        _lastAcceptedAt = nowSec;
        Accepted++;
        return true;
    }
}
