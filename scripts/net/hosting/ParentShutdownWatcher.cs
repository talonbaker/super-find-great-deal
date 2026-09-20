using System;
using System.Threading;
using Godot;

namespace MpFoundation.Net.Hosting;

/// <summary>
/// The child half of <see cref="LocalServerHost"/>'s graceful-shutdown handshake. A
/// parent-managed dedicated server (spawned with a redirected stdin and
/// <c>--parent-managed</c>) watches that stdin on a background thread. The parent never
/// writes to it — closing the pipe is the signal, surfacing as an EOF from
/// <see cref="System.IO.TextReader.ReadLine"/>. On that EOF the child requests a clean
/// <see cref="SceneTree.Quit()"/> on the main thread, so it runs its normal teardown
/// (NetworkManager._ExitTree → SteamService.Shutdown → SteamGameServer.LogOff).
///
/// Why this exists: hosting spawns the match server as a child and, on leave, the host used
/// to <see cref="System.Diagnostics.Process.Kill(bool)"/> it. A hard kill skips the log-off,
/// so Steam's backend keeps the anonymous game-server session registered until its heartbeat
/// times out (~1-2 min). Re-hosting inside that window fails GameServer.Init with CantCreate
/// ("ports in use"), and the new child exits before it can print its listening line — the
/// "server exited during startup" the host reports. Logging off cleanly frees the session at
/// once. The parent's job object still hard-reaps a child that ignores the signal.
/// </summary>
public static class ParentShutdownWatcher
{
    private static Thread? _thread;

    /// <summary>Idempotent. Call once from the dedicated-server boot path when
    /// <c>--parent-managed</c> was passed (Boot).</summary>
    public static void Start()
    {
        if (_thread is not null)
            return;
        _thread = new Thread(Watch) { IsBackground = true, Name = "parent-shutdown-watch" };
        _thread.Start();
    }

    private static void Watch()
    {
        try
        {
            // Blocks until the parent closes its end of our stdin, at which point ReadLine
            // returns null. (The parent sends no data; any line it did send is ignored.)
            while (Console.In.ReadLine() is not null)
            {
            }
        }
        catch
        {
            // Pipe faulted — treat it the same as the parent going away and shut down.
        }
        GD.Print("[server] parent closed control channel — shutting down");
        Callable.From(RequestQuit).CallDeferred();
    }

    private static void RequestQuit()
    {
        if (Engine.GetMainLoop() is SceneTree tree)
            tree.Quit();
    }
}
