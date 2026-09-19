using System.IO;
using System.Text;
using Godot;

namespace MpFoundation.Net;

/// <summary>
/// Structured, leveled, timestamped logging for the dedicated match server, written to
/// both a size-rolled file and stdout. Covers the security-relevant events the spec
/// requires an operator to be able to diagnose after the fact (connects, disconnects
/// and why, rejections, malformed input, unhandled errors) without a live repro.
/// </summary>
public static class ServerLog
{
    private const long MaxBytes = 2 * 1024 * 1024;
    private const int Backups = 2;

    private static readonly object Gate = new();
    private static string _path = "";
    private static long _written;

    /// <summary>Resolves a log directory ("user://..." or a real path) and opens the log file.
    /// A pre-existing log rotates into the backup chain so THIS server session starts a fresh
    /// file: appending across sessions let the test harness's log assertions match lines an
    /// EARLIER server wrote (stale-evidence false greens — the shared matchserver.log was the
    /// most plausible source of both false passes and residual marathon flakes), and rotating
    /// rather than truncating keeps the previous session available for post-mortems.</summary>
    public static void Init(string logDir, string fileName)
    {
        lock (Gate)
        {
            string dir = logDir.StartsWith("user://") || logDir.StartsWith("res://")
                ? ProjectSettings.GlobalizePath(logDir)
                : logDir;
            Directory.CreateDirectory(dir);
            _path = Path.Combine(dir, fileName);
            try
            {
                if (File.Exists(_path) && new FileInfo(_path).Length > 0)
                {
                    _written = MaxBytes + 1; // force the rotation path
                    Roll(0);
                }
            }
            catch { /* rotation is best-effort; worst case we append as before */ }
            _written = File.Exists(_path) ? new FileInfo(_path).Length : 0;
        }
    }

    public static void Info(string message, string? kv = null) => Write("INFO", message, kv);
    public static void Warn(string message, string? kv = null) => Write("WARN", message, kv);
    public static void Error(string message, string? kv = null) => Write("ERROR", message, kv);

    private static void Write(string level, string message, string? kv)
    {
        string ts = System.DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        var sb = new StringBuilder(128);
        sb.Append(ts).Append(' ').Append(level).Append(" [server] ").Append(message);
        if (!string.IsNullOrEmpty(kv))
            sb.Append(' ').Append(kv);
        string line = sb.ToString();

        // stdout too, so the harness can also gate on lines like "[server] listening".
        if (level == "ERROR")
            GD.PrintErr(line);
        else
            GD.Print(line);

        lock (Gate)
        {
            if (_path.Length == 0)
                return;
            try
            {
                // Count what actually lands on disk: UTF-8 bytes + the 1-byte "\n" (counting
                // UTF-16 chars undercounts multi-byte content like Steam persona names). And
                // if the file vanished externally, restart the counter with it.
                if (!File.Exists(_path))
                    _written = 0;
                int bytes = System.Text.Encoding.UTF8.GetByteCount(line) + 1;
                Roll(bytes);
                File.AppendAllText(_path, line + "\n");
                _written += bytes;
            }
            catch
            {
                // Logging must never take the server down; drop the line if the disk misbehaves.
            }
        }
    }

    private static void Roll(int incoming)
    {
        if (_written + incoming <= MaxBytes || !File.Exists(_path))
            return;
        for (int i = Backups; i >= 1; i--)
        {
            string src = i == 1 ? _path : $"{_path}.{i - 1}";
            string dst = $"{_path}.{i}";
            if (File.Exists(src))
            {
                File.Delete(dst);
                File.Move(src, dst);
            }
        }
        _written = 0;
    }
}
