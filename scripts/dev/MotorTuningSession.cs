using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MpFoundation.Net;

namespace MpFoundation.Dev;

/// <summary>
/// Writes a tuning, or refuses and says why. <b>The production implementation is
/// <see cref="MotorTuning.TryApply"/> and there is no other</b> — this delegate exists so the
/// session's decisions can be unit-tested without ever mutating <see cref="MotorTuning.Current"/>,
/// which is process-global state that seven parallel test classes now read live
/// (MOVE-4b's standing rule).
/// </summary>
public delegate bool TuningWriter(in MotorTuning next, out string refusal);

/// <summary>
/// <b>The knob panel's decisions, with no Godot in them (MOVE-4d).</b> Everything the lab does to a
/// tuning — load it at startup, nudge one knob, reset, save, render the paste block — lives here;
/// <see cref="MotorTuningPanel"/> is a thin widget shell over it.
///
/// <para><b>Why the split.</b> The xUnit suite references the game assembly but has no native
/// engine, so a <c>Control</c> cannot be instantiated in a test. Put the arithmetic and the
/// state transitions in a class with no <c>Godot</c> namespace in it and every one of them is
/// demonstrable rather than asserted — which is what MOVE-4d's acceptance criteria 5, 7 and 8
/// actually ask for.</para>
///
/// <para><b>Every write goes through MOVE-4b's single writer</b> (§7.2). This class never assigns
/// <see cref="MotorTuning.Current"/>; it calls <see cref="TuningWriter"/>, whose default is
/// <see cref="MotorTuning.TryApply"/>. A refusal is displayed, never worked around — the lab being
/// a special case is exactly what the guard is written to allow, so if it refuses here the answer
/// is that a network session is live and the panel must stay inert.</para>
/// </summary>
public sealed class MotorTuningSession
{
    /// <summary>How many notice lines are kept. A lab session generates them steadily (every clamp
    /// is reported), and a readout that grows without bound stops being readable long before it
    /// stops being correct.</summary>
    public const int MaxNotices = 24;

    private readonly TuningWriter _write;
    private readonly Func<MotorTuning> _read;
    private readonly List<string> _notices = new();

    /// <summary>
    /// <paramref name="filePath"/> is the <i>absolute</i> path of <c>user://movement-tuning.json</c>,
    /// resolved by the caller — this class never asks the engine anything. <b>Empty disables file
    /// I/O entirely</b>, which is the scripted-capture mode: a measurement run must read the shipped
    /// defaults, and it must never overwrite the tuning a human left in that file.
    /// </summary>
    public MotorTuningSession(string filePath, TuningWriter? writer = null,
        Func<MotorTuning>? reader = null)
    {
        FilePath = filePath ?? "";
        _write = writer ?? MotorTuning.TryApply;
        _read = reader ?? (() => MotorTuning.Current);
    }

    /// <summary>Absolute path of the tuning file, or empty when file I/O is off.</summary>
    public string FilePath { get; }

    public bool FileIoEnabled => FilePath.Length > 0;

    /// <summary>Newest last. Every clamp, every ignored field, every refusal — §6.3 requires these
    /// on the playground readout, not only in a console nobody is reading.</summary>
    public IReadOnlyList<string> Notices => _notices;

    /// <summary><b>The live tuning, read back through the writer's own store every time.</b> There
    /// is deliberately no cached copy anywhere in this class: a lab that shows a number the motor
    /// is not running on is the one thing a lab may never do.</summary>
    public MotorTuning Live => _read();

    /// <summary>Empty unless the last write was refused by the parity guard.</summary>
    public string LastRefusal { get; private set; } = "";

    /// <summary>How many times the file has been written this session. Read by the capture log so a
    /// headed run can prove the round trip fired rather than assert it.</summary>
    public int SaveCount { get; private set; }

    /// <summary>How many times the file has been read this session.</summary>
    public int LoadCount { get; private set; }

