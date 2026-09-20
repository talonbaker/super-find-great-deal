using System.IO;
using System.Text.Json;
using Godot;

namespace MpFoundation.Net;

/// <summary>
/// Server-side bandwidth instrumentation (--net-stats &lt;path&gt;). One JSON object per second
/// on the dedicated server describing what actually crossed the wire in that second.
///
/// WHY THIS EXISTS. The 2026-08-07 perf audit's entire network budget (§2.2) was ARITHMETIC:
/// payload sizes read from NetCodec, rates read from NetProfile, and a 20 B/message framing
/// constant the audit itself flagged as the number it was least sure of. Nothing in the repo
/// had ever measured a byte. This is the measurement, and it is the thing that makes every
/// claim about the voice proximity gate falsifiable instead of merely plausible.
///
/// WHERE THE NUMBERS COME FROM. <c>ENetConnection.PopStatistic</c> — the transport's own
/// counters, below Godot's RPC layer and below our own packing, so they include ENet command
/// overhead and UDP payload exactly as ENet emitted it. PopStatistic READS AND RESETS, so each
/// line is a delta over the interval, not a cumulative total; the cumulative fields are
/// accumulated here from those deltas. NOTE the naming trap the audit already hit: Godot's C#
/// bindings strip ENet's <c>HOST_TOTAL_</c> prefix, so it is <c>HostStatistic.SentData</c>, not
/// <c>TotalSentData</c>.
///
/// WHAT IT CANNOT SEE. The Steam transport (SteamPeer) does not go through ENet at all, so this
/// logger is silent under --transport steam and says so once. Everything measured here is
/// therefore the ENet/LAN/CI path — which is the path every headless test uses, and the right
/// one for a relative before/after, but it is NOT a measurement of the shipping Steam-relay
/// byte count.
///
/// POSITIVE CONTROL. A counter that reports "less traffic" is worthless until it has been shown
/// to report "more traffic" — this repo has shipped diagnostics that silently never matched
/// anything. tests/Run-NetStatsTest.ps1 runs the same scenario three ways (gate off / gate on
/// with peers apart / gate on with peers together) and requires the middle cell to be the only
/// low one. If the counter were stuck, all three would read alike and the suite fails.
/// </summary>
public partial class NetStatsLogger : Node
{
    public const string NodeName = "NetStatsLogger";

    private const double IntervalSec = 1.0;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private StreamWriter? _writer;
    private double _since;
    private double _elapsed;
    private long _cumSent;
    private long _cumRecv;
    private bool _warnedNoEnet;

    /// <summary>Opens the log. Call once, server side, right after the transport is listening
    /// (the peer must already exist for the very first sample to mean anything).</summary>
    public void Setup(string path)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        _writer = new StreamWriter(File.Open(path, FileMode.Create, System.IO.FileAccess.Write, FileShare.Read));
        GD.Print($"[net-stats] logging to {path} (interval {IntervalSec:F0}s)");
    }

    public override void _ExitTree()
    {
        _writer?.Dispose();
        _writer = null;
    }

    public override void _Process(double delta)
    {
        if (_writer == null)
            return;
        _elapsed += delta;
        _since += delta;
        if (_since < IntervalSec)
            return;
        double interval = _since;
        _since = 0;
        WriteSample(interval);
    }

    private void WriteSample(double intervalSec)
    {
        long sent = 0, recv = 0, sentPkts = 0, recvPkts = 0;
        if (Multiplayer.MultiplayerPeer is ENetMultiplayerPeer enet && enet.Host != null)
        {
            ENetConnection host = enet.Host;
            sent = (long)host.PopStatistic(ENetConnection.HostStatistic.SentData);
            recv = (long)host.PopStatistic(ENetConnection.HostStatistic.ReceivedData);
            sentPkts = (long)host.PopStatistic(ENetConnection.HostStatistic.SentPackets);
            recvPkts = (long)host.PopStatistic(ENetConnection.HostStatistic.ReceivedPackets);
        }
        else if (!_warnedNoEnet)
        {
            _warnedNoEnet = true;
            GD.Print("[net-stats] transport is not ENet — byte counters will read 0 for this run");
        }
        _cumSent += sent;
        _cumRecv += recv;

        (long relayed, long gated, long paExempt) = Voice.VoiceManager.Instance.TakeRelayCounters();
        int peers = Multiplayer.HasMultiplayerPeer() ? Multiplayer.GetPeers().Length : 0;

        var sample = new Sample(
            (long)(_elapsed * 1000), System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            System.Math.Round(intervalSec, 3), peers,
            sent, recv, sentPkts, recvPkts, _cumSent, _cumRecv,
            // Bits per second on the wire, upstream, over this interval — the one number §2.2
            // predicted and the one Talon's "can my friend host?" question turns on.
            System.Math.Round(sent * 8.0 / intervalSec, 1),
            VoiceProximityGate.Enabled, relayed, gated, paExempt);
        _writer!.WriteLine(JsonSerializer.Serialize(sample, JsonOptions));
        _writer.Flush();
    }

    // Sent/Recv are BYTES over this interval (ENet's own counters, read-and-reset); CumSent/
    // CumRecv accumulate them since process start. UpBps is the derived upstream bit rate.
    // Relayed/Gated/PaExempt are voice packets the relay passed, dropped, and passed for being
    // on the PA — Gated is 0 by construction whenever Gate is false.
    private sealed record Sample(long T, long Wall, double IntervalSec, int Peers,
        long Sent, long Recv, long SentPkts, long RecvPkts, long CumSent, long CumRecv,
        double UpBps,
        bool Gate, long Relayed, long Gated, long PaExempt);
}
