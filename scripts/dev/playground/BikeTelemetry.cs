using System;
using System.Globalization;
using Godot;

namespace MpFoundation.Dev.Playground;

/// <summary>
/// <b>One physics tick, as the telemetry sees it.</b> Every field is handed in by the harness;
/// nothing in <see cref="BikeTelemetry"/> goes and fetches any of it.
///
/// <para><b>Why a value type with no behaviour.</b> The recorder must never become a second place
/// that knows what the bike is doing. If it read <c>BikeLayer</c> it would have an opinion about
/// mount state; if it read <c>BikeHandling</c> it would have an opinion about tiers; and the first
/// time one of those disagreed with the layer, the log would be arguing with the game about a
/// session nobody can re-run. Everything arrives through this struct, so the log can only ever be
/// a transcript of what the lab already decided.</para>
///
/// <para>The string fields are declared non-nullable because that is the intent, but a
/// default-constructed sample has nulls in them and the recorder coalesces every one on read: a
/// telemetry class that throws is a telemetry class that takes the lab down, which is the exact
/// thing this file exists not to do.</para>
/// </summary>
public readonly record struct BikeTelemetrySample
{
    /// <summary>The body's world position this tick — the heatmap's whole point.</summary>
    public Vector3 Position { get; init; }

    /// <summary>Horizontal speed, m/s. Horizontal because a fall is not riding.</summary>
    public float SpeedMps { get; init; }

    /// <summary><c>BikeLayer.Mounted</c>. The mount/dismount edge is taken off this, not off the
    /// event string — see <see cref="BikeTelemetry.Sample"/>.</summary>
    public bool Mounted { get; init; }

    /// <summary><c>BikeLayer.Blend</c>, 0 on foot, 1 riding, between while the tuning walks.</summary>
    public float Blend { get; init; }

    /// <summary>Is the body on the floor. Used as the fallback when an event string never names
    /// which kind of mount or dismount just happened.</summary>
    public bool Grounded { get; init; }

    /// <summary><c>BikeLayer.LastEvent</c>, verbatim. <b>It persists across ticks</b>, which is why
    /// the recorder latches on the change and never on the value.</summary>
    public string LastEvent { get; init; }

    /// <summary><c>BikeHandling.DriftResult.Entered</c> — true on exactly the tick a drift starts,
    /// so it is already an edge and needs no latch of its own.</summary>
    public bool DriftEntered { get; init; }

    /// <summary><c>BikeHandling.DriftResult.ExitTier</c> — non-zero on exactly the tick a drift
    /// ends. The session's peak tier is the running max of this.</summary>
    public int DriftExitTier { get; init; }

    /// <summary><c>BikeHandling.DriftState.ChargeSec</c>, the live charge. Recorded, never
    /// converted to a tier here: <see cref="BikeHandling.DriftTier"/> needs the handling tuning,
    /// and a second copy of the ladder in this file is a second source of truth about it.</summary>
    public float DriftChargeSec { get; init; }

    /// <summary>Which course the body is on, for the heatmap's sake — a trace with no space to
    /// draw it on is a list of numbers.</summary>
    public string CourseName { get; init; }

    /// <summary>The live foot preset's name, as the banner has it.</summary>
    public string FootPreset { get; init; }

    /// <summary>The live bike preset's name, as the banner has it.</summary>
    public string BikePreset { get; init; }

    /// <summary>Seconds this tick covered. The mounted/on-foot split is the sum of these, so it is
    /// correct at any physics rate and correct if the lab is ever stepped by hand.</summary>
    public float DtSec { get; init; }
}