    // --- Notices -----------------------------------------------------------------------------

    /// <summary>
    /// Mirror for every notice as it is raised. The panel points it at the Godot console, so a
    /// warning that has scrolled past the panel's visible lines — or that was raised while the panel
    /// was hidden — is still findable afterwards. Left null by the tests, which is what keeps this
    /// class free of <c>Godot</c>.
    /// </summary>
    public Action<string>? NoticeSink { get; set; }

    public void Note(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;
        _notices.Add(line);
        while (_notices.Count > MaxNotices)
            _notices.RemoveAt(0);
        NoticeSink?.Invoke(line);
    }

    public void ClearNotices() => _notices.Clear();

    // --- Load, apply, save -------------------------------------------------------------------

    /// <summary>
    /// <b>Startup load, through the guard</b> (§6.3, scope item 5). Every warning the loader
    /// produced is kept for the readout, then the loaded tuning is applied by the single writer —
    /// including in the file-absent case, where the tuning is <see cref="MotorTuning.Default"/> and
    /// the apply is a no-op that still proves the writer is reachable.
    ///
    /// <para><b>Deliberately does not save.</b> Writing the file back at startup would rewrite a
    /// hand-edited file with its clamped self before anyone had a chance to see the warning saying
    /// it was clamped.</para>
    /// </summary>
    /// <returns>True if the tuning was applied; false if the guard refused, in which case
    /// <see cref="LastRefusal"/> says why and every knob is at whatever the writer already held.</returns>
    public bool LoadFromFile()
    {
        if (!FileIoEnabled)
        {
            Note("tuning file I/O is OFF for this run — every knob is at its shipped default and "
               + "nothing will be written.");
            return ApplyInternal(MotorTuning.Default, save: false);
        }

        MotorTuningLoad load = MotorTuningFile.LoadFrom(FilePath);
        LoadCount++;
        foreach (string w in load.Warnings)
            Note(w);
        if (load.Discarded)
            Note($"the whole file was discarded — {FilePath}. Never a partial application: half a "
               + "corrupt file is a tuning nobody authored.");
        else if (load.FileExisted)
            Note($"loaded {FilePath} — {ChangedCount(load.Tuning)} of {MotorTuningKnobs.All.Count} "
               + "knobs differ from the shipped defaults.");

        return ApplyInternal(load.Tuning, save: false);
    }

    /// <summary>Re-reads the file mid-session and applies it. The A/B partner of
    /// <see cref="ResetAll"/>: reset to feel the shipped numbers, reload to feel yours again.</summary>
    public bool ReloadFromFile() => LoadFromFile();

    /// <summary>Applies a whole tuning and writes the file (§6.2 — on every apply, not only on
    /// quit: a session that ends in a crash or an Alt-F4 must not lose the setting).</summary>
    public bool Apply(in MotorTuning next) => ApplyInternal(next, save: true);

    private bool ApplyInternal(in MotorTuning next, bool save)
    {
        // Validate here as well as inside TryApply, and keep the warnings this time. TryApply
        // discards them (`out _`) and §6.3 requires every one of them on the readout. Validation is
        // idempotent (MOVE-4b's ValidationIsIdempotent_SoTheLegalSetIsClosed), so handing it an
        // already-validated value is a no-op rather than a second opinion — and this is NOT a
        // second write path: the write below is still the one writer.
        MotorTuning validated = MotorTuning.Validate(next, out IReadOnlyList<string> warnings);
        foreach (string w in warnings)
            Note(w);

        if (!_write(validated, out string refusal))
        {
            LastRefusal = refusal;
            Note("REFUSED — " + refusal);
            return false;
        }

        LastRefusal = "";
        if (save)
            Save();
        return true;
    }

    /// <summary>Writes what actually landed, not what was requested — a value the validator moved
    /// must not be written back as the value that was asked for.</summary>
    public void Save()
    {
        if (!FileIoEnabled)
            return;
        try
        {
            MotorTuningFile.SaveTo(FilePath, _read(), DateTimeOffset.UtcNow);
            SaveCount++;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Note($"could not write {FilePath}: {e.Message}");
        }
    }

