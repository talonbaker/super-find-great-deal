using System.Collections.Generic;
using Godot;
using MpFoundation.Game.Sandbox;
using MpFoundation.Net;

namespace MpFoundation.Game.Round;

/// <summary>
/// <b>The round's one poll and the round's one voice</b> (CLOCK-1, 2026-09-19).
///
/// <para>Two jobs, and they are the same job: both are "read the replicated view, once, at a
/// steady cadence, and make the world say it". The clocks show the second; the cues say the edge.
/// Splitting them into two nodes would be two polls of one value, two chances to disagree about
/// which message was the last one seen, and two <c>_Process</c> callbacks.</para>
///
/// <para><b>One <c>_Process</c> for every clock in the world.</b> This is
/// <c>GameHud</c>'s design, deliberately copied: a single tick fans out to widgets that early-out
/// when their own value has not moved. The cadence is not a second opinion either —
/// <see cref="PollIntervalSec"/> IS <c>GameHud.PollIntervalSec</c>, and
/// <c>RoundClockTests.TheClockPollsAtTheHudsCadence</c> fails if the two ever stop being one
/// number. A per-clock <c>_Process</c> would be three scene-tree callbacks a frame to read one
/// int ten times a second.</para>
///
/// <para><b>Why it is not attached under <c>GameHud</c>.</b> The HUD is client chrome and
/// <c>GameHud.Attach</c> is a no-op on a headless peer — and every bot in <c>tests/</c> is a
/// headless peer. A clock hung off the HUD's poll would therefore be untestable by any suite this
/// repo can run, which is the same as untested.</para>
///
/// <para><b>Where it runs.</b> Every peer that is not a dedicated server, plus a dedicated server
/// <i>only</i> when <c>--log-clock</c> is set, in which case it plays nothing, drives no clock,
/// and prints one reference line a second for the smoke to compare the clients against. Putting
/// that reference line inside <c>HideSeekDriver</c> instead would have edited ROUND-1's file for
/// a test fixture.</para>
///
/// <para><b>The hum is a flag with nothing behind it, on purpose.</b> The proposal's
/// <c>Seeking</c> hum (<c>RoundAudioTuning.HumOn</c>, default off) is the one item in the packet
/// marked optional. The loop pool IS free — <c>SfxLoop.FireBody</c> is the only member and
/// nothing in this world acquires it — but an <c>AcquireLoop</c>/<c>ReleaseLoop</c> pair that
/// ships off by default is an unexercised path that leaks a pool slot the first time somebody
/// turns it on mid-phase, and the pool never steals a loop back. The dial is here so the decision
/// is recorded rather than forgotten; the loop is not.</para>
/// </summary>
public partial class RoundAudio : Node
{
    public const string NodeName = "RoundAudio";

    /// <summary>The cadence, which is the HUD's cadence and must stay the HUD's cadence — see
    /// the class doc, and the test that pins it.</summary>
    public const double PollIntervalSec = Ui.Hud.GameHud.PollIntervalSec;

    /// <summary>How often <c>--log-clock</c> prints. One line per clock per second per peer: at
    /// three clocks and three peers that is nine lines a second, which is a readable log and a
    /// parseable one. Faster adds nothing — the assertion is "within one second".</summary>
    public const double LogIntervalSec = 1.0;

    /// <summary>The prefix every line this feature prints carries, so a runner can grep for it
    /// without matching the round driver's <c>[round]</c> or the engine's own noise.</summary>
    public const string LogPrefix = "[clock]";

    /// <summary>Volume trim for the flat layer. Quieter than the positional one: it is a second
    /// copy of a sound the player is already hearing from the room, and its job is to guarantee
    /// audibility, not to be the performance.</summary>
    private const float FlatVolumeDb = -9f;

    /// <summary>Volume trim for a cue coming out of a clock.</summary>
    private const float PositionalVolumeDb = -6f;

    /// <summary>How far a clock is heard. The rooms are 10–14 m across and 40 m apart, so this
    /// carries across a room and dies well before the next one — the same property the 40 m room
    /// separation buys for voice, and for the same reason: the hider must not hear the seeker's
    /// room.</summary>
    private const float ClockMaxDistanceM = 22f;

    /// <summary>The registry. STATIC because the clocks are children of the world and this node
    /// is a child of <c>Gameplay</c>, and a clock must not have to go looking up the tree for a
    /// node that may not exist yet — a world can be built before this is added, and in a
    /// dedicated-server process this node is usually never added at all.</summary>
    private static readonly List<RoundClock> Clocks = new();

    /// <summary>
    /// <b>The boards</b> (HOLD-1, 2026-09-19), kept in their own list rather than folded in with
    /// the clocks above. A clock and a board are painted by the same poll from the same view,
    /// but a clock is also an AUDIO EMITTER — <see cref="Fire"/> plays the positional layer once
    /// per clock, which is what makes the buzzer come out of the building. A board is furniture
    /// that reads; putting it in <see cref="Clocks"/> would silently give every cue an extra
    /// voice out of a wall that is not a clock, and the voice budget is the one place in this
    /// file where "one more entry in the list" is not free.
    /// </summary>
    private static readonly List<HoldingBoard> Boards = new();

