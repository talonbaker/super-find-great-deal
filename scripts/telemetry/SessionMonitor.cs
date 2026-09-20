using System;
using System.IO;
using Godot;

namespace MpFoundation.Telemetry;

/// <summary>
/// Crash detection, unified into one "pending report, resolved at next boot" mechanism
/// (spec decision 6):
///  - A managed C# exception (AppDomain.UnhandledException / TaskScheduler.UnobservedTaskException)
///    is caught in-process and its message + stack are written SYNCHRONOUSLY to a pending-crash
///    file — no network call while the process may be dying.
///  - A session-active marker is written at boot and cleared on clean quit. If the marker is
///    still present at the next boot, the previous session died without a clean quit — evidence
///    of an engine-level crash no C# handler could catch (native/GPU/hard freeze); that path has
///    no stack trace, only hardware/build info + the tail of the previous log.
/// BootCheck() reports which (if either) signal is present, preferring the detailed file.
///
/// Writes go through System.IO on globalized paths so they are synchronous and independent of
/// the Godot main loop, which cannot be trusted mid-crash.
/// </summary>
public static class SessionMonitor
{
    public enum CrashSignal { None, Detail, NoDetail }

    private static bool _hooksInstalled;

    // Globalized paths are cached (keyed on the test seam's Root) and their directory is created
    // eagerly, so the crash-time write path is pure System.IO — ProjectSettings/DirAccess cannot
    // be trusted from a finalizer thread or a dying process.
    private static string? _markerAbs, _pendingAbs, _cachedRoot;

    private static string MarkerAbs { get { EnsureCrashTimePaths(); return _markerAbs!; } }
    private static string PendingAbs { get { EnsureCrashTimePaths(); return _pendingAbs!; } }

    private static void EnsureCrashTimePaths()
    {
        if (_cachedRoot == TelemetryPaths.Root && _markerAbs != null)
            return;
        _cachedRoot = TelemetryPaths.Root;
        _markerAbs = ProjectSettings.GlobalizePath(TelemetryPaths.MarkerFile);
        _pendingAbs = ProjectSettings.GlobalizePath(TelemetryPaths.PendingCrashFile);
        try { Directory.CreateDirectory(Path.GetDirectoryName(_pendingAbs)!); } catch { /* best-effort */ }
    }

    /// <summary>Writes the "a session is running" marker (cleared only on a clean quit).</summary>
    public static void WriteSessionMarker()
    {
        TelemetryPaths.EnsureDirs();
        try { File.WriteAllText(MarkerAbs, DateTime.UtcNow.ToString("o")); }
        catch (Exception e) { GD.PushWarning($"[telemetry] could not write session marker: {e.Message}"); }
    }

    /// <summary>Clears the marker — called at clean quit, and after a crash is resolved.</summary>
    public static void ClearSessionMarker()
    {
        try { if (File.Exists(MarkerAbs)) File.Delete(MarkerAbs); } catch { /* best-effort */ }
    }

    /// <summary>Classifies the previous session: a detailed pending-crash file wins; otherwise a
    /// leftover marker means an unclean shutdown; otherwise no crash.</summary>
    public static CrashSignal BootCheck()
    {
        if (File.Exists(PendingAbs))
            return CrashSignal.Detail;
        if (File.Exists(MarkerAbs))
            return CrashSignal.NoDetail;
        return CrashSignal.None;
    }

    /// <summary>Synchronously records a managed exception's message + stack (crash-time write).
    /// Pure System.IO after path caching; JSON is hand-escaped because Godot's Json class may not
    /// be callable from the thread a crash lands on.</summary>
    public static void WritePendingCrash(string message, string stackTrace)
    {
        string json = "{\"exception_message\":" + JsonQuote(message ?? "")
            + ",\"stack_trace\":" + JsonQuote(stackTrace ?? "") + "}";
        try { File.WriteAllText(PendingAbs, json); }
        catch
        {
            // Nothing safe to do from a dying process; GD logging may itself fault here.
        }
    }

    private static string JsonQuote(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length + 2);
        sb.Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    /// <summary>Reads the pending-crash file's fields; false when absent/unreadable.</summary>
    public static bool ReadPendingCrash(out string? message, out string? stackTrace)
    {
        message = null;
        stackTrace = null;
        if (!File.Exists(PendingAbs))
            return false;
        try
        {
            var parsed = Json.ParseString(File.ReadAllText(PendingAbs));
            if (parsed.VariantType != Variant.Type.Dictionary)
                return false;
            var dict = parsed.AsGodotDictionary();
            message = dict.TryGetValue("exception_message", out Variant m) ? m.AsString() : null;
            stackTrace = dict.TryGetValue("stack_trace", out Variant s) ? s.AsString() : null;
            return true;
        }
        catch { return false; }
    }

    /// <summary>Deletes the pending-crash file so a crash is never asked about twice.</summary>
    public static void ClearPendingCrash()
    {
        try { if (File.Exists(PendingAbs)) File.Delete(PendingAbs); } catch { /* best-effort */ }
    }