    // --- The knob operations the panel's keys and sliders call --------------------------------

    /// <summary>Sets one knob, snapped to its step grid. A value equal to what is already live is
    /// not written at all, so holding an arrow key against a slider's end does not spend a file
    /// write per frame.</summary>
    public bool SetKnob(MotorKnob knob, float value)
    {
        MotorTuning live = Live;
        float snapped = Snap(knob, value);
        if (snapped == knob.Get(live))
            return true;
        return Apply(knob.Set(live, snapped));
    }

    /// <summary>Moves one knob by whole steps. <paramref name="steps"/> may be negative.</summary>
    public bool Nudge(MotorKnob knob, int steps) =>
        SetKnob(knob, knob.Get(Live) + steps * knob.Step);

    /// <summary>Puts one knob back to its shipped default and saves — this is an edit like any
    /// other, and an edit that is not written is an edit a crash takes with it.</summary>
    public bool ResetKnob(MotorKnob knob) => SetKnob(knob, knob.Default);

    /// <summary>
    /// <b>Everything back to <see cref="MotorTuning.Default"/>, and the file is NOT touched.</b>
    ///
    /// <para>Acceptance criterion 7 requires a save → reset → load round trip to return the saved
    /// values, which it can only do if reset leaves the file alone. That is also the better lab
    /// behaviour by a distance: reset is the A/B key — drop to the shipped numbers, feel the
    /// difference, reload — and a reset that silently destroyed the session's work would be the
    /// single most expensive keystroke in the panel.</para>
    /// </summary>
    public bool ResetAll()
    {
        bool ok = ApplyInternal(MotorTuning.Default, save: false);
        if (ok)
            Note(FileIoEnabled
                ? "reset to the shipped defaults. The saved file is untouched — reload it to get "
                + "your tuning back."
                : "reset to the shipped defaults.");
        return ok;
    }

    /// <summary>§6.4's paste block for the live tuning. Pure — the console/clipboard/archive half
    /// (<c>MotorTuningPrint.Publish</c>) needs an engine and stays in the panel.</summary>
    public string Render(bool all, DateTimeOffset stamp) => MotorTuningPrint.Render(Live, stamp, all);

    // --- Derived counts, for the status line and the capture log ------------------------------

    public static int ChangedCount(in MotorTuning t)
    {
        MotorTuning local = t;   // an `in` parameter cannot be captured by a lambda
        return MotorTuningKnobs.All.Count(k => k.Get(local) != k.Default);
    }

    /// <summary>How many pinned rows are currently outside their window. Coupled windows are
    /// evaluated against the live tuning, so a combination neither knob breaches alone is
    /// counted (§3.4).</summary>
    public static int BreachedKnobCount(in MotorTuning t)
    {
        MotorTuning local = t;
        return MotorTuningKnobs.All.Count(k => k.Breaches(local, out _, out _, out _));
    }

    public static int BreachedInvariantCount(in MotorTuning t) =>
        MotorTuningInvariants.Evaluate(t).Count(i => !i.Holds);

    /// <summary>The one line the playground's own readout carries, so the state of the tuning is
    /// visible even with the panel hidden.</summary>
    public string StatusLine()
    {
        MotorTuning t = Live;
        int changed = ChangedCount(t);
        int knobs = BreachedKnobCount(t);
        int invariants = BreachedInvariantCount(t);
        string breach = knobs == 0 && invariants == 0
            ? "all pins hold"
            : $"{knobs} pin{(knobs == 1 ? "" : "s")} / {invariants} invariant"
              + $"{(invariants == 1 ? "" : "s")} BREACHED";
        string refusal = LastRefusal.Length > 0 ? "   WRITES REFUSED" : "";
        // The notice count travels on the harness readout as well as inside the panel, because
        // §6.3 requires every warning to reach the playground readout and the panel is hideable:
        // a clamp reported only on a hidden panel is a warning that did not happen.
        string notices = _notices.Count > 0
            ? $"   {_notices.Count} notice{(_notices.Count == 1 ? "" : "s")} (F1)"
            : "";
        return $"tuning     {changed} of {MotorTuningKnobs.All.Count} moved   {breach}"
             + $"{refusal}{notices}";
    }

