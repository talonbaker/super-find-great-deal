using System.Collections.Generic;
using Godot;
using MpFoundation.Game;
using MpFoundation.Net;

namespace MpFoundation.Net.Steam;

/// <summary>
/// Headless pure-logic checks of the Steam-transport plumbing that must hold WITHOUT a
/// live Steam client — the CI-safe half of the split this project's no-mocking rule
/// forces: mechanical logic proves itself here; real relay connectivity is a weekend
/// manual test with real Steam accounts (see docs/STEAM.md). Run via --steam-selftest
/// (Run-SteamLogicTest.ps1); exits the process 0/1.
/// </summary>
public static class SteamSelfTest
{
    private static readonly List<string> Failures = new();

    public static int Run()
    {
        AddressRoundTrip();
        AddressRejection();
        TransportSelection();
        AppIdHandoff();
        IdentityKeyedRateLimiting();
        RoomCodeCodec();
        ReconnectFastFailDecision();

        if (Failures.Count == 0)
        {
            GD.Print("[steam-selftest] PASS (address codec, transport selection, App ID hand-off, " +
                "identity rate-limit keys, room codes, host-gone fast-fail decision)");
            return 0;
        }
        foreach (string failure in Failures)
            GD.PrintErr($"[steam-selftest] FAIL: {failure}");
        return 1;
    }

    private static void Check(bool condition, string what)
    {
        if (!condition)
            Failures.Add(what);
    }

    private static void AddressRoundTrip()
    {
        const ulong id = 90071996842377216; // shape of a real anonymous game-server id
        string address = SteamAddress.Format(id);
        Check(address == "steam:90071996842377216", $"Format produced '{address}'");
        Check(SteamAddress.TryParse(address, out ulong parsed) && parsed == id, "round-trip parse");
        Check(SteamAddress.TryParse("STEAM:42", out ulong ci) && ci == 42, "case-insensitive prefix");
        Check(SteamAddress.TryParse("  steam:7  ", out _), "surrounding whitespace tolerated");
    }

    private static void AddressRejection()
    {
        // Every shape a hostile or buggy phonebook payload could take.
        string?[] garbage =
        {
            null, "", "steam:", "steam:0", "steam:abc", "steam:-1", "steam:12x4",
            "steam:999999999999999999999", // 21 digits — overlong
            "127.0.0.1:7777", "host:port", "steamid:44", "steam :44",
        };
        foreach (string? input in garbage)
            Check(!SteamAddress.TryParse(input, out _), $"accepted garbage '{input}'");

        // And the ENet parser must not swallow steam addresses into host:port (a 17-digit
        // "port" overflows int; the prefix branch must win before this is ever consulted).
        Check(!LaunchOptions.TryParseAddress("steam:90071996842377216", out _, out _),
            "TryParseAddress accepted a steam address");
    }

    private static void TransportSelection()
    {
        var steam = LaunchOptions.Parse(new[] { "--server", "--transport", "steam" });
        Check(steam.Transport == LaunchOptions.TransportSteam, "--transport steam");

        var defaulted = LaunchOptions.Parse(new[] { "--server" });
        Check(defaulted.Transport == LaunchOptions.TransportEnet, "default transport is enet");

        var junk = LaunchOptions.Parse(new[] { "--server", "--transport", "carrier-pigeon" });
        Check(junk.Transport == LaunchOptions.TransportEnet, "unknown transport falls back to enet");

        var appId = LaunchOptions.Parse(new[] { "--steam-app-id", "12345" });
        Check(appId.SteamAppId == 12345, "--steam-app-id parses");
        Check(SteamService.ResolveAppId(appId) == 12345, "CLI app id wins resolution");
        // Without the flag, resolution walks env → file → Spacewar; whichever source wins
        // on this machine, it must land on SOMETHING nonzero (a zero App ID can't init).
        Check(SteamService.ResolveAppId(defaulted) != 0, "app id resolution never yields zero");
    }

