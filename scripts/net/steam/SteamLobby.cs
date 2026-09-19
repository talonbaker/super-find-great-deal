using System;
using System.Threading.Tasks;
using Godot;
using Steamworks;

namespace MpFoundation.Net.Steam;

/// <summary>
/// Room codes over Steam's own Lobby API (ISteamMatchmaking) — the zero-hosting phonebook.
/// The HOST'S OWN CLIENT creates a public lobby carrying {derived room key → server identity}
/// as lobby metadata; a joiner derives the same key from the code it was given, filters the
/// public lobby list by it, and connects to the server identity it finds. Nobody ever joins
/// the lobby but its creator: the lobby is a key-value directory entry, not a session
/// container — the match itself rides the existing SteamPeer relay transport, unchanged.
///
/// The code itself is NEVER published (see <see cref="KeyRoomKey"/> and
/// <see cref="RoomCode.DeriveDirectoryKey"/>). Both sides derive independently and the key is
/// never transmitted, so a scraper of the world-readable index comes away with a server
/// identity it has no credential for, and the handshake gate turns it away.
///
/// Lifecycle guarantees this leans on (verified against the SDK, Task 0 of the
/// steam-native-matchmaking dispatch):
///  - Lobbies are a CLIENT api. There is no game-server lobby surface in the SDK, so the
///    anonymous server child can't own one — the host's logged-in session must (which is
///    why this class lives behind SteamService.EnsureClient, never StartGameServer).
///  - Steam destroys a lobby when its last member leaves — including by crash or network
///    death. The old phonebook's stale-sweeper/heartbeat machinery has no replacement
///    here because Valve runs it for us: a dead host means the code stops resolving.
///
/// Async pattern: every call result is completed by <see cref="SteamService.Pump"/> on the
/// main thread, so awaiting these from UI code resumes safely (Godot's sync context).
/// </summary>
public static class SteamLobby
{
    // Lobby metadata keys. "game" + "protocol" scope the search so room codes never
    // collide with other titles' lobbies while developing on the shared Spacewar App ID
    // (480), and so mismatched builds fail with "room not found" instead of a version
    // error deep inside the connect path.
    public const string KeyGame = "game";
    public const string KeyProtocol = "protocol";

    /// <summary>
    /// Carries <see cref="RoomCode.DeriveDirectoryKey"/>, NOT the room code.
    ///
    /// The lobby index is world-readable — a non-member who receives a lobby from
    /// RequestLobbyList gets every metadata key AND value, and the call returns 50 lobbies
    /// per invocation, worldwide, with no documented rate limit. This key used to be
    /// <c>room_code</c> holding the code in the clear, which published the join credential in
    /// the same record as the join address: scraping the directory was enough to satisfy the
    /// handshake gate. Renamed along with the change of meaning so a stale build reading a new
    /// lobby (or the reverse) finds nothing and reports "room not found", rather than matching
    /// a value it would misinterpret.
    ///
    /// Note the key NAME was never the protection — hiding a plaintext code under an innocuous
    /// name is theatre, because non-members receive names and values alike. The value being
    /// one-way is the protection.
    /// </summary>
    public const string KeyRoomKey = "room_key";
    public const string KeyHostSteamId = "host_steamid";
    public const string KeyWorld = "world"; // carried for free; not yet negotiated (world selection is CLI-only)
    public const string GameTag = NetProfile.GameTag;

    public static string LastError { get; private set; } = "";

    /// <summary>The lobby this client created and still owns; 0 when none.</summary>
    public static ulong CurrentLobbyId { get; private set; }

    private static CallResult<LobbyCreated_t>? _createCall;
    private static CallResult<LobbyMatchList_t>? _listCall;

