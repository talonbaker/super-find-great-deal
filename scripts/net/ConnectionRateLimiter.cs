using System.Collections.Generic;

namespace MpFoundation.Net;

/// <summary>
/// Per-source sliding-window limiter for inbound connection attempts. Blunts trivial
/// connect-flood DoS without a database or timers. Thresholds are deliberately generous:
/// the project's own harness spins up ~10 bots from 127.0.0.1 in quick succession and
/// must never be rate-limited, so the default window comfortably clears legitimate local
/// testing while still capping a genuine flood.
///
/// Generic in the key as of the 2026-08-07 perf followups. Connection-level use keys on a
/// STRING identity (a remote IP, or "steam:&lt;id64&gt;") and is served by the non-generic
/// <see cref="ConnectionRateLimiter"/> below, unchanged. The voice relay keys on an INT peer
/// id and runs on every inbound voice packet — 300/sec at six talkers — where
/// <c>peerId.ToString()</c> was allocating a string per packet purely to index this table.
/// Same window semantics either way; only the key type moves.
/// </summary>
public class ConnectionRateLimiter<TKey> where TKey : notnull
{
    // Every Nth call sweeps the whole table for entries whose window has fully expired.
    // Allow() only ever trims the *current* source's queue, so a source that connects
    // once and never returns (e.g. a one-off legitimate client on a long-lived dedicated
    // server) would otherwise leave a permanent empty-eventually-but-never-removed entry
    // in _hits. A periodic full sweep bounds that growth without paying an O(n) cost on
    // every single call.
    private const int SweepInterval = 128;

    private readonly int _maxPerWindow;
    private readonly double _windowSec;
    private readonly Dictionary<TKey, Queue<double>> _hits = new();
    private int _callsSinceSweep;

    public ConnectionRateLimiter(int maxPerWindow = 40, double windowSec = 10.0)
    {
        _maxPerWindow = maxPerWindow;
        _windowSec = windowSec;
    }

    /// <summary>Records an attempt from <paramref name="source"/> at <paramref name="nowSec"/>; false if it exceeds the window budget.</summary>
    public bool Allow(TKey source, double nowSec)
    {
        if (!_hits.TryGetValue(source, out Queue<double>? window))
        {
            window = new Queue<double>();
            _hits[source] = window;
        }

        double cutoff = nowSec - _windowSec;
        TrimWindow(window, cutoff);

        bool allowed;
        if (window.Count >= _maxPerWindow)
        {
            allowed = false;
        }
        else
        {
            window.Enqueue(nowSec);
            allowed = true;
        }

        if (++_callsSinceSweep >= SweepInterval)
        {
            _callsSinceSweep = 0;
            SweepStaleEntries(cutoff);
        }

        return allowed;
    }

    private static void TrimWindow(Queue<double> window, double cutoff)
    {
        while (window.Count > 0 && window.Peek() < cutoff)
            window.Dequeue();
    }

    /// <summary>Trims every tracked source's window and drops any that are now empty.</summary>
    private void SweepStaleEntries(double cutoff)
    {
        List<TKey>? toRemove = null;
        foreach (KeyValuePair<TKey, Queue<double>> entry in _hits)
        {
            TrimWindow(entry.Value, cutoff);
            if (entry.Value.Count == 0)
                (toRemove ??= new List<TKey>()).Add(entry.Key);
        }

        if (toRemove == null)
            return;
        foreach (TKey source in toRemove)
            _hits.Remove(source);
    }
}

/// <summary>The string-keyed limiter, which is what connection-level identity actually is
/// (a remote IP, or "steam:&lt;id64&gt;" — see NetworkManager.RemoteAddressFor). Kept as its own
/// name rather than making every call site spell <c>ConnectionRateLimiter&lt;string&gt;</c>: this is
/// the original type, its behaviour is byte-for-byte unchanged, and nothing that used it
/// moved.</summary>
public sealed class ConnectionRateLimiter : ConnectionRateLimiter<string>
{
    public ConnectionRateLimiter(int maxPerWindow = 40, double windowSec = 10.0)
        : base(maxPerWindow, windowSec)
    {
    }
}