/// <summary>
/// <b>The bike lab's session recorder</b> (BIKE-2x, 2026-09-02). One CSV per lab session at
/// <c>user://bike-telemetry/&lt;timestamp&gt;.csv</c>.
///
/// <para><b>What it is for, in one sentence:</b> so the weekend playtest can answer <i>"is the bike
/// actually used"</i> without anyone having to ask the player. Asking is the worst instrument
/// available — it is asked after the fact, of someone who was busy, about a thing they were not
/// counting, and the answer that comes back is a feeling about the bike rather than a record of it.
/// A file that says the body spent 41 seconds mounted and 320 on foot, mounted nine times and
/// dismounted eight of them by stumbling, settles the question in a way no recollection can. So the
/// counters here are deliberately the ones a person cannot self-report: how many mounts were
/// airborne, how many dismounts were the landing chain the grammar was built for, how many drifts
/// were entered at all, and where on the course the body actually was.</para>
///
/// <para><b>It reads no node and no singleton.</b> Everything arrives through
/// <see cref="BikeTelemetrySample"/>. That is what keeps it testable without an engine, and it is
/// also what stops it becoming a second source of truth about the bike's state: this class cannot
/// disagree with <c>BikeLayer</c>, because it has no independent way to form an opinion.</para>
///
/// <para><b><c>user://</c> on this project is a REAL profile directory, not a sandbox.</b>
/// <c>.claude/rules/test-suite.md</c> records what that already cost: <c>user://</c> resolves by
/// project NAME, so <c>%APPDATA%/Godot/app_userdata/mp-foundation/</c> is one directory shared by
/// every worktree on the machine, and the file in it is Talon's own — a bot run had already spent a
/// real achievement into <c>settings.cfg</c> before anyone noticed. This class therefore creates
/// files under <b>its own <c>bike-telemetry/</c> subdirectory and nowhere else</b>. It never opens
/// <c>settings.cfg</c>, never touches <c>user://telemetry</c> (the shipped usage-report queue, a
/// different module entirely — see <c>MpFoundation.Telemetry.TelemetryPaths</c>), and never deletes
/// anything. Its filenames are per-session timestamps, so a second lab instance writes a second
/// file rather than overwriting the first, and nothing here can ever clobber a file it did not
/// create. This is a note to get right rather than a bug to fix, and it is written down because the
/// next person adding a lab writer will not have read that rule.</para>
///
/// <para><b>It is inert and safe when the file cannot be opened.</b> A headless run, a read-only
/// profile directory, a second lab instance holding the same path, a full disk — none of them may
/// take the lab down, because the lab is the thing being playtested and the recorder is a
/// bystander. Every file operation is guarded; a failure sets <see cref="Recording"/> false and
/// records why in <see cref="LastError"/>, and <b>the counters keep counting in memory regardless</b>,
/// so the on-screen readout and the self-test are unaffected by whether a file exists. The one thing
/// a failure must not do is throw, and the one thing it must not be is silent — hence the readout
/// says so.</para>
///
/// <para><b>Every number is formatted with <see cref="CultureInfo.InvariantCulture"/>.</b> Not
/// pedantry: a machine with a comma decimal separator writes <c>3,25</c> into a comma-separated
/// file, which is not a parse error — it is an extra column, silently, in some rows and not others,
/// and the corruption is only visible once someone plots the wrong thing.</para>
/// </summary>
public sealed partial class BikeTelemetry : Node
{
    /// <summary>The directory this class may write, and the only one. See the class doc.</summary>
    public const string Dir = "user://bike-telemetry";

    /// <summary>Seconds between trace rows. 1 Hz: enough to draw a line through a course at lab
    /// speeds (9 m/s is ~9 m between rows), few enough that a twenty-minute session is ~1200 rows
    /// and opens in anything.</summary>
    private const float TraceIntervalSec = 1f;

    /// <summary>
    /// <b>How many ticks a mount or dismount waits to be classified.</b> The kind of a toggle is
    /// written into <c>BikeLayer.LastEvent</c> in two places a tick apart: the press half runs in
    /// <c>NextIntent</c> and writes <c>MOUNT</c> / <c>DISMOUNT</c>, and the burst half runs in
    /// <c>PostStep</c> and overwrites it with the string that actually names the kind
    /// (<c>HOP ON</c>, <c>AIR MOUNT BURST</c>, <c>LANDING DISMOUNT BURST</c>, …). Whether the
    /// harness samples between those two or after both is the harness's business, not this file's,
    /// so a toggle is held open for a few ticks and classified by the most specific string seen
    /// while it is open. Three ticks is 50 ms at the lab's rate — far shorter than any real
    /// mount-dismount-mount chain, and long enough to cover either sampling order.
    /// </summary>
    private const int ToggleResolveTicks = 3;