    /// <summary>
    /// Creates the public lobby advertising this match and stamps its metadata. Requires
    /// a live client session (SteamService.EnsureClient) — the caller checks that first
    /// so the player sees the specific "Steam is not available" message, not a generic one.
    /// </summary>
    public static async Task<bool> CreateForMatchAsync(
        string roomCode, ulong serverSteamId, string world, double timeoutSec = 20.0)
    {
        LastError = "";
        if (!SteamService.ClientStarted)
        {
            LastError = "Steam is not available — make sure Steam is running and you are logged in.";
            return false;
        }
        if (_createCall is not null)
        {
            LastError = "A lobby is already being created.";
            return false;
        }

        // Derive BEFORE anything is created on Steam's side. This is ~150-300ms of deliberate KDF
        // work (RoomCode.DirectoryKeyIterations), and it can throw on a malformed code — doing it
        // after CreateLobby would mean an exception escaping past a lobby we are already a member
        // of, which LeaveCurrent could never find to destroy because CurrentLobbyId was not set
        // yet. Off the main thread because this runs behind the host menu and a blocking third of
        // a second there is a visible hitch; nothing Godot-owned is touched inside, and the await
        // resumes on the Godot sync context as the rest of this method expects.
        string roomKey;
        try
        {
            roomKey = await Task.Run(() => RoomCode.DeriveDirectoryKey(roomCode));
        }
        catch (ArgumentException)
        {
            LastError = "Internal error: the generated room code was malformed.";
            return false;
        }
        LeaveCurrent(); // a stale lobby from an abandoned host attempt must not shadow the new code

        bool abandoned = false; // set on timeout; the late callback then reaps instead of resolving
        var tcs = new TaskCompletionSource<LobbyCreated_t>(TaskCreationOptions.RunContinuationsAsynchronously);
        _createCall = CallResult<LobbyCreated_t>.Create((result, ioFailure) =>
        {
            if (abandoned)
            {
                // The waiter gave up, but Steam finished the creation anyway — we are now a
                // member of a lobby nobody recorded, which LeaveCurrent could never destroy.
                // This callback runs on the main thread (SteamService.Pump), so leave here.
                if (!ioFailure && result.m_eResult == EResult.k_EResultOK && result.m_ulSteamIDLobby != 0)
                {
                    try
                    {
                        SteamMatchmaking.LeaveLobby(new CSteamID(result.m_ulSteamIDLobby));
                        GD.Print($"[steam] reaped late-created lobby {result.m_ulSteamIDLobby} (creation had timed out)");
                    }
                    catch { /* client session gone; the lobby dies with our membership */ }
                }
                return;
            }
            if (ioFailure)
                tcs.TrySetResult(new LobbyCreated_t { m_eResult = EResult.k_EResultIOFailure });
            else
                tcs.TrySetResult(result);
        });
        // Public: joiners aren't necessarily Steam friends of the host. Member cap matches
        // the game's player cap; members are only ever the host, so slots stay available
        // and the lobby stays visible to RequestLobbyList.
        _createCall.Set(SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypePublic, Protocol.MaxPlayers));

