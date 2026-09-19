using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Godot;

namespace MpFoundation.Net;

/// <summary>
/// <b>Print-as-C# — the deliverable of the whole MOVE-4 wave</b> (spec §6.4). A tuning Talon likes
/// must paste back into the source without hand-editing; that round trip is the point, and vague
/// here means the wave fails quietly.
///
/// <para><b>MOVE-4d retargeted this at <c>MotorTuning.Default</c>, and the reason is measured
/// rather than argued.</b> §6.4 was written before MOVE-4b's seam existed, when the shipped values
/// lived as <c>const</c>s in <c>AvatarMotor.cs</c> and <c>AvatarVisual.cs</c>. They do not any
/// more: those symbols are properties reading <see cref="MotorTuning.Current"/>. Pasting the old
/// shape over them <b>does not compile</b> — the reshaped skid row emitted
/// <c>const float SkidEnterSpeedMps = MoveSpeed * 0.8f</c> and <c>MoveSpeed</c> is no longer a
/// constant expression (CS0133) — and the lines that <i>did</i> compile turned each knob back into
/// a <c>const</c>, switching off the very slider that produced them and reddening three of MOVE-4b's
/// own tests. <b>Where §6.4's stated shape and a paste that compiles disagree, the paste that
/// compiles wins.</b> The target is the one place a shipped value now lives:
/// <c>MotorTuning.Default</c>'s initializer, at its own eight-space indentation.</para>
///
/// <para><b>The acceptance test for this format is literal: paste, save, build, no edits.</b> Every
/// emitted line is a complete initializer element carrying its own <c>// was &lt;old&gt;</c>, so a
/// paste is reviewable in a diff, and the block names its single target file so nothing has to be
/// hunted.</para>
///
/// <para><b>And it carries its own warning.</b> Promoting a tuning is <i>always</i> at least a
/// two-file edit — <c>MotorTuningDefaultIdentityTests</c> asserts every default against the literal
/// the spec documents — and twenty-three of the thirty-one knobs are additionally pinned by a
/// test, eight of them frozen to within ±0.1% by a hand-typed literal (§3.2b). <b>This block is
/// the only place the full edit set can be known.</b> A breached pin gets a banner, a per-line
/// tag, the arithmetic of the breach, and the list of tests that will fail on paste.</para>
///
/// <para>Rendering is pure: <see cref="Render"/> takes a tuning and returns text.
/// <see cref="Publish"/> is the engine-touching half MOVE-4d's button calls.</para>
/// </summary>
public static class MotorTuningPrint
{
    private const int CommentColumn = 58;
    private const int LabelWidth = 30;

    /// <summary>The one file a printed tuning pastes into — <c>MotorTuning.Default</c>'s
    /// initializer. Named once in the header rather than once per row: a single target is a
    /// stronger form of §6.4 rule 4's "nothing has to be hunted" than thirty-one line numbers
    /// that go stale.</summary>
    public const string TargetFile = "scripts/net/MotorTuning.cs";

    /// <summary>The test that asserts every default against the literal the spec documents. It goes
    /// red on <b>every</b> promoted tuning, breached pins or not, which is why the "will fail on
    /// paste" block is emitted whenever anything moved rather than only on a breach.</summary>
    public const string IdentityTest =
        "MotorTuningDefaultIdentityTests.EveryDefault_IsTheLiteralTheSpecDocuments";

    private const string IdentityTestSource = "tests/unit/MotorTuningDefaultIdentityTests.cs:58";

    // --- Number rendering (§6.4 rule 5) ---------------------------------------------------------

    /// <summary>Decimals needed to represent one step exactly — 1 for a step of 0.1 or 0.5, 2 for
    /// 0.01 or 0.05, 0 for 1.0.</summary>
    private static int DecimalsFor(float step)
    {
        for (int d = 0; d <= 4; d++)
        {
            float scaled = step * MathF.Pow(10f, d);
            if (MathF.Abs(scaled - MathF.Round(scaled)) < 1e-4f)
                return d;
        }
        return 4;
    }

    /// <summary>
    /// <b>A C# <c>float</c> literal</b>: the minimum number of decimals that round-trips the
    /// slider's step, always with the <c>f</c> suffix and never fewer than one decimal —
    /// <c>12.5f</c>, not <c>12.500000f</c>, and never <c>12.5</c>, which is a <c>double</c> and
    /// will not compile into a <c>float</c> const.
    /// </summary>
    public static string Literal(float value, float step)
    {
        string body = Trim(value, DecimalsFor(step));
        if (!body.Contains('.'))
            body += ".0";
        return body + "f";
    }