    // --- the file ---------------------------------------------------------------------------

    private Godot.FileAccess? _file;
    private bool _began;
    private bool _ended;

    /// <summary>True while a file is open and taking rows. False if one was never opened, if
    /// opening failed (<see cref="LastError"/> says why), or after <see cref="End"/>.</summary>
    public bool Recording { get; private set; }

    /// <summary>Why there is no file, empty when there is one or when none was ever asked for.
    /// Shown in the readout: a recorder that silently records nothing is worse than none.</summary>
    public string LastError { get; private set; } = "";

    /// <summary>The path being written, empty when nothing is.</summary>
    public string FilePath { get; private set; } = "";

    // --- the counters -----------------------------------------------------------------------
    // All public, all read-only, all maintained whether or not a file exists.

    /// <summary>Mounts pressed with the wheels on the floor — the hop onto the bike.</summary>
    public int MountsGround { get; private set; }

    /// <summary>Mounts pressed in the air — the burst, and the slingshot out of a swing.</summary>
    public int MountsAir { get; private set; }

    /// <summary>Plain dismounts on the ground, stumbling or not. A stumbling dismount counts here
    /// AND in <see cref="Stumbles"/>: it is one dismount, and it is also a stumble.</summary>
    public int DismountsGround { get; private set; }

    /// <summary>Dismounts inside the landing window — the chain the grammar was built for, and the
    /// number that says whether anyone found it.</summary>
    public int DismountsLanding { get; private set; }

    /// <summary>Kick-offs: the double jump off the bike, including the late one the armed-landing
    /// cast got wrong.</summary>
    public int DismountsKickOff { get; private set; }

    /// <summary>Stumbles from either route — a rolling dismount over the foot cap, and landing on
    /// foot over it after a mid-air dismount.</summary>
    public int Stumbles { get; private set; }

    /// <summary>Swings started — one per press. Not one per hit: a swing that connects emits a hit
    /// event as well, and not one per moment either, since a swing writes up to four different
    /// strings across its life. See the swing window in <see cref="Sample"/>.</summary>
    public int Swings { get; private set; }

    /// <summary>Air swings that spent the lunge. A subset of <see cref="Swings"/>.</summary>
    public int Lunges { get; private set; }

    /// <summary>Swing-into-mount slingshots.</summary>
    public int Slingshots { get; private set; }

    /// <summary>Drifts entered, from <c>BikeHandling.DriftResult.Entered</c>.</summary>
    public int DriftEntries { get; private set; }

    /// <summary>The highest tier any drift in the session paid out on, 0..3.</summary>
    public int PeakDriftTier { get; private set; }

    /// <summary>The largest live charge seen, seconds. Recorded beside the tier because a session
    /// whose drifts all died at 0.59 s against a 0.60 s tier-1 threshold reads as "nobody drifted"
    /// on the tier alone, and as "the threshold is wrong" on this.</summary>
    public float PeakDriftChargeSec { get; private set; }

    /// <summary>Seconds the body spent mounted.</summary>
    public float SecondsMounted { get; private set; }

    /// <summary>Seconds the body spent on foot. With <see cref="SecondsMounted"/> this is the whole
    /// answer to "is the bike actually used".</summary>
    public float SecondsOnFoot { get; private set; }

    /// <summary>Trace rows produced. Counted even with no file, so a self-test can assert the 1 Hz
    /// cadence without a writable profile.</summary>
    public int TraceRows { get; private set; }

    // --- derived state, none of it authoritative about anything -------------------------------

    private float _elapsedSec;
    private float _traceAccumSec;
    private string _prevEvent = "";
    private bool _haveMounted;
    private bool _prevMounted;
    private bool _swingOpen;
    private string _course = "-";
    private string _footPreset = "-";
    private string _bikePreset = "-";

    private enum Toggle { None, Mount, Dismount }

    private Toggle _pendingToggle;
    private int _pendingTicks;
    private bool _pendingGrounded;

    // --- opening and closing -------------------------------------------------------------------

