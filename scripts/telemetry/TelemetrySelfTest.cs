using System.Collections.Generic;
using Godot;

namespace MpFoundation.Telemetry;

/// <summary>
/// Headless pure-logic checks of the telemetry module (TelemetryStore, SessionMonitor,
/// PendingQueue flush, payload construction) with TelemetryClient's real HTTP send stubbed —
/// CI must never hit the live Firestore project. Same CI-safe split this project already uses
/// for Steam/reconnect logic. Run via --telemetry-self-test (Run-TelemetryTest.ps1); exits 0/1.
/// </summary>
public static class TelemetrySelfTest
{
    private static readonly List<string> Failures = new();

    public static int Run()
    {
        return RunAsync().GetAwaiter().GetResult();
    }

    private static async System.Threading.Tasks.Task<int> RunAsync()
    {
        // Isolate every telemetry file under a scratch root so a dev's real settings/state
        // are never touched, and start from a clean slate.
        TelemetryPaths.ResetForTests("user://telemetry_selftest", "user://telemetry_selftest/settings.cfg");
        TelemetryPaths.Wipe();
        TelemetryPaths.EnsureDirs();

        try
        {
            ConsentPersistsAcrossRestart();
            ReportingIsOnByDefault();
            OptOutSurvivesLosingTheConfig();
            DeviceIdIsStable();
            SessionMarkerLifecycleAndCrashClassification();
            ManagedExceptionWritesPendingCrash();
            await QueueFlushSendsAndDeletesOnSuccessLeavesOnFailure();
            await StaleQueueFilesAreDroppedAtFlush();
            EnqueueFilenamesAreCultureInvariant();
            PayloadConstructionMatchesSchema();
            RedactionCoversWhatTheGameActuallyLogs();
        }
        catch (System.Exception ex)
        {
            // An unhandled throw here used to escape all the way out through Boot._Ready(),
            // where Godot logs it and the headless process then sits forever with nothing left
            // to quit it — the harness could only report "did not exit in time", 60s later,
            // naming neither the assertion nor the exception. Recording it as an ordinary
            // failure turns a silent hang into a named, immediate, non-zero exit.
            Failures.Add($"unhandled exception: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
        }
        finally
        {
            TelemetryPaths.Wipe();
        }

        if (Failures.Count == 0)
        {
            GD.Print("[telemetry-selftest] PASS");
            return 0;
        }
        foreach (string failure in Failures)
            GD.PrintErr($"[telemetry-selftest] FAIL: {failure}");
        return 1;
    }

    // usage_consent survives a simulated restart: set it, drop in-memory state, reload, read back.
    private static void ConsentPersistsAcrossRestart()
    {
        TelemetryStore.Load();
        TelemetryStore.SetUsageConsent(true);
        Check(TelemetryStore.UsageConsent == TelemetryStore.Consent.Granted, "consent: expected Granted after grant");

        TelemetryStore.Load(); // simulate a fresh process reading the persisted file
        Check(TelemetryStore.UsageConsent == TelemetryStore.Consent.Granted, "consent: Granted did not persist across reload");

        TelemetryStore.SetUsageConsent(false);
        TelemetryStore.Load();
        Check(TelemetryStore.UsageConsent == TelemetryStore.Consent.Denied, "consent: Denied did not persist across reload");
    }

    // ON BY DEFAULT (Talon, 2026-08-30 note 15) — and the POSITIVE CONTROL for the check below.
    // "The opt-out survived" is worth nothing from an instrument that cannot show reporting ON, so
    // this asserts the enabled state first, from a profile with no stored answer at all.
    private static void ReportingIsOnByDefault()
    {
        WipeSettingsFile();
        TelemetryPaths.Wipe();
        TelemetryPaths.EnsureDirs();
        TelemetryStore.Load();
        Check(TelemetryStore.UsageConsent == TelemetryStore.Consent.Unset,
            "default: a fresh profile must have no stored answer (Unset)");
        Check(TelemetryStore.UsageReportingEnabled,
            "default: a fresh profile must have usage reporting ON");
    }

    // THE ONE TALON WROTE TWICE: "Once they disable this setting, please do not reenable it on
    // them restarting the game."
    //
    // With reporting on by default, LOSING the stored state is what re-enables it — so the check
    // that matters is not "does a decline persist" (it did before) but "does a decline survive the
    // settings file going away". Both loss modes are exercised: deleted outright, and corrupted so
    // badly that LoadSharedConfig moves it aside and every section restarts from defaults, which
    // is the exact path that used to land back on Unset.
    private static void OptOutSurvivesLosingTheConfig()
    {
        TelemetryStore.Load();
        TelemetryStore.SetUsageConsent(false);
        Check(!TelemetryStore.UsageReportingEnabled, "opt-out: reporting must be off after declining");
        Check(Godot.FileAccess.FileExists(TelemetryPaths.OptOutMarkerFile),
            "opt-out: declining must write the durable marker");

        // 1. The settings file is deleted.
        WipeSettingsFile();
        TelemetryStore.Load();
        Check(TelemetryStore.UsageConsent == TelemetryStore.Consent.Denied,
            "opt-out: a DELETED settings.cfg re-enabled usage reporting");
        Check(!TelemetryStore.UsageReportingEnabled,
            "opt-out: reporting came back on after the settings file was deleted");

        // 2. The settings file is unparseable. LoadSharedConfig backs it up and resets, so this is
        //    the state-loss path with a file still on disk.
        string abs = ProjectSettings.GlobalizePath(TelemetryPaths.SettingsFile);
        System.IO.File.WriteAllText(abs, "this is not a ConfigFile [[[ not a section header");
        TelemetryStore.Load();
        Check(TelemetryStore.UsageConsent == TelemetryStore.Consent.Denied,
            "opt-out: a CORRUPTED settings.cfg re-enabled usage reporting");

        // 3. And the marker is not a one-way trap: an explicit opt back IN clears it, or a player
        //    who changed their mind would be silently stuck off.
        TelemetryStore.SetUsageConsent(true);
        Check(!Godot.FileAccess.FileExists(TelemetryPaths.OptOutMarkerFile),
            "opt-out: opting back in must delete the durable marker");
        TelemetryStore.Load();
        Check(TelemetryStore.UsageReportingEnabled, "opt-out: opting back in did not persist");

        // Leave the scratch profile clean for the sections that follow.
        WipeSettingsFile();
        TelemetryPaths.Wipe();
        TelemetryPaths.EnsureDirs();
        TelemetryStore.Load();
    }

    private static void WipeSettingsFile()
    {
        string abs = ProjectSettings.GlobalizePath(TelemetryPaths.SettingsFile);
        try { System.IO.File.Delete(abs); } catch { /* absent is the desired state */ }
        try { System.IO.File.Delete(abs + ".corrupt"); } catch { /* best-effort */ }
    }

    // A device id is generated once and stays identical across reloads (per-tester identity).
    private static void DeviceIdIsStable()
    {
        TelemetryStore.Load();
        string first = TelemetryStore.DeviceId;
        Check(first.Length > 0, "device id: expected a non-empty id");
        TelemetryStore.Load();
        Check(TelemetryStore.DeviceId == first, "device id: expected the same id across reloads");
    }

    // The session-active marker is written at boot and cleared on clean quit; a boot that
    // still sees the marker (and no detailed file) is classified as an unclean-shutdown crash.
    private static void SessionMarkerLifecycleAndCrashClassification()
    {
        SessionMonitor.ClearSessionMarker();
        SessionMonitor.ClearPendingCrash();

        // Clean prior session: marker written then cleared -> no crash next boot.
        SessionMonitor.WriteSessionMarker();
        SessionMonitor.ClearSessionMarker();
        Check(SessionMonitor.BootCheck() == SessionMonitor.CrashSignal.None,
            "boot check: a cleanly-cleared marker must classify as None");

        // Unclean prior session: marker left behind, no detail file -> NoDetail crash.
        SessionMonitor.WriteSessionMarker();
        Check(SessionMonitor.BootCheck() == SessionMonitor.CrashSignal.NoDetail,
            "boot check: a leftover marker with no detail must classify as NoDetail");

        // Detailed file present -> Detail wins even if the marker is also present.
        SessionMonitor.WritePendingCrash("boom", "at Foo()");
        Check(SessionMonitor.BootCheck() == SessionMonitor.CrashSignal.Detail,
            "boot check: a pending-crash file must classify as Detail (prefer detail)");

        SessionMonitor.ClearPendingCrash();
        SessionMonitor.ClearSessionMarker();
        Check(SessionMonitor.BootCheck() == SessionMonitor.CrashSignal.None,
            "boot check: fully cleared state must classify as None");
    }

    // The managed-exception path writes a pending-crash file carrying the message + stack that
    // ReadPendingCrash reads back (the fields a crash report is built from).
    private static void ManagedExceptionWritesPendingCrash()
    {
        SessionMonitor.ClearPendingCrash();
        SessionMonitor.WritePendingCrash("NullReferenceException: x", "at A()\n at B()");

        bool found = SessionMonitor.ReadPendingCrash(out string? message, out string? stack);
        Check(found, "pending crash: expected the written file to be readable");
        Check(message == "NullReferenceException: x", $"pending crash: message round-trip, got '{message}'");
        Check(stack == "at A()\n at B()", "pending crash: stack round-trip mismatch");

        SessionMonitor.ClearPendingCrash();
        Check(!SessionMonitor.ReadPendingCrash(out _, out _), "pending crash: clear must remove the file");
    }

    // The flush sends each queued report and deletes it on success, but leaves it queued when the
    // (stubbed) send fails — so an offline quit loses nothing and retries next boot.
    private static async System.Threading.Tasks.Task QueueFlushSendsAndDeletesOnSuccessLeavesOnFailure()
    {
        // Start empty.
        while (PendingQueue.PendingCount() > 0)
            await PendingQueue.FlushAsync(_ => System.Threading.Tasks.Task.FromResult(true));

        PendingQueue.Enqueue(new Godot.Collections.Dictionary { { "report_type", "feedback" }, { "text", "a" } });
        PendingQueue.Enqueue(new Godot.Collections.Dictionary { { "report_type", "usage" }, { "session_length_sec", 5 } });
        Check(PendingQueue.PendingCount() == 2, $"queue: expected 2 pending, got {PendingQueue.PendingCount()}");

        // Failing send leaves everything queued.
        int sentOnFailure = await PendingQueue.FlushAsync(_ => System.Threading.Tasks.Task.FromResult(false));
        Check(sentOnFailure == 0, $"queue: a failing send must delete nothing, deleted {sentOnFailure}");
        Check(PendingQueue.PendingCount() == 2, "queue: failed flush must leave all reports queued");

        // Succeeding send drains and deletes.
        int sentOnSuccess = await PendingQueue.FlushAsync(_ => System.Threading.Tasks.Task.FromResult(true));
        Check(sentOnSuccess == 2, $"queue: a successful flush must send both, sent {sentOnSuccess}");
        Check(PendingQueue.PendingCount() == 0, "queue: successful flush must empty the queue");
    }

    // MRF-C / master-review F12. PendingQueue.MaxAgeDays was declared and never read, so the class
    // comment's "stale files are dropped at flush time rather than retried forever" was simply
    // false: a permanently-failing backend re-POSTed month-old reports at every boot, forever.
    //
    // Both halves are asserted in one stream because the FRESH file is the stale file's positive
    // control. "The queue emptied" would pass just as well against a flush that deleted everything;
    // what has to be true is that the old one goes and the new one stays, in the same pass.
    private static async System.Threading.Tasks.Task StaleQueueFilesAreDroppedAtFlush()
    {
        while (PendingQueue.PendingCount() > 0)
            await PendingQueue.FlushAsync(_ => System.Threading.Tasks.Task.FromResult(true));

        string queueDir = ProjectSettings.GlobalizePath(TelemetryPaths.QueueDir);
        string Name(System.DateTime utc) =>
            utc.ToString(PendingQueue.StampFormat, System.Globalization.CultureInfo.InvariantCulture)
            + "-" + System.Guid.NewGuid().ToString("N") + ".json";

        // One a day past the cap, one a day inside it. Written directly rather than through
        // Enqueue, which can only stamp "now".
        string stale = System.IO.Path.Combine(queueDir,
            Name(System.DateTime.UtcNow.AddDays(-(PendingQueue.MaxAgeDays + 1))));
        string fresh = System.IO.Path.Combine(queueDir,
            Name(System.DateTime.UtcNow.AddDays(-(PendingQueue.MaxAgeDays - 1))));
        System.IO.File.WriteAllText(stale, "{\"report_type\":\"usage\"}");
        System.IO.File.WriteAllText(fresh, "{\"report_type\":\"usage\"}");
        Check(PendingQueue.PendingCount() == 2, $"queue age: expected 2 seeded, got {PendingQueue.PendingCount()}");

        // The send FAILS, so nothing can be removed by the ordinary success path — every deletion
        // this pass makes is the age rule's.
        int sent = await PendingQueue.FlushAsync(_ => System.Threading.Tasks.Task.FromResult(false));
        Check(sent == 0, $"queue age: an evicted file must not count as sent, returned {sent}");
        Check(!System.IO.File.Exists(stale),
            $"queue age: a file older than {PendingQueue.MaxAgeDays} days survived the flush");
        Check(System.IO.File.Exists(fresh),
            "queue age: the flush evicted a file INSIDE the age cap — the rule is over-eager");

        // An unparseable name is kept, not deleted: the only names that cannot parse are ones this
        // build did not write, and dropping a tester's report for being unfamiliar is the worse
        // failure of the two.
        string odd = System.IO.Path.Combine(queueDir, "hand-dropped.json");
        System.IO.File.WriteAllText(odd, "{\"report_type\":\"usage\"}");
        await PendingQueue.FlushAsync(_ => System.Threading.Tasks.Task.FromResult(false));
        Check(System.IO.File.Exists(odd), "queue age: an unparseable filename must be kept, not dropped");

        System.IO.File.Delete(fresh);
        System.IO.File.Delete(odd);
    }

    // MRF-C / master-review F12, the other half. Enqueue's filename used
    // $"{DateTime.UtcNow:yyyyMMddHHmmssfff}", which renders through CurrentCulture's CALENDAR — so
    // a th-TH boot writes the year 2569 and an ar-SA boot 1448. Those names still sort among
    // themselves, but not against Gregorian ones, and TrimToCap sorts ordinally: a th-TH name sorts
    // ABOVE every Gregorian name, so the cap evicts the newest reports and keeps the oldest.
    //
    // The test parks a real non-Gregorian culture on the thread for the duration. Its own positive
    // control is the first Check: it renders the SAME instant the old way and requires the two to
    // differ. Without that, a machine in globalization-invariant mode (where "th-TH" silently
    // resolves to something Gregorian) would run this test green while exercising nothing at all.
    private static void EnqueueFilenamesAreCultureInvariant()
    {
        System.Globalization.CultureInfo previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("th-TH");
            var probe = new System.DateTime(2026, 8, 30, 12, 0, 0, System.DateTimeKind.Utc);
            string cultured = $"{probe:yyyyMMddHHmmssfff}";
            string invariant = probe.ToString(PendingQueue.StampFormat,
                System.Globalization.CultureInfo.InvariantCulture);
            Check(cultured != invariant,
                "queue culture: POSITIVE CONTROL FAILED — this run's th-TH culture rendered the "
                + $"Gregorian year ({cultured}), so the test could not have caught the defect. "
                + "Globalization-invariant mode, or a th-TH without the Buddhist calendar.");

            while (PendingQueue.PendingCount() > 0)
                PendingQueue.FlushAsync(_ => System.Threading.Tasks.Task.FromResult(true))
                    .GetAwaiter().GetResult();

            PendingQueue.Enqueue(new Godot.Collections.Dictionary { { "report_type", "usage" } });
            string[] files = System.IO.Directory.GetFiles(
                ProjectSettings.GlobalizePath(TelemetryPaths.QueueDir), "*.json");
            Check(files.Length == 1, $"queue culture: expected 1 enqueued file, got {files.Length}");
            if (files.Length != 1)
                return;

            string stamp = System.IO.Path.GetFileNameWithoutExtension(files[0]).Split('-')[0];
            Check(System.DateTime.TryParseExact(stamp, PendingQueue.StampFormat,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal
                        | System.Globalization.DateTimeStyles.AdjustToUniversal,
                    out System.DateTime parsed),
                $"queue culture: filename stamp '{stamp}' is not an invariant timestamp");
            Check(parsed.Year == System.DateTime.UtcNow.Year,
                $"queue culture: filename year {parsed.Year} is not the Gregorian year "
                + $"{System.DateTime.UtcNow.Year} — the current culture's calendar leaked in");

            System.IO.File.Delete(files[0]);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    // Crash-report redaction, asserted against the REAL strings this game emits rather than
    // against invented ones. This test exists because the original room-code rule required a
    // literal "code" token while the only line that ever prints a room code says "room QRTKMW" —
    // so the redactor looked correct, read correctly, and matched nothing. A redactor is worth
    // exactly the log lines it has been pinned against; when a new identifier starts being
    // logged, add its literal line here.
    private static void RedactionCoversWhatTheGameActuallyLogs()
    {
        // Verbatim from SteamLobby.CreateForMatchAsync. Re-pinned when that line gained the
        // derived-key prefix; the room code is still printed there, so the room-code rule still
        // has a live site to defend and this is still a real assertion rather than a museum piece.
        string lobby = SessionMonitor.RedactIdentifiers(
            "[steam] lobby 109775241234567890 up (room QRTKMW key a91f3e04 -> steam:76561198012345678)");
        Check(!lobby.Contains("QRTKMW"), $"redact: room code survived the lobby line -> {lobby}");
        Check(!lobby.Contains("76561198012345678"), $"redact: SteamID64 survived -> {lobby}");
        Check(!lobby.Contains("109775241234567890"), $"redact: lobby id survived -> {lobby}");
        // The directory key is public by construction (it IS the index entry) and must survive:
        // it is the only token that makes a derivation-skew failure diagnosable from a crash tail.
        Check(lobby.Contains("a91f3e04"), $"redact: over-redacted, lost the directory key -> {lobby}");

        // Verbatim shape from NetworkManager's eight ServerLog call sites. Those now emit a
        // per-session LogIdentity token rather than the address itself, so this asserts the
        // property that actually ships: the line carries nothing to redact.
        string peer = SessionMonitor.RedactIdentifiers(
            "rate limit: connection rejected from=3f9a2b1c peer=1284461039");
        Check(peer.Contains("3f9a2b1c"), $"redact: over-redacted the pseudonymous peer token -> {peer}");

        // The ip= rule has no live emitter after that change, but it is NOT dead: a crash tail
        // can be read from a log rotated by an EARLIER build, and the engine and third-party
        // code log addresses in shapes this game does not control. Pinned against a real
        // pre-change line so the rule keeps being exercised rather than quietly rotting.
        string legacyPeer = SessionMonitor.RedactIdentifiers(
            "rate limit: connection rejected ip=203.0.113.44 peer=1284461039");
        Check(!legacyPeer.Contains("203.0.113.44"), $"redact: peer IPv4 survived -> {legacyPeer}");

        // The backstop that does not depend on any field name: a bare IPv4 anywhere in the tail.
        string bareIp = SessionMonitor.RedactIdentifiers("connection reset by 198.51.100.7 during handshake");
        Check(!bareIp.Contains("198.51.100.7"), $"redact: bare IPv4 survived -> {bareIp}");

        // A Windows user-profile path, as any file-IO exception message carries it. The account
        // name is very often the player's real name, so this is the highest-value rule here.
        string winPath = SessionMonitor.RedactIdentifiers(
            @"Could not find a part of the path 'C:\Users\jennifer.okafor\AppData\Roaming\Godot\app_userdata\mp-foundation\telemetry\queue'.");
        Check(!winPath.Contains("jennifer.okafor"), $"redact: Windows account name survived -> {winPath}");
        Check(winPath.Contains("AppData"), $"redact: over-redacted, lost the diagnostic path -> {winPath}");

        string nixPath = SessionMonitor.RedactIdentifiers(
            "IOException: /home/jokafor/.local/share/godot/app_userdata/mp-foundation/logs/godot.log");
        Check(!nixPath.Contains("jokafor"), $"redact: Linux account name survived -> {nixPath}");

        // Must not throw or mangle on the trivial inputs.
        Check(SessionMonitor.RedactIdentifiers("") == "", "redact: empty input must round-trip");
        Check(SessionMonitor.RedactIdentifiers("nothing to redact here").Contains("nothing"),
            "redact: clean text must survive intact");
    }

    // Each report type's flat payload carries the common envelope + its type-specific fields with
    // the right value types, and converts to a well-formed Firestore typed body.
    private static void PayloadConstructionMatchesSchema()
    {
        TelemetryStore.Load();
        // Pin the consent state this section builds under instead of inheriting whatever the
        // earlier checks happened to leave on disk (ConsentPersistsAcrossRestart ends on
        // Denied). Feedback's envelope is consent-dependent, so an inherited value silently
        // changes what these assertions mean — the exact coupling that made this a hang.
        TelemetryStore.SetUsageConsent(true);

        // The envelope splits in two. The identity/build half rides EVERY report. The
        // hardware/locale snapshot is disclosed only under usage consent, so BuildFeedback
        // strips it for a player who never granted that — the feedback panel promises
        // anonymity and never asked for hardware. Usage and crash reports are only ever built
        // on the consented path, so they always carry both halves.
        string[] core = { "device_id", "report_type", "sent_at", "build_version", "build_kind", "os" };
        string[] hardware = { "os_version", "cpu", "gpu", "ram_mb", "resolution", "renderer",
            "locale", "region" };

        void CheckEnvelope(Godot.Collections.Dictionary d, string type, bool expectHardware = true)
        {
            foreach (string key in core)
                Check(d.ContainsKey(key), $"payload[{type}]: missing envelope field '{key}'");
            foreach (string key in hardware)
            {
                // Asserted in BOTH directions: present when consent allows it, and genuinely
                // absent (not blanked, not a sentinel) when it does not.
                Check(d.ContainsKey(key) == expectHardware, expectHardware
                    ? $"payload[{type}]: missing envelope field '{key}'"
                    : $"payload[{type}]: '{key}' must be stripped without usage consent");
            }
            Check(d["report_type"].AsString() == type, $"payload[{type}]: report_type mismatch");
            Check(d["device_id"].AsString() == TelemetryStore.DeviceId, $"payload[{type}]: device_id mismatch");
            if (expectHardware)
                Check(d["ram_mb"].VariantType == Variant.Type.Int, $"payload[{type}]: ram_mb must be Int");
        }

        var sections = new Godot.Collections.Dictionary { { "CyanRun", 4.5f }, { "Hub", 9.0f } };
        var usage = TelemetryPayload.BuildUsage(120, "host", "enet", 3, true, 12, 5, 7,
            13.5f, 6.75f, sections);
        CheckEnvelope(usage, "usage");
        Check(usage["session_length_sec"].AsInt32() == 120, "payload[usage]: session_length_sec");
        Check(usage["role"].AsString() == "host", "payload[usage]: role");
        Check(usage["transport"].AsString() == "enet", "payload[usage]: transport");
        Check(usage["player_count_seen"].AsInt32() == 3, "payload[usage]: player_count_seen");
        Check(usage["voice_used"].VariantType == Variant.Type.Bool, "payload[usage]: voice_used must be Bool");
        Check(usage["props_grabbed"].VariantType == Variant.Type.Int, "payload[usage]: props_grabbed must be Int");
        Check(usage["props_grabbed"].AsInt32() == 12, "payload[usage]: props_grabbed value");
        Check(usage["props_thrown"].VariantType == Variant.Type.Int, "payload[usage]: props_thrown must be Int");
        Check(usage["props_thrown"].AsInt32() == 5, "payload[usage]: props_thrown value");
        Check(usage["props_dropped"].VariantType == Variant.Type.Int, "payload[usage]: props_dropped must be Int");
        Check(usage["props_dropped"].AsInt32() == 7, "payload[usage]: props_dropped value");
        // LD-2: the three cadence fields. Floats (a duration, not a count) and a name-keyed
        // dictionary; the values round-trip exactly because they are what was passed in.
        Check(usage["stop_seconds"].VariantType == Variant.Type.Float, "payload[usage]: stop_seconds must be Float");
        Check(usage["stop_seconds"].AsSingle() == 13.5f, "payload[usage]: stop_seconds value");
        Check(usage["stop_seconds_per_minute"].VariantType == Variant.Type.Float,
            "payload[usage]: stop_seconds_per_minute must be Float");
        Check(usage["stop_seconds_per_minute"].AsSingle() == 6.75f, "payload[usage]: stop_seconds_per_minute value");
        Check(usage["stop_seconds_by_section"].VariantType == Variant.Type.Dictionary,
            "payload[usage]: stop_seconds_by_section must be a Dictionary");
        var bySection = usage["stop_seconds_by_section"].AsGodotDictionary();
        Check(bySection.Count == 2, "payload[usage]: stop_seconds_by_section must carry both sections");
        Check(bySection["CyanRun"].AsSingle() == 4.5f, "payload[usage]: stop_seconds_by_section[CyanRun]");
        Check(bySection["Hub"].AsSingle() == 9.0f, "payload[usage]: stop_seconds_by_section[Hub]");

        var crash = TelemetryPayload.BuildCrash("at A()", "Boom", null, "managed_exception");
        CheckEnvelope(crash, "crash");
        Check(crash["stack_trace"].AsString() == "at A()", "payload[crash]: stack_trace");
        Check(crash["exception_message"].AsString() == "Boom", "payload[crash]: exception_message");
        Check(crash["detection"].AsString() == "managed_exception", "payload[crash]: detection");
        Check(crash["log_tail"].VariantType == Variant.Type.Nil, "payload[crash]: null log_tail must be Nil");

        // Feedback: legacy shape — nothing structured answered, just the free-text box (spec §7,
        // decision 6 back-compat requirement). Must reproduce the original {text}-only envelope
        // exactly: no "answers" key at all, so an older reader built against the {text}-only
        // schema still works against this document.
        var legacyFeedback = TelemetryPayload.BuildFeedback(new Godot.Collections.Dictionary(), "great game");
        CheckEnvelope(legacyFeedback, "feedback");
        Check(legacyFeedback["text"].AsString() == "great game", "payload[feedback legacy]: text");
        Check(!legacyFeedback.ContainsKey("answers"), "payload[feedback legacy]: must not carry an answers key");

        // Feedback: fully answered — both structured questions (overall + performance; clarity
        // and tide_fairness were dropped in the survey rework) plus the optional text.
        var fullAnswers = new Godot.Collections.Dictionary
        {
            { "overall", 4 },
            { "performance", "Smooth" },
        };
        var fullFeedback = TelemetryPayload.BuildFeedback(fullAnswers, "loved the sunset");
        CheckEnvelope(fullFeedback, "feedback");
        Check(fullFeedback.ContainsKey("answers"), "payload[feedback full]: expected an answers key");
        var fullAnswersOut = fullFeedback["answers"].AsGodotDictionary();
        Check(fullAnswersOut["overall"].VariantType == Variant.Type.Int, "payload[feedback full]: overall must be Int");
        Check(fullAnswersOut["overall"].AsInt32() == 4, "payload[feedback full]: overall value");
        Check(fullAnswersOut["performance"].AsString() == "Smooth", "payload[feedback full]: performance value");
        Check(!fullAnswersOut.ContainsKey("clarity"), "payload[feedback full]: clarity must no longer exist");
        Check(!fullAnswersOut.ContainsKey("tide_fairness"), "payload[feedback full]: tide_fairness must no longer exist");
        Check(fullFeedback["text"].AsString() == "loved the sunset", "payload[feedback full]: text");

        // Feedback: partially answered — only one of two tapped, no text. The untapped
        // question must be a genuinely absent key, never a null/empty sentinel, and an empty
        // text box must not leave a "" text key behind.
        var partialAnswers = new Godot.Collections.Dictionary { { "overall", 2 } };
        var partialFeedback = TelemetryPayload.BuildFeedback(partialAnswers, "");
        CheckEnvelope(partialFeedback, "feedback");
        var partialAnswersOut = partialFeedback["answers"].AsGodotDictionary();
        Check(partialAnswersOut.ContainsKey("overall"), "payload[feedback partial]: expected overall");
        Check(!partialAnswersOut.ContainsKey("performance"), "payload[feedback partial]: performance must be absent, not a sentinel");
        Check(!partialFeedback.ContainsKey("text"), "payload[feedback partial]: empty text must not leave a text key");

        // Feedback: none answered and nothing typed (Skip, or Send with nothing touched) — the
        // report carries only the envelope, no answers key and no text key.
        var emptyFeedback = TelemetryPayload.BuildFeedback(new Godot.Collections.Dictionary(), "");
        CheckEnvelope(emptyFeedback, "feedback");
        Check(!emptyFeedback.ContainsKey("answers"), "payload[feedback empty]: must not carry an answers key");
        Check(!emptyFeedback.ContainsKey("text"), "payload[feedback empty]: must not carry a text key");

        // Feedback WITHOUT usage consent: the anonymity promise. The hardware/locale snapshot is
        // stripped (asserted key-by-key inside CheckEnvelope), while the identity/build core and
        // the player's actual answers and text still ride — stripping must not cost the report
        // its content. This is the behaviour BuildFeedback's consent gate exists for, and it had
        // no coverage until now.
        TelemetryStore.SetUsageConsent(false);
        var anonFeedback = TelemetryPayload.BuildFeedback(
            new Godot.Collections.Dictionary { { "overall", 5 } }, "no hardware please");
        CheckEnvelope(anonFeedback, "feedback", expectHardware: false);
        Check(anonFeedback["text"].AsString() == "no hardware please", "payload[feedback anon]: text must survive stripping");
        Check(anonFeedback["answers"].AsGodotDictionary()["overall"].AsInt32() == 5,
            "payload[feedback anon]: answers must survive stripping");
        TelemetryStore.SetUsageConsent(true);

        // Firestore typed-body conversion: strings/ints/bools/nulls map to the right typed keys.
        string body = TelemetryClient.ToFirestoreBody(crash);
        Check(body.Contains("\"stringValue\":\"Boom\""), "firestore: string field not typed correctly");
        Check(body.Contains("\"nullValue\":null"), "firestore: null field not typed as nullValue");
        var usageBody = TelemetryClient.ToFirestoreBody(usage);
        Check(usageBody.Contains("\"integerValue\":\"120\""), "firestore: int must be a stringified integerValue");
        Check(usageBody.Contains("\"booleanValue\":true"), "firestore: bool field not typed correctly");

        // Firestore typed-body conversion: a nested dictionary (structured "answers") recurses
        // into a mapValue of typed fields rather than falling through to a stringified dump.
        string fullFeedbackBody = TelemetryClient.ToFirestoreBody(fullFeedback);
        Check(fullFeedbackBody.Contains("\"mapValue\""), "firestore: nested answers dict must produce a mapValue");
        Check(fullFeedbackBody.Contains("\"integerValue\":\"4\""), "firestore: nested int (overall) must be an integerValue");
        Check(fullFeedbackBody.Contains("\"stringValue\":\"Smooth\""), "firestore: nested string (performance) must be a stringValue");
        string legacyFeedbackBody = TelemetryClient.ToFirestoreBody(legacyFeedback);
        Check(!legacyFeedbackBody.Contains("\"mapValue\""), "firestore: legacy {text}-only feedback must carry no mapValue");
    }

    internal static void Check(bool condition, string what)
    {
        if (!condition)
            Failures.Add(what);
    }
}
