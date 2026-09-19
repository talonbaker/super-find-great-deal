using Godot;
using MpFoundation.Game.World;

namespace Sail.Game.World.BubbleTest;

/// <summary>
/// Publishes <c>bt_darkness</c> — one global shader parameter, 0 at noon and 1 at deep night —
/// so the bubble-test level's night aids can fade in with the dark without any of them owning a
/// clock. Program decision D10.
///
/// <para><b>This class derives nothing.</b> It reads
/// <see cref="OutdoorAtmosphere.Evaluate"/>'s <c>NightFactor</c> — the exact curve the sky itself
/// is drawn from — and republishes it under a name shaders can reach. That indirection is the
/// entire point of the file. The alternative, a shader computing darkness from its own copy of
/// the phase breakpoints, is how two systems come to disagree about what time it is; this repo
/// already carries a standing rule that <see cref="OutdoorAtmosphere"/> is the sole writer of
/// sky, sun, moon and ambient, and a second darkness authority would be that rule broken by the
/// back door. Nothing here writes back into the atmosphere, and
/// <c>OutdoorAtmosphere.NightAmbientFloor</c> is not touched, read for a decision, or resolved
/// (THRILL-BIBLE §6.2 is Talon's fork and stays open).</para>
///
/// <para><b>Why a global rather than a per-material uniform.</b> The aids are spread across four
/// section scenes authored by four different packets — blue rims, the beam, red goal blocks, cyan
/// obstacle rims — and every one of them needs the same number in the same frame. A global is one
/// write per frame for the whole level regardless of how many meshes wear the material, and it
/// means a geometry packet assigns a material and is finished: nothing to wire, nothing to
/// remember to update, and no way for one section to drift out of step with another.</para>
///
/// <para><b>TRAP, AND IT WILL LOOK EXACTLY LIKE THE UNIFORM BEING IGNORED.</b> This driver
/// republishes <c>bt_darkness</c> every single frame. A value poked in the inspector, or passed on
/// the command line, is therefore overwritten silently on the next frame — the classic
/// per-frame-publisher-versus-<c>--set</c> failure this repo has already paid for once. To hold a
/// value: use the <c>darkness_override</c> uniform on <c>night_edge.gdshader</c> (which wins over
/// the global by design, and is what the BT-9 captures use), or stop this node's processing.</para>
///
/// <para><b>Before the clock is synced, the level renders as DAY.</b> A client that has not yet
/// received the replicated phase must never render <c>t = 0</c> first — the same guard
/// <see cref="OutdoorAtmosphere"/> uses. Here the failure would be worse than a wrong sky: every
/// aid in the level would flash on at full strength for the first frames of a join, which is the
/// one thing D10's "nothing else in the level gets a light" is protecting against. So the global
/// is seeded to 0 in <c>_Ready</c> and only ever moves once the driver reports <c>Synced</c>.</para>
/// </summary>
public partial class NightAidDriver : Node
{
    /// <summary>The <c>[shader_globals]</c> name. Declared in <c>project.godot</c>; a shader that
    /// declares <c>global uniform float bt_darkness;</c> reads whatever is last written here.</summary>
    public const string GlobalName = "bt_darkness";

    /// <summary>Command-line flag that makes <c>_Ready</c> print the acceptance-criterion table
    /// and nothing else. Exists so the curve can be proved in a HEADLESS run, without a live
    /// <see cref="CycleDriver"/>, a GPU, or waiting out a cycle.</summary>
    private const string ProbeFlag = "--bt-darkness-probe";

    /// <summary>Command-line flag that prints, once a second, what this driver is publishing in a
    /// LIVE session: whether the clock is synced, the phase, and the value written that frame.
    ///
    /// <para><b>Why <see cref="ProbeFlag"/> was not enough</b> (FIX-1, 2026-08-28).
    /// <c>--bt-darkness-probe</c> evaluates the curve in <c>_Ready</c>, so it proves the MATHS and
    /// nothing else. It cannot say whether this driver is in the tree, or whether
    /// <see cref="CycleDriver"/> ever reported <c>Synced</c> on this peer — and "the night aids do
    /// not light" is consistent with both. This flag closes that half of the gap.</para>
    ///
    /// <para><b>It deliberately does NOT read the global back</b>, because in a running project
    /// nothing can — see the comment in <see cref="MaybeReadOut"/>. The render is the only witness
    /// to what the shader actually received.</para></summary>
    private const string ReadoutFlag = "--bt-darkness-readout";

