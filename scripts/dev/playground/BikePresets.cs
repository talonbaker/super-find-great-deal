using System.Collections.Generic;

namespace MpFoundation.Dev.Playground;

/// <summary>
/// <b>Named bike-feel presets, on ALT plus the number row</b> (BIKE-1b, 2026-09-01). The same
/// idea as <see cref="MovementPresets"/> — a whole tuning under one key, with a sentence saying
/// what you should be feeling and a sentence saying what to do to feel it — for the bike's own
/// numbers rather than the motor's.
///
/// <para><b>Why a separate row and not more digits.</b> A bike preset is a <see cref="BikeTuning"/>,
/// which is a rewrite RULE over the foot tuning, not a tuning: the ride's top speed is
/// <c>MoveSpeed x RideSpeedMul x SprintMultiplier</c>, so the same bike preset over 6 (FORGIVING)
/// and over SHIFT+5 (the fast one) rides differently, on purpose. The two rows therefore
/// compose: press a foot preset for the body, an ALT preset for the bike, and the banner names
/// both. That is the two-axis experiment the number row alone could not hold, and it is why
/// nothing here touches a <c>MotorTuning</c> row directly — <c>BikePresetTests</c> sweeps every
/// bike preset over every foot preset and checks the derived ride is inside every knob's range at
/// five blend points.</para>
///
/// <para><b>Every preset is derived from <see cref="BikeTuning.Default"/></b>, never from the live
/// bike tuning, for the reason <see cref="MovementPresets"/> gives: a preset must mean the same
/// thing whatever was pressed before it. ALT+0 IS the default; press it between the others.</para>
///
/// <para><b>The ten are a ladder of questions, not ten opinions.</b> 1 and 2 bracket "how much
/// faster than running" (Talon: <i>"walking is nice and riding is great"</i>). 3 is the air toy.
/// 4 is the drift. 5 and 6 bracket the stumble he is unsure about, with the default in the
/// middle. 7 and 8 bracket the mount's speed (<i>"quick pop on and off"</i> against a transition
/// you can see). 9 is the momentum descent turned all the way up.</para>
/// </summary>
public static class BikePresets
{
    /// <param name="Key">The number-row digit, with ALT.</param>
    /// <param name="Name">Short, shouted, what a note refers to it by.</param>
    /// <param name="Feel">What you should be feeling — a claim Talon can disagree with.</param>
    /// <param name="Try">The specific thing to DO, and where (a lane of the Bike Park).</param>
    /// <param name="Tuning">The whole bike tuning.</param>
    public readonly record struct Preset(int Key, string Name, string Feel, string Try, BikeTuning Tuning);

    public static IReadOnlyList<Preset> All => _all ??= Build();
    private static IReadOnlyList<Preset>? _all;

    public static Preset? ForKey(int digit)
    {
        foreach (var p in All)
            if (p.Key == digit)
                return p;
        return null;
    }

    public static string KeyLabel(in Preset p) => $"ALT+{p.Key}";

