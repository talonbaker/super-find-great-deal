using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;
using MpFoundation.Game.Sandbox;
using MpFoundation.Game.Props;
using MpFoundation.Game.World;

namespace MpFoundation.Game;

/// <summary>
/// Attached (client-side only) when running with --bot. Periodically logs this
/// client's view of every player position as one JSON object per line, then quits
/// with exit code 0 after the configured duration. The test orchestrator parses
/// these logs to prove replication converged across all peers.
/// </summary>
public partial class BotHarness : Node
{
    private const double LogIntervalSec = 0.2;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private Node3D _players = null!;
    private PropManager _propManager = null!;
    private Node3D? _entities;
    private LaunchOptions _options = null!;
    private StreamWriter? _writer;
    private double _elapsed;
    private double _sinceLog;
    private bool _done;

    // --capture-at/--capture-dir: index of the next capture mark to fire. Marks arrive sorted
    // ascending from LaunchOptions, so one cursor is enough; a frame long enough to skip past
    // several marks fires them in order on subsequent frames rather than dropping them.
    private int _nextCaptureIndex;
    private int _nextTickCaptureIndex;
    private bool _warnedNoNetClock;
    private bool _capturing;

    public void Setup(Node3D players, PropManager propManager, LaunchOptions options,
        Node3D? entities = null)
    {
        _players = players;
        _propManager = propManager;
        _options = options;
        _entities = entities;
    }

