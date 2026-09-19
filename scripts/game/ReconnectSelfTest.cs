using System.Collections.Generic;
using Godot;

namespace MpFoundation.Game;

/// <summary>
/// Headless pure-logic checks of the reconnect-grace-window bookkeeping
/// (<see cref="ReconnectRegistry"/>) that must hold WITHOUT a live Steam client or a
/// running match — the same CI-safe split this project already uses for Steam-adjacent
/// logic (see Net.Steam.SteamSelfTest): mechanical logic proves itself here; a real
/// disconnect/reconnect over the Steam relay needs live Steam accounts and stays a
/// weekend manual protocol, never mocked in CI. Run via --reconnect-selftest
/// (Run-ReconnectTest.ps1); exits the process 0/1.
/// </summary>
public static class ReconnectSelfTest
{
    private static readonly List<string> Failures = new();

    public static int Run()
    {
        ResumeWithinWindow();
        ExpiredWindowFallsThroughToFreshSpawn();
        ResumeIsOneShot();
        SweepRemovesOnlyExpiredEntries();
        IdentityIsolation();
        HeldPropsAndColorIndexRoundTrip();
        NoHeldPropsResumesWithEmptyArray();
        ResolveColorIndexPrefersRecordWhenResumed();
        ResolveColorIndexUsesFreshIndexWhenNotResumed();
        ResolveColorIndexHonorsZeroAsAValidRecordIndex();

        if (Failures.Count == 0)
        {
            GD.Print("[reconnect-selftest] PASS (resume-within-window, expiry, one-shot consume, sweep, " +
                "identity isolation, held-props/color-index round trip, empty-held-props default, " +
                "resume-branch color-index decision)");
            return 0;
        }
        foreach (string failure in Failures)
            GD.PrintErr($"[reconnect-selftest] FAIL: {failure}");
        return 1;
    }

    private static void Check(bool condition, string what)
    {
        if (!condition)
            Failures.Add(what);
    }

    // A reconnect inside the 60s grace window resumes at the exact captured position and
    // consumes the record (Gameplay.OnPeerConnected's happy path).
    private static void ResumeWithinWindow()
    {
        var registry = new ReconnectRegistry();
        const ulong steamId = 76561197960287930UL;
        var savedPos = new Vector3(3.5f, 1.1f, -7.25f);
        registry.Capture(steamId, savedPos, System.Array.Empty<int>(), colorIndex: -1, nowSec: 1000.0);

        bool found = registry.TryConsume(steamId, nowSec: 1030.0, out ReconnectRegistry.ResumeData resume);
        Check(found, "resume within window: expected a hit 30s after disconnect");
        Check(resume.Position == savedPos, $"resume within window: got {resume.Position}, expected {savedPos}");
    }

    // A reconnect attempt after the 60s window must NOT resume — a normal fresh spawn
    // follows instead (Gameplay.OnPeerConnected's fallback path).
    private static void ExpiredWindowFallsThroughToFreshSpawn()
    {
        var registry = new ReconnectRegistry();
        const ulong steamId = 76561197960287931UL;
        registry.Capture(steamId, new Vector3(1, 1, 1), System.Array.Empty<int>(), colorIndex: -1, nowSec: 0.0);

        bool found = registry.TryConsume(steamId, nowSec: ReconnectRegistry.WindowSec + 0.01, out _);
        Check(!found, "expired window: a reconnect at 60.01s must not resume");

        // Exactly at the boundary must also be treated as expired (nowSec strictly less
        // than the expiry, never equal) - a fresh spawn is always the safe default.
        var registry2 = new ReconnectRegistry();
        registry2.Capture(steamId, new Vector3(1, 1, 1), System.Array.Empty<int>(), colorIndex: -1, nowSec: 0.0);
        bool foundAtBoundary = registry2.TryConsume(steamId, nowSec: ReconnectRegistry.WindowSec, out _);
        Check(!foundAtBoundary, "expired window: a reconnect at exactly 60.0s must not resume");
    }

    // A record can only be claimed once: a second reconnect for the same identity right
    // after a successful resume must get a fresh spawn, not the same stale position again.
    private static void ResumeIsOneShot()
    {
        var registry = new ReconnectRegistry();
        const ulong steamId = 76561197960287932UL;
        registry.Capture(steamId, new Vector3(2, 2, 2), System.Array.Empty<int>(), colorIndex: -1, nowSec: 0.0);

        bool first = registry.TryConsume(steamId, nowSec: 1.0, out _);
        bool second = registry.TryConsume(steamId, nowSec: 2.0, out _);
        Check(first, "one-shot: first consume should hit");
        Check(!second, "one-shot: second consume for the same identity must miss");
    }

