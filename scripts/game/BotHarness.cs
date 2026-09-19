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

    // THE SCRIPTED-VERB PROBES THAT USED TO LIVE HERE went with their systems at the fork
    // (BASE-1, 2026-09-19): --flashlight-at, --honk-at, --honk-forge, --honk-flood and the
    // --flow-ready-at / --flow-play-again-at flow requests. Two things about their SHAPE are
    // worth copying when BTN-1 and ROUND-1 script their own presses, because both were learned
    // the hard way and neither is obvious:
    //
    //   - Drive the SHIPPED verb, not a test-only hook. The scripted presses went through the
    //     same method the key calls, which is what made a capture taken between two marks
    //     evidence about the game rather than about the harness.
    //   - To test a rule the server enforces and the client mirrors, you need a deliberately
    //     NON-COMPLIANT client. The honk flood existed because seven scripted presses through the
    //     shipped verb produced two honks and the server logged nothing at all: the local
    //     courtesy latch suppressed the extras before a packet left the machine, so the suite was
    //     proving the copy while the authoritative rule had never executed once.

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
        var sample = new Sample(Time.GetTicksMsec(),
            System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), // cross-process comparable
            Multiplayer.GetUniqueId(), peers, props,
            Voice.VoiceManager.Instance.GetReceiveCounts(),
            avatarCount, _propManager.RuntimePropCount, dupNames, heldPropId, colorIdx,
            cyclePhase, cyclesElapsed, cycleSynced,
            runCycles, runEnded, runSynced, runHistory, entities);
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
        // (The LoopUi / playthrough-flow / quota / bubble / honk columns were dropped with
        // their systems at the fork - BASE-1, 2026-09-19. What every one of them had in common is
        // the reason to copy the pattern rather than the fields: each recorded a SYNCED flag
        // beside its numbers, because "0 banked" and "has not heard from the server yet" are the
        // same bytes and the second is a true-sounding wrong answer. Anything ROUND-1 samples
        // here needs its own synced flag for that reason.)
        List<EntitySample> Entities);

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