    /// <summary>
    /// <b>Opens the session file and writes the header block.</b> Safe to call once; a second call
    /// is ignored rather than opening a second file, because a lab that re-enters its own setup
    /// (a scene reload, a double-wired signal) must not end up with two writers on one session.
    ///
    /// <para>Failure is not an error condition here — see the class doc. If the directory cannot be
    /// made or the file cannot be opened, this returns having set <see cref="Recording"/> false and
    /// <see cref="LastError"/> to the reason, and every counter still works for the rest of the
    /// session.</para>
    /// </summary>
    /// <param name="sessionNote">A line naming what this run-out is for, written into the header.
    /// A file whose header says "kicker lane, ALT+3, wiggle charge" is evidence; one that says
    /// nothing is a pile of rows nobody can attribute two weeks later.</param>
    public void Begin(string sessionNote)
    {
        if (_began)
            return;
        _began = true;

        string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
        string path = $"{Dir}/{stamp}.csv";
        try
        {
            Error dirErr = DirAccess.MakeDirRecursiveAbsolute(Dir);
            if (dirErr != Error.Ok && dirErr != Error.AlreadyExists)
            {
                Fail($"could not create {Dir}: {dirErr}");
                return;
            }
            _file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Write);
            if (_file == null)
            {
                Fail($"could not open {path}: {Godot.FileAccess.GetOpenError()}");
                return;
            }
        }
        catch (Exception e)
        {
            // Nothing here is expected to throw, and that is exactly why it is caught: the failure
            // mode this guard exists for is the one nobody predicted, on the one afternoon the lab
            // is in front of a player.
            Fail($"{e.GetType().Name}: {e.Message}");
            return;
        }

        Recording = true;
        FilePath = path;
        LastError = "";

        // The header block. Every comment line in this file is `# key,value`, with notes carrying a
        // third field for the second they arrived at (see Note). A reader that drops lines starting
        // with '#' is left with exactly the trace table and nothing else.
        Comment("section", "header");
        Comment("format", "sail-bike-telemetry v1");
        Comment("session_note", sessionNote);
        Comment("started", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        Comment("trace_hz", Num(1f / TraceIntervalSec));
        WriteLine("t_sec,x,y,z,speed_mps,mounted,grounded,blend,drift_charge_sec,course,last_event");
        _file?.Flush();
        GD.Print($"[bike-telemetry] recording to {path}");
    }

    /// <summary>
    /// <b>A key/value line, at any point in the session.</b> What the harness records against the
    /// run rather than against a tick: a preset pressed, a camera row changed, a note typed.
    ///
    /// <para>Written as <c># key,value,t_sec</c>. The third field is the elapsed second, and it is
    /// there because chronology is the whole value of a mid-session note: "SNAP preset" tells you
    /// nothing without "…from 4:12 onward", and a header that collects every preset the session ever
    /// held, undated, cannot attribute a single trace row to any of them.</para>
    ///
    /// <para>With no file open this is dropped. Deliberately: notes are prose for a human, the
    /// counters are the evidence, and the counters survive a failed open on their own.</para>
    /// </summary>
    public void Note(string key, string value)
    {
        if (!Recording || _file == null)
            return;
        WriteLine($"# {Csv(key)},{Csv(value)},{Num(_elapsedSec)}");
        _file?.Flush();     // null if that write is what failed; Fail() has already dropped it
    }

