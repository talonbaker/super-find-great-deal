using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Godot;

namespace MpFoundation.Net.Hosting;

/// <summary>
/// Spawns the dedicated match server as a child of the HOSTING PLAYER'S OWN CLIENT — the
/// client-local relocation of the retired matchmaking provisioner's spawn logic
/// (matchmaking/Provisioning/LocalProcessProvisioner.cs). Practice Mode's "one real
/// dedicated server as a child process" pattern, upgraded with the three guarantees the
/// provisioner had that fire-and-forget <c>OS.CreateProcess</c> can't give:
///
///  - <b>Readiness</b>: the child's own existing stdout line
///    (<c>"[server] listening via Steam relay as steam:&lt;id64&gt;"</c> over Steam,
///    <c>"[server] listening on udp/&lt;port&gt;"</c> over ENet) is the IPC — the parent
///    learns the child's real, post-logon address with no second registration channel.
///  - <b>Orphan prevention</b>: every child is assigned to a Windows job object with
///    kill-on-close, so a hard-killed host client can never strand a server process.
///  - <b>Crash visibility</b>: the child's exit raises <see cref="Exited"/> (threadpool
///    thread — marshal before touching the scene tree) and both pipes drain to a log
///    file, so a chatty child can't deadlock and a dead one can be diagnosed.
/// </summary>
public sealed class LocalServerHost : IDisposable
{
    public readonly record struct SpawnOutcome(bool Ok, string Address, string Error);

    private static readonly Regex SteamReady = new(@"\[server\] listening via Steam relay as (steam:\d+)", RegexOptions.Compiled);
    private static readonly Regex EnetReady = new(@"\[server\] listening on udp/(\d+)", RegexOptions.Compiled);

    private readonly WindowsJobObject _job = new();
    private readonly TaskCompletionSource<string> _ready =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Process? _process;
    private StreamWriter? _log;
    private readonly object _logGate = new();
    private bool _disposed;

    /// <summary>The child's connectable address ("steam:&lt;id64&gt;" or "127.0.0.1:&lt;port&gt;"),
    /// valid once <see cref="StartAsync"/> returned Ok.</summary>
    public string Address { get; private set; } = "";

    public int Pid => _process?.Id ?? 0;
    public bool HasExited => _process is null || _process.HasExited;

    /// <summary>Raised when the child exits, with its exit code. Fires on a threadpool
    /// thread — do not touch the scene tree from the handler without deferring.</summary>
    public event Action<int>? Exited;