    // --- The step grid ------------------------------------------------------------------------

    /// <summary>Decimal places the step needs — 2 for 0.05, 1 for 0.5, 0 for 1.0. Mirrors the rule
    /// <c>MotorTuningPrint</c> prints literals with (§6.4 rule 5), so a nudged value is always a
    /// value the printer can render short.</summary>
    public static int DecimalsFor(float step)
    {
        float s = MathF.Abs(step);
        int d = 0;
        while (d < 6 && MathF.Abs(s - MathF.Round(s)) > 1e-4f)
        {
            s *= 10f;
            d++;
        }
        return d;
    }

    /// <summary>
    /// Snaps to the knob's step grid, anchored at its minimum, then clamps into
    /// <c>[Min, Max]</c>.
    ///
    /// <para><b>The rounding is done in <c>double</c> and then to the step's own decimal places</b>,
    /// because <c>0.75f + 0.01f</c> repeated forty times is not <c>1.15f</c> — and a knob that has
    /// drifted to 1.1500001 prints as a literal nobody typed and cannot be nudged back onto the
    /// grid. All thirty shipped defaults sit exactly on their own grid
    /// (<c>MotorTuningSessionTests.EveryShippedDefault_SitsOnItsOwnStepGrid</c>), so snapping can
    /// never move a knob off its default.</para>
    /// </summary>
    public static float Snap(MotorKnob knob, float raw)
    {
        if (!float.IsFinite(raw))
            return knob.Default;
        double steps = Math.Round(((double)raw - knob.Min) / knob.Step, MidpointRounding.AwayFromZero);
        double snapped = Math.Round(knob.Min + steps * knob.Step, DecimalsFor(knob.Step),
            MidpointRounding.AwayFromZero);
        return Math.Clamp((float)snapped, knob.Min, knob.Max);
    }

    /// <summary>Is <paramref name="value"/> exactly on the knob's step grid?</summary>
    public static bool IsOnStepGrid(MotorKnob knob, float value) => Snap(knob, value) == value;

    // --- The panel's row order, derived from the knob table -----------------------------------

    /// <summary>One §2.2 group and the rows in it, in knob-table order.</summary>
    public readonly record struct KnobGroup(string Name, IReadOnlyList<MotorKnob> Knobs);

    /// <summary>
    /// <b>The panel's layout, derived from <see cref="MotorTuningKnobs.All"/> and from nothing
    /// else</b> — not a second hand-typed list of rows, and not a hand-typed list of group names
    /// either. A knob added to the table is a knob that appears in the panel, in the right group,
    /// with no edit here.
    /// </summary>
    public static IReadOnlyList<KnobGroup> Groups { get; } = BuildGroups();

    private static IReadOnlyList<KnobGroup> BuildGroups()
    {
        var groups = new List<KnobGroup>();
        var current = new List<MotorKnob>();
        string name = "";

        foreach (MotorKnob knob in MotorTuningKnobs.All)
        {
            if (knob.Group != name)
            {
                if (current.Count > 0)
                    groups.Add(new KnobGroup(name, current));
                current = new List<MotorKnob>();
                name = knob.Group;
            }
            current.Add(knob);
        }
        if (current.Count > 0)
            groups.Add(new KnobGroup(name, current));
        return groups;
    }

    /// <summary>Every row, in table order — the flat view the keyboard selection walks.</summary>
    public static IReadOnlyList<MotorKnob> Rows { get; } =
        Groups.SelectMany(g => g.Knobs).ToArray();
}