    /// <summary>
    /// <b>Writes the summary block and closes the file.</b> Idempotent — calling it twice, or
    /// calling it after <see cref="_ExitTree"/> already did, writes one summary and closes once.
    /// The counters stay readable afterwards; only the file goes away.
    /// </summary>
    public void End()
    {
        if (_ended)
            return;
        _ended = true;

        // Any toggle still waiting on its classifying event is committed on what is known now,
        // rather than being dropped: a mount that happened is a mount, and losing the last one of
        // the session because the lab was closed a tick after it is exactly the kind of quiet
        // undercount this file exists to avoid.
        CommitToggle("");

        if (_file != null)
        {
            Comment("section", "summary");
            Comment("duration_sec", Num(_elapsedSec));
            Comment("seconds_mounted", Num(SecondsMounted));
            Comment("seconds_on_foot", Num(SecondsOnFoot));
            Comment("mounted_fraction", Num(MountedFraction));
            Comment("mounts_ground", Int(MountsGround));
            Comment("mounts_air", Int(MountsAir));
            Comment("dismounts_ground", Int(DismountsGround));
            Comment("dismounts_landing", Int(DismountsLanding));
            Comment("dismounts_kickoff", Int(DismountsKickOff));
            Comment("stumbles", Int(Stumbles));
            Comment("swings", Int(Swings));
            Comment("lunges", Int(Lunges));
            Comment("slingshots", Int(Slingshots));
            Comment("drift_entries", Int(DriftEntries));
            Comment("peak_drift_tier", Int(PeakDriftTier));
            Comment("peak_drift_charge_sec", Num(PeakDriftChargeSec));
            Comment("trace_rows", Int(TraceRows));
            Comment("course_last", _course);
            Comment("foot_preset_last", _footPreset);
            Comment("bike_preset_last", _bikePreset);
            Comment("summary", SummaryLine());
            try
            {
                _file.Flush();
                _file.Close();
            }
            catch (Exception e)
            {
                LastError = $"close failed - {e.GetType().Name}: {e.Message}";
            }
            _file = null;
            GD.Print($"[bike-telemetry] {TraceRows} trace rows -> {FilePath}");
        }
        Recording = false;
    }

    /// <summary>The lab closed without anyone calling <see cref="End"/> — the window's X, a crash
    /// in another node's teardown, a scene change. The summary is written anyway, because a session
    /// that ended untidily is still a session that happened.</summary>
    public override void _ExitTree() => End();

    // --- the per-tick sample -------------------------------------------------------------------