    /// <summary>
    /// Spawns the dedicated server child and waits for its listening line.
    /// <paramref name="transport"/> is "steam" for real hosted matches ("enet" keeps the
    /// spawn/readiness/reap machinery exercisable in CI, where no Steam exists).
    /// <paramref name="steamAppId"/> is forwarded to the child (already resolved by the
    /// caller via SteamService.ResolveAppId — the single documented resolution path).
    /// Call on the main thread (reads Godot project/executable paths).
    /// </summary>
    public async Task<SpawnOutcome> StartAsync(
        string transport, string world, uint steamAppId, string roomCode = "", double timeoutSec = 45.0)
    {
        if (_process is not null)
            return new SpawnOutcome(false, "", "server already started");

        int port = FindFreeUdpPort();
        string exe = OS.GetExecutablePath();
        string logDir = ProjectSettings.GlobalizePath("user://logs");
        Directory.CreateDirectory(logDir);

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // Our end of the child's stdin is the graceful-shutdown control channel: closing
            // it (Stop) is the EOF the child's ParentShutdownWatcher waits for. See Stop().
            RedirectStandardInput = true,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("--headless");
        // Server-side control args are passed after "--" below; --parent-managed (there)
        // arms the child's stdin shutdown watcher. The engine flags come first.
        // In dev the executable is the Godot binary and needs the project path; an exported
        // build IS the project (feature tag "template") and self-locates its pack.
        if (!OS.HasFeature("template"))
        {
            psi.ArgumentList.Add("--path");
            psi.ArgumentList.Add(ProjectSettings.GlobalizePath("res://"));
        }
        psi.ArgumentList.Add("--");
        psi.ArgumentList.Add("--server");
        psi.ArgumentList.Add("--parent-managed");
        psi.ArgumentList.Add("--port");
        psi.ArgumentList.Add(port.ToString());
        psi.ArgumentList.Add("--transport");
        psi.ArgumentList.Add(transport);
        psi.ArgumentList.Add("--world");
        psi.ArgumentList.Add(world);
        psi.ArgumentList.Add("--log-dir");
        psi.ArgumentList.Add(Path.Combine(logDir, "hosted-server"));
        if (roomCode.Length > 0)
        {
            // The server enforces this against every joiner's handshake, so a scraped host id
            // can't be joined without the code (NetworkManager.ExpectedRoomCode).
            psi.ArgumentList.Add("--room-code");
            psi.ArgumentList.Add(roomCode);
        }
        if (steamAppId != 0)
        {
            psi.ArgumentList.Add("--steam-app-id");
            psi.ArgumentList.Add(steamAppId.ToString());
        }

        // Cycle dev hooks, forwarded to the child. The day/night clock is SERVER-authoritative
        // (CycleDriver.Setup takes isServer; clients receive the phase), so --cycle-period and
        // --cycle-start-phase given to the process a HUMAN launches reach only the client half and
        // do nothing whatever to the clock. The symptom is the worst kind: the flags are accepted,
        // the client-side ones visibly work, and the session still runs a full 720s day starting
        // at dawn — so it reads as "that flag is broken", or is simply never noticed.
        //
        // Only forwarded when explicitly set (the same "0 = unset" sentinel the rest of
        // LaunchOptions uses), so every CI spawn and every ordinary Host press builds a child
        // command line byte-identical to before. --cycle-start-day rides along to keep the
        // server's own presentation-side day index agreeing with the client's; it is
        // presentation-only on both (see LaunchOptions.CycleStartDay) and never touches the
        // replicated CyclesElapsed.
        LaunchOptions cycle = NetworkManager.Instance.Options;
        if (cycle.CyclePeriodSec > 0)
        {
            psi.ArgumentList.Add("--cycle-period");
            psi.ArgumentList.Add(cycle.CyclePeriodSec.ToString(CultureInfo.InvariantCulture));
        }
        if (cycle.CycleStartPhase > 0f)
        {
            psi.ArgumentList.Add("--cycle-start-phase");
            psi.ArgumentList.Add(cycle.CycleStartPhase.ToString(CultureInfo.InvariantCulture));
        }
        if (cycle.CycleStartDay > 0)
        {
            psi.ArgumentList.Add("--cycle-start-day");
            psi.ArgumentList.Add(cycle.CycleStartDay.ToString(CultureInfo.InvariantCulture));
        }
        // STYLE-4: the freeze rides the same only-when-set forwarding, for the exact reason the
        // block above spells out. It is decided on the AUTHORITY — CycleDriver stops its own
        // elapsed time (CycleDriver.Setup takes isServer) — so given to the process a human
        // launches it reaches only the client half and does nothing at all, while looking
        // accepted. Note --cycle-start-phase above is forwarded ALREADY RESOLVED (LaunchOptions
        // turns "noon" into a number before this runs), so the child never has to agree with the
        // parent about what a name means.
        if (cycle.CycleFreeze)
            psi.ArgumentList.Add("--cycle-freeze");
        // (The flow/quota server hooks that rode the same only-when-set forwarding as the cycle
        // flags above went with the quota spine at the fork - BASE-1, 2026-09-19. The rule they
        // were here for still binds: a server-side flag passed to the process a HUMAN launches
        // reaches only the client half and silently does nothing, so anything the server must see
        // is appended here or it does not exist.)

        // --authored-clips is DELIBERATELY NOT forwarded (ANIM-M3), for the same reason
        // --night-brightness is not: this child is launched --headless and renders nothing, so
        // there is no pose on it to drive. It is presentation only and it decides nothing, so a
        // host whose own process has it on and whose server child does not is not a disagreement
        // about anything — which is the parity law's whole point. If it is ever forwarded, that is
        // a change that needs a reason, not a tidy-up.

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        // Drain both pipes into one log file (a chatty child must never fill a pipe and
        // stall) while watching for the readiness line.
        var log = new StreamWriter(Path.Combine(logDir, "hosted-server.out.log"), append: false)
        { AutoFlush = true };
        process.OutputDataReceived += (_, e) => OnChildLine(e.Data);
        process.ErrorDataReceived += (_, e) => OnChildLine(e.Data);
        process.Exited += (_, _) => OnChildExited();

        try
        {
            if (!process.Start())
            {
                log.Dispose();
                return new SpawnOutcome(false, "", "failed to start the server process");
            }
        }
        catch (Exception ex)
        {
            log.Dispose();
            return new SpawnOutcome(false, "", $"server spawn threw {ex.GetType().Name}");
        }

        _process = process;
        _log = log;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _job.Assign(process);
        GD.Print($"[host] spawned dedicated server pid {process.Id} (transport {transport})");

        Task<string> ready = _ready.Task;
        Task first = await Task.WhenAny(ready, Task.Delay(TimeSpan.FromSeconds(timeoutSec)));
        if (first != ready)
        {
            // Failed spawns release their resources here — callers that drop the instance
            // after a failed outcome must not leak the log handle or Process until finalization.
            Dispose();
            return new SpawnOutcome(false, "", "the server did not become ready in time");
        }
        // A child that died during startup completes the TCS with "" (see OnChildExited).
        string address = ready.Result;
        if (address.Length == 0)
        {
            Dispose();
            return new SpawnOutcome(false, "", "the server exited during startup — see logs/hosted-server.out.log");
        }

        Address = address;
        return new SpawnOutcome(true, address, "");
    }

