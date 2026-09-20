namespace MpFoundation.Game.Props;

/// <summary>
/// <b>PROBE-1's A/B switches, and they exist so a performance claim is a measurement rather than
/// an argument.</b>
///
/// <para>Each flag below turns OFF one of this packet's at-rest fixes, restoring the behaviour
/// the tree had at <c>integration/2026-09-19-mvp</c>. They default to ON (fixed), so a shipped
/// build never reads a flag to decide what to do — it does the right thing; the flags only ever
/// make it do the OLD thing, and only for the process that was launched with
/// <c>--probe-ablate</c>.</para>
///
/// <para><b>Why a switch rather than two builds.</b> The number this packet has to produce is
/// "what did the fix save", and two builds differ by their JIT, their import state and whichever
/// Godot happened to be warm. One build, one machine, one window, one flag flipped is the only
/// shape where the difference IS the fix. <c>.claude/rules/test-suite.md</c>'s whole
/// discrimination section is about measuring the quantity rather than the verdict; this is the
/// same discipline applied forwards.</para>
///
/// <para><b>Not a knob and not a setting.</b> Nothing in <c>project.godot</c>, nothing in the
/// player's profile, nothing on the wire. A process launched without <c>--probe-ablate</c> never
/// touches these.</para>
/// </summary>
public static class PropCostSwitches
{
    /// <summary><b>ON: a Carryable that is frozen, unheld, unhighlighted and visually settled
    /// stops writing its scale, its emission and its outline every physics tick.</b> OFF restores
    /// the unconditional per-tick writes. See <c>CarryIdle.IsSettled</c> and
    /// <c>Carryable._PhysicsProcess</c>.</summary>
    public static bool CarryableIdleGate { get; set; } = true;

    /// <summary><b>ON: the server's per-tick prop loop early-outs on a Loose COUNT the registry
    /// maintains, instead of walking every prop in the world to discover there are none.</b> OFF
    /// restores the full scan. See <c>PropRegistry.LooseCount</c> and
    /// <c>PropManager._PhysicsProcess</c>.</summary>
    public static bool LooseIndex { get; set; } = true;

    /// <summary>Applies <c>--probe-ablate</c>'s comma-separated token list. Unknown tokens are
    /// reported by the caller rather than swallowed: a misspelt ablation that silently measured
    /// the fixed build would be a wrong number in a handoff, which is the one failure this whole
    /// class is built to avoid. Returns the tokens it did not recognise.</summary>
    public static System.Collections.Generic.List<string> Ablate(string csv)
    {
        var unknown = new System.Collections.Generic.List<string>();
        foreach (string raw in csv.Split(','))
        {
            string t = raw.Trim().ToLowerInvariant();
            switch (t)
            {
                case "":
                case "none":
                    break;
                case "all":
                    CarryableIdleGate = false;
                    LooseIndex = false;
                    break;
                case "carryable":
                    CarryableIdleGate = false;
                    break;
                case "loop":
                    LooseIndex = false;
                    break;
                default:
                    unknown.Add(t);
                    break;
            }
        }
        return unknown;
    }
}
