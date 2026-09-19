using System;
using System.Collections.Generic;
using Godot;

namespace MpFoundation;

/// <summary>
/// In-process network-condition simulator (--net-sim &lt;latencyMs&gt;,&lt;lossPct&gt;,&lt;jitterMs&gt;).
/// When active on a client, the avatar routes every outgoing input send and every
/// incoming snapshot delivery through <see cref="Queue"/>, which drops a percentage and
/// delays the rest by latency + random jitter before executing them. Latency is per
/// message direction (one-way), so `--net-sim 80,...` yields an effective 160 ms RTT.
/// Jitter can reorder deliveries — deliberately, since real UDP does too; the netcode's
/// sequence/tick filtering is exactly what this exercises. This is what makes the
/// prediction/reconciliation/interpolation suites provable headless in CI.
/// </summary>
public partial class NetSim : Node
{
    /// <summary>Set while a net-sim node is in the tree; null in normal play (zero overhead).</summary>
    public static NetSim? Instance { get; private set; }

    private readonly double _latencyMs;
    private readonly double _lossPct;
    private readonly double _jitterMs;
    private readonly RandomNumberGenerator _rng = new();
    private readonly List<(double DueMs, Action Deliver)> _pending = new();

    public NetSim(LaunchOptions options)
    {
        Name = "NetSim";
        _latencyMs = options.NetSimLatencyMs;
        _lossPct = options.NetSimLossPct;
        _jitterMs = options.NetSimJitterMs;
        _rng.Randomize();
    }

    public override void _EnterTree()
    {
        Instance = this;
        GD.Print($"[netsim] active: latency={_latencyMs}ms loss={_lossPct}% jitter={_jitterMs}ms (one-way, per message)");
    }

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
        _pending.Clear();
    }

    /// <summary>Drops or schedules one message delivery. The action must self-guard
    /// against its target having left the tree by the time it fires.</summary>
    public void Queue(Action deliver)
    {
        if (_lossPct > 0 && _rng.RandfRange(0f, 100f) < _lossPct)
            return; // lost on the wire
        double due = Time.GetTicksMsec() + _latencyMs + (_jitterMs > 0 ? _rng.RandfRange(0f, (float)_jitterMs) : 0);
        _pending.Add((due, deliver));
    }

    public override void _Process(double delta)
    {
        if (_pending.Count == 0)
            return;
        double now = Time.GetTicksMsec();
        // Fire everything due, in due-time order (jitter may have queued them out of order).
        for (int i = 0; i < _pending.Count;)
        {
            if (_pending[i].DueMs > now)
            {
                i++;
                continue;
            }
            // Earliest-due first among the ready ones, so causality within a burst holds.
            int earliest = i;
            for (int j = i + 1; j < _pending.Count; j++)
            {
                if (_pending[j].DueMs <= now && _pending[j].DueMs < _pending[earliest].DueMs)
                    earliest = j;
            }
            Action deliver = _pending[earliest].Deliver;
            _pending.RemoveAt(earliest);
            deliver();
        }
    }
}
