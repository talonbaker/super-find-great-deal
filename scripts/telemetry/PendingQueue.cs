using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Godot;

namespace MpFoundation.Telemetry;

/// <summary>
/// A durable, on-disk queue of unsent reports (spec decision 11). Every report is written here
/// first so a failed/timed-out/offline send loses nothing; a best-effort flush runs at boot and
/// at quit. No retry loop or backoff — the scale (3–4 testers) does not justify it; a failed
/// file simply waits for the next boot. Invisible to the player.
///
/// Each queue entry is one file holding the FLAT report JSON (device_id, report_type, envelope
/// + type-specific fields). The flush hands that JSON to the injected send delegate, which the
/// real client turns into a Firestore POST and the self-test stubs.
/// </summary>
public static class PendingQueue
{
    // Caps against unbounded disk growth when sends fail permanently (misconfigured backend,
    // corporate proxy): oldest-first eviction past the file cap, and stale files are dropped at
    // flush time rather than retried forever. Filenames sort chronologically by construction.
    private const int MaxQueueFiles = 100;
    internal const int MaxAgeDays = 30;

    /// <summary>The filename timestamp's format. <b>Always rendered and parsed against
    /// <see cref="CultureInfo.InvariantCulture"/></b> — see <see cref="Enqueue"/>.</summary>
    internal const string StampFormat = "yyyyMMddHHmmssfff";

    private static string QueueAbs => ProjectSettings.GlobalizePath(TelemetryPaths.QueueDir);

    /// <summary>Writes a flat report to the queue (durable). Uses a high-resolution, collision-
    /// resistant filename so multiple reports enqueued in the same moment never clobber.
    ///
    /// <para><b>The timestamp is formatted with the invariant culture, and that is load-bearing.</b>
    /// <c>$"{DateTime.UtcNow:yyyyMMddHHmmssfff}"</c> renders through
    /// <see cref="CultureInfo.CurrentCulture"/>, whose <c>Calendar</c> is not the Gregorian one
    /// everywhere: under <c>th-TH</c> the Buddhist calendar writes the year as 2569 and under
    /// <c>ar-SA</c> the Umm al-Qura calendar writes 1448. Both still sort, but neither sorts
    /// against the files a Gregorian-calendar boot wrote — so <see cref="TrimToCap"/>'s ordinal
    /// sort would evict the wrong file (a th-TH name sorts newest of all, so the cap would eat the
    /// genuinely recent reports first), and the age check below could not parse the stamp at all.
    /// A Thai tester's queue is exactly the queue you most want intact.</para></summary>
    public static void Enqueue(Godot.Collections.Dictionary flatReport)
    {
        TelemetryPaths.EnsureDirs();
        string name = DateTime.UtcNow.ToString(StampFormat, CultureInfo.InvariantCulture)
            + "-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".json";
        try { File.WriteAllText(Path.Combine(QueueAbs, name), Json.Stringify(flatReport)); }
        catch (Exception e) { GD.PushWarning($"[telemetry] could not enqueue report: {e.Message}"); }
        TrimToCap();
    }

    /// <summary>Applies a revoked (or never-granted) usage consent to everything ALREADY queued.
    /// Usage reports are deleted outright. Every other report keeps its own explicit prompt, but
    /// is rewritten WITHOUT the hardware/locale snapshot — the same envelope half BuildFeedback
    /// strips at construction time.
    ///
    /// The previous version deleted usage files and deliberately left the rest alone, so a
    /// feedback report queued while consent was Granted and then stranded by a failed flush still
    /// uploaded its CPU/GPU/RAM/resolution/locale/region AFTER the player revoked. Consent has to
    /// govern the data at send time, not only at build time, because the queue outlives the
    /// decision that filled it.
    ///
    /// Parses the JSON rather than substring-matching "report_type":"usage": that match worked
    /// only because Json.Stringify happens to emit space-free output, so a serializer change
    /// would have silently disabled revocation everywhere at once.</summary>
    public static void ApplyRevokedConsent()
    {
        foreach (string file in QueueFiles())
        {
            try
            {
                Variant parsed = Json.ParseString(File.ReadAllText(file));
                if (parsed.VariantType != Variant.Type.Dictionary)
                    continue;
                Godot.Collections.Dictionary d = parsed.AsGodotDictionary();
                string type = d.TryGetValue("report_type", out Variant t) ? t.AsString() : "";
                if (type == "usage")
                {
                    File.Delete(file);
                    continue;
                }
                bool changed = false;
                foreach (string key in TelemetryPayload.HardwareEnvelopeKeys)
                    changed |= d.Remove(key);
                if (changed)
                    File.WriteAllText(file, Json.Stringify(d));
            }
            catch { /* unreadable/locked — retried on the next revoke or flush */ }
        }
    }