    // The periodic sweep (Gameplay._Process's existing status-tick cadence) must discard
    // only records that have actually expired, leaving live ones untouched.
    private static void SweepRemovesOnlyExpiredEntries()
    {
        var registry = new ReconnectRegistry();
        registry.Capture(1001, new Vector3(), System.Array.Empty<int>(), colorIndex: -1, nowSec: 0.0);   // expires at 60
        registry.Capture(1002, new Vector3(), System.Array.Empty<int>(), colorIndex: -1, nowSec: 100.0); // expires at 160

        int removed = registry.SweepExpired(nowSec: 90.0);
        Check(removed == 1, $"sweep at t=90: expected 1 removal, got {removed}");
        Check(registry.Count == 1, $"sweep at t=90: expected 1 record left, got {registry.Count}");

        bool staleFound = registry.TryConsume(1001, nowSec: 90.0, out _);
        Check(!staleFound, "sweep at t=90: the swept identity must no longer resolve");
        bool liveFound = registry.TryConsume(1002, nowSec: 90.0, out _);
        Check(liveFound, "sweep at t=90: the not-yet-expired identity must still resolve");
    }

    // Two different SteamID64s never see each other's saved position - the table is keyed
    // per identity, not shared/global state.
    private static void IdentityIsolation()
    {
        var registry = new ReconnectRegistry();
        registry.Capture(2001, new Vector3(9, 9, 9), System.Array.Empty<int>(), colorIndex: -1, nowSec: 0.0);
        bool found = registry.TryConsume(2002, nowSec: 1.0, out _);
        Check(!found, "identity isolation: an unrelated SteamID64 must not resolve someone else's record");
    }

    // A resumed peer's grace record carries what it held (P2) and its dealt palette index
    // (P11) alongside its position - all three survive the capture/consume round trip
    // unmodified (Gameplay.OnPeerDisconnected -> ReconnectRegistry.Capture -> ... ->
    // Gameplay.OnPeerConnected -> ReconnectRegistry.TryConsume).
    private static void HeldPropsAndColorIndexRoundTrip()
    {
        var registry = new ReconnectRegistry();
        const ulong steamId = 76561197960287933UL;
        var heldIds = new[] { 4, 7 };
        registry.Capture(steamId, new Vector3(5, 1, 5), heldIds, colorIndex: 2, nowSec: 0.0);

        bool found = registry.TryConsume(steamId, nowSec: 1.0, out ReconnectRegistry.ResumeData resume);
        Check(found, "held-props round trip: expected a hit");
        Check(resume.HeldPropIds.Length == 2 && resume.HeldPropIds[0] == 4 && resume.HeldPropIds[1] == 7,
            $"held-props round trip: got [{string.Join(",", resume.HeldPropIds)}], expected [4,7]");
        Check(resume.ColorIndex == 2, $"held-props round trip: colorIndex got {resume.ColorIndex}, expected 2");
    }

    // The common case - a disconnecting peer holding nothing - must resume with a non-null
    // empty array, never null, so a caller's foreach (Gameplay.OnPeerConnected's restore loop)
    // never needs a defensive null check.
    private static void NoHeldPropsResumesWithEmptyArray()
    {
        var registry = new ReconnectRegistry();
        const ulong steamId = 76561197960287934UL;
        registry.Capture(steamId, new Vector3(), System.Array.Empty<int>(), colorIndex: 0, nowSec: 0.0);

        bool found = registry.TryConsume(steamId, nowSec: 1.0, out ReconnectRegistry.ResumeData resume);
        Check(found, "empty held-props: expected a hit");
        Check(resume.HeldPropIds != null && resume.HeldPropIds.Length == 0,
            "empty held-props: expected a non-null empty array");
    }

    // P11 (Task A3): the resumed-vs-fresh palette-color decision itself. This is the one seam
    // Gameplay.OnPeerConnected's branch has that doesn't require a running scene tree - pulled
    // out as Gameplay.ResolveColorIndex (a pure static function, no Node/state involved) purely
    // so this decision is directly testable. See task-A3-brief.md's KNOWN WALL: the live
    // Gameplay.OnPeerConnected branch that CALLS this can never actually take the "resumed"
    // path in ENet-bot CI (SteamId64Of always returns 0 for bots), so this pure-logic check is
    // the only place the decision itself gets proven in CI.
    private static void ResolveColorIndexPrefersRecordWhenResumed()
    {
        int result = Gameplay.ResolveColorIndex(resumed: true, resumeColorIndex: 3, freshIndex: 7);
        Check(result == 3, $"resolve color index (resumed): got {result}, expected the record's 3, not the fresh index 7");
    }

    private static void ResolveColorIndexUsesFreshIndexWhenNotResumed()
    {
        int result = Gameplay.ResolveColorIndex(resumed: false, resumeColorIndex: 3, freshIndex: 7);
        Check(result == 7, $"resolve color index (fresh): got {result}, expected the fresh index 7, not the (irrelevant) record value 3");
    }

    // 0 is a legitimate palette slot (the first color dealt) - the decision must not
    // mistake it for "no record" and fall through to the fresh index.
    private static void ResolveColorIndexHonorsZeroAsAValidRecordIndex()
    {
        int result = Gameplay.ResolveColorIndex(resumed: true, resumeColorIndex: 0, freshIndex: 7);
        Check(result == 0, $"resolve color index (resumed, record=0): got {result}, expected 0, not the fresh index 7");
    }
}