    /// <summary>
    /// <b>One physics tick.</b> Cheap by construction: arithmetic, one string comparison, and a
    /// file write once a second. Nothing is printed — a <c>GD.Print</c> per tick is 60 lines a
    /// second into a console someone is trying to read the readout in, and it costs more frame time
    /// than everything else in this file put together.
    ///
    /// <para><b>The mount and dismount counts latch on the CHANGE, not on the value.</b>
    /// <c>BikeLayer.LastEvent</c> is a readout string that persists until something else overwrites
    /// it, so it still says <c>MOUNT</c> a hundred ticks after the mount. Counting the value counts
    /// one mount a hundred times, which is the single most likely way for this file to be quietly
    /// wrong, and it would be wrong in the direction that flatters the bike. So: the toggle itself
    /// is taken off the <see cref="BikeTelemetrySample.Mounted"/> edge (unambiguous, exactly once
    /// per real toggle), its KIND is read from the event string while the toggle is held open, and
    /// every other event count — swings, lunges, slingshots, stumbles — fires only on the tick the
    /// string differs from the previous tick's.</para>
    ///
    /// <para>Two identical events back to back would defeat a change latch, so it is worth saying
    /// why they cannot happen here: <c>BikeLayer</c> writes a different string between every pair of
    /// same-kind events. A mount cannot repeat without an intervening dismount and vice versa; a
    /// swing cannot start until the previous one is over, and a swing that ends normally writes
    /// <c>SWING done</c> while one cut short by a slingshot writes the slingshot. The one count that
    /// does not rely on that argument is the swing's, which carries its own window because the lunge
    /// overwrites the press string inside a single tick — see below.</para>
    /// </summary>
    public void Sample(in BikeTelemetrySample s)
    {
        float dt = Mathf.Max(s.DtSec, 0f);
        _elapsedSec += dt;

        if (s.Mounted)
            SecondsMounted += dt;
        else
            SecondsOnFoot += dt;

        _course = Text(s.CourseName, _course);
        _footPreset = Text(s.FootPreset, _footPreset);
        _bikePreset = Text(s.BikePreset, _bikePreset);

        string ev = s.LastEvent ?? "";
        bool changed = !string.Equals(ev, _prevEvent, StringComparison.Ordinal);
        _prevEvent = ev;

        // --- the toggle edge, off the boolean rather than off the string.
        if (_haveMounted && s.Mounted != _prevMounted)
        {
            // A toggle inside another toggle's resolve window (Q spammed) closes the older one on
            // what is known, rather than letting the second toggle steal the first one's event.
            CommitToggle(ev);
            _pendingToggle = s.Mounted ? Toggle.Mount : Toggle.Dismount;
            _pendingTicks = ToggleResolveTicks;
            _pendingGrounded = s.Grounded;
        }
        _prevMounted = s.Mounted;
        _haveMounted = true;

        if (_pendingToggle != Toggle.None)
        {
            if (Decisive(_pendingToggle, ev))
                CommitToggle(ev);
            else if (--_pendingTicks <= 0)
                CommitToggle("");
        }

        // --- everything else, on the change and only on the change.
        if (changed)
        {
            // The swing, counted through a one-swing-at-a-time window rather than off a single
            // string. It has to be, because an AIR swing that spends the lunge writes its press
            // string in NextIntent and then OVERWRITES it in PostStep with the lunge's burst — so a
            // harness sampling after PostStep never sees "SWING in the air - LUNGE" at all, and
            // counting only the press strings would silently miss every lunging swing, which is
            // exactly the swing worth counting. The window opens on whichever of the two arrives
            // first and refuses to open twice, so both sampling orders give the same count.
            bool swingStart = ev.StartsWith("SWING on the ground", StringComparison.Ordinal)
                           || ev.StartsWith("SWING in the air", StringComparison.Ordinal);
            bool swingLunge = ev.StartsWith("SWING LUNGE", StringComparison.Ordinal);
            if (swingStart || swingLunge)
            {
                if (!_swingOpen)
                {
                    Swings++;
                    _swingOpen = true;
                }
                // The lunge itself is the PostStep burst and nothing else: it is written on exactly
                // the ticks the impulse was applied, once per airtime, so it needs no window.
                if (swingLunge)
                    Lunges++;
            }
            else if (!ev.StartsWith("SWING HIT", StringComparison.Ordinal))
            {
                // "SWING HIT" is mid-swing and keeps the window open. Everything else closes it —
                // "SWING done" normally, and any other event for the case where a burst or a slide
                // jump overwrote "SWING done" on the tick it was written. Closing on the general
                // case rather than on that one string is what stops a missed end from swallowing
                // the NEXT swing.
                _swingOpen = false;
            }

            if (ev.StartsWith("SLINGSHOT", StringComparison.Ordinal))
                Slingshots++;

            // Both stumble routes. The rolling one also resolves a dismount to "ground" below; that
            // is one dismount and one stumble, not two of either.
            if (ev.StartsWith("DISMOUNT + STUMBLE", StringComparison.Ordinal)
             || ev.StartsWith("LANDED OVER THE FOOT CAP", StringComparison.Ordinal))
                Stumbles++;
        }

        // --- the drift, already edged by the handling model. Nothing is re-derived here.
        if (s.DriftEntered)
            DriftEntries++;
        if (s.DriftExitTier > PeakDriftTier)
            PeakDriftTier = s.DriftExitTier;
        if (s.DriftChargeSec > PeakDriftChargeSec)
            PeakDriftChargeSec = s.DriftChargeSec;

        // --- the 1 Hz trace.
        _traceAccumSec += dt;
        if (_traceAccumSec >= TraceIntervalSec)
        {
            // Subtract rather than zero, so the cadence does not drift with the tick length; but a
            // hitch longer than the interval must not then emit a burst of rows for a body that was
            // only ever in one place, so the carry is capped at one interval.
            _traceAccumSec = Mathf.Min(_traceAccumSec - TraceIntervalSec, TraceIntervalSec);
            TraceRow(s);
        }
    }

    /// <summary>The fraction of the session spent on the bike, 0..1; 0 for a session with no time
    /// in it at all. The one number the weekend question reduces to.</summary>
    public float MountedFraction
    {
        get
        {
            float total = SecondsMounted + SecondsOnFoot;
            return total <= 0f ? 0f : SecondsMounted / total;
        }
    }