    /// <summary>
    /// F2 of the 2026-08-30 master review: Steam's hand-off of the real App ID.
    ///
    /// Two independent defects, both of which could only ever fire once the real App ID
    /// existed — so nothing on the Spacewar route could have caught them, which is why they
    /// are proven here as a decision table rather than waited for:
    ///  1. <c>ResolveAppId</c> read only <c>STEAM_APP_ID</c>, a name Steam never sets, so a
    ///     Steam-launched build resolved to Spacewar;
    ///  2. <c>PrepareNative</c> then OVERWROTE Valve's <c>SteamAppId</c>/<c>SteamGameId</c>
    ///     with that fallback, so even a correct resolution could be undone.
    ///
    /// The fallback itself is NOT under test and is NOT changed: 480 is correct until the
    /// three Steamworks numbers land (docs/store/2026-08-29-STEAM-STATE-private-playtest.md
    /// §3b), and <c>NetProfile.FallbackSteamAppId</c> stays its single source.
    ///
    /// Mutating process env in a test is normally a smell; it is the honest instrument here
    /// because the env IS the mechanism under test, this is a dedicated single-purpose
    /// self-test process, and every variable is restored in the finally.
    /// </summary>
    private static void AppIdHandoff()
    {
        const string valveApp = "SteamAppId";
        const string valveGame = "SteamGameId";
        const string custom = "STEAM_APP_ID";
        string? savedApp = System.Environment.GetEnvironmentVariable(valveApp);
        string? savedGame = System.Environment.GetEnvironmentVariable(valveGame);
        string? savedCustom = System.Environment.GetEnvironmentVariable(custom);
        var bare = LaunchOptions.Parse(new[] { "--server" });

        try
        {
            System.Environment.SetEnvironmentVariable(valveApp, null);
            System.Environment.SetEnvironmentVariable(valveGame, null);
            System.Environment.SetEnvironmentVariable(custom, null);

            // (a) The unchanged baseline. With no override anywhere, resolution is exactly
            //     what it was before this fix. Asserted against the FILE probe rather than a
            //     bare "== 480" because a gitignored steam_appid.txt legitimately exists on
            //     some dev machines and asserting past it would be a flake, not a finding.
            uint bareResolved = SteamService.ResolveAppId(bare);
            if (SteamService.TryAppIdFile(out uint fromFile))
                Check(bareResolved == fromFile, $"bare resolution should read steam_appid.txt ({fromFile}), got {bareResolved}");
            else
                Check(bareResolved == NetProfile.FallbackSteamAppId, $"bare resolution should be Spacewar, got {bareResolved}");
            Check(SteamService.ShouldWriteAppIdEnv(NetProfile.FallbackSteamAppId, null),
                "an unset env must still be written (today's behaviour must not change)");
            Check(SteamService.ShouldWriteAppIdEnv(NetProfile.FallbackSteamAppId, ""), "an empty env is written");
            Check(SteamService.ShouldWriteAppIdEnv(NetProfile.FallbackSteamAppId, "0"), "a zero env is written");
            Check(SteamService.ShouldWriteAppIdEnv(NetProfile.FallbackSteamAppId, "not-a-number"),
                "an unparseable env is written");

            // (b) Ship day: Steam launches the build and sets its own name. That value wins,
            //     and PrepareNative leaves it alone.
            System.Environment.SetEnvironmentVariable(valveApp, "123456");
            Check(SteamService.ResolveAppId(bare) == 123456, "SteamAppId env must win resolution");
            Check(!SteamService.ShouldWriteAppIdEnv(123456, "123456"), "a matching env is not rewritten");
            Check(!SteamService.ShouldWriteAppIdEnv(NetProfile.FallbackSteamAppId, "123456"),
                "the Spacewar fallback must NEVER clobber a real App ID — this is the ship-day defect");

            // (c) SteamGameId is the documented second name; a packed (mod) value is skipped
            //     rather than truncated into a wrong ID.
            System.Environment.SetEnvironmentVariable(valveApp, null);
            System.Environment.SetEnvironmentVariable(valveGame, "654321");
            Check(SteamService.ResolveAppId(bare) == 654321, "SteamGameId is the second Valve name");
            // 0x4_0000_EA60: a mod-shaped packed id whose LOW 32 bits are the plausible-looking
            // App ID 60000. A ulong-parse-and-mask would hand that straight to SteamAPI.Init;
            // the uint parse rejects the whole string and falls through instead.
            System.Environment.SetEnvironmentVariable(valveGame, "17179929184");
            uint packed = SteamService.ResolveAppId(bare);
            Check(packed != 60000, "a packed SteamGameId must not be truncated into a plausible wrong App ID");
            Check(packed != 0, "a packed SteamGameId falls through; it never yields zero");

            // (d) Precedence. The project's own STEAM_APP_ID still works, and still ranks
            //     below Valve's names so a stale shell variable cannot beat a real launch.
            System.Environment.SetEnvironmentVariable(valveGame, null);
            System.Environment.SetEnvironmentVariable(custom, "777");
            Check(SteamService.ResolveAppId(bare) == 777, "STEAM_APP_ID still resolves when Valve's names are absent");
            System.Environment.SetEnvironmentVariable(valveApp, "888");
            Check(SteamService.ResolveAppId(bare) == 888, "Valve's SteamAppId outranks STEAM_APP_ID");
            var cli = LaunchOptions.Parse(new[] { "--steam-app-id", "999" });
            Check(SteamService.ResolveAppId(cli) == 999, "--steam-app-id outranks every env source");
            Check(SteamService.ShouldWriteAppIdEnv(999, "888"),
                "a deliberate override is still allowed to overwrite a set env");
        }
        finally
        {
            System.Environment.SetEnvironmentVariable(valveApp, savedApp);
            System.Environment.SetEnvironmentVariable(valveGame, savedGame);
            System.Environment.SetEnvironmentVariable(custom, savedCustom);
        }
    }