    private static void TrimToCap()
    {
        try
        {
            string[] files = QueueFiles();
            if (files.Length <= MaxQueueFiles)
                return;
            Array.Sort(files, StringComparer.Ordinal); // timestamp-prefixed names: oldest first
            for (int i = 0; i < files.Length - MaxQueueFiles; i++)
                try { File.Delete(files[i]); } catch { /* best-effort */ }
        }
        catch { /* best-effort */ }
    }

    /// <summary>Number of reports currently waiting to be sent.</summary>
    public static int PendingCount() => QueueFiles().Length;

    /// <summary>
    /// <b>How old a queued file is</b>, from its own filename stamp — the same
    /// <see cref="StampFormat"/> <see cref="Enqueue"/> writes, parsed invariantly and as UTC.
    ///
    /// <para>The name is preferred over the filesystem's timestamps because it is the only clock
    /// that survives the things that reset those: a profile copied to a new machine, a backup
    /// restore, and — the live one — <see cref="ApplyRevokedConsent"/>, which REWRITES every
    /// non-usage file in place and would otherwise hand a two-year-old report a fresh 30-day
    /// lease every time consent was revoked.</para>
    ///
    /// <para>An unparseable name returns <c>null</c> and the file is <b>kept</b>. That is the
    /// deliberate direction to fail in: the only names that cannot parse are ones this build did
    /// not write — a hand-dropped file, or one from the pre-fix culture-sensitive format (a th-TH
    /// boot's "2569…" is a real date but not one <see cref="StampFormat"/> accepts as this era) —
    /// and silently deleting a tester's report because its name is unfamiliar is a worse failure
    /// than keeping it until the file cap evicts it.</para>
    /// </summary>
    private static TimeSpan? AgeOf(string file, DateTime utcNow)
    {
        string name = Path.GetFileNameWithoutExtension(file) ?? "";
        int dash = name.IndexOf('-');
        string stamp = dash >= 0 ? name.Substring(0, dash) : name;
        if (!DateTime.TryParseExact(stamp, StampFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime written))
            return null;
        return utcNow - written;
    }

    /// <summary>Best-effort flush: hands each queued report's JSON to <paramref name="send"/>;
    /// deletes it on success, leaves it on failure. Returns how many were sent+deleted. One pass,
    /// no retries.
    ///
    /// <para><b>Files older than <see cref="MaxAgeDays"/> are dropped before the send, not
    /// retried.</b> The class comment has claimed that since the queue shipped and nothing
    /// implemented it — <c>MaxAgeDays</c> was declared and never read (2026-08-30 master review,
    /// F12) — so a permanently-failing backend kept up to <see cref="MaxQueueFiles"/> reports
    /// being re-POSTed at every boot forever, and month-old hardware/locale envelopes stayed on a
    /// tester's disk indefinitely. A dropped file is NOT counted in the return value: that number
    /// means "reached the backend", and folding evictions into it would make a queue that is
    /// silently rotting read as a queue that is draining.</para></summary>
    public static async Task<int> FlushAsync(Func<string, Task<bool>> send)
    {
        int sent = 0;
        DateTime utcNow = DateTime.UtcNow;
        foreach (string file in QueueFiles())
        {
            TimeSpan? age = AgeOf(file, utcNow);
            if (age is { TotalDays: > MaxAgeDays })
            {
                try { File.Delete(file); } catch { /* best-effort; retried next flush */ }
                continue;
            }

            string json;
            try { json = File.ReadAllText(file); }
            catch { continue; } // unreadable now; try again next boot
            bool ok;
            try { ok = await send(json); }
            catch { ok = false; }
            if (!ok)
                continue;
            try { File.Delete(file); sent++; } catch { /* left for next boot */ }
        }
        return sent;
    }

    private static string[] QueueFiles()
    {
        try
        {
            return Directory.Exists(QueueAbs)
                ? Directory.GetFiles(QueueAbs, "*.json")
                : Array.Empty<string>();
        }
        catch { return Array.Empty<string>(); }
    }
}