    private void OnChildLine(string? line)
    {
        if (line is null)
            return;
        lock (_logGate)
        {
            try { _log?.WriteLine(line); } catch { /* disposed during shutdown */ }
        }
        if (_ready.Task.IsCompleted)
            return;

        Match steam = SteamReady.Match(line);
        if (steam.Success)
        {
            _ready.TrySetResult(steam.Groups[1].Value);
            return;
        }
        Match enet = EnetReady.Match(line);
        if (enet.Success)
            _ready.TrySetResult($"127.0.0.1:{enet.Groups[1].Value}");
    }

    private void OnChildExited()
    {
        int code = 0;
        try { code = _process?.ExitCode ?? 0; } catch { /* process handle already gone */ }
        _ready.TrySetResult(""); // startup waiter (if any) learns the spawn failed
        if (!_disposed)
            Exited?.Invoke(code);
    }

    /// <summary>How long a voluntary Stop waits for the child to exit on its own after the
    /// control channel closes, before force-killing it. A clean quit (which runs the Steam
    /// log-off) is normally sub-second; this only bites if the child is wedged.</summary>
    private const int GracefulStopMs = 3000;

    /// <summary>Stops the child (if alive) without disposing the job object. Asks it to shut
    /// down GRACEFULLY first — closing our end of its stdin is the EOF its
    /// <see cref="ParentShutdownWatcher"/> waits for, letting it log off its Steam
    /// game-server session (which a hard kill would orphan, blocking an immediate re-host —
    /// see that class). Force-kills only as a fallback if the child ignores the signal.</summary>
    public void Stop()
    {
        _disposed = true; // voluntary stop — suppress the crash event
        Process? proc = _process;
        if (proc is null)
            return;
        try
        {
            if (proc.HasExited)
                return;

            // Close the control channel (EOF → the child quits cleanly and logs off Steam).
            try { proc.StandardInput.Close(); }
            catch { /* no redirected stdin, or the child already went away */ }

            if (proc.WaitForExit(GracefulStopMs))
            {
                GD.Print($"[host] dedicated server pid {proc.Id} shut down gracefully");
                return;
            }

            // Ignored the signal (wedged mid-startup, etc.) — fall back to force so we never
            // leak the child; the job object is the last-resort backstop beyond this.
            proc.Kill(entireProcessTree: true);
            GD.Print($"[host] dedicated server pid {proc.Id} did not exit in time; force-killed");
        }
        catch
        {
            // Already gone — that's the outcome we wanted.
        }
    }

    public void Dispose()
    {
        Stop();
        lock (_logGate)
        {
            try { _log?.Dispose(); } catch { /* draining callbacks may race disposal */ }
            _log = null;
        }
        _process?.Dispose();
        _process = null;
        _job.Dispose(); // kill-on-close reaps anything that slipped through
    }

    private static int FindFreeUdpPort()
    {
        using var udp = new System.Net.Sockets.UdpClient(0);
        return ((System.Net.IPEndPoint)udp.Client.LocalEndPoint!).Port;
    }
}
