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
        // Per-frame high-water sample of the 3D voice count (SFX-1). Free unless asked for, and
        // it has to be per-frame as well as per-fire: a steal happens when a NEW sound arrives at
        // a full pool, so sampling only at fire time would record the count after the steal has
        // already made room, never the pressure that caused it.
        if (_options.LogSfx)
            SfxLab.SamplePeak();
        if (_sinceLog >= LogIntervalSec)
        {
            _sinceLog = 0;
            WriteSample();
        }
        MaybeCapture();
        MaybeCaptureAtTick();
        MaybePlace();
        MaybePress();
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

    // --carry-place: fired once, and only once, however the server answers.
    private bool _placeSent;

    /// <summary>
    /// <b>The scripted PLACE</b> (CARRY-1, <c>tests/Run-PlaceTest.ps1</c>): once this bot is
    /// holding something and <c>--carry-place</c>'s delay has elapsed since, ask the server to set
    /// it down at the flag's exact transform.
    ///
    /// <para><b>It calls the same public client entry point a human's E press calls</b> —
    /// <see cref="PropManager.ClientRequestPlace"/> — so what this suite exercises is the real
    /// server-side place path, arbitration and all. What it does NOT exercise is
    /// <c>SandboxAvatar.HandleCarryIntent</c>'s place-vs-drop choice, which is a decision about
    /// where a person is looking and belongs to the two-client gate, not to a bot. That split is
    /// deliberate: the alternative is a test branch inside the verb, and a verb with a test branch
    /// in it is a verb nobody has tested.</para>
    ///
    /// <para><b>Anchored on the observed hold</b> (<see cref="_holdingSinceSec"/>'s sibling logic)
    /// rather than on the wall clock, for the reason <c>--exit-when-holding</c> exists: under a
    /// loaded marathon a bot's walk slips by seconds and a constant does not.</para>
    ///
    /// <para><b>Fires exactly once, refusal included.</b> A refused place is the OUTCOME three of
    /// this suite's four cases are asserting, so retrying would turn a correct refusal into a
    /// flood of identical ones and would make "still held at the end" unfalsifiable.</para>
    /// </summary>
    private void MaybePlace()
    {
        if (!_options.CarryPlace || _placeSent || _propManager == null)
            return;
        int self = (int)Multiplayer.GetUniqueId();
        NetworkedProp? held = _propManager.FindHeldBy(self);
        if (held == null)
        {
            _placeStartedSec = -1;
            return;
        }
        if (_placeStartedSec < 0)
        {
            _placeStartedSec = _elapsed;
            return;
        }
        if (_elapsed - _placeStartedSec < _options.CarryPlaceAfterSec)
            return;
        _placeSent = true;
        Transform3D at = _options.CarryPlaceAt;
        GD.Print($"[bot] {_options.DisplayName} PLACING prop {held.PropId} at "
                 + $"({at.Origin.X:F2}, {at.Origin.Y:F2}, {at.Origin.Z:F2}) at {_elapsed:F2}s");
        _propManager.ClientRequestPlace(held.PropId, at);
    }

    private double _placeStartedSec = -1;

    // --press: index of the next scheduled press. Marks arrive sorted ascending from
    // LaunchOptions, so one cursor is enough — the same shape --capture-at uses.
    private int _nextPressIndex;

    /// <summary>
    /// <b>The scripted button press</b> (BTN-1, <c>tests/Run-ButtonsTest.ps1</c>). Fires the
    /// shipped client verb at each <c>--press</c> mark, at most one per frame.
    ///
    /// <para><b>It calls <c>RoundControls.ClientRequestPress</c></b> — the exact method
    /// <c>RoundButton.Press</c> calls on a human's Interact edge — so the server sees a press
    /// that is indistinguishable from a player's, gate and all. What it does NOT exercise is
    /// <c>InteractHighlighter</c>'s "which interactable did you mean" pick, which is a question
    /// about where a person is looking and belongs to the two-client headed gate rather than to
    /// a bot. Same split, and the same reason, as <see cref="MaybePlace"/>'s.</para>
    ///
    /// <para><b>It does NOT walk the bot to the button.</b> The server re-checks reach against
    /// authoritative positions, so a bot standing on its spawn marker pressing a button across
    /// the room is refused with <c>TooFarAway</c> — which is a real case worth staging, and is
    /// why the suite pairs this flag with <c>--carry-script</c>'s walk when it wants an accepted
    /// press. A press flag that teleported the bot into range would be a flag that cannot
    /// produce the refusal.</para>
    ///
    /// <para><b>Fires exactly once per mark, refusal included</b>, for <see cref="MaybePlace"/>'s
    /// reason: a retry would turn a correct refusal into a flood of identical ones and make "the
    /// round did not start" unfalsifiable.</para>
    /// </summary>
    private void MaybePress()
    {
        if (_nextPressIndex >= _options.PressScript.Count
            || _elapsed < _options.PressScript[_nextPressIndex].AtSec)
        {
            return;
        }
        (string kind, double at) = _options.PressScript[_nextPressIndex];
        _nextPressIndex++;
        Round.RoundButtonKind button = kind switch
        {
            "confirm" => Round.RoundButtonKind.Confirm,
            "end" => Round.RoundButtonKind.End,
            _ => Round.RoundButtonKind.Start,
        };
        Round.RoundControls? controls = Round.RoundControls.Instance;
        if (controls == null)
        {
            // Loud rather than silent: a world with no RoundControls is a world with no buttons,
            // and a suite whose presses went nowhere would read as a round that refused them.
            GD.PushWarning($"[bot] {_options.DisplayName} --press {kind}@{at:F1} had no "
                           + "RoundControls to ask — this world has no round buttons.");
            return;
        }
        GD.Print($"[bot] {_options.DisplayName} PRESSING {button} at {_elapsed:F2}s "
                 + $"(scheduled {at:F1}s)");
        controls.ClientRequestPress(button);
    }

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
                // INT-0: commanded teleports (epoch bumps) this owner CONSUMED since the last
                // sample, and how far the last one moved it. Take-and-reset, so it cannot be
                // missed the way LastCorrectionM is: pe is overwritten by every one of the ~12
                // ordinary sub-centimetre reconciles between two samples, and a teleport
                // therefore leaves no trace in it at all. Self only -- a remote proxy does not
                // reconcile, and its teleports are already excluded from mrs by design.
                int tp = 0;
                float tj = 0;
                if (id == selfId)
                {
                    pe = player.LastCorrectionM;
                    ve = player.VisualErrorM;
                    (tp, tj) = player.TakeCommandedTeleports();
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
                    (int)player.AimStance, player.AimSteadiness01, (int)player.CarryPoseNow, tp, tj));
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
            // The prop's ORIENTATION, as a normalized quaternion. CARRY-1's place suite has to
            // prove the observing peer sees a placed prop within 5 DEGREES of the intended pose,
            // and position alone cannot see a crate that landed square but face-down. Logged for
            // every prop rather than only held ones, because the assertion is made on a peer that
            // is not the holder and about a prop that is at rest by then.
            Quaternion q = prop.Body.GlobalBasis.Orthonormalized().GetRotationQuaternion();
            // The holder-side spring's live gap from the hand it is chasing (0 on every peer that
            // is not the holder). This is the spring-vs-anchor mismatch the CARRY-1 handoff
            // reports, sampled rather than estimated — see NetworkedProp.SpringLagM.
            // FEEL-1: the two numbers Talon's ride verdict turned into gates.
            //   Cap = signed clearance between the held prop and its HOLDER'S CAPSULE. Negative
            //         is the prop inside the person carrying it -- "the item clips into their
            //         body, which is stupid and feels bad" -- and Run-CarryHoldTest asserts it
            //         never goes negative, on the holder's own view AND on a witness.
            //   Spd = how fast this prop is observed to be moving, from whichever source this
            //         peer has (a simulating body's own velocity, or the loose stream's observed
            //         speed on a peer that is only watching). The domino bar reads it: a held
            //         crate driven into a stack must not fling anything.
            props.Add(new PropSample(prop.PropId, (int)prop.Kind, prop.HolderPeerId, pp.X, pp.Y, pp.Z, off, bd,
                q.X, q.Y, q.Z, q.W, prop.SpringLagM, prop.HolderClearanceM,
                Mathf.Max(prop.Body.LinearVelocity.Length(), prop.Body.ObservedSpeedMps)));
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
        // FIRST-PERSON LENS instrumentation (INT-0, 2026-09-19 -- the FP-1 x ROUND-1 cross-lane
        // seam). Nothing else in this log can see the camera. `Peers` is a list of AVATAR NODE
        // positions, and the first-person lens is a CHILD of this peer's own avatar, so a lens
        // that had gone stale, been unparented, or gone NaN across a ROUND-1 room teleport would
        // read in `Peers` as a perfectly healthy body standing in the right room. The claim the
        // cross-lane check has to settle is "the eye went with the body", and `Off` -- the
        // distance from the avatar's own origin to the lens, which is the eyeline offset and
        // nothing else -- is the quantity that says so: it must hold its pre-teleport value
        // through the jump, on the same sample where `Pe` (the owner's last prediction
        // correction) spikes with the epoch bump.
        //
        // Null unless this process actually built a first-person rig (--first-person-cam,
        // --first-person-selftest, or a human client). A null here is "this bot has no lens",
        // never "the lens is at the origin" -- the same true-sounding-wrong-answer trap every
        // Synced flag in this record exists for.
        LensSample? lens = null;
        if (avatarsById.TryGetValue(selfId, out SandboxAvatar? lensBody)
            && lensBody.GetNodeOrNull<Sandbox.FirstPersonCamera>("FirstPersonCamera") is { } fpRig
            && fpRig.CameraNode is { } fpLens)
        {
            Vector3 eye = fpLens.GlobalPosition;
            Vector3 fwd = -fpLens.GlobalTransform.Basis.Z;
            Vector3 body = lensBody.GlobalPosition;
            bool nan = !eye.IsFinite() || !fwd.IsFinite() || !body.IsFinite();
            lens = new LensSample(eye.X, eye.Y, eye.Z, fwd.X, fwd.Y, fwd.Z,
                nan ? -1f : body.DistanceTo(eye), fpRig.Yaw, fpRig.Pitch, fpLens.Current, nan);
        }
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
        // Hide-seek round instrumentation (ROUND-1): this peer's own converged view of the round,
        // polled off HideSeekDriver.Instance every sample in exactly the style CycleDriver.Phase
        // above is polled. `roundSynced` is the whole reason this is a block of fields rather than
        // one: "Holding, round 1, nobody scoring" and "I have not heard from the server yet" are
        // the same bytes, and the second is a true-sounding wrong answer — the trap the comment on
        // the Sample record below was written about, honoured here.
        //
        // The SCORES are flattened to a string-keyed map because JSON object keys are strings and
        // a peer id is an int; the round's whole wire design refuses to clamp an identity, so it
        // is not about to lose one to a serializer. Run-RoundLoopSmoke.ps1 compares two peers'
        // maps for equality at Tally, which is the "both received the same wire" assertion.
        var round = Round.HideSeekDriver.Instance;
        Round.HideSeekView roundView = round?.View ?? default;
        var roundScores = new Dictionary<string, int>();
        if (roundView.Scores is not null)
            foreach (KeyValuePair<int, int> row in roundView.Scores)
                roundScores[row.Key.ToString()] = row.Value;
        var roundTally = roundView.LastTally;
        // The newest PLACE refusal this peer was told about, as the wire ordinal
        // (PropManager.PlaceDenial), or 0 for "none so far". A place suite asserting that a
        // placement into a wall was refused needs the REASON, not just the absence of a
        // placement — "refused for the right reason" and "the packet never arrived" look
        // identical from the outside, which is the whole argument for the enum existing.
        int placeDeny = (int)_propManager.LastPlaceDenial;
        var sample = new Sample(Time.GetTicksMsec(),
            System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), // cross-process comparable
            Multiplayer.GetUniqueId(), peers, props,
            Voice.VoiceManager.Instance.GetReceiveCounts(),
            avatarCount, _propManager.RuntimePropCount, dupNames, heldPropId, colorIdx,
            cyclePhase, cyclesElapsed, cycleSynced,
            runCycles, runEnded, runSynced, runHistory,
            lens,
            round?.Synced ?? false, (int)roundView.Phase, roundView.Round, roundView.RemainingSec,
            roundView.HiderPeerId, roundView.SeekerPeerId, (int)roundView.Refusal,
            roundView.FoundTick, roundView.SortsCompleted, roundScores,
            roundTally is { } card
                ? new RoundTallySample(card.RoundIndex, card.HiderPeerId, card.HiderGained,
                    card.SeekerPeerId, card.SeekerGained, card.EndedByDisconnect,
                    card.MatchOver, card.MatchIndex, card.WinnerPeerId, card.HiderTotal,
                    card.SeekerTotal)
                : null,
            entities, placeDeny,
            // VOICE-1: this peer's whole voice-routing decision — its own room, every known
            // peer's room, the route each pair resolved to, and which hooks are wired. Embedded
            // as the object rather than as a pre-serialised string so it reads as real JSON in
            // the line and a suite can index into it.
            //
            // The ROOMS ride along with the routes deliberately: "both peers say pa" is also
            // what two UNKNOWN rooms produce through the fail-open rule, so a suite asserting
            // only the route could pass on a session where the round never synced at all.
            Voice.VoiceManager.Instance.GetEmitRoutingVerdict(),
            // SHELF-1 (2026-09-19): THIS PEER'S OWN PHYSICS FRAME TIME, milliseconds.
            //
            // ReachCostProbe measures the same monitor on the SERVER and is the only frame-time
            // instrument this repo had. SHELF-1's packet asks for the number on the CLIENT as
            // well, and the reason is not symmetry: a client does not simulate a loose prop (it
            // lerps a frozen kinematic body toward a streamed transform), so "130 props cost the
            // server 6 ms" says nothing at all about what they cost the player watching them.
            // The two numbers are about different work and both have to be read.
            //
            // Sampled per LOGGED sample rather than per frame, so it is a 5 Hz spot reading of a
            // per-frame quantity and its percentiles are coarser than the probe's 60 Hz ones.
            // That is deliberately cheap: a per-frame accumulator here would be a measurement rig
            // running inside every bot in the repo, which is exactly what ReachCostProbe's own
            // header argues against.
            (float)(Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000.0));
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
        // --log-sfx (SFX-1): the voice-budget figures, once, at the end. PeakLive3DVoices is a
        // running high-water mark sampled in _Process (and again the instant any sound starts),
        // so the number printed here covers the whole run rather than the last frame of it.
        if (_options.LogSfx)
            GD.Print(Presentation.ActorFx.BudgetSummaryLine());
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
        // This peer's own first-person lens (INT-0), or null if this process built none. See the
        // computation site: it is null rather than zeroed for the same reason every Synced flag
        // here exists.
        LensSample? Lens,
        // The hide-seek round (ROUND-1), with its own Synced flag for the reason stated two
        // paragraphs down and at the computation site.
        bool RoundSynced, int RoundPhase, int RoundIndex, float RoundRemaining,
        int RoundHider, int RoundSeeker, int RoundRefusal, int RoundFoundTick,
        // TASK-1: the hider's LIVE sort count as THIS peer folded it off the wire. Sampled
        // rather than derived, and sampled per peer, because that is the only thing the sort
        // puts on any wire at all -- every other fact about the job (which object is which
        // colour, which bin wants what, whether a settle has landed) is derived locally on each
        // peer from the authored room, so "the count agrees everywhere" is the ONE assertion
        // that can distinguish a working sort from three peers each quietly scoring their own.
        // It rides HideSeekWire's existing slot: no new field, no bump.
        int RoundSorts,
        Dictionary<string, int> RoundScores, RoundTallySample? RoundTally,
        // (The LoopUi / playthrough-flow / quota / bubble / honk columns were dropped with
        // their systems at the fork - BASE-1, 2026-09-19. What every one of them had in common is
        // the reason to copy the pattern rather than the fields: each recorded a SYNCED flag
        // beside its numbers, because "0 banked" and "has not heard from the server yet" are the
        // same bytes and the second is a true-sounding wrong answer. Anything ROUND-1 samples
        // here needs its own synced flag for that reason.)
        List<EntitySample> Entities,
        // CARRY-1: the newest place refusal's ordinal, 0 = none. See its computation site.
        int PlaceDeny,
        // VOICE-1: the routing verdict, as `vroute`. See its computation site and
        // VoiceManager.GetEmitRoutingVerdict.
        Voice.VoiceRouting.Verdict Vroute,
        // SHELF-1: this peer's own physics frame time in ms, as `pms`. See its computation site.
        float Pms);

    // THE FIRST-PERSON LENS, in world space (INT-0, 2026-09-19). X/Y/Z is the lens itself, not the
    // rig node it hangs off; Dx/Dy/Dz is the direction it faces (-Z of its own basis, which is
    // Godot's camera-forward convention). Off is |avatar origin -> lens|: the eyeline offset, and
    // the ONE quantity that discriminates "the camera went with the body through a teleport" from
    // "the body arrived and the eye stayed behind" -- the avatar positions in Peers[] read
    // identically under both. Nan is a first-class field rather than an absent sample because a
    // NaN that silently dropped out of the log would look exactly like a bot that stopped
    // sampling; when it is true, Off is -1 rather than a NaN that JSON cannot represent anyway.
    // Current says whether this lens is the one actually rendering (a --spectate-cam bot can own
    // a second camera; a headless one renders nothing at all and still reports the transform).
    private sealed record LensSample(float X, float Y, float Z, float Dx, float Dy, float Dz,
        float Off, float Yaw, float Pitch, bool Current, bool Nan);

    // The frozen card as this peer folded it, or null before the first round finishes. Carries
    // its own RoundIndex because the loop's index has already advanced by the time the card
    // exists — see HideSeekTally's doc.
    // MatchOver / MatchIndex / WinnerPeerId / the two totals are MATCH-1's: a match is
    // HideSeekTuning.MatchRounds rounds and the card that ends one carries its result. They are
    // sampled from the CARD rather than recomputed from roundScores here for the reason the card
    // carries them at all -- the next match's Start zeroes the live scores while this card is
    // still the last one every peer holds, so a log that derived "who won" from the score map
    // would start disagreeing with the game one round later.
    private sealed record RoundTallySample(int RoundIndex, int HiderPeerId, int HiderGained,
        int SeekerPeerId, int SeekerGained, bool EndedByDisconnect,
        bool MatchOver, int MatchIndex, int WinnerPeerId, int HiderTotal, int SeekerTotal);

    // One server-simulated NetworkedEntity as this peer sees it: name, rendered position, and the
    // peak per-frame render step since the last sample (teleport frames excluded — see the
    // computation site). Empty unless the session spawned entities.
    private sealed record EntitySample(string Name, float X, float Y, float Z, float Mrs);

    private sealed record PeerSample(long Id, string Name, string AvatarKey, float X, float Y, float Z,
        float Pe, float Ve, float Mrs, bool Pa, int AimStance, float AimSteadiness01, int CarryPose,
        // Tp/Tj (INT-0): commanded teleports this OWNER consumed since the last sample, and the
        // distance the last one moved it. Always 0 on a remote peer's row — see the computation
        // site for why this is a take-and-reset counter rather than a reading of Pe.
        int Tp, float Tj);

    // One networked prop as this peer sees it: id, kind, current holder (0 = none), position, and
    // — while held — two carry-drift scalars: Off = distance from the holder's carry anchor (the
    // by-design chase lag), Bd = distance from the holder's rendered body (the full-chain drift
    // signal a player sees). Both 0 when unheld.
    // Qx/Qy/Qz/Qw: the prop's world orientation (CARRY-1) — what a place assertion compares
    // against its intended pose. SpringLag: the holder-side hold's distance from the point it is
    // chasing, 0 on every peer that is not the holder. Cap/Spd: FEEL-1's two gates — see the
    // comment at the call site.
    private sealed record PropSample(int Id, int Kind, int Holder, float X, float Y, float Z,
        float Off, float Bd, float Qx, float Qy, float Qz, float Qw, float SpringLag,
        float Cap, float Spd);
}
