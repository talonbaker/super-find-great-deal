namespace MpFoundation.Net;

/// <summary>
/// Client-side desync detector that needs no wire change: it watches the reconciliation
/// correction magnitude the owner already computes every snapshot (predicted-vs-authoritative
/// distance at the acked input) and distinguishes a SUSTAINED divergence — the two simulations
/// genuinely disagreeing — from the ordinary one-off corrections that packet loss and jitter
/// produce constantly and harmlessly.
///
/// A single large correction is normal (a dropped input, a hitch). A large correction that
/// repeats for many consecutive snapshots means prediction and authority have actually drifted
/// apart — a real bug (a nondeterministic Step, an unsynced input, a physics divergence) that is
/// otherwise invisible because each individual correction "looks like latency". <see cref="Observe"/>
/// returns true exactly once per divergence episode so the caller can log it without spamming.
///
/// Pure logic (no Godot dependency) so it is unit-testable and reusable by any predicted entity.
/// </summary>
public sealed class DesyncMonitor
{
    private readonly float _thresholdM;
    private readonly int _sustainedCount;
    private int _consecutive;
    private bool _reported;

    /// <param name="thresholdM">Correction distance (metres) that counts as "large". Should sit
    /// above the steady-state correction a healthy connection produces under loss/jitter.</param>
    /// <param name="sustainedCount">Consecutive large corrections before it's called a desync.</param>
    public DesyncMonitor(float thresholdM = 1.0f, int sustainedCount = 10)
    {
        _thresholdM = thresholdM;
        _sustainedCount = sustainedCount;
    }

    /// <summary>Feed one reconciliation correction magnitude. Returns true on the single frame a
    /// sustained divergence is first detected (so the caller logs once); false otherwise.</summary>
    public bool Observe(float correctionM)
    {
        if (correctionM >= _thresholdM)
        {
            _consecutive++;
            if (_consecutive >= _sustainedCount && !_reported)
            {
                _reported = true;
                return true; // first detection of this episode
            }
        }
        else
        {
            // Recovered — the divergence closed; arm for the next episode.
            _consecutive = 0;
            _reported = false;
        }
        return false;
    }

    /// <summary>Consecutive large corrections observed (for logging context).</summary>
    public int ConsecutiveCount => _consecutive;
}