    /// <summary>
    /// <b>The on-screen line</b>, in the same voice as <c>BikeLayer.Line()</c>: a label, then the
    /// facts, then the state, no punctuation doing work that spacing can do. Terse because it sits
    /// under four other rows of readout and the lab is being played while it is read.
    /// </summary>
    public string SummaryLine()
    {
        string where = Recording ? $"rec {TraceRows} rows"
            : LastError.Length > 0 ? $"NO FILE ({LastError}) - counting in memory"
            : _ended ? $"closed {TraceRows} rows"
            : $"idle {TraceRows} rows";
        return $"TELEMETRY  {where}   ride {Num(SecondsMounted)}s / foot {Num(SecondsOnFoot)}s "
             + $"({Pct(MountedFraction)})   mount {MountsGround}g {MountsAir}air   "
             + $"dismount {DismountsGround}g {DismountsLanding}land {DismountsKickOff}kick   "
             + $"stumble {Stumbles}   swing {Swings} ({Lunges} lunge {Slingshots} sling)   "
             + $"drift {DriftEntries} (peak tier {PeakDriftTier}, charge {Num(PeakDriftChargeSec)}s)";
    }

    // --- classification ------------------------------------------------------------------------

    /// <summary>
    /// <b>Does this event string name the kind of the toggle we are holding open?</b> The strings
    /// are <c>BikeLayer</c>'s, verbatim, and matched on their prefix because most of them carry
    /// interpolated numbers after it.
    ///
    /// <para>Note that a bare <c>MOUNT</c> or <c>DISMOUNT</c> is deliberately NOT decisive: those
    /// are the press half, written before the layer's PostStep has decided which burst this was, so
    /// treating them as an answer would classify every landing dismount as a plain one.</para>
    /// </summary>
    private static bool Decisive(Toggle toggle, string ev) => toggle switch
    {
        Toggle.Mount => ev.StartsWith("HOP ON", StringComparison.Ordinal)
                     || ev.StartsWith("AIR MOUNT BURST", StringComparison.Ordinal)
                     || ev.StartsWith("SLINGSHOT", StringComparison.Ordinal),
        Toggle.Dismount => ev.StartsWith("LANDING DISMOUNT BURST", StringComparison.Ordinal)
                        || ev.StartsWith("KICK-OFF", StringComparison.Ordinal)
                        || ev.StartsWith("DISMOUNT + STUMBLE", StringComparison.Ordinal)
                        || ev.StartsWith("HOP OFF", StringComparison.Ordinal),
        _ => false,
    };

    /// <summary>
    /// <b>Counts the toggle being held open</b>, using <paramref name="ev"/> if it names a kind and
    /// the grounded flag from the toggle's own tick if it does not.
    ///
    /// <para>The fallback is the honest reading of what is left: <c>BikeLayer.OnMountPress</c>
    /// branches on grounded and nothing else, so a mount with no burst string is a ground mount, and
    /// a dismount in the air with no burst string can only have come out of the kick-off path (the
    /// landing routes all write their burst). It only ever fires when a tuned burst is zero or the
    /// harness stops sampling mid-toggle; every ordinary press resolves on its own string.</para>
    /// </summary>
    private void CommitToggle(string ev)
    {
        switch (_pendingToggle)
        {
            case Toggle.Mount:
                if (ev.StartsWith("AIR MOUNT BURST", StringComparison.Ordinal)
                 || ev.StartsWith("SLINGSHOT", StringComparison.Ordinal))
                    MountsAir++;
                else if (ev.StartsWith("HOP ON", StringComparison.Ordinal))
                    MountsGround++;
                else if (_pendingGrounded)
                    MountsGround++;
                else
                    MountsAir++;
                break;

            case Toggle.Dismount:
                if (ev.StartsWith("LANDING DISMOUNT BURST", StringComparison.Ordinal))
                    DismountsLanding++;
                else if (ev.StartsWith("KICK-OFF", StringComparison.Ordinal))
                    DismountsKickOff++;
                else if (ev.StartsWith("DISMOUNT + STUMBLE", StringComparison.Ordinal)
                      || ev.StartsWith("HOP OFF", StringComparison.Ordinal))
                    DismountsGround++;
                else if (_pendingGrounded)
                    DismountsGround++;
                else
                    DismountsKickOff++;
                break;
        }
        _pendingToggle = Toggle.None;
        _pendingTicks = 0;
    }