    // Room codes now live in the game assembly (they used to be the phonebook's — the
    // host's client mints them and publishes them as lobby metadata, see SteamLobby).
    private static void RoomCodeCodec()
    {
        for (int i = 0; i < 200; i++)
        {
            string code = RoomCode.Generate();
            Check(code.Length == RoomCode.Length, $"generated code '{code}' has wrong length");
            Check(RoomCode.IsValid(code), $"generated code '{code}' fails its own validation");
            Check(code.ToUpperInvariant() == code, $"generated code '{code}' is not uppercase");
        }

        // The ambiguous letters (I, L, O) and digits are excluded by design; junk shapes die.
        string?[] garbage = { null, "", "AB", "ABCDE", "AB1D", "ABIO", "abcd", "A CD", "ABL D" };
        foreach (string? input in garbage)
            Check(!RoomCode.IsValid(input), $"accepted invalid room code '{input}'");
    }

    // P4 (Task A4): the host-gone fast-fail decision. After a FAILED reconnect attempt, the
    // retry loop re-resolves the room code against the Steam lobby directory; this pure decision
    // turns (window-remaining x lobby-probe) into keep-trying vs give-up. The Steam query that
    // produces the probe is interactive-only (no relay in CI — Gameplay.ProbeHostLobby); THIS
    // matrix is the CI-safe proof that a provably-gone host ends the 60s spin the instant it's
    // known gone, while a mere connection blip or a Steam hiccup keeps retrying as before.
    private static void ReconnectFastFailDecision()
    {
        // A provably-gone lobby fast-fails immediately, with OR without window left — the whole
        // point of P4 (the host was the sole lobby owner; a gone lobby == a gone host for good).
        Check(Gameplay.DecideReconnect(true, Gameplay.LobbyProbe.Gone) == Gameplay.ReconnectDecision.HostGone,
            "gone lobby within window -> host gone");
        Check(Gameplay.DecideReconnect(false, Gameplay.LobbyProbe.Gone) == Gameplay.ReconnectDecision.HostGone,
            "gone lobby past window -> host gone (proof of death outranks the timeout)");

        // Lobby still up: keep retrying while the window has time, give up when it runs out —
        // today's behavior, unchanged.
        Check(Gameplay.DecideReconnect(true, Gameplay.LobbyProbe.Found) == Gameplay.ReconnectDecision.Retry,
            "found lobby within window -> retry");
        Check(Gameplay.DecideReconnect(false, Gameplay.LobbyProbe.Found) == Gameplay.ReconnectDecision.WindowExpired,
            "found lobby past window -> window expired");

        // No room code to check (a direct steam:<id64> joiner has nothing to re-resolve) —
        // behaves exactly as today: retry within the window, expire past it.
        Check(Gameplay.DecideReconnect(true, Gameplay.LobbyProbe.NotChecked) == Gameplay.ReconnectDecision.Retry,
            "unchecked within window -> retry");
        Check(Gameplay.DecideReconnect(false, Gameplay.LobbyProbe.NotChecked) == Gameplay.ReconnectDecision.WindowExpired,
            "unchecked past window -> window expired");

        // A transient query failure (Steam hiccup, lookup timeout, a lookup already in flight) is
        // NOT proof the host left — our own network's problem must never be read as host-gone.
        // Inconclusive keeps retrying exactly like a lobby that was found.
        Check(Gameplay.DecideReconnect(true, Gameplay.LobbyProbe.Inconclusive) == Gameplay.ReconnectDecision.Retry,
            "inconclusive within window -> retry (a hiccup is not host death)");
        Check(Gameplay.DecideReconnect(false, Gameplay.LobbyProbe.Inconclusive) == Gameplay.ReconnectDecision.WindowExpired,
            "inconclusive past window -> window expired");

        // The raw SteamLobby result -> probe classification that feeds the matrix above. This is
        // where found-vs-honestly-absent-vs-errored is decided, so it must be pure and tested:
        // only an authoritative "no such lobby" (found=false, no error) is Gone.
        Check(Gameplay.ClassifyLobbyResult(true, "") == Gameplay.LobbyProbe.Found,
            "found result -> Found");
        Check(Gameplay.ClassifyLobbyResult(false, "") == Gameplay.LobbyProbe.Gone,
            "not-found with no error -> Gone");
        Check(Gameplay.ClassifyLobbyResult(false, "Steam room lookup timed out.") == Gameplay.LobbyProbe.Inconclusive,
            "not-found WITH error -> Inconclusive");
    }

    private static void IdentityKeyedRateLimiting()
    {
        // The limiter is identity-string keyed; a SteamID key must budget independently of
        // IP keys and of other SteamIDs — the property the auth boundary relies on when the
        // transport swaps identities from IPs to steam:<id64>.
        var limiter = new ConnectionRateLimiter(maxPerWindow: 3, windowSec: 10);
        string steamA = SteamAddress.Format(101);
        string steamB = SteamAddress.Format(202);

        for (int i = 0; i < 3; i++)
        {
            Check(limiter.Allow(steamA, i * 0.1), $"steamA attempt {i} within budget");
            Check(limiter.Allow("10.0.0.1", i * 0.1), $"ip attempt {i} within budget");
        }
        Check(!limiter.Allow(steamA, 0.9), "steamA over budget is limited");
        Check(limiter.Allow(steamB, 0.9), "steamB budget independent of steamA");
        Check(!limiter.Allow("10.0.0.1", 0.9), "ip over budget is limited");
        Check(limiter.Allow(steamA, 20.0), "steamA budget recovers after the window");
    }
}