    public override void _Ready()
    {
        if (_options.LogPath.Length > 0)
        {
            string? dir = Path.GetDirectoryName(_options.LogPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            _writer = new StreamWriter(File.Open(_options.LogPath, FileMode.Create, System.IO.FileAccess.Write, FileShare.Read));
        }
        GD.Print($"[bot] {_options.DisplayName} running as peer {Multiplayer.GetUniqueId()} for {_options.DurationSec}s");

        // --capture-cam: a fixed camera framing the SUBJECT of a visual gate rather than this
        // bot's own avatar. Made current, so it wins over any --spectate-cam follow camera
        // attached to the avatar. View-only — nothing below this line touches bot behaviour.
        if (_options.HasCaptureCam)
        {
            var camera = new Camera3D { Name = "CaptureCam" };
            AddChild(camera);
            camera.GlobalPosition = _options.CaptureCamPos;
            camera.LookAt(_options.CaptureCamLookAt, Vector3.Up);
            camera.MakeCurrent();
            GD.Print($"[bot] capture cam at {_options.CaptureCamPos} looking at {_options.CaptureCamLookAt}");
        }

        Voice.VoiceTestSender.Mode? voiceMode =
            _options.VoiceFlood ? Voice.VoiceTestSender.Mode.Flood :
            _options.VoiceGarbage ? Voice.VoiceTestSender.Mode.Garbage :
            _options.VoiceOversize ? Voice.VoiceTestSender.Mode.Oversize :
            _options.VoiceSend ? Voice.VoiceTestSender.Mode.Tone : null;
        if (voiceMode is Voice.VoiceTestSender.Mode mode)
        {
            var sender = new Voice.VoiceTestSender();
            sender.Setup(mode);
            AddChild(sender);
        }
    }

    public override void _Process(double delta)
    {
        if (_done)
            return;
        _elapsed += delta;
        _sinceLog += delta;
        if (_sinceLog >= LogIntervalSec)
        {
            _sinceLog = 0;
            WriteSample();
        }
        MaybeCapture();
        MaybeCaptureAtTick();
        MaybeSendFlowRequests();
        MaybeToggleFlashlight();
        MaybeHonk();
        MaybeForgeHonk();
        MaybeFloodHonk();
        if (MaybeFinishAfterHolding())
            return;
        if (_elapsed >= _options.DurationSec)
            Finish();
    }

    // --exit-when-holding: quit cleanly N seconds after this bot FIRST holds anything, so a
    // disconnect-while-holding is staged on an OBSERVED event instead of on a guess about how fast
    // the bot walks. CARRY-1 measured the guess losing: the server moved its bot 2.4 m in 5.8 s
    // (~0.42 m/s against a ~3.6 m/s walk) and a 6 s lifetime expired with the prop never picked up,
    // so the case the suite existed for silently never ran. Run-ThrowTest's header already states
    // the rule ("bot lifetimes are COMPLETION BUDGETS, not assumptions about when any step lands");
    // this is what lets --duration go back to being one.
    //
    // Latches on the FIRST observation and never un-latches: if something took the prop away in
    // between, exiting anyway is correct, because the suite's own positive control is what decides
    // whether the hold it needed was ever witnessed. Both events are printed, so a log tail says
    // which of the two happened without re-reading the JSONL.
    private double _holdingSinceSec = -1;

    private bool MaybeFinishAfterHolding()
    {
        if (_options.ExitWhenHoldingSec < 0 || _propManager == null)
            return false;
        if (_holdingSinceSec < 0)
        {
            int self = (int)Multiplayer.GetUniqueId();
            if (_propManager.FindHeldBy(self) == null)
                return false;
            _holdingSinceSec = _elapsed;
            GD.Print($"[bot] {_options.DisplayName} is holding at {_elapsed:F2}s — " +
                     $"exiting in {_options.ExitWhenHoldingSec:F2}s (--exit-when-holding)");
            return false;
        }
        if (_elapsed - _holdingSinceSec < _options.ExitWhenHoldingSec)
            return false;
        GD.Print($"[bot] {_options.DisplayName} disconnecting at {_elapsed:F2}s WHILE STILL HOLDING");
        Finish();
        return true;
    }

    // NIGHT-2 (--flashlight-at): scripted flashlight toggles over the real wire. Same elapsed-clock
    // idiom as --capture-at and --flow-ready-at, and it goes through
    // FlashlightManager.ClientRequestToggle — the SAME method the F key calls — so a capture taken
    // between two marks is evidence about the shipped verb rather than about a capture-only hook.
    // No-op cost when the flag was not passed.
    private readonly HashSet<int> _flashlightFired = new();

    private void MaybeToggleFlashlight()
    {
        if (_options.FlashlightAtSec.Count == 0)
            return;
        if (Light.FlashlightManager.Instance is not { } flashlight)
            return;
        for (int i = 0; i < _options.FlashlightAtSec.Count; i++)
        {
            if (_elapsed >= _options.FlashlightAtSec[i] && _flashlightFired.Add(i))
            {
                GD.Print($"[bot] {_options.DisplayName} toggling flashlight at {_elapsed:F1}s " +
                         $"(lit peers before: {flashlight.LitCount})");
                flashlight.ClientRequestToggle();
            }
        }
    }

    // HONK-1 (--honk-at): scripted honk presses over the real wire. Same elapsed-clock idiom as
    // --flashlight-at above, and for the same reason: it goes through
    // HonkManager.ClientRequestHonk — the SAME method the H key calls — so what a suite proves is
    // the shipped verb rather than a test-only hook.
    //
    // The mash case is expressed as several marks a few hundredths of a second apart rather than
    // as a "spam for N seconds" flag, deliberately: the suite then quotes an EXACT number of
    // presses ("6 presses inside 0.5 s produced 1 honk"), and a count the runner typed is a count
    // it can assert against, where a duration would only let it assert an inequality.
    private readonly HashSet<int> _honkFired = new();

    private void MaybeHonk()
    {
        if (_options.HonkAtSec.Count == 0)
            return;
        if (Honk.HonkManager.Instance is not { } honk)
            return;
        for (int i = 0; i < _options.HonkAtSec.Count; i++)
        {
            if (_elapsed >= _options.HonkAtSec[i] && _honkFired.Add(i))
            {
                bool sent = honk.ClientRequestHonk();
                GD.Print($"[bot] {_options.DisplayName} honk press #{i} at {_elapsed:F3}s " +
                         $"(request {(sent ? "sent" : "suppressed by the local cooldown copy")})");
            }
        }
    }

    // HONK-1 (--honk-forge): the forgery probe. Fires the honk BROADCAST rpc from this CLIENT
    // straight at every other peer, naming a peer id that is not this bot's — the attack
    // HonkManager's doc claims RpcMode.Authority makes impossible. It exists so that claim is a
    // measured fact rather than a reading of an attribute: the suite asserts the victim's
    // received AND heard counters for the claimed id both stay at zero.
    private bool _honkForged;

    private void MaybeForgeHonk()
    {
        if (_options.HonkForgeAtSec < 0 || _honkForged || _elapsed < _options.HonkForgeAtSec)
            return;
        if (Honk.HonkManager.Instance is not { } honk)
            return;
        // Peer ids come from the replicated player list, NOT from Multiplayer.GetPeers(). Measured
        // the hard way: on an ENet CLIENT, GetPeers() returns only the server (id 1), so the
        // original spelling of this loop attempted zero forgeries and the suite's forgery check
        // passed vacuously — it stayed green with RpcMode.Authority deliberately downgraded to
        // AnyPeer. An attacker has the same list this does (they can see everyone's avatar), so
        // this is also the more honest model of the attack.
        List<(int Id, string Name)> targets = Voice.VoiceManager.Instance.GetRemotePlayers();
        _honkForged = true;
        int self = (int)Multiplayer.GetUniqueId();
        int attempts = 0;
        foreach ((int target, string _) in targets)
        {
            // Two lies in each call: aimed client->client (routed past the server's own authority)
            // and claiming a honker id that is not this peer's.
            //
            // The two claimed ids are chosen so a landed forgery is UNAMBIGUOUS: this bot's own id
            // and the victim's own id both belong to peers that never legitimately honk in this
            // suite, so any nonzero count against either of them can only have come from here.
            // Claiming the real honker's id would land in the same counter its real honks land in
            // and be unassertable.
            honk.TestForgeHonk(target, self);
            honk.TestForgeHonk(target, target);
            attempts += 2;
        }
        GD.Print($"[bot] {_options.DisplayName} FORGED {attempts} honk broadcasts at {_elapsed:F2}s " +
                 $"across {targets.Count} remote peers (client->client, claiming other peers' ids)");
    }

    // HONK-1 (--honk-flood): the "modified client" probe. For HonkFloodDurationSec from the mark,
    // ask the server for a honk EVERY FRAME with the client-side cooldown copy bypassed. The
    // shipped path cannot reach the server's own latch — a real masher is stopped locally first —
    // so without this the authoritative half of the anti-spam rule is an unexercised branch.
    // Precedent and reasoning: --voice-flood, and HonkManager.TestRawRequest's doc.

    /// <summary>How long <c>--honk-flood</c> hammers for. Two seconds is three-and-a-bit cooldowns:
    /// long enough that the granted count is a rate rather than an accident of timing, short enough
    /// that it does not dominate a suite's runtime.</summary>
    public const double HonkFloodDurationSec = 2.0;

    private int _honkFloodSent;
    private bool _honkFloodReported;

    private void MaybeFloodHonk()
    {
        if (_options.HonkFloodAtSec < 0 || _elapsed < _options.HonkFloodAtSec)
            return;
        if (Honk.HonkManager.Instance is not { } honk)
            return;
        if (_elapsed < _options.HonkFloodAtSec + HonkFloodDurationSec)
        {
            honk.TestRawRequest();
            _honkFloodSent++;
            return;
        }
        if (_honkFloodReported)
            return;
        _honkFloodReported = true;
        // The window AND the server's cooldown are printed, not just the count, so the runner can
        // DERIVE its ceiling instead of typing one beside a constant that lives here. Raising
        // HonkFloodDurationSec would otherwise turn a legitimate result into a false red blaming
        // the server (.claude/rules/test-suite.md, "derive bot lifetimes from the schedule").
        GD.Print($"[bot] {_options.DisplayName} honk flood: {_honkFloodSent} raw requests " +
                 $"over {HonkFloodDurationSec:F2}s window against a " +
                 $"{Honk.HonkConfig.CooldownSec:F2}s server cooldown (client cooldown bypassed)");
    }

    // CORE-PROG-A1 (--flow-ready-at / --flow-play-again-at): scripted client→server flow
    // requests over the real wire — RequestReadyAdvance marks may land in-state (exercising
    // the all-ready skip) or out-of-state (exercising the server's silent stale-echo drop,
    // spec §3.3); the Play Again mark drives T10 from a real remote peer. Same elapsed-clock
    // idiom as --capture-at above. No-op cost when neither flag was passed.
    private readonly HashSet<int> _flowReadyFired = new();
    private bool _flowPlayAgainSent;

    private void MaybeSendFlowRequests()
    {
        if (Sail.Game.Run.PlaythroughDriver.Instance is not { } flow)
            return;
        for (int i = 0; i < _options.FlowReadyAtSec.Count; i++)
        {
            if (_elapsed >= _options.FlowReadyAtSec[i] && _flowReadyFired.Add(i))
            {
                GD.Print($"[bot] {_options.DisplayName} sending RequestReadyAdvance at {_elapsed:F1}s");
                flow.SendReadyAdvance();
            }
        }
        if (_options.FlowPlayAgainAtSec >= 0 && !_flowPlayAgainSent && _elapsed >= _options.FlowPlayAgainAtSec)
        {
            _flowPlayAgainSent = true;
            GD.Print($"[bot] {_options.DisplayName} sending RequestPlayAgain at {_elapsed:F1}s");
            flow.SendPlayAgain();
        }
    }

    /// <summary>Fires the next due --capture-at mark, at most one per frame. Skipped entirely
    /// when --capture-dir is unset (every CI run), and a no-op under --headless, where there is
    /// no rendered viewport to read — the caller gets a loud warning rather than a 0-byte png.
    /// </summary>
    private void MaybeCapture()
    {
        if (_capturing
            || _options.CaptureDir.Length == 0
            || _nextCaptureIndex >= _options.CaptureAtSec.Count
            || _elapsed < _options.CaptureAtSec[_nextCaptureIndex])
        {
            return;
        }
        double mark = _options.CaptureAtSec[_nextCaptureIndex];
        _nextCaptureIndex++;
        _capturing = true;
        _ = CaptureAsync(mark);
    }

    /// <summary>
    /// <b>The tick-anchored capture</b> (ANIM-M3, <c>--capture-at-tick</c>). Fires when the shared
    /// replicated clock reaches each mark, at most one per frame.
    ///
    /// <para><b>Why it is a separate method and a separate mark list rather than a mode on
    /// <see cref="MaybeCapture"/>.</b> The two answer different questions and both are legitimate:
    /// "what does this look like eight seconds in" is a question about one process, and
    /// "do these two clients agree at tick 900" is a question about two. A caller may want both in
    /// one run, and folding them into one list would make the marks ambiguous.</para>
    ///
    /// <para><b>It says so out loud when it cannot work.</b> A process with no remote proxy has no
    /// shared clock (see <c>NetClock</c>), and silently substituting elapsed seconds there is
    /// precisely the false-parity the flag exists to prevent — so it warns once and captures
    /// nothing.</para>
    /// </summary>
    private void MaybeCaptureAtTick()
    {
        if (_capturing
            || _options.CaptureDir.Length == 0
            || _nextTickCaptureIndex >= _options.CaptureAtTick.Count)
        {
            return;
        }

        long now = Net.NetClock.ObservedTick;
        if (now == Net.NetClock.Unavailable)
        {
            // Only after a few seconds: the clock is legitimately unavailable for the first frames
            // of every session, before the first snapshot lands.
            if (!_warnedNoNetClock && _elapsed > 5.0)
            {
                _warnedNoNetClock = true;
                GD.PushWarning(
                    $"[bot] --capture-at-tick was given {_options.CaptureAtTick.Count} mark(s) but " +
                    "this process has no remote proxy, so there is no shared replicated clock to " +
                    "anchor them to. Nothing will be captured on ticks. Two clients OBSERVING a " +
                    "third body is the shape this flag is for.");
            }
            return;
        }

        if (now < _options.CaptureAtTick[_nextTickCaptureIndex])
            return;

        long mark = _options.CaptureAtTick[_nextTickCaptureIndex];
        _nextTickCaptureIndex++;
        _capturing = true;
        GD.Print($"[bot] {_options.DisplayName} capture at replicated tick {mark} " +
                 $"(observed {now}, elapsed {_elapsed:F2}s)");
        _ = CaptureAsync(mark, "t");
    }

    private async System.Threading.Tasks.Task CaptureAsync(double mark, string unitSuffix = "s")
    {
        try
        {
            // Stem: --capture-at's combined "dir,name,..." form names it explicitly; otherwise the
            // bot's own display name, which is what keeps two bots capturing one session from
            // overwriting each other. See LaunchOptions.CaptureName's merge note.
            string stem = _options.CaptureName.Length > 0 ? _options.CaptureName : _options.DisplayName;
            string path = Path.Combine(
                _options.CaptureDir,
                $"{stem}-{mark.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}{unitSuffix}.png");
            // The frame settle, the headless guard and the write all live in ViewportCapture —
            // one implementation shared with the dev screenshot key, so a fix to either applies
            // to both rather than to whichever copy someone remembered.
            await Dev.ViewportCapture.SaveAsync(this, path, "bot");
        }
        catch (System.Exception e)
        {
            GD.PushError($"[bot] capture at {mark}{unitSuffix} failed: {e}");
        }
        finally
        {
            _capturing = false;
        }
    }

    public override void _ExitTree()
    {
        _writer?.Dispose();
        _writer = null;
    }

    private void WriteSample()
    {
        long selfId = Multiplayer.GetUniqueId();
        var peers = new List<PeerSample>();
        // Reconnect-teardown instrumentation (audit P1 / Task A1): avatarCount counts only
        // SandboxAvatar children (a stray un-freed camera — see SandboxCamera's doc comment on
        // where it's parented — would otherwise inflate this); dupNames lists any _players child
        // whose Godot-assigned name contains "@", the auto-rename Godot itself applies when a
        // MultiplayerSpawner replay collides with a node name already present in the tree (the
        // exact symptom of a stale un-torn-down node from a prior connection — see
        // Gameplay.TeardownReplicatedNodes). Both should read back to their pre-drop values after
        // a clean resume; pre-fix, a forced reconnect leaves this nonzero/inflated.
        int avatarCount = 0;
        var dupNames = new List<string>();
        // Holder-peer -> that avatar, so a held prop below can be measured against its holder's
        // live carry anchor (the drift regression's discriminating signal — see PropSample.Off).
        var avatarsById = new Dictionary<long, SandboxAvatar>();
        foreach (Node child in _players.GetChildren())
        {
            string childName = child.Name.ToString();
            if (childName.Contains('@'))
                dupNames.Add(childName);
            if (child is SandboxAvatar)
                avatarCount++;
            if (child is SandboxAvatar player && long.TryParse(player.Name.ToString(), out long id))
            {
                avatarsById[id] = player;
                Vector3 pos = player.Position;
                // Netcode health metrics: our own avatar reports its last
                // predicted-vs-authoritative correction and current smoothing offset;
                // remote avatars report their peak per-frame rendered movement since the
                // last sample (a teleport shows up as a huge step). The reconciliation
                // and smoothness suites assert on these.
                float pe = 0, ve = 0, mrs = 0;
                if (id == selfId)
                {
                    pe = player.LastCorrectionM;
                    ve = player.VisualErrorM;
                }
                else
                {
                    mrs = player.TakeMaxRenderStep();
                }
                // Voice-route view: would this peer's voice reach me over the PA bus right
                // now, per VoiceManager.PaResolver (null by default in the foundation)?
                bool pa = Voice.VoiceManager.Instance.DescribeRouteFor((int)id) == "pa";
                // Avatar-identity replication instrumentation (P3, 2026-07-23): this peer's
                // live view of every player's roster key, so the test orchestrator can prove
                // every bot agrees on every player's model (including a late joiner's FIRST
                // sample) and that an invalid/spoofed key never survives as anything but the
                // clamped default. See docs/superpowers/2026-07-23-wp-avatar-identity-dispatch.md.
                // Aim-substrate instrumentation (WP-L3, Issue #106): this peer's live view of
                // that player's raise/lower stance (as an int — the enum ordinal is the wire
                // contract, see AimStance's doc comment) and steadiness, so Run-AimTest.ps1 can
                // prove AimStance replicates identically to every observer, including remote
                // proxies (not just the owner's own local prediction).
                // Carry-pose instrumentation (W7-8, 2026-08-30): this peer's live view of HOW that
                // player's body is holding whatever it is holding (CarryPose ordinal — None 0,
                // Handle 1, Armful 2). Same job and same reason as aimStance directly above: the
                // pose is derived on every peer from replicated registers, so "a crate is held with
                // both arms for EVERYONE, not just its owner" is only checkable if a witness bot can
                // report what it resolved. Run-ArmfulCarryTest.ps1 asserts on it.
                peers.Add(new PeerSample(id, player.DisplayName, player.AvatarKey, pos.X, pos.Y, pos.Z, pe, ve, mrs, pa,
                    (int)player.AimStance, player.AimSteadiness01, (int)player.CarryPoseNow));
            }
        }
        var props = new List<PropSample>();
        foreach (NetworkedProp prop in _propManager.AllProps())
        {
            Vector3 pp = prop.WorldPosition;
            // Carry-drift instrumentation (two complementary scalars, both 0 when unheld or the
            // holder avatar isn't on this peer). Measured directly against SandboxAvatar's own
            // authoritative accessors — the carry anchor and the rendered body position — never
            // reconstructed externally from position + rest offset + bob, which the bob
            // oscillation would make fragile enough to manufacture fake drift:
            //   Off = |prop - CarryAnchorGlobalTransform.Origin|: the held item's chase lag
            //         behind its anchor (the by-design FollowLerp lag). Isolates a chase bug.
            //   Bd  = |prop - RenderGlobalPosition|: the held item's distance from the rendered
            //         body — exactly what a player sees. This is the drift signal: it sums the
            //         WHOLE chain (body -> anchor -> item), so a drifting anchor (its local↔global
            //         round trip, a reconciliation bias) shows up here even though the item still
            //         chases the anchor perfectly and Off stays bounded. Correct behaviour keeps
            //         this near |CarryAnchorRest| (~0.88m); accumulating drift makes it climb.
            float off = 0f, bd = 0f;
            if (prop.HolderPeerId != 0 && avatarsById.TryGetValue(prop.HolderPeerId, out SandboxAvatar? holder))
            {
                off = (pp - holder.CarryAnchorGlobalTransform.Origin).Length();
                bd = (pp - holder.RenderGlobalPosition).Length();
            }
            props.Add(new PropSample(prop.PropId, (int)prop.Kind, prop.HolderPeerId, pp.X, pp.Y, pp.Z, off, bd));
        }
        // NetworkedEntity instrumentation (empty unless the session spawned any). Mirrors the
        // remote-avatar metrics above deliberately: `mrs` is the peak per-frame rendered movement
        // since the last sample, and NetworkedEntity.TakeMaxRenderStep EXCLUDES samples the
        // SnapshotBuffer flagged as teleports. So a run where the entity jumps 28m between anchors
        // but `mrs` stays small is positive proof the epoch-bump snap works; a broken epoch signal
        // would interpolate the gap and show up here as a huge step. Run-EntityTest.ps1 asserts
        // both that the jump happened (from x/z) and that mrs stayed bounded across it.
        //
        // entity.Synced guard (found 2026-08-06): a DYNAMICALLY-spawned entity can exist in this
        // peer's tree — MultiplayerSpawner's replication landed — for one or more sample intervals
        // before its first snapshot has arrived; until then RenderGlobalPosition reads the
        // un-teleported default (0,0,0), a real-looking but fictitious world position, not an
        // obviously-fake sentinel. An entity spawned once at session start never exposes this,
        // because it lands long before any bot's first sample. Skipping an un-Synced entity here
        // means a caller (e.g. the cross-peer position-agreement check) never mistakes "hasn't
        // arrived yet" for "arrived at the origin" — same discipline every Synced flag elsewhere
        // in this codebase already applies.
        var entities = new List<EntitySample>();
        if (_entities is Node3D entityRoot)
        {
            foreach (Node child in entityRoot.GetChildren())
            {
                if (child is not NetworkedEntity entity || !entity.Synced)
                    continue;
                Vector3 ep = entity.RenderGlobalPosition;
                entities.Add(new EntitySample(entity.Name.ToString(), ep.X, ep.Y, ep.Z,
                    entity.TakeMaxRenderStep()));
            }
        }
        // Reconnect held-prop-restore instrumentation (P2 / Task A2): this peer's own converged
        // view of what it currently holds (PropManager.FindHeldBy — the same funnel every other
        // holder-state read in this codebase uses), or -1 for "holds nothing". Post-resume, this
        // should read back to the id it held pre-drop if the grace record still had it (and
        // nobody grabbed it during the gap); -1 if a rival grabbed it first (first-grab-wins,
        // no steal-back) or the grace record had nothing.
        int heldPropId = _propManager.FindHeldBy((int)selfId)?.PropId ?? -1;
        // Palette-restore instrumentation (P11 / Task A3): this peer's own dealt color, as a
        // palette index (SandboxAvatar.PaletteIndexFor, the reverse of PaletteColorFor), or -1
        // if its avatar isn't in this peer's view yet (e.g. the very first sample before the
        // spawn replay lands). A resumed peer's colorIdx should read back to its pre-drop value
        // if the grace record restored it; -1 has no meaning here (index 0 is a real color) so a
        // resumed avatar without a valid palette match would surface as -1 too, same as "no
        // avatar yet". See Run-ReconnectTest.ps1 for what this can and cannot prove over ENet.
        int colorIdx = avatarsById.TryGetValue(selfId, out SandboxAvatar? selfAvatar)
            ? SandboxAvatar.PaletteIndexFor(selfAvatar.BodyColor)
            : -1;
        // Cycle-clock instrumentation (CycleDriver): this peer's own converged view of the
        // tidal-loop phase clock. `synced` is what Run-CycleTest.ps1's late-join assertion
        // keys on — a sample logged before the server's first phase delivery lands must read
        // synced=false, never a silently-plausible phase=0 that could be mistaken for "the
        // server really is at phase 0" (see CycleDriver.Synced's doc comment).
        float cyclePhase = CycleDriver.Instance?.Phase ?? 0f;
        int cyclesElapsed = CycleDriver.Instance?.CyclesElapsed ?? 0;
        bool cycleSynced = CycleDriver.Instance?.Synced ?? false;
        // Run-driver instrumentation (L1, Issue #104): this peer's own converged view of the
        // typed phase-crossing history, run length, and run-end/reset state — polled directly
        // off RunDriver.Instance every sample, the exact same style CycleDriver.Phase above is
        // polled (no subscription-timing race: whichever events have landed on THIS peer by the
        // time this sample is taken are already in History). Run-RunDriverTest.ps1 reads the
        // FINAL sample's cumulative History to assert exactly-once-in-order per peer, and a
        // pre-Synced sample (runSynced=false) must never be mistaken for "the run really has
        // zero configured cycles" — same reasoning as cycleSynced above.
        int runCycles = RunDriver.Instance?.RunCycles ?? 0;
        bool runEnded = RunDriver.Instance?.RunEnded ?? false;
        bool runSynced = RunDriver.Instance?.Synced ?? false;
        List<PhaseEventSample> runHistory = RunDriver.Instance != null
            ? new List<PhaseEventSample>(RunDriver.Instance.History)
            : new List<PhaseEventSample>();
        // L11 (Issue #114) instrumentation: LoopUiTelemetry mirrors the actual (non-headless-only)
        // UI's own dismiss/toast/summary decisions off the SAME pure functions the real UI uses —
        // see that class's doc. Present on every peer including a bot (unlike the real CanvasLayer
        // UI), so a headless run can prove the loading overlay never strands, exactly which phase
        // toasts fired and in what order, and the summary/reset lifecycle, without a windowed
        // client. Null only if this peer's Gameplay somehow never wired one in (never in practice —
        // see the unconditional AddChild in Gameplay._Ready) — defaulted defensively rather than
        // risking a NullReferenceException taking down the whole bot process mid-run.
        bool loopUiOverlayDismissed = LoopUiTelemetry.Instance?.OverlayDismissed ?? false;
        List<byte> loopUiToasts = LoopUiTelemetry.Instance != null
            ? new List<byte>(LoopUiTelemetry.Instance.ToastsFired)
            : new List<byte>();
        bool loopUiSummaryShown = LoopUiTelemetry.Instance?.SummaryShown ?? false;
        int loopUiResetCount = LoopUiTelemetry.Instance?.ResetCount ?? 0;
        // CORE-PROG-A1 flow/quota instrumentation: this peer's own converged view of the
        // playthrough state machine and the quota ledger, polled the same style as the
        // run/cycle fields above (no subscription-timing race). flowSynced=false must never be
        // mistaken for "the playthrough really is in Boot" and quotaSynced=false must never be
        // mistaken for "nothing is banked yet" — both zero defaults are true-sounding wrong
        // answers, the same discipline as cycleSynced/runSynced above.
        // FlowTransitions is LoopUiTelemetry's applied-transition mirror ("from>to@round"),
        // which is what lets Run-FlowTest.ps1 assert the exact transition sequence per peer
        // rather than inferring it from sampled states that can skip a short-lived one.
        var flowDriver = Sail.Game.Run.PlaythroughDriver.Instance;
        int flowState = (int)(flowDriver?.State ?? Sail.Game.Run.PlaythroughState.Boot);
        int flowRound = flowDriver?.Round ?? 0;
        bool flowSynced = flowDriver?.Synced ?? false;
        double flowRemainingSec = flowDriver?.StateRemainingSec ?? -1;
        int flowOutcomeKind = flowDriver?.LastOutcome is { } flowOutcome ? (int)flowOutcome.Kind : -1;
        List<string> flowTransitions = LoopUiTelemetry.Instance != null
            ? new List<string>(LoopUiTelemetry.Instance.FlowTransitions)
            : new List<string>();
        var quota = Sail.Game.Run.QuotaLedger.Instance;
        bool quotaSynced = quota?.Synced ?? false;
        int quotaBanked = quota?.CumulativeBanked ?? 0;
        int quotaBankedRound = quota?.BankedThisRound ?? 0;
        int quotaDemandRound = quota?.DemandCurrentRound ?? 0;
        int quotaCumDemand = quota?.CumulativeDemand ?? 0;
        int quotaLastDenial = (int)(quota?.LastDenial ?? Sail.Game.Run.QuotaLedger.QuotaDenial.None);
        // BT-6 bubble instrumentation. Null on every launch without --bubble-selftest and in any
        // world with no BubbleCounter, which is what keeps every other suite's JSONL byte-identical.
        //
        // FIVE FIELDS, and they are five on purpose: "the peers agree" is worth
        // nothing from one number every peer could have arrived at alone. Count is the replicated
        // tally, Popped is this peer's own bitset, Hidden is read off the actual scene nodes, and
        // the three are told by different mechanisms — a peer whose count matches while a bubble
        // is still rendering has a bug the tally alone cannot see. Synced separates "agrees with
        // the server" from "has not heard from the server yet", which is the whole late-join
        // assertion. MaxOffset is acceptance criterion 6's measured quantity.
        BubbleSample? bubble = null;
        if (Sail.Game.Bubble.BubbleCounter.Instance is { } bubbleCounter)
        {
            var hidden = new List<int>();
            foreach (Node node in GetTree().GetNodesInGroup(Sail.Game.Bubble.Bubble.Group))
            {
                if (node is Sail.Game.Bubble.Bubble b && b.Popped && b.Id >= 0)
                    hidden.Add(b.Id);
            }
            hidden.Sort();
            bubble = new BubbleSample(
                bubbleCounter.Synced, bubbleCounter.Count, bubbleCounter.PoppedIds(), hidden,
                bubbleCounter.BubbleCount,
                Sail.Game.Bubble.BubbleSelfTest.Instance?.MaxOffsetM ?? 0f,
                bubbleCounter.CurrentEmissionEnergy,
                Sail.Game.Bubble.BubbleCelebration.Received,
                Sail.Game.Bubble.BubbleCelebration.Celebrated);
        }
        var sample = new Sample(Time.GetTicksMsec(),
            System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), // cross-process comparable
            Multiplayer.GetUniqueId(), peers, props,
            Voice.VoiceManager.Instance.GetReceiveCounts(),
            avatarCount, _propManager.RuntimePropCount, dupNames, heldPropId, colorIdx,
            cyclePhase, cyclesElapsed, cycleSynced,
            runCycles, runEnded, runSynced, runHistory, entities,
            loopUiOverlayDismissed, loopUiToasts, loopUiSummaryShown, loopUiResetCount,
            flowState, flowRound, flowSynced, flowRemainingSec, flowOutcomeKind, flowTransitions,
            quotaSynced, quotaBanked, quotaBankedRound, quotaDemandRound, quotaCumDemand, quotaLastDenial,
            bubble,
            Honk.HonkManager.Instance?.GetHeardCounts(),
            Honk.HonkManager.Instance?.GetReceivedCounts());
        string line = JsonSerializer.Serialize(sample, JsonOptions);
        if (_writer != null)
        {
            _writer.WriteLine(line);
            _writer.Flush();
        }
        else
        {
            GD.Print(line);
        }
    }