    // --- writing ---------------------------------------------------------------------------------

    private void TraceRow(in BikeTelemetrySample s)
    {
        TraceRows++;                       // counted with or without a file, so a self-test can run
        if (!Recording || _file == null)   // on a machine whose profile directory is read-only.
            return;
        WriteLine(string.Join(',',
            Num(_elapsedSec),
            Num(s.Position.X), Num(s.Position.Y), Num(s.Position.Z),
            Num(s.SpeedMps),
            s.Mounted ? "1" : "0",
            s.Grounded ? "1" : "0",
            Num(s.Blend),
            Num(s.DriftChargeSec),
            Csv(_course),
            Csv(s.LastEvent ?? "")));
        // Flushed per row rather than per session: a lab that crashes is precisely the session whose
        // trace is most worth having, and one write a second costs nothing. Null-conditional because
        // a write that failed has already dropped the handle through Fail().
        _file?.Flush();
    }

    private void Comment(string key, string value) => WriteLine($"# {Csv(key)},{Csv(value)}");

    private void WriteLine(string line)
    {
        try
        {
            _file?.StoreLine(line);
        }
        catch (Exception e)
        {
            // A write that fails mid-session stops the file and keeps the session: the counters and
            // the readout carry on, and the readout says the file is gone.
            Fail($"write failed - {e.GetType().Name}: {e.Message}");
        }
    }

    private void Fail(string why)
    {
        Recording = false;
        LastError = why;
        try
        {
            _file?.Close();
        }
        catch
        {
            // Already failing. A close that throws on top of a write that threw has nothing left to
            // tell anyone, and the whole point of this path is that it ends quietly.
        }
        _file = null;
        GD.PrintErr($"[bike-telemetry] {why} - counting in memory only");
    }

    // --- formatting ------------------------------------------------------------------------------

    /// <summary>A number, invariant, up to three decimals and no trailing zeroes. A non-finite
    /// value writes 0 rather than <c>NaN</c> or <c>∞</c>, neither of which is a number any
    /// spreadsheet will read back. See the class doc for why the culture is named on every one of
    /// these rather than set once somewhere.</summary>
    private static string Num(float v)
        => float.IsFinite(v) ? v.ToString("0.###", CultureInfo.InvariantCulture) : "0";

    private static string Int(int v) => v.ToString(CultureInfo.InvariantCulture);

    private static string Pct(float frac)
        => (frac * 100f).ToString("0", CultureInfo.InvariantCulture) + "%";

    private static string Text(string? value, string fallback)
        => string.IsNullOrEmpty(value) ? fallback : value;

    /// <summary>
    /// <b>One CSV cell, escaped.</b> RFC 4180: a cell containing a comma, a quote, or leading or
    /// trailing space is wrapped in quotes and its own quotes are doubled.
    ///
    /// <para><b>This is not defensive tidiness — <c>BikeLayer.LastEvent</c> really does contain
    /// commas and parentheses</b>, in most of its interesting strings:
    /// <c>KICK-OFF (0.31s to floor, window 0.20)</c>, <c>SLINGSHOT: swing -&gt; mount, +4.0 up
    /// +7.5 fwd</c>, <c>SWING LUNGE +4.5 fwd, vY &gt;= 2.5</c>. Written raw, each of those splits
    /// one row into two columns more than the header declares, and every reader either errors or —
    /// worse — silently shifts the columns after it. So no string reaches this file unescaped; the
    /// course and preset names go through here too, because a preset called
    /// <c>"CRUISER, but slower"</c> is one rename away.</para>
    ///
    /// <para>Newlines are replaced rather than quoted. A quoted newline is legal CSV, but every
    /// comment line in this file is one line by construction and a note containing a line break
    /// would end the comment and leave prose sitting where a data row goes.</para>
    /// </summary>
    private static string Csv(string? value)
    {
        string v = (value ?? "").Replace('\r', ' ').Replace('\n', ' ');
        bool needs = v.Contains(',') || v.Contains('"')
                  || v.StartsWith(' ') || v.EndsWith(' ');
        return needs ? '"' + v.Replace("\"", "\"\"") + '"' : v;
    }
}
