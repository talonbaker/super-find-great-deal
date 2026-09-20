using System.Globalization;
using MpFoundation.Net;

namespace MpFoundation.Dev;

/// <summary>
/// <b>The verb half of the movement playground's readout (MOVE-5d, stripped by MOVE-5g), with no
/// Godot in it.</b> Two lines — the crouch verb with its shared clock, and the air-jump counter with
/// the variant it is running — composed from a <see cref="MoveState"/> and the tuning that state was
/// simulated against. Those three are exactly what §6.6 permits a lab readout to show.
///
/// <para><b>Why this is a separate, engine-free class.</b> The same argument
/// <see cref="MotorTuningSession"/> makes: the xUnit suite references the game assembly but has no
/// native engine, so a <c>Label</c> cannot be instantiated in a test. Put the composition in a pure
/// function and the one thing this packet must <i>prove</i> rather than assert — that the chain is
/// nowhere on the readout — becomes a property of a value a test can hold, instead of a claim about
/// a string that only ever existed inside a running window.</para>
///
/// <para><b>Nothing here is a display copy.</b> Both functions are pure and are called with the
/// live <see cref="MoveState"/> every frame; there is no field on this class at all, so a number
/// shown by it cannot be one the motor has moved on from.</para>
///
/// <para><b>SPEC §6.6 IS A PROHIBITION, NOT AN OMISSION, AND IT NAMES THIS BUILD.</b> "The chain
/// has no numeric readout, no bar, no icon, no text, no HUD element of any kind, in any build,
/// <i>including the lab</i>. §12 is the entire communication channel. This is stated as a
/// prohibition rather than an omission so that a debug readout does not arrive later as a
/// convenience and stay." So the two chain fields on <see cref="MoveState"/> — the depth and its
/// grace timer — are <b>never read here at all</b>, let alone rendered, and
/// <c>MovementVerbReadoutTests</c> sweeps every legal value of both to prove the rendered text does
/// not move when they do. The chain's only channel is the body's posture, which is MOVE-5c's.</para>
///
/// <para><b>The one residual bit is gone (MOVE-5g), and the reversal is worth recording.</b>
/// MOVE-5d ended the air line with an "air-speed ceiling HELD / capped" verdict —
/// <see cref="AvatarMotor.MomentumGranted"/> asked directly rather than re-derived — and the
/// orchestrator permitted it, on the argument that one bit distinguishes "this mechanic is on" from
/// "you are at depth 4". <c>ChainNoUiAuditTests</c> then shipped §6.6 as a sweep over every UI and
/// lab source, and went red on exactly that line. <b>The ruling was reversed and the audit is the
/// specification.</b> The argument that won: the entire design premise is that the chain is read
/// from posture and never from a number, so a boolean on the panel is the crutch that stops the
/// posture being evaluated — the one question the lab exists to answer — and an invariant that
/// holds for all sources is worth more than a convenience that makes it a judgement call forever
/// after. The air line is the counter and the mode now, and nothing else.</para>
/// </summary>
public static class MovementVerbReadout
{
    /// <summary>Column the second field of each line starts at, so the two lines this class
    /// produces align with the five <c>MovementPlayground</c> writes by hand.</summary>
    private const int LabelWidth = 11;

    /// <summary>What <see cref="MoveVerb"/> reads as on the readout. <see cref="MoveVerb.DuckWalk"/>
    /// is two words because it is two words everywhere else — the spec, the knob group and the
    /// transition table all say "duck walk".</summary>
    public static string VerbName(MoveVerb verb) => verb switch
    {
        MoveVerb.Normal => "NORMAL",
        MoveVerb.Tuck => "TUCK",
        MoveVerb.Slide => "SLIDE",
        MoveVerb.DuckWalk => "DUCK WALK",
        _ => "verb " + ((byte)verb).ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>Both lines, newline-separated, in the order the readout prints them.</summary>
    public static string Lines(in MotorTuning tuning, in MoveState state) =>
        VerbLine(tuning, state) + "\n" + AirLine(tuning, state);

    /// <summary>
    /// <b>The verb and its clock.</b> One clock byte serves four verbs and means something
    /// different in each (§3.2), so the line names the bound it is counting against rather than
    /// printing a bare number: a "0.183 s" that could be a hold window, a slide duration or an
    /// unbounded dwell is a number nobody can act on.
    ///
    /// <para><b>The airborne sentinel is shown as "air", never as 255.</b>
    /// <see cref="AvatarMotor.VerbClockAirborne"/> is a distinguished not-counting value rather than
    /// a count — printing it raw would be a readout claiming a 4.25-second hold.</para>
    /// </summary>
    public static string VerbLine(in MotorTuning tuning, in MoveState state)
    {
        bool airborne = state.VerbClockTicks == AvatarMotor.VerbClockAirborne;
        string clock = airborne
            ? "air"
            : $"{Seconds(state.VerbClockTicks)} ({state.VerbClockTicks}t)";

        string against = airborne
            ? "not counting — the previous tick was airborne"
            : state.Verb switch
            {
                // Law V2: a release SPENDS the window, so this is progress toward an entry that
                // costs a full fresh window if the button comes up first.
                MoveVerb.Normal => $"hold window {Seconds(tuning.JumpHoldWindowTicks)}",
                // Both duration exits, because SlideMinSec gates only the speed exit and
                // SlideMaxSec ends the slide outright — one number could not say which is next.
                MoveVerb.Slide => $"min {Seconds(tuning.SlideMinTicks)}   "
                                + $"max {Seconds(tuning.SlideMaxTicks)}",
                // T9: unconditional, in one tick, at any clock value. There is no bound to show.
                MoveVerb.Tuck => "held — releasing pops it in one tick",
                // T14: the one latched verb. Saying so is the whole reason this field is here.
                MoveVerb.DuckWalk => "latched — a release does NOT exit",
                _ => "",
            };

        return Label("verb") + $"{VerbName(state.Verb),-9}   clock {clock,-17}   {against}";
    }

    /// <summary>
    /// <b>The air-jump counter and the variant it is running.</b> Both are read straight off the
    /// state and the tuning every frame, so the counter cannot survive a flight it did not happen
    /// in and the mode cannot name a variant the motor is not applying.
    ///
    /// <para><b>Nothing about the momentum grant is on this line any more</b> (MOVE-5g). The grant
    /// is a function of the chain, so asking it here — even for a single bit, even at a tuning
    /// where the chain half of the disjunction is dead — is a chain readout wearing a different
    /// name, and §6.6 forbids the chain reaching a human through any channel but the body. The
    /// counter and the mode are §6.6's own named exceptions and stay.</para>
    /// </summary>
    public static string AirLine(in MotorTuning tuning, in MoveState state)
    {
        int max = (int)(tuning.AirJumpCountMax + 0.5f);
        string mode = tuning.AirJumpMode switch
        {
            < 0.5f => "0 off",
            < 1.5f => "1 traditional",
            _ => "2 the Kick",
        };

        return Label("air") + $"jumps {state.AirJumpsUsed}/{max} (mode {mode})";
    }

    private static string Label(string name) => name.PadRight(LabelWidth);

    /// <summary>Whole ticks as seconds, on the motor's own tick rate — three decimals, which is the
    /// resolution one tick has (0.0167 s) and the one every other timer on this readout uses.</summary>
    private static string Seconds(int ticks) =>
        (ticks * NetProfile.TickDelta).ToString("F3", CultureInfo.InvariantCulture) + " s";
}