    private static RoundAudio? _instance;

    private double _poll;
    private double _logAccum;
    private bool _logClock;
    private bool _logOnly;
    private bool _silent;

    /// <summary>The last view a cue was derived from. <c>null</c> until the first message, which
    /// is exactly the late-join case <c>RoundAudioCues.ForEdge</c> answers with silence.</summary>
    private HideSeekView? _last;

    /// <summary>
    /// Adds the round's audio/clock poll if this peer should have one. Idempotent, same contract
    /// as <c>GameHud.Attach</c>: a reload or a resumed session does not stack two polls.
    /// </summary>
    public static void Attach(Node sceneRoot)
    {
        if (_instance != null && GodotObject.IsInstanceValid(_instance))
            return;

        NetworkManager? net = NetworkManager.Instance;
        bool dedicated = net is { Role: NetworkManager.SessionRole.Server, IsHeadless: true };
        bool logClock = net?.Options?.LogClock == true;
        if (dedicated && !logClock)
            return;

        sceneRoot.AddChild(new RoundAudio { Name = NodeName });
    }

    /// <summary>A clock joining the world. Called from <c>RoundClock._Ready</c>; safe before
    /// this node exists and safe if it never does.</summary>
    public static void RegisterClock(RoundClock clock)
    {
        if (!Clocks.Contains(clock))
            Clocks.Add(clock);
    }

    /// <inheritdoc cref="RegisterClock"/>
    public static void UnregisterClock(RoundClock clock) => Clocks.Remove(clock);

    /// <summary>A board joining the world. Called from <c>HoldingBoard._Ready</c>; safe before
    /// this node exists and safe if it never does. <see cref="Boards"/> says why it is not the
    /// same list as the clocks.</summary>
    public static void RegisterBoard(HoldingBoard board)
    {
        if (!Boards.Contains(board))
            Boards.Add(board);
    }

    /// <inheritdoc cref="RegisterBoard"/>
    public static void UnregisterBoard(HoldingBoard board) => Boards.Remove(board);

    public override void _Ready()
    {
        _instance = this;
        NetworkManager? net = NetworkManager.Instance;
        _logClock = net?.Options?.LogClock == true;
        _logOnly = net is { Role: NetworkManager.SessionRole.Server, IsHeadless: true };
        // A headless client renders nothing and has no audio device worth the pool slots, but it
        // MUST still derive and log the cue sequence — that sequence is what the smoke asserts,
        // and every client in tests/ is headless. So the derivation always runs and only the
        // playback is skipped, which is the same split ActorFx makes.
        _silent = net?.IsHeadless == true;

        if (_logClock)
            GD.Print($"{LogPrefix} logging at {1.0 / LogIntervalSec:0} Hz "
                     + $"({(_logOnly ? "server reference line" : "clocks + cues")}), "
                     + $"poll {PollIntervalSec * 1000:0} ms, playback {(_silent ? "off" : "on")}");
    }

    public override void _ExitTree()
    {
        if (_instance == this)
            _instance = null;
    }

    public override void _Process(double delta)
    {
        _poll += delta;
        if (_poll < PollIntervalSec)
            return;
        double sincePoll = _poll;
        _poll = 0;
        Poll(sincePoll);
    }

    private void Poll(double sincePoll)
    {
        if (HideSeekDriver.Instance is not { Synced: true } driver)
        {
            // Not synced: the clocks come off the wall and the cue history is dropped, so a
            // reconnecting peer is a late joiner again rather than one that resumes mid-edge and
            // chimes at a transition it did not see.
            _last = null;
            if (!_logOnly)
            {
                foreach (RoundClock clock in Clocks)
                    clock.Blank();
                foreach (HoldingBoard board in Boards)
                    board.Blank();
            }
            return;
        }

        HideSeekView view = driver.View;

        if (!_logOnly)
        {
            foreach (RoundCue cue in RoundAudioCues.ForEdge(_last, view))
                Fire(cue);
            foreach (RoundClock clock in Clocks)
                clock.Apply(view, driver.NameOf, driver.Tuning);
            // The board needs one thing the clock does not: who is reading it, for the pronoun
            // in that peer's own row. Multiplayer.GetUniqueId() is what RoundStripWidget already
            // reads for the same question, so the strip and the board cannot disagree about
            // which row is yours.
            if (Boards.Count > 0)
            {
                int self = (int)Multiplayer.GetUniqueId();
                foreach (HoldingBoard board in Boards)
                    board.Apply(view, self, driver.NameOf, driver.Tuning);
            }
        }

        _last = view;

        if (!_logClock)
            return;
        _logAccum += sincePoll;
        if (_logAccum < LogIntervalSec)
            return;
        _logAccum = 0;
        LogSample(view);
    }