    private void Finish()
    {
        _done = true;
        WriteSample(); // final settled sample
        _writer?.Dispose();
        _writer = null;
        GD.Print($"[bot] {_options.DisplayName} done");
        GetTree().Quit(0);
    }

    // Voice maps sender peer id (string, for JSON keys) -> packets received from them; the voice
    // test asserts relay + sender tagging from these counters. Wall is Unix milliseconds
    // (comparable across processes on one machine, unlike engine ticks).
    // AvatarCount/PropCount/DupNames: reconnect-teardown instrumentation (audit P1 / Task A1) —
    // see the doc comment at their computation site in WriteSample. HeldPropId: held-prop-restore
    // instrumentation (P2 / Task A2) — same site. ColorIdx: palette-restore instrumentation
    // (P11 / Task A3) — same site.
    private sealed record Sample(ulong T, long Wall, long Self, List<PeerSample> Peers, List<PropSample> Props,
        Dictionary<string, long> Voice,
        int AvatarCount, int PropCount, List<string> DupNames, int HeldPropId, int ColorIdx,
        float CyclePhase, int CyclesElapsed, bool CycleSynced,
        int RunCycles, bool RunEnded, bool RunSynced, List<PhaseEventSample> RunHistory,
        List<EntitySample> Entities,
        // L11 (Issue #114): see the computation site in WriteSample for what each field proves.
        // LoopUiToasts is PhaseEventKind values (byte) in firing order, only the ones that
        // produced a toast line (DawnToDay is deliberately excluded — see PhaseToastText).
        bool LoopUiOverlayDismissed, List<byte> LoopUiToasts, bool LoopUiSummaryShown, int LoopUiResetCount,
        // CORE-PROG-A1 playthrough-flow + quota instrumentation — see the computation site in
        // WriteSample for what each field proves. FlowState is a PlaythroughState ordinal,
        // FlowOutcomeKind a RunOutcomeKind ordinal (-1 = no latched outcome), FlowTransitions
        // the applied "from>to@round" sequence, QuotaLastDenial a QuotaDenial ordinal.
        int FlowState, int FlowRound, bool FlowSynced, double FlowRemainingSec,
        int FlowOutcomeKind, List<string> FlowTransitions,
        bool QuotaSynced, int QuotaBanked, int QuotaBankedRound, int QuotaDemandRound,
        int QuotaCumDemand, int QuotaLastDenial,
        // BT-6 bubble instrumentation. Null outside --bubble-selftest / a world with no
        // BubbleCounter — see the computation site in WriteSample for what each field proves.
        BubbleSample? Bubble,
        // HONK-1 instrumentation. Null in a scene with no HonkManager — which is a null in the
        // JSON, not an absent key: JsonOptions is JsonSerializerDefaults.Web, which sets camelCase
        // but NOT DefaultIgnoreCondition, so every bot log in the repo now carries
        // "honkHeard":null,"honkReceived":null. Nothing compares JSONL bytes today (the only
        // Get-FileHash in tests/ hashes generator output), and Bubble above is already in the same
        // position — but a future schema check would need to know. TWO maps, and that is the whole
        // point: HonkHeard counts
        // the honks this peer PLAYED and HonkReceived the messages that ARRIVED. A far peer with
        // received > 0 and heard == 0 proves the range rule culled it; received == 0 alone would
        // equally be a broken wire, so one map could not tell the two apart. Both are keyed by the
        // HONKER's peer id, as strings, for JSON.
        Dictionary<string, long>? HonkHeard = null, Dictionary<string, long>? HonkReceived = null);