    /// <summary>Last <paramref name="maxLines"/> lines of the PREVIOUS session's log. Godot
    /// rotates user://logs/godot.log at process start, so by the time the crash prompt runs,
    /// godot.log belongs to the *current* session — the crashed session's lines live in the
    /// newest rotated godot_*.log. Reads only the file's tail (never the whole log into memory)
    /// and redacts identifying tokens (SteamID64s, lobby ids, room codes) so the uploaded tail
    /// honors the dialog's "nothing identifying" promise. Null when absent; never throws.</summary>
    public static string? ReadLogTail(int maxLines)
    {
        try
        {
            string dirAbs = ProjectSettings.GlobalizePath("user://logs");
            if (!Directory.Exists(dirAbs))
                return null;

            // Prefer the newest rotated log (the crashed session); fall back to the live one.
            string? logAbs = null;
            DateTime newest = DateTime.MinValue;
            foreach (string f in Directory.GetFiles(dirAbs, "godot_*.log"))
            {
                DateTime t = File.GetLastWriteTimeUtc(f);
                if (t > newest) { newest = t; logAbs = f; }
            }
            logAbs ??= Path.Combine(dirAbs, "godot.log");
            if (!File.Exists(logAbs))
                return null;

            // Tail-read: last 64 KB is far more than 60 lines ever needs.
            const int TailBytes = 64 * 1024;
            using var fs = new FileStream(logAbs, FileMode.Open, System.IO.FileAccess.Read, FileShare.ReadWrite);
            if (fs.Length > TailBytes)
                fs.Seek(-TailBytes, SeekOrigin.End);
            using var reader = new StreamReader(fs);
            string tailText = reader.ReadToEnd();

            string[] lines = tailText.Split('\n');
            int take = Math.Min(maxLines, lines.Length);
            string joined = string.Join("\n", lines[(lines.Length - take)..]).TrimEnd();
            return RedactIdentifiers(joined);
        }
        catch { return null; }
    }

    /// <summary>Strips identifying values out of anything bound for a crash report. Public
    /// because it must be applied to the exception message and stack trace too, not only to the
    /// log tail — see Telemetry.EnqueueCrashReport. Deliberately over-broad: this text is
    /// uploaded, so a false redaction costs a little diagnostic detail while a missed one costs
    /// a player their privacy.</summary>
    public static string RedactIdentifiers(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;
        // SteamID64s and lobby ids.
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\b\d{15,20}\b", "[id]");
        // Room codes. The "code" token is OPTIONAL here because the one line that actually
        // prints a room code (SteamLobby's `lobby N up (room QRTKMW key a91f3e04 -> …)`) never
        // says "code" — the previous pattern required it and so never fired on the only site it
        // existed for. The `key …` token on that same line is the DERIVED directory key, which is
        // public by construction and deliberately left intact.
        // A room code is a join capability now (NetworkManager.ExpectedRoomCode), so leaking one
        // hands over entry, not just a label.
        text = System.Text.RegularExpressions.Regex.Replace(
            text, @"(?i)(\broom(?:[ _-]?code)?\W{0,3})[A-Z0-9]{4,8}\b", "$1[redacted]");
        // Peer addresses. NetworkManager no longer emits them — its ServerLog lines carry a
        // per-session LogIdentity token instead — but this rule stays: a crash tail can come from
        // a log rotated by an earlier build, and the engine and Steamworks log addresses in
        // shapes this game does not author. The bare-IPv4 rule below is the field-name-agnostic
        // backstop, and is the one that matters most on the direct/LAN transport, where the
        // address is a real remote IP belonging to someone who never consented to anything here.
        text = System.Text.RegularExpressions.Regex.Replace(text, @"(?i)\bip=\S+", "ip=[redacted]");
        text = System.Text.RegularExpressions.Regex.Replace(
            text, @"\b(?:\d{1,3}\.){3}\d{1,3}\b", "[ip]");
        // User-profile paths. Every path this game builds is a globalized "user://", which on
        // Windows expands under C:\Users\<account>\ — and an account name is very often the
        // player's real name. Any file-IO exception message carries it verbatim.
        text = System.Text.RegularExpressions.Regex.Replace(
            text, @"(?i)([A-Z]:\\Users\\)[^\\/""'\s]+", "$1[user]");
        text = System.Text.RegularExpressions.Regex.Replace(
            text, @"(?i)(/(?:home|Users)/)[^/""'\s]+", "$1[user]");
        return text;
    }

    /// <summary>Installs the managed-exception hooks (once). Each writes a pending-crash file so
    /// the report is offered at the next boot. Idempotent.</summary>
    public static void InstallCrashHooks()
    {
        if (_hooksInstalled)
            return;
        _hooksInstalled = true;

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                WritePendingCrash($"{ex.GetType().Name}: {ex.Message}", ex.StackTrace ?? "");
            else
                WritePendingCrash("Unhandled non-CLS exception", "");
        };

        // Deliberately NOT written as a pending crash: an unobserved task fault surfaces at GC
        // time on the finalizer thread while the game keeps running fine — writing the crash file
        // here made the NEXT boot tell the player "the game crashed" (and upload an unrelated
        // stack) for a session that ended cleanly. Log it locally instead.
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            args.SetObserved(); // recorded below; don't escalate
            try { GD.PushWarning($"[telemetry] unobserved task exception: {args.Exception.GetType().Name}: {args.Exception.Message}"); }
            catch { /* finalizer thread — logging is best-effort */ }
        };
    }
}