    /// <summary>
    /// Plays one cue. The flat layer is one shot on this peer; the positional layer is one shot
    /// PER CLOCK, which is what makes the buzzer come out of the building rather than out of the
    /// player's head.
    ///
    /// <para><b>Three clocks means three voices, and that is inside the budget on purpose.</b>
    /// The one-shot pool is 14 and <c>AudioVoiceBudget</c> caps the world at 24 concurrent 3D
    /// players; the loudest thing this feature can do is three ticks in one frame, once a second,
    /// for ten seconds. The clocks are also 40 m apart, so at most one room's worth is ever
    /// audible.</para>
    /// </summary>
    private void Fire(in RoundCue cue)
    {
        if (_logClock)
        {
            string layer = cue.Flat && cue.Positional ? "flat+positional"
                : cue.Flat ? "flat" : "positional";
            GD.Print($"{LogPrefix} {UnixMs()} cue {cue.Sound} {layer}");
        }

        if (_silent)
            return;

        if (cue.Flat)
            SfxLab.PlayUi(cue.Sound, FlatVolumeDb, pitchJitter: 0f, pitchBias: cue.PitchBias);

        if (!cue.Positional)
            return;
        foreach (RoundClock clock in Clocks)
        {
            if (!GodotObject.IsInstanceValid(clock) || !clock.IsInsideTree())
                continue;
            SfxLab.PlayStream3D(clock, clock.GlobalPosition, SfxLab.Get(cue.Sound),
                PositionalVolumeDb, pitchJitter: 0.03f, maxDistance: ClockMaxDistanceM,
                pitchBias: cue.PitchBias);
        }
    }

    /// <summary>
    /// One second's worth of <c>--log-clock</c>.
    ///
    /// <para>The client line is the packet's <c>clock &lt;room&gt; &lt;phase&gt; &lt;M:SS&gt;</c>
    /// plus one trailing <c>shown=</c> field, and the split between them is deliberate. The phase
    /// word and <c>shown=</c> are read off the LABELS — so a clock that never painted logs an
    /// empty phase and the suite goes red, where a line recomputed from the view would pass with
    /// a blank panel on the wall. The <c>M:SS</c> is the floored view value, because in
    /// <c>Together</c> and <c>Tally</c> the painted second line is the tally, not a time, and the
    /// assertion the suite makes is about the SECOND. In the two phases that have a clock the
    /// suite asserts the two agree, which closes the gap.</para>
    ///
    /// <para>The server line is <c>server &lt;phase&gt; &lt;M:SS&gt;</c>: the reference, off
    /// <c>ServerState</c>'s own clock rather than off the server's folded view, so a fold bug
    /// cannot make both sides of the comparison wrong in the same direction.</para>
    ///
    /// <para>Every line carries a Unix millisecond stamp because the comparison is
    /// "within one second of the server AT THE SAME INSTANT", and the three processes are on one
    /// machine and one wall clock. Without it the suite would be comparing a client's last line
    /// to a server line from an arbitrary earlier second, which is a tolerance, not a check.</para>
    /// </summary>
    private void LogSample(in HideSeekView view)
    {
        long stamp = UnixMs();
        if (_logOnly)
        {
            HideSeekState s = HideSeekDriver.Instance!.ServerState;
            GD.Print($"{LogPrefix} {stamp} server {HideSeekText.PhaseName(s.Phase)} "
                     + $"{HideSeekText.TimerText(s.RemainingSec)}");
            return;
        }

        string mmss = HideSeekText.TimerText(view.RemainingSec);
        foreach (RoundClock clock in Clocks)
        {
            if (!GodotObject.IsInstanceValid(clock))
                continue;
            (string phase, string shown) = clock.CurrentText();
            string room = clock.Room.Length > 0 ? clock.Room : clock.Name.ToString();
            // "-" rather than an empty field: a parser splitting on whitespace cannot tell an
            // empty trailing field from a missing one, and "the clock is deliberately blank in
            // Holding" and "the clock never painted" are the two things this line exists to keep
            // apart.
            GD.Print($"{LogPrefix} {stamp} clock {room} "
                     + $"{(phase.Length > 0 ? phase : "-")} {mmss} "
                     + $"shown={(shown.Length > 0 ? shown.Replace(' ', '_') : "-")}");
        }

        // The board prints its OWN line with its own prefix rather than joining the clock's
        // format: it carries N rows and a footer that already contains spaces, so squeezing it
        // into a whitespace-delimited clock line would make both harder to parse. Same stamp,
        // same poll, so the two are trivially pairable on one timeline.
        foreach (HoldingBoard board in Boards)
        {
            if (!GodotObject.IsInstanceValid(board))
                continue;
            GD.Print(board.LogLine(stamp));
        }
    }

    private static long UnixMs() => (long)(Time.GetUnixTimeFromSystem() * 1000.0);
}