    private static IReadOnlyList<Preset> Build()
    {
        BikeTuning d = BikeTuning.Default;
        return new[]
        {
            new Preset(0, "BASELINE",
                "The BIKE-0 numbers: 1.55x the run, a 0.25 s blend, a stumble at 85 % of the cap. " +
                "The bike you have been riding.",
                "Press it BETWEEN the others. The comparison is the measurement."
              , d),

            new Preset(1, "CRUISER",
                "Barely faster than sprinting, but it never lets go: hard acceleration, tight turns, " +
                "quick to stop. Riding is an extension of running and nothing more.",
                "Bike Park, SLALOM lane. Weave the pylons without the drift; then press ALT+0 and " +
                "feel what 1.55x costs you in the corners.",
                d with
                {
                    RideSpeedMul = 1.30f,
                    RideAcceleration = 16f,
                    RideDeceleration = 5f,
                    RideTurnMul = 1.35f,
                    RideTurnAccelMul = 1.3f,
                    RideSlopeGain = 0.7f,
                    SlopeBonusMaxMps = 5f,
                }),

            new Preset(2, "ROCKET",
                "Nearly twice the run. Slow to wind up, wide in the corners, and it commits you to " +
                "the line you left the lip on. Speed is a thing you have to earn and then manage.",
                "Roller Run from the plateau, then the Bike Park KICKER lane. Note where you " +
                "stop being able to turn.",
                d with
                {
                    RideSpeedMul = 1.90f,
                    RideAcceleration = 7f,
                    RideDeceleration = 2.0f,      // the Deceleration knob floors at 2.0
                    RideTurnMul = 0.80f,
                    RideTurnAccelMul = 0.8f,
                    RideAirControlBuild = 0.15f,
                    RideAirControlTurn = 0.10f,
                    RideAirControlBrake = 0.05f,
                    RideSlopeGain = 1.3f,
                    SlopeBonusMaxMps = 7f,
                    StumbleSpeedFraction = 0.80f,
                }),

            new Preset(3, "BMX",
                "The air toy. A taller bunny-hop, a bigger double jump on the bike, real air " +
                "steering, a hard kick-off. Every gap is an invitation.",
                "Bike Park, KICKER and DROP lanes: SPACE off the lip, SPACE again at the apex, Q " +
                "at the top of that. Then land on the bike and Q inside the window.",
                d with
                {
                    RideJumpMul = 1.20f,
                    RideAirJumpMul = 1.30f,
                    RideAirControlBuild = 0.60f,
                    RideAirControlTurn = 0.55f,
                    RideAirControlBrake = 0.40f,
                    RampLaunchGain = 1.15f,
                    KickOffUpMps = 7.5f,
                    KickOffForwardMps = 1.5f,
                    LandDismountUpMps = 8f,
                    LandDismountWindowSec = 0.24f,
                    SwingLungeForwardMps = 4.5f,
                    SwingLungeUpMps = 2.5f,
                    SlingshotForwardMps = 4.5f,
                }),

            new Preset(4, "CARVE",
                "The drift is the whole bike: the nose comes round at 360 deg/s and you keep " +
                "sixty percent of your speed through a hairpin. Corners are the fun part.",
                "Bike Park, SLALOM lane to the HAIRPIN at the end. Hold right mouse into the wall " +
                "and steer through it. Release and feel it straighten.",
                d with
                {
                    RideTurnMul = 1.15f,
                    DriftTurnDegPerSec = 360f,
                    DriftDecel = 2f,
                    DriftSpeedFloorFraction = 0.60f,
                }),

            new Preset(5, "NO STUMBLE",
                "Nothing is ever taken from you. Hop off the bike at full speed and keep running " +
                "at it, while the foot tuning bleeds you down over the blend. The no-punishment reading.",
                "Roller Run RUN-OUT: ride to top speed, Q, keep holding forward. Then ALT+6 and do " +
                "it again. Which one do you want?",
                d with
                {
                    StumbleEnabled = false,
                    DismountHopMps = 2.2f,
                    DismountBlendSec = 0.35f,
                }),

            new Preset(6, "HARD STUMBLE",
                "A dismount at bike speed is a mistake you feel: half a second of clipped " +
                "steering under the foot cap, steering at a fifth. Get off on a landing instead.",
                "Roller Run RUN-OUT: ride to top speed, Q. Then jump, Q as you land, and feel that " +
                "the landing dismount costs nothing.",
                d with
                {
                    StumbleEnabled = true,
                    StumbleSec = 0.50f,
                    StumbleSpeedFraction = 0.70f,
                    StumbleSteerFraction = 0.20f,
                }),

            new Preset(7, "SNAP",
                "The bike is a button. It is under you before you noticed pressing it and gone " +
                "the same way. Talon's 'quick pop on and off'.",
                "Anywhere: Q Q Q Q while running. Then on the CURB lane, Q one step before each " +
                "curb — does the hop-on carry you over it?",
                d with
                {
                    MountBlendSec = 0.08f,
                    DismountBlendSec = 0.08f,
                    MountHopMps = 3.2f,
                    DismountHopMps = 2.0f,
                    UnfoldSec = 0.18f,
                    FoldSec = 0.12f,
                    UnfoldOvershoot = 1.22f,
                }),

            new Preset(8, "UNFOLD",
                "The mount is a moment: the bike leaves the back, arcs, springs open, and you land " +
                "on it. Half a second you can watch. The transition, played for the juice.",
                "Anywhere, standing still: Q, and watch. Then at a sprint: is the half second a " +
                "wait, or a thing?",
                d with
                {
                    MountBlendSec = 0.50f,
                    DismountBlendSec = 0.35f,
                    MountHopMps = 1.6f,
                    UnfoldSec = 0.50f,
                    FoldSec = 0.30f,
                    UnfoldOvershoot = 1.10f,
                    UnfoldArcM = 0.65f,
                }),

            new Preset(9, "DOWNHILL",
                "Gravity is the engine. A hill pays sixty percent more than baseline and the bike " +
                "barely bleeds it; the flat goes on forever. Air control is nearly gone.",
                "Roller Run top to bottom without touching the drift. Then the Bike Park SLOPE " +
                "lane, UP: how steep before it stalls?",
                d with
                {
                    RideSlopeGain = 1.6f,
                    SlopeBonusMaxMps = 9.5f,
                    RideDeceleration = 2.0f,      // the Deceleration knob floors at 2.0 - the bleed goes into the gain instead
                    RideAirControlBuild = 0.10f,
                    RideAirControlTurn = 0.08f,
                    RideAirControlBrake = 0.05f,
                    RampLaunchGain = 1.0f,
                }),
        };
    }
}