        Task first = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(timeoutSec)));
        if (first != tcs.Task)
        {
            // Deliberately NOT disposed: the CallResult must stay registered so the late
            // completion (if any) can be reaped above. Steamworks.NET auto-cancels a
            // CallResult after it dispatches, so the registration cannot leak beyond that.
            abandoned = true;
            _createCall = null;
            LastError = "Steam lobby creation timed out.";
            return false;
        }
        _createCall.Dispose();
        _createCall = null;
        LobbyCreated_t created = tcs.Task.Result;
        if (created.m_eResult != EResult.k_EResultOK)
        {
            LastError = $"Steam lobby creation failed: {created.m_eResult}";
            return false;
        }

        var lobby = new CSteamID(created.m_ulSteamIDLobby);
        bool ok = SteamMatchmaking.SetLobbyData(lobby, KeyGame, GameTag)
            & SteamMatchmaking.SetLobbyData(lobby, KeyProtocol, Protocol.Version.ToString())
            & SteamMatchmaking.SetLobbyData(lobby, KeyRoomKey, roomKey)
            & SteamMatchmaking.SetLobbyData(lobby, KeyHostSteamId, serverSteamId.ToString())
            & SteamMatchmaking.SetLobbyData(lobby, KeyWorld, world);
        if (!ok)
        {
            SteamMatchmaking.LeaveLobby(lobby);
            LastError = "Steam accepted the lobby but rejected its metadata.";
            return false;
        }

        CurrentLobbyId = created.m_ulSteamIDLobby;
        // The key prefix is logged, not the code: it is the value both sides must agree on, so
        // it is what makes a "room not found" caused by derivation skew distinguishable from
        // one caused by a mistyped code. It is public by construction — it is in the index.
        GD.Print($"[steam] lobby {CurrentLobbyId} up (room {roomCode} key {roomKey[..8]} -> steam:{serverSteamId})");
        return true;
    }

    /// <summary>
    /// Resolves a room code to the match server's Steam identity by filtering the public
    /// lobby list. Found=false with empty Error means the honest "no such room" outcome.
    /// </summary>
    public static async Task<(bool Found, ulong HostSteamId, string Error)> FindHostByCodeAsync(
        string roomCode, double timeoutSec = 15.0)
    {
        LastError = "";
        if (!SteamService.ClientStarted)
            return (false, 0, "Steam is not available — make sure Steam is running and you are logged in.");
        if (_listCall is not null)
            return (false, 0, "A room lookup is already in progress.");

        // Derive before opening the call result, and off the main thread (see CreateForMatchAsync).
        // A malformed code cannot reach a lobby search at all — the callers validate first, and
        // DeriveDirectoryKey throws rather than searching for a key nobody could have published.
        string roomKey;
        try
        {
            roomKey = await Task.Run(() => RoomCode.DeriveDirectoryKey(roomCode));
        }
        catch (ArgumentException)
        {
            return (false, 0, "");  // honest "no such room" — an unmintable code matches nothing
        }

        var tcs = new TaskCompletionSource<LobbyMatchList_t>(TaskCreationOptions.RunContinuationsAsynchronously);
        _listCall = CallResult<LobbyMatchList_t>.Create((result, ioFailure) =>
        {
            if (ioFailure)
                tcs.TrySetResult(new LobbyMatchList_t { m_nLobbiesMatching = 0 });
            else
                tcs.TrySetResult(result);
        });
        // Worldwide: a friend across an ocean typed this code on purpose; Steam's default
        // distance filter would hide the lobby from them.
        SteamMatchmaking.AddRequestLobbyListDistanceFilter(ELobbyDistanceFilter.k_ELobbyDistanceFilterWorldwide);
        SteamMatchmaking.AddRequestLobbyListStringFilter(KeyGame, GameTag, ELobbyComparison.k_ELobbyComparisonEqual);
        SteamMatchmaking.AddRequestLobbyListStringFilter(KeyProtocol, Protocol.Version.ToString(), ELobbyComparison.k_ELobbyComparisonEqual);
        // Equality on the DERIVED key. Steam does the matching server-side, so the joiner still
        // resolves a room in one call without enumerating anything — the privacy change costs
        // the join path a KDF, not a directory sweep.
        SteamMatchmaking.AddRequestLobbyListStringFilter(KeyRoomKey, roomKey, ELobbyComparison.k_ELobbyComparisonEqual);
        _listCall.Set(SteamMatchmaking.RequestLobbyList());

        Task first = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(timeoutSec)));
        _listCall.Dispose();
        _listCall = null;
        if (first != tcs.Task)
            return (false, 0, "Steam room lookup timed out.");

        int matches = (int)tcs.Task.Result.m_nLobbiesMatching;
        for (int i = 0; i < matches; i++)
        {
            CSteamID lobby = SteamMatchmaking.GetLobbyByIndex(i);
            string idText = SteamMatchmaking.GetLobbyData(lobby, KeyHostSteamId);
            if (ulong.TryParse(idText, out ulong hostId) && hostId != 0)
                return (true, hostId, "");
        }
        return (false, 0, "");
    }

    /// <summary>Leaves (and thereby, as sole member, destroys) the current lobby. Idempotent.</summary>
    public static void LeaveCurrent()
    {
        if (CurrentLobbyId == 0)
            return;
        try
        {
            SteamMatchmaking.LeaveLobby(new CSteamID(CurrentLobbyId));
            GD.Print($"[steam] left lobby {CurrentLobbyId}");
        }
        catch
        {
            // Client session already torn down; the lobby dies with our membership anyway.
        }
        CurrentLobbyId = 0;
    }
}