    /// <summary>The same number for prose — a <c>// was</c> comment or a warning line — with no
    /// suffix and no decorative trailing zero. <c>9</c>, <c>21</c>, <c>1.35</c>.</summary>
    public static string Plain(float value, float step) => Trim(value, DecimalsFor(step));

    private static string Trim(float value, int decimals)
    {
        string s = value.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture),
            CultureInfo.InvariantCulture);
        if (!s.Contains('.'))
            return s;
        s = s.TrimEnd('0').TrimEnd('.');
        return s.Length == 0 || s == "-" ? "0" : s;
    }

    // --- What gets printed ----------------------------------------------------------------------

    /// <summary>The rows this print emits: everything that moved, plus (§6.4 rule 11) any row that
    /// is inert at its default and became live because a companion row moved.</summary>
    private static List<MotorKnob> RowsToPrint(MotorTuning t, bool all)
    {
        if (all)
            return MotorTuningKnobs.All.ToList();

        var moved = new HashSet<string>(MotorTuningKnobs.All
            .Where(k => k.Get(t) != k.Default)
            .Select(k => k.Name), StringComparer.Ordinal);

        return MotorTuningKnobs.All
            .Where(k => moved.Contains(k.Name)
                     || (k.LiveWhenMoved.Length > 0 && moved.Contains(k.LiveWhenMoved)))
            .ToList();
    }

    // --- The block ------------------------------------------------------------------------------

    /// <summary>
    /// Renders the paste block for <paramref name="t"/>.
    /// </summary>
    /// <param name="t">The tuning to print. Usually <c>MotorTuning.Current</c>.</param>
    /// <param name="stamp">Print time, rendered into the header.</param>
    /// <param name="all"><c>true</c> for "Print All" — every one of the table's rows in the same
    /// shape, rather than only what moved.</param>
    public static string Render(MotorTuning t, DateTimeOffset stamp, bool all = false)
    {
        List<MotorKnob> rows = RowsToPrint(t, all);
        int changed = MotorTuningKnobs.All.Count(k => k.Get(t) != k.Default);

        // Evaluate every pin on the rows being printed, once.
        var verdicts = new List<(MotorKnob Knob, bool Breached, float Value, float? Lo, float? Hi)>();
        foreach (MotorKnob k in rows.Where(k => k.IsPinned))
        {
            bool breached = k.Breaches(t, out float value, out float? lo, out float? hi);
            verdicts.Add((k, breached, value, lo, hi));
        }
        var breaches = verdicts.Where(v => v.Breached).ToList();

        var sb = new StringBuilder();
        string rule = new string('─', 78);

        string head = "// ─── MotorTuning print — "
                    + stamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " ";
        sb.AppendLine(head + new string('─', Math.Max(3, 81 - head.Length)));
        sb.AppendLine($"// {changed} of {MotorTuningKnobs.All.Count} knobs differ from the shipped defaults.");
        sb.AppendLine("//");
        sb.AppendLine($"// PASTE TARGET: {TargetFile} — inside MotorTuning.Default's initializer.");
        sb.AppendLine("// Paste each line below over the field of the same name; the indentation already matches.");
        sb.AppendLine("// That initializer IS the shipped value since MOVE-4b's seam — AvatarMotor's and");
        sb.AppendLine("// AvatarVisual's old constants are properties reading MotorTuning.Current now, so pasting");
        sb.AppendLine("// a `const` over one of them would switch its own slider off.");
        sb.AppendLine("//");

        if (breaches.Count > 0)
        {
            sb.AppendLine($"// !! {breaches.Count} PINNED KNOB{(breaches.Count == 1 ? " IS" : "S ARE")} OUT OF BOUNDS. THE SUITE WILL GO RED IF YOU PASTE THIS. !!");
            sb.AppendLine("//");
        }

        foreach ((MotorKnob k, bool breached, float value, float? lo, float? hi) in verdicts)
        {
            string quantity = k.PinQuantityLabel.Length > 0
                ? $"{k.Name} {Plain(k.Get(t), k.Step)} ({k.PinQuantityLabel} {value:0.###})"
                : $"{k.Name} {Plain(value, k.Step)}";
            sb.AppendLine($"//    {quantity} is {Verdict(breached, lo, hi)}, asserted by");
            sb.AppendLine($"//      {k.PinTest}");
            sb.AppendLine($"//      ({k.PinSource}).{(breached ? "" : $" OK, with {MarginText(value, lo, hi)}.")}");
        }
        if (verdicts.Count > 0)
            sb.AppendLine("//");

        // Emitted whenever anything actually moved, not only on a breach: the identity test goes
        // red on EVERY promoted tuning, so "no breaches" must never read as "a one-file edit".
        // Gated on `changed` rather than on the printed row count, because Print All at the shipped
        // tuning is a paste of the defaults over themselves — a no-op that fails nothing.
        if (changed > 0 || breaches.Count > 0)
        {
            sb.AppendLine("// TESTS THAT WILL FAIL ON PASTE, and the literals each one asserts:");
            sb.AppendLine($"//    {IdentityTest}");
            sb.AppendLine($"//      {IdentityTestSource}");
            sb.AppendLine($"//      asserts: every one of the {MotorTuningKnobs.All.Count} defaults against "
                        + "the literal the spec");
            sb.AppendLine("//               documents. It fires on ANY promoted tuning, breach or not —");
            sb.AppendLine("//               promoting one is always at least a two-file edit.");
            foreach ((MotorKnob k, _, _, _, _) in breaches)
            {
                sb.AppendLine($"//    {k.PinTest}");
                sb.AppendLine($"//      {k.PinSource}");
                sb.AppendLine($"//      asserts: {k.PinLiteral}");
            }
            sb.AppendLine("//    ... and this list is the PINS, not the whole set. Every test that uses");
            sb.AppendLine("//      MotorTuning.Default as its fixture moves when the defaults move, and that");
            sb.AppendLine("//      set cannot be known without running the suite. Paste, then run BOTH suite");
            sb.AppendLine("//      commands before you believe the tuning.");
            sb.AppendLine("//");
        }

        sb.AppendLine("// live invariant readout at print time:");
        foreach (MotorInvariant inv in MotorTuningInvariants.Evaluate(t))
        {
            string label = inv.Name.Length >= LabelWidth
                ? inv.Name
                : inv.Name + " " + new string('.', LabelWidth - inv.Name.Length - 1);
            string measured = inv.BoundSpeaksForItself
                ? inv.Bound
                : inv.Bound.StartsWith("(", StringComparison.Ordinal)
                    ? $"{inv.Value:0.000} {inv.Bound}"
                    : $"{inv.Value:0.000} / {inv.Bound}";
            sb.AppendLine($"//    {label} {measured,-24} {(inv.Holds ? "holds" : "BREACHED")}");
        }
        sb.AppendLine("// " + rule);

        // The initializer elements, in knob-table order, with a blank line between §2.2 groups —
        // which is exactly how MotorTuning.Default's own initializer is laid out, so a pasted
        // block lands in the shape the file already has. One contiguous run of pasteable lines:
        // there is no per-row header comment any more, because there is only one target and it is
        // named once above.
        sb.AppendLine();
        string? currentGroup = null;
        foreach (MotorKnob k in rows)
        {
            if (currentGroup is not null && k.Group != currentGroup)
                sb.AppendLine();
            currentGroup = k.Group;

            (MotorKnob _, bool breached, float value, float? lo, float? hi) =
                verdicts.FirstOrDefault(v => ReferenceEquals(v.Knob, k));
            bool isBreach = breaches.Any(b => ReferenceEquals(b.Knob, k));
            string tag = isBreach ? $"   [PINNED {Window(lo, hi)} — BREACHED]" : "";

            sb.AppendLine(Declaration(k, t, tag));
        }

        if (!all)
        {
            int unchanged = MotorTuningKnobs.All.Count - rows.Count;
            sb.AppendLine();
            string tail = $"// ─── unchanged ({unchanged}) — press Print All to include them ";
            sb.AppendLine(tail + new string('─', Math.Max(3, 81 - tail.Length)));
        }

        return sb.ToString();
    }

    /// <summary>
    /// One complete initializer element at <c>MotorTuning.Default</c>'s own eight-space
    /// indentation, with its <c>// was</c> comment (§6.4 rules 2, 3, 11) and, for a breached pin,
    /// rule 6's per-line tag.
    ///
    /// <para><b>§6.4 rule 9 — the derived skid form — is satisfied structurally now rather than
    /// textually.</b> The old target needed <c>SkidEnterSpeedMps = MoveSpeed * 0.75f</c> re-emitted
    /// so the threshold could not be stranded at an absolute number when <c>MoveSpeed</c> moved.
    /// The field being a <i>fraction</i> is that guarantee, and <c>MotorTuning.SkidEnterSpeedMps</c>
    /// does the multiply. The effective m/s is still printed beside it, because the number a reader
    /// wants to see is the speed, not the fraction.</para>
    /// </summary>
    private static string Declaration(MotorKnob k, MotorTuning t, string tag)
    {
        float value = k.Get(t);
        string decl = $"        {k.Name} = {Literal(value, k.Step)},";

        string was = $"// was {Plain(k.Default, k.Step)}";
        if (k.PinQuantity is not null && k.PinQuantityLabel.Length > 0)
            was += $"  ({k.PinQuantityLabel} {k.PinQuantity(t):0.###})";
        if (value == k.Default && k.LiveWhenMoved.Length > 0)
            was += $" (unchanged, printed because {k.LiveWhenMoved} makes it live)";
        was += tag;

        int pad = Math.Max(CommentColumn - decl.Length, 2);
        return decl + new string(' ', pad) + was;
    }

    /// <summary>How the pin reads in prose — "inside [a, b]", "inside its ceiling of x", "below
    /// its floor of x". A one-sided window described as "inside &lt;= 0.376" is a sentence nobody
    /// wrote on purpose.</summary>
    private static string Verdict(bool breached, float? lo, float? hi) => (lo, hi) switch
    {
        (float l, float h) when l == h => breached ? $"NOT exactly {l:0.####}" : $"exactly {l:0.####}",
        (float l, float h) => $"{(breached ? "outside" : "inside")} [{l:0.####}, {h:0.####}]",
        (float l, null) => breached ? $"below its floor of {l:0.####}" : $"above its floor of {l:0.####}",
        (null, float h) => breached ? $"above its ceiling of {h:0.####}" : $"inside its ceiling of {h:0.####}",
        _ => "unpinned",
    };

    private static string Window(float? lo, float? hi) => (lo, hi) switch
    {
        (float l, float h) when l == h => $"exactly {l:0.####}",
        (float l, float h) => $"[{l:0.####}, {h:0.####}]",
        (float l, null) => $"> {l:0.####}",
        (null, float h) => $"<= {h:0.####}",
        _ => "(unbounded)",
    };

    private static string MarginText(float value, float? lo, float? hi)
    {
        float? slack = null;
        if (hi is float h && h != 0f)
            slack = (h - value) / MathF.Abs(h);
        if (lo is float l && l != 0f)
        {
            float low = (value - l) / MathF.Abs(l);
            slack = slack is float s ? MathF.Min(s, low) : low;
        }
        return slack is float m && float.IsFinite(m)
            ? $"about {m * 100f:0}% of margin"
            : "no margin to report";
    }

    // --- The engine-touching half (MOVE-4d's button calls this) ----------------------------------

    /// <summary>
    /// <b>Three places at once</b> (§6.4): the Godot console, the OS clipboard, and a timestamped
    /// archive under <c>user://movement-tuning-prints/</c>.
    ///
    /// <para><b>The clipboard is not optional</b> — "paste it back" is the workflow, and making
    /// Talon hunt a console line for it is how the round trip quietly stops happening.</para>
    /// </summary>
    /// <returns>The absolute path of the archived copy, or empty if it could not be written.</returns>
    public static string Publish(string block, DateTimeOffset stamp)
    {
        GD.Print(block);
        DisplayServer.ClipboardSet(block);

        try
        {
            string dir = MotorTuningFile.PrintDirAbsolutePath();
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir,
                stamp.ToString("yyyy-MM-dd-HHmmss", CultureInfo.InvariantCulture) + ".cs");
            File.WriteAllText(path, block, new UTF8Encoding(false));
            return path;
        }
        catch (IOException e)
        {
            GD.PushWarning($"MotorTuning print archive failed: {e.Message}");
            return "";
        }
    }
}
