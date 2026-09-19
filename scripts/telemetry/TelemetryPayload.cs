using System;
using System.Globalization;
using Godot;

namespace MpFoundation.Telemetry;

/// <summary>
/// Builds the flat report dictionaries the queue stores and the client sends. The common
/// envelope (device id, build, hardware/OS snapshot, locale) is shared by all three types;
/// each builder appends its own fields. All hardware/OS probes are defensive so payload
/// construction never throws — including under --headless, where some display/GPU probes
/// return empty (the CI self-test asserts shape, not populated values).
/// </summary>
public static class TelemetryPayload
{
    /// <param name="stopSeconds">LD-2 (research §A2-R3): seconds the local body spent grounded,
    /// below the stop threshold and not in a menu, over the session. A duration, nothing
    /// identifying.</param>
    /// <param name="stopSecondsPerMinute">The same, normalised over the session's length — the
    /// number every level change after this one reports the delta of.</param>
    /// <param name="stopSecondsBySection">Stop-seconds keyed by level-section name (bubbletest's
    /// <c>BubbleTestLayout.Section</c>); empty in a world with no sections. Level geography, not
    /// player geography.</param>
    public static Godot.Collections.Dictionary BuildUsage(
        int sessionLengthSec, string role, string transport, int playerCountSeen, bool voiceUsed,
        int propsGrabbed, int propsThrown, int propsDropped,
        float stopSeconds, float stopSecondsPerMinute,
        Godot.Collections.Dictionary stopSecondsBySection)
    {
        var d = Envelope("usage");
        d["session_length_sec"] = sessionLengthSec;
        d["role"] = role;
        d["transport"] = transport;
        d["player_count_seen"] = playerCountSeen;
        d["voice_used"] = voiceUsed;
        d["props_grabbed"] = propsGrabbed;
        d["props_thrown"] = propsThrown;
        d["props_dropped"] = propsDropped;
        d["stop_seconds"] = stopSeconds;
        d["stop_seconds_per_minute"] = stopSecondsPerMinute;
        d["stop_seconds_by_section"] = stopSecondsBySection;
        return d;
    }

    public static Godot.Collections.Dictionary BuildCrash(
        string? stackTrace, string? exceptionMessage, string? logTail, string detection)
    {
        var d = Envelope("crash");
        d["stack_trace"] = NullableString(stackTrace);
        d["exception_message"] = NullableString(exceptionMessage);
        d["log_tail"] = NullableString(logTail);
        d["detection"] = detection;
        return d;
    }

    /// <summary>Builds a feedback report: the structured one-tap answers (spec §7, decision 6,
    /// trimmed by the survey rework to overall/performance — clarity and tide_fairness were
    /// dropped) plus the optional free-text box, in the same {report_type: "feedback"} envelope.
    /// Additive over the original {text}-only shape: a key
    /// appears only when that question was actually answered/filled — an omitted key is how
    /// "unanswered" is represented, never a sentinel value. When nothing was answered and the
    /// text box was empty (or on Skip), this reproduces the original {envelope, text-omitted}
    /// shape exactly, so older readers built against the {text}-only schema still work.</summary>
    /// <summary>The half of the envelope disclosed only under usage consent. Single source of
    /// truth: BuildFeedback strips it at construction time, PendingQueue.ApplyRevokedConsent
    /// strips it from anything already queued when consent is withdrawn. Two copies of this list
    /// would drift, and the drift would be silent and one-directional — toward sending more.</summary>
    public static readonly string[] HardwareEnvelopeKeys =
        { "os_version", "cpu", "gpu", "ram_mb", "resolution", "renderer", "locale", "region" };

    public static Godot.Collections.Dictionary BuildFeedback(Godot.Collections.Dictionary answers, string text)
    {
        var d = Envelope("feedback");
        // The feedback panel promises anonymous answers/text and never asked for hardware
        // consent — only players whose usage reporting is ON (which discloses the hardware
        // snapshot) ship the full envelope; a player who opted out sends feedback without it.
        if (!TelemetryStore.UsageReportingEnabled)
        {
            foreach (string key in HardwareEnvelopeKeys)
                d.Remove(key);
        }
        if (answers.Count > 0)
            d["answers"] = answers;
        if (!string.IsNullOrEmpty(text))
            d["text"] = text;
        return d;
    }

    private static Godot.Collections.Dictionary Envelope(string reportType)
    {
        var d = new Godot.Collections.Dictionary
        {
            { "device_id", TelemetryStore.DeviceId },
            { "report_type", reportType },
            { "sent_at", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) },
            { "build_version", TelemetryConfig.BuildVersion },
            { "build_kind", OS.IsDebugBuild() ? "dev" : "release" },
            { "os", Safe(OS.GetName) },
            { "os_version", Safe(OS.GetVersion) },
            { "cpu", Safe(OS.GetProcessorName) },
            { "gpu", Safe(() => RenderingServer.GetVideoAdapterName()) },
            { "ram_mb", RamMb() },
            { "resolution", Resolution() },
            { "renderer", Safe(() => (string)ProjectSettings.GetSetting("rendering/renderer/rendering_method", "")) },
            { "locale", Safe(OS.GetLocale) },
            { "region", Region() },
        };
        return d;
    }

    // A null nullable field is stored as a nil Variant so ToFirestoreBody emits nullValue.
    private static Variant NullableString(string? s) => s is null ? new Variant() : s;

    private static string Safe(Func<string> probe)
    {
        try { return probe() ?? ""; } catch { return ""; }
    }

    private static int RamMb()
    {
        try
        {
            Godot.Collections.Dictionary info = OS.GetMemoryInfo();
            if (info.TryGetValue("physical", out Variant physical))
            {
                long bytes = physical.AsInt64();
                return bytes > 0 ? (int)(bytes / (1024 * 1024)) : 0;
            }
        }
        catch { /* fall through */ }
        return 0;
    }

    private static string Resolution()
    {
        try
        {
            Vector2I size = DisplayServer.WindowGetSize();
            return size.X > 0 && size.Y > 0 ? $"{size.X}x{size.Y}" : "";
        }
        catch { return ""; }
    }

    // "en_US" -> "US"; empty when the locale has no region part.
    private static string Region()
    {
        string locale = Safe(OS.GetLocale);
        int us = locale.IndexOf('_');
        if (us < 0)
            return "";
        string tail = locale[(us + 1)..];
        int dot = tail.IndexOfAny(new[] { '.', '@' });
        return dot < 0 ? tail : tail[..dot];
    }
}