    /// <summary>Day index offset, mirroring <see cref="OutdoorAtmosphere"/>'s own export so a dev
    /// launch can jump to a later night. 0 is day one and is what a normal session runs.</summary>
    [Export] public int DayIndexOffset { get; set; }

    private bool _readout;
    private double _readoutTimer;

    public override void _Ready()
    {
        // Seed the day value before anything renders a frame. See the class doc.
        Publish(0f);

        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg == ProbeFlag)
                PrintProbe();
            else if (arg == ReadoutFlag)
                _readout = true;
        }
    }

    public override void _Process(double delta)
    {
        // Identical guard to OutdoorAtmosphere._Process, for the reason in the class doc: an
        // unsynced client holds the last-applied value rather than rendering phase zero.
        if (CycleDriver.Instance is not { Synced: true } driver)
        {
            MaybeReadOut(delta, synced: false, phase: 0f);
            return;
        }

        Publish(DarknessAt(driver.Phase, driver.CyclesElapsed + DayIndexOffset));
        MaybeReadOut(delta, synced: true, driver.Phase);
    }

    /// <summary>The readout. Prints <c>synced</c> alongside the value, because an unsynced peer is
    /// the one failure mode that produces a perfectly plausible 0 rather than an error.</summary>
    private void MaybeReadOut(double delta, bool synced, float phase)
    {
        if (!_readout)
            return;
        _readoutTimer -= delta;
        if (_readoutTimer > 0)
            return;
        float value = synced && CycleDriver.Instance is { } d
            ? DarknessAt(d.Phase, d.CyclesElapsed + DayIndexOffset) : 0f;
        _readoutTimer = 1.0;
        // EVERY "read it back" API HERE IS EDITOR-ONLY, and both of them fail in a way that reads
        // as evidence (FIX-1, 2026-08-28, two wasted capture rounds).
        // `RenderingServer.GlobalShaderParameterGet` fails with "should never be used outside the
        // editor" and hands back a NIL Variant -- which looks exactly like "the global is unset".
        // `GlobalShaderParameterGetList` fails the same way and returns an EMPTY list -- which
        // looks exactly like "the global was never registered". Neither is true: the
        // `[shader_globals]` block in project.godot registers `bt_darkness` correctly, proved by
        // `global_shader_parameter_add` refusing a duplicate ("Condition ...has(p_name) is true").
        // So this readout prints only what a running project can honestly know -- whether the clock
        // is synced, and the value this driver computed and wrote -- and the RENDER is the only
        // instrument that can testify to what the shader received.
        GD.Print($"[bt-darkness] synced={synced} phase={phase:F3} published={value:F4}");
    }

    /// <summary>The single source of the number. Pure, so the probe below and the live frame
    /// cannot disagree, and so a test can assert on it without an engine loop.</summary>
    public static float DarknessAt(float phase, int cyclesElapsed)
        => Mathf.Clamp(OutdoorAtmosphere.Evaluate(phase, cyclesElapsed).NightFactor, 0f, 1f);

    private static void Publish(float darkness)
        => RenderingServer.GlobalShaderParameterSet(GlobalName, darkness);

    /// <summary>Acceptance criterion 3: <c>bt_darkness</c> reads 0.0 at noon and >= 0.9 at deep
    /// night. The two phases are <see cref="OutdoorAtmosphere"/>'s own documented breakpoints —
    /// index 1 (midday) and index 6 (deep night) of the day-one band table — so this prints the
    /// shipped curve rather than a restatement of it.</summary>
    private void PrintProbe()
    {
        const float middayPhase = 0.275f;    // breakpoint 1, "Midday"
        const float deepNightPhase = 0.780f; // breakpoint 6, "Deep night"
        int day = DayIndexOffset;

        GD.Print("[BT-9] bt_darkness probe (source: OutdoorAtmosphere.Evaluate().NightFactor)");
        GD.Print($"[BT-9]   phase={middayPhase:F3} (midday)     bt_darkness={DarknessAt(middayPhase, day):F4}");
        GD.Print($"[BT-9]   phase={deepNightPhase:F3} (deep night) bt_darkness={DarknessAt(deepNightPhase, day):F4}");
        // The dusk sweep, printed because the night_edge shader's `darkness_onset` (0.12) is
        // chosen against it: the aids must stay dark through the golden hour and arrive with the
        // purple, and these are the numbers that claim is made from.
        GD.Print($"[BT-9]   phase=0.550 (dusk start)  bt_darkness={DarknessAt(0.550f, day):F4}");
        GD.Print($"[BT-9]   phase=0.650 (night start) bt_darkness={DarknessAt(0.650f, day):F4}");
    }
}