    /// <summary>This peer's converged view of the shared bubble tally (BT-6). Popped is the
    /// bitset's own answer; Hidden is read off the scene nodes; Adopted is how many bubbles this
    /// peer's own AdoptAuthoredBubbles found (a peer that adopted a different number has an id
    /// space that cannot be compared at all, so it is worth knowing before any other assertion is
    /// believed). MaxOffset is the worst distance any bubble has sat from its authored position
    /// on this peer, and Glow the emission energy currently written to the shared film.
    ///
    /// <para>CELEBRATE-1 adds the last two, and they are TWO for exactly the reason
    /// <c>HonkHeard</c>/<c>HonkReceived</c> above are two: <c>CelebrateReceived</c> counts the
    /// completion broadcasts that ARRIVED on this peer and <c>CelebratePlayed</c> the ones its
    /// human-input gate let through. A bot with <c>received &gt; 0</c> and <c>played == 0</c>
    /// proves the GATE culled it; <c>received == 0</c> alone would equally be a broken wire, and
    /// one number could not tell the two apart. The suite's whole bot-gate assertion is that
    /// pair.</para></summary>
    private sealed record BubbleSample(bool Synced, int Count, List<int> Popped, List<int> Hidden,
        int Adopted, float MaxOffset, float Glow,
        long CelebrateReceived = 0, long CelebratePlayed = 0);

    // One server-simulated NetworkedEntity as this peer sees it: name, rendered position, and the
    // peak per-frame render step since the last sample (teleport frames excluded — see the
    // computation site). Empty unless the session spawned entities.
    private sealed record EntitySample(string Name, float X, float Y, float Z, float Mrs);

    private sealed record PeerSample(long Id, string Name, string AvatarKey, float X, float Y, float Z,
        float Pe, float Ve, float Mrs, bool Pa, int AimStance, float AimSteadiness01, int CarryPose);

    // One networked prop as this peer sees it: id, kind, current holder (0 = none), position, and
    // — while held — two carry-drift scalars: Off = distance from the holder's carry anchor (the
    // by-design chase lag), Bd = distance from the holder's rendered body (the full-chain drift
    // signal a player sees). Both 0 when unheld.
    private sealed record PropSample(int Id, int Kind, int Holder, float X, float Y, float Z,
        float Off, float Bd);
}
