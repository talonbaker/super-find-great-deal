using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using MpFoundation.Game.Sandbox;
using MpFoundation.Game.Sandbox.Anim;
using Xunit;

namespace Sail.Tests;

/// <summary>
/// <b><c>stride × cadence = ground speed</c>, carried across the move to authored clips — and the
/// measurement that rejected the obvious way of doing it.</b>
///
/// <para>Half of this file is arithmetic on the warp itself. The other half reads the shipped
/// <c>BoxKid.glb</c> bytes, interpolates the exported <c>ThighL</c> rotation samplers the way a glTF
/// LINEAR sampler does, turns them into a foot track and measures how far the <b>planted</b> foot
/// departs from a straight line — with no engine, no import and no GPU, so it runs everywhere
/// <c>GreyboxAssetContractTests</c> does and for the same reason.</para>
///
/// <para><b>What it caught.</b> ANIM-M2 flagged a Walk/Run duty-factor mismatch under a blend and
/// explicitly refused to assume it cancelled. It does not: a weighted blend is measured below at up
/// to <b>99.5 mm</b> of skate against 0.09 mm and 1.09 mm at the pure endpoints, which is the exact
/// defect MOVE-1 removed, reintroduced by a blend weight. That number is why the locomotion blend
/// space is DISCRETE_CARRY.</para>
/// </summary>
public class ClipTimeWarpTests
{
    // ---------------------------------------------------------------------------------------------
    // The identity
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(Gear.Walk, 0.5f)]
    [InlineData(Gear.Walk, 2.43f)]
    [InlineData(Gear.Jog, 5.4f)]
    [InlineData(Gear.Sprint, 8.64f)]
    public void TheWarpedClipExpressesExactlyTheGroundSpeedItWasGiven(Gear gear, float speed)
    {
        // The whole contract, in one line: whatever rate the warp picks, playing the clip at that
        // rate produces the speed the ground is actually moving at. This is what stops the planted
        // foot skating, and it is not a tuning value — it is an identity.
        float rate = ClipTimeWarp.RateFor(gear, speed);
        Assert.Equal(speed, ClipTimeWarp.GroundSpeedExpressed(gear, rate), precision: 4);
    }

    [Fact]
    public void TheRateIsNeverClamped_BecauseAClampIsTheSkate()
    {
        // A clamp is arithmetically identical to reintroducing the defect: the moment the rate stops
        // tracking the speed, the planted foot stops being world-stationary. So a speed well past
        // anything the game asks for still gets its exact ratio rather than a ceiling.
        Assert.Equal(20f / ClipTimeWarp.RunNominalMps, ClipTimeWarp.RateFor(Gear.Sprint, 20f), precision: 5);
        Assert.Equal(0.001f / ClipTimeWarp.WalkNominalMps, ClipTimeWarp.RateFor(Gear.Walk, 0.001f), precision: 6);
    }

    [Fact]
    public void IdleIsAStandRatherThanAGait_AndPlaysAtItsAuthoredRate()
    {
        Assert.Equal(1f, ClipTimeWarp.RateFor(Gear.Idle, 0f));
        Assert.Equal(1f, ClipTimeWarp.RateFor(Gear.Idle, 3f));
    }

    [Fact]
    public void ANonFiniteSpeed_ProducesAFiniteRate()
    {
        Assert.Equal(0f, ClipTimeWarp.RateFor(Gear.Walk, float.NaN));
        Assert.True(float.IsFinite(ClipTimeWarp.RateFor(Gear.Sprint, float.PositiveInfinity)));
    }

    [Fact]
    public void ClipSelectionIsKeyedOnTheGear_SoThereIsNoSecondThresholdToDriftOutOfAgreement()
    {
        // LocomotionProfile's own rule: "a second copy of a speed is how a mirror goes stale."
        // Gear is derived from replicated ground speed, is already hysteretic and is already
        // identical on every peer, so a clip chosen from it cannot chatter and cannot disagree
        // between clients.
        Assert.Equal(AvatarClipNames.Idle, ClipTimeWarp.LocomotionClipFor(Gear.Idle));
        Assert.Equal(AvatarClipNames.Walk, ClipTimeWarp.LocomotionClipFor(Gear.Walk));
        Assert.Equal(AvatarClipNames.Run, ClipTimeWarp.LocomotionClipFor(Gear.Jog));
        Assert.Equal(AvatarClipNames.Run, ClipTimeWarp.LocomotionClipFor(Gear.Sprint));
    }

    [Fact]
    public void TheDutyFactorSurvivesTheWarp_BecauseWarpingScalesCycleAndStanceTogether()
    {
        Assert.Equal(ClipTimeWarp.WalkDutyFactor, ClipTimeWarp.DutyFactorFor(Gear.Walk));
        Assert.Equal(ClipTimeWarp.RunDutyFactor, ClipTimeWarp.DutyFactorFor(Gear.Sprint));
    }

    [Fact]
    public void TheCadenceReadoutTracksTheClipRatherThanTheProceduralGait()
    {
        // CadenceHz is what _gaitPhase integrates, and _gaitPhase is what the footstep latch reads.
        // A phase advancing at the procedural cadence under a clip playing at the warped cadence is
        // two gaits at two speeds on one body.
        Assert.Equal(2.5f, ClipTimeWarp.CadenceHzFor(Gear.Walk, ClipTimeWarp.WalkNominalMps), precision: 3);
        Assert.Equal(3.75f, ClipTimeWarp.CadenceHzFor(Gear.Sprint, ClipTimeWarp.RunNominalMps), precision: 3);
        Assert.Equal(0f, ClipTimeWarp.CadenceHzFor(Gear.Idle, 0f));
    }

    [Fact]
    public void TheRunClipIsNeverExtrapolatedPastItsOwnExtremes()
    {
        // Authored deliberately faster than anything the game asks for, so a warp is always DOWN.
        Assert.True(ClipTimeWarp.RunNominalMps > LocomotionProfile.SprintSpeedMps,
            $"Run's nominal {ClipTimeWarp.RunNominalMps:F4} m/s is below the game's top speed " +
            $"{LocomotionProfile.SprintSpeedMps:F4} m/s; every sprint now extrapolates the clip");
        Assert.True(ClipTimeWarp.RateFor(Gear.Sprint, LocomotionProfile.SprintSpeedMps) < 1f);
    }

    /// <summary>
    /// <b>MOVE-8 FORK: the walk clip and the walk gear have come apart, and neither side can be
    /// moved by a movement packet.</b>
    ///
    /// <para><c>WalkNominalMps</c> is <b>measured off the shipped <c>Greybox.glb</c></b> — it is a
    /// property of the authored animation, not a tuning row, and 2.4207 m/s used to sit within
    /// 0.4% of a 2.43 m/s walk gear. That closeness is what kept the time warp near 1.0 through the
    /// gait a player spends most of their time in. Talon's 2026-08-28 speed ruling takes the walk
    /// gear to <b>1.71 m/s</b>, so the clip is now <b>41.6% fast</b> and every walk plays warped to
    /// about <b>0.71x</b>.</para>
    ///
    /// <para><b>Neither repair belongs here.</b> Re-authoring the clip is a Blender export
    /// (BLENDER-EXPORT.md's contract, the animation role's); re-opening the speed is Talon's. So
    /// the anchor claim is pinned as the breach it now is, with the measured error, and MOVE-8's
    /// report carries it. <b>This test goes red when the fork closes</b> — when the clip is
    /// re-authored or the gear moves back — at which point it is deleted and the <c>&lt; 0.01</c>
    /// anchor assertion goes back.</para>
    ///
    /// <para>The RUN clip is fine and is checked by
    /// <see cref="TheRunClipIsNeverExtrapolatedPastItsOwnExtremes"/>: it was authored deliberately
    /// fast, so a slower sprint only widens its margin.</para>
    /// </summary>
    [Fact]
    public void MOVE8_TheWalkClipNoLongerAnchorsTheWalkGear_AndIsAwaitingARuling()
    {
        float walkErr = Math.Abs(ClipTimeWarp.WalkNominalMps - LocomotionProfile.WalkSpeedMps)
            / LocomotionProfile.WalkSpeedMps;

        Assert.True(walkErr > 0.05f,
            $"Walk's nominal is only {walkErr * 100f:F1}% off WalkSpeedMps — the fork has CLOSED: "
            + "delete this test and restore `Assert.True(walkErr < 0.01f)`");
        Assert.InRange(walkErr, 0.40f, 0.43f);

        // The consequence, stated as the number an animator would want: the rate the walk clip is
        // actually played at.
        float rate = ClipTimeWarp.RateFor(Gear.Walk, LocomotionProfile.WalkSpeedMps);
        Assert.InRange(rate, 0.69f, 0.72f);
    }

    // ---------------------------------------------------------------------------------------------
    // Measured off the shipped bytes
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// <b>The nominal speeds are re-derived from the file, not trusted.</b> ANIM-M2 measured them
    /// during authoring; this re-measures them out of the shipped <c>.glb</c>, so a re-export that
    /// changes a cadence turns a test red instead of quietly moving the skate back in.
    /// </summary>
    [Theory]
    [InlineData(AvatarClipNames.Walk, ClipTimeWarp.WalkNominalMps)]
    [InlineData(AvatarClipNames.Run, ClipTimeWarp.RunNominalMps)]
    public void TheClipsNominalGroundSpeed_IsWhatTheConstantSays(string clip, float declared)
    {
        FootTrack track = ReadFootTrack(clip);
        float measured = track.NominalGroundSpeedMps();
        Assert.True(Math.Abs(measured - declared) / declared < 0.02f,
            $"{clip}'s nominal ground speed measures {measured:F4} m/s off the shipped bytes but " +
            $"ClipTimeWarp declares {declared:F4} m/s. The clip was re-authored; the constant has " +
            "not caught up, and every stride at every speed is now skating by that ratio.");
    }

    /// <summary>
    /// <b>Both gait clips sit on the same leg cap, and that is forced rather than maintained.</b>
    /// It is also the property that makes a phase-carrying hard switch between them invisible: what
    /// differs is cadence and duty, not amplitude.
    /// </summary>
    [Fact]
    public void WalkAndRunShareTheSameFootSweep_WhichIsWhatMakesADiscreteSwitchInvisible()
    {
        float walk = ReadFootTrack(AvatarClipNames.Walk).StanceSweepM();
        float run = ReadFootTrack(AvatarClipNames.Run).StanceSweepM();
        // 1 cm of slack against the declared sweep, because this reader resamples the exported
        // keys onto 512 uniform phase steps and the stance window it FINDS lands on a step boundary
        // rather than exactly on the authored handover — worth about 5 mm at Walk's cadence. The
        // tight claim is the one below: the two clips agreeing with EACH OTHER, which the same
        // quantisation affects identically and therefore cannot manufacture.
        Assert.True(Math.Abs(walk - ClipTimeWarp.SharedFootSweepM) < 0.01f,
            $"Walk sweeps {walk:F4} m against a declared {ClipTimeWarp.SharedFootSweepM:F4} m");
        Assert.True(Math.Abs(run - ClipTimeWarp.SharedFootSweepM) < 0.01f,
            $"Run sweeps {run:F4} m against a declared {ClipTimeWarp.SharedFootSweepM:F4} m");
        // 1 cm, and the bound is the READER's resolution rather than a design tolerance. Resampling
        // onto 512 uniform phase steps puts the found stance boundary up to one step off the authored
        // handover, which at Run's cadence is about 1 deg of hip angle and therefore about 8 mm of
        // foot travel. Measured today: Walk 0.6809 m, Run 0.6768 m, a 4.1 mm difference that is
        // entirely inside that. A real divergence between the two clips would be a fraction of the
        // sweep, not a fraction of a sample.
        Assert.True(Math.Abs(walk - run) < 0.01f,
            $"Walk sweeps {walk:F4} m and Run sweeps {run:F4} m — a {Math.Abs(walk - run) * 1000f:F1} mm " +
            "difference, past this reader's ~8 mm resolution. A discrete switch between them is no " +
            "longer invisible and AnimationCrossfade.LocomotionBlendSec's DISCRETE_CARRY decision " +
            "needs re-measuring rather than inheriting.");
    }

    /// <summary>
    /// <b>Each clip on its own keeps its planted foot on its line.</b> The positive control, without
    /// which the blend measurement below would prove nothing — "the blend is bad" is worthless until
    /// the same method can show "the endpoints are good".
    /// </summary>
    [Theory]
    [InlineData(AvatarClipNames.Walk, 1.0f)]
    [InlineData(AvatarClipNames.Run, 3.0f)]
    public void APureClipsPlantedFoot_StaysOnItsLine(string clip, float toleranceMm)
    {
        float worst = ReadFootTrack(clip).WorstPlantedDepartureMm();
        Assert.True(worst < toleranceMm,
            $"{clip}'s planted foot departs its line by {worst:F2} mm (budget {toleranceMm:F1} mm)");
    }

    /// <summary>
    /// <b>The measurement that decided the blend mode.</b> ANIM-M2's open question 4, answered with a
    /// number instead of an assumption.
    ///
    /// <para>Walk's stance is 35.4% of its cycle and Run's is 14.1%. Through the 21% of the cycle
    /// where they disagree, one clip's foot is still planted and the other's is already swinging
    /// forward — so the weighted average is a foot doing neither, and it slides. The damage is worst
    /// near the Walk end, where a small amount of Run's swing is being averaged into a long
    /// stance.</para>
    /// </summary>
    [Fact]
    public void InterpolatedWalkRunBlend_SkatesFarWorseThanEitherEndpoint()
    {
        FootTrack walk = ReadFootTrack(AvatarClipNames.Walk);
        FootTrack run = ReadFootTrack(AvatarClipNames.Run);

        float pureWalk = walk.WorstPlantedDepartureMm();
        float pureRun = run.WorstPlantedDepartureMm();
        float worstBlend = 0f;
        float worstAt = 0f;
        foreach (float w in new[] { 0.1f, 0.25f, 0.4f, 0.5f, 0.6f, 0.75f, 0.9f })
        {
            float d = FootTrack.Blend(walk, run, w).WorstPlantedDepartureMm();
            if (d > worstBlend)
            {
                worstBlend = d;
                worstAt = w;
            }
        }

        // Both endpoints are sub-millimetre-to-millimetre; the blend is an order of magnitude worse.
        Assert.True(pureWalk < 1.0f && pureRun < 3.0f);
        Assert.True(worstBlend > 10f * Math.Max(pureWalk, pureRun),
            $"a weighted Walk<->Run blend departs by {worstBlend:F2} mm at weight {worstAt:F2}, " +
            $"against {pureWalk:F2} mm and {pureRun:F2} mm at the endpoints. If this ever stops " +
            "being true the clips have changed and AnimationCrossfade.LocomotionBlendSec's " +
            "DISCRETE_CARRY decision should be re-measured rather than inherited.");

        // And the mode the tree actually uses carries no blend at all.
        Assert.Equal(0f, AnimationCrossfade.LocomotionBlendSec);
    }

    // ---------------------------------------------------------------------------------------------
    // The glTF reader — deliberately independent of the engine, per GreyboxAssetContractTests' note
    // ---------------------------------------------------------------------------------------------

    /// <summary>One clip's <c>ThighL</c> hip-pitch track, resampled onto a uniform normalised phase so
    /// two clips of different lengths can be averaged the way a blend space averages them.</summary>
    private sealed class FootTrack
    {
        /// <summary>Hip pitch in radians, 512 samples across one full cycle.</summary>
        public float[] Theta = Array.Empty<float>();

        /// <summary>The authored cycle length, seconds.</summary>
        public float CycleSec;

        /// <summary>
        /// <b>The leg length the foot hangs at — READ OFF THE SHIPPED BYTES, not declared here.</b>
        /// It is <c>ThighL</c>'s own origin height, because the ankle is on the ground at the bottom
        /// of the leg (ANIM-M2b) and so the hip's height above the floor IS the radius the foot
        /// swings on.
        ///
        /// <para><b>BODY-3 (2026-08-29): this was <c>const float LegM = 0.548f</c>, and that constant
        /// is the whole reason three tests in this file went red.</b> Conforming the box kid to the
        /// classic greybox moved the hip from 0.548 m to 0.440 m. The exported hip ANGLES did not
        /// change at all — both libraries saturate the same <c>GAIT_MAX_LEG_SWING_RAD</c>, which is
        /// <c>acos(1 − 0.22)</c> and has no leg length in it — so this reader kept converting an
        /// unchanged angle into metres with a leg 24.5% too long, and reported every distance, and
        /// every speed derived from one, 24.5% high. A hardcoded copy of a body dimension in a test
        /// that reads the body is a mirror with nothing to keep it honest; reading it removes the
        /// class of failure rather than the instance.</para>
        /// </summary>
        public float LegM = 0.440f;

        public const int N = 512;

        /// <summary>Ankle position along Z, metres, at sample <paramref name="i"/>. A rotation of
        /// theta about X takes (0, -L, 0) to (0, -L cos θ, -L sin θ), so this is -L sin θ, and -Z is
        /// forward in Godot.</summary>
        public float FootZ(int i) => -LegM * MathF.Sin(Theta[i]);

        /// <summary>The contiguous window in which the foot is travelling backwards at a near-constant
        /// rate — the stance. Found rather than assumed, so it is a measurement of the clip and not a
        /// restatement of the duty factor the clip was authored from.</summary>
        public (int Start, int Length) FindStance()
        {
            float[] dz = new float[N];
            for (int i = 0; i < N; i++)
                dz[i] = FootZ((i + 1) % N) - FootZ(i);

            (int start, int length, float score) best = (0, 0, float.MinValue);
            for (int len = (int)(0.06f * N); len < (int)(0.45f * N); len++)
            {
                for (int start = 0; start < N; start += 4)
                {
                    float min = float.MaxValue, sum = 0f;
                    for (int k = 0; k < len; k++)
                    {
                        float v = dz[(start + k) % N];
                        min = Math.Min(min, v);
                        sum += v;
                    }
                    if (min <= 0f)
                        continue;
                    float mean = sum / len;
                    float dev = 0f;
                    for (int k = 0; k < len; k++)
                        dev = Math.Max(dev, Math.Abs(dz[(start + k) % N] - mean) / mean);
                    float score = len * (1f - Math.Min(dev, 1f));
                    if (score > best.score)
                        best = (start, len, score);
                }
            }
            return (best.start, best.length);
        }

        /// <summary>How far the planted foot departs from a straight line through the stance,
        /// millimetres. This is the claim that matters — "the planted foot does not skate" — measured
        /// as a distance rather than inferred from an angle.</summary>
        public float WorstPlantedDepartureMm()
        {
            (int start, int len) = FindStance();
            if (len < 4)
                return float.MaxValue;
            float a = FootZ(start);
            float b = FootZ((start + len - 1) % N);
            float worst = 0f;
            for (int k = 0; k < len; k++)
            {
                float ideal = a + (b - a) * k / (len - 1);
                worst = Math.Max(worst, Math.Abs(FootZ((start + k) % N) - ideal));
            }
            return worst * 1000f;
        }

        /// <summary>The distance the planted foot travels across one stance, metres — the step.</summary>
        public float StanceSweepM()
        {
            (int start, int len) = FindStance();
            return Math.Abs(FootZ((start + len - 1) % N) - FootZ(start));
        }

        /// <summary>The ground speed this clip is authored at: the rate its planted foot travels
        /// backwards, which is by definition the speed the body travels forwards.</summary>
        public float NominalGroundSpeedMps()
        {
            (int start, int len) = FindStance();
            float stanceSec = CycleSec * len / N;
            return StanceSweepM() / stanceSec;
        }

        /// <summary>Two clips averaged at a weight, on a shared normalised phase — which is exactly
        /// what an interpolated <c>AnimationNodeBlendSpace1D</c> with <c>sync</c> on does.</summary>
        public static FootTrack Blend(FootTrack a, FootTrack b, float w)
        {
            // LegM rides along: a blend of two tracks off the same body is still that body's leg,
            // and defaulting it here would silently reintroduce the constant this class just lost.
            var t = new FootTrack
            {
                Theta = new float[N],
                CycleSec = a.CycleSec * (1f - w) + b.CycleSec * w,
                LegM = a.LegM,
            };
            for (int i = 0; i < N; i++)
                t.Theta[i] = a.Theta[i] * (1f - w) + b.Theta[i] * w;
            return t;
        }
    }

    /// <summary>
    /// <b>The hip's height above the floor, accumulated down the node graph.</b> This is the leg the
    /// foot swings on, and it is the number BODY-3 moved (0.548 -> 0.440) while leaving every
    /// exported hip angle untouched.
    ///
    /// <para>Summing translations is exact rather than approximate, and that is not an assumption:
    /// <c>GreyboxAssetContractTests</c>' own glTF reader asserts on every load that no node in this
    /// file carries a rotation or a scale. If that ever stops being true this sum is wrong, and
    /// every test in that file is what says so first.</para>
    /// </summary>
    private static float HipHeightM(JsonElement nodes, int nodeIndex)
    {
        var parent = new Dictionary<int, int>();
        for (int i = 0; i < nodes.GetArrayLength(); i++)
        {
            if (!nodes[i].TryGetProperty("children", out JsonElement kids))
                continue;
            foreach (JsonElement k in kids.EnumerateArray())
                parent[k.GetInt32()] = i;
        }

        float y = 0f;
        int at = nodeIndex;
        while (true)
        {
            if (nodes[at].TryGetProperty("translation", out JsonElement t))
                y += t[1].GetSingle();
            if (!parent.TryGetValue(at, out int up))
                return y;
            at = up;
        }
    }

    private static FootTrack ReadFootTrack(string clipName)
    {
        JsonElement gltf = ReadGltfJson(GreyboxPath());
        byte[] bin = ReadBinChunk(GreyboxPath());

        JsonElement nodes = gltf.GetProperty("nodes");
        int footIndex = -1;
        for (int i = 0; i < nodes.GetArrayLength(); i++)
        {
            if (nodes[i].TryGetProperty("name", out JsonElement n) && n.GetString() == "ThighL")
                footIndex = i;
        }
        Assert.True(footIndex >= 0, "BoxKid.glb has no node named ThighL");

        JsonElement clip = default;
        bool found = false;
        foreach (JsonElement a in gltf.GetProperty("animations").EnumerateArray())
        {
            if (a.GetProperty("name").GetString() == clipName)
            {
                clip = a;
                found = true;
            }
        }
        Assert.True(found, $"BoxKid.glb carries no clip named '{clipName}'");

        float[] times = Array.Empty<float>();
        float[][] quats = Array.Empty<float[]>();
        foreach (JsonElement ch in clip.GetProperty("channels").EnumerateArray())
        {
            JsonElement target = ch.GetProperty("target");
            if (target.GetProperty("node").GetInt32() != footIndex
                || target.GetProperty("path").GetString() != "rotation")
            {
                continue;
            }
            JsonElement sampler = clip.GetProperty("samplers")[ch.GetProperty("sampler").GetInt32()];
            times = ReadScalars(gltf, bin, sampler.GetProperty("input").GetInt32());
            quats = ReadVec4(gltf, bin, sampler.GetProperty("output").GetInt32());
        }
        Assert.True(times.Length > 1, $"'{clipName}' carries no ThighL rotation track");

        float cycle = times[^1];
        var track = new FootTrack
        {
            Theta = new float[FootTrack.N],
            CycleSec = cycle,
            LegM = HipHeightM(nodes, footIndex),
        };
        for (int i = 0; i < FootTrack.N; i++)
        {
            float t = cycle * i / FootTrack.N;
            track.Theta[i] = SampleAngle(times, quats, t);
        }
        return track;
    }

    /// <summary>A glTF LINEAR rotation sampler, evaluated the way the spec says: nlerp with the
    /// shorter-arc sign fix. The clips are pure X quaternions, so the resulting angle is
    /// <c>2·atan2(x, w)</c>.</summary>
    private static float SampleAngle(float[] times, float[][] quats, float t)
    {
        if (t <= times[0])
            return AngleOf(quats[0]);
        for (int k = 0; k < times.Length - 1; k++)
        {
            if (t < times[k] || t > times[k + 1])
                continue;
            float span = times[k + 1] - times[k];
            float f = span <= 0f ? 0f : (t - times[k]) / span;
            float[] q0 = quats[k];
            float[] q1 = quats[k + 1];
            float dot = q0[0] * q1[0] + q0[1] * q1[1] + q0[2] * q1[2] + q0[3] * q1[3];
            float sign = dot < 0f ? -1f : 1f;
            float[] q = new float[4];
            float len = 0f;
            for (int c = 0; c < 4; c++)
            {
                q[c] = q0[c] + (q1[c] * sign - q0[c]) * f;
                len += q[c] * q[c];
            }
            len = MathF.Sqrt(len);
            for (int c = 0; c < 4; c++)
                q[c] /= len;
            return AngleOf(q);
        }
        return AngleOf(quats[^1]);
    }

    private static float AngleOf(float[] q) => 2f * MathF.Atan2(q[0], q[3]);

    private static float[] ReadScalars(JsonElement gltf, byte[] bin, int accessor)
    {
        (int offset, int count) = AccessorSpan(gltf, accessor, 1);
        var values = new float[count];
        for (int i = 0; i < count; i++)
            values[i] = BitConverter.ToSingle(bin, offset + i * 4);
        return values;
    }

    private static float[][] ReadVec4(JsonElement gltf, byte[] bin, int accessor)
    {
        (int offset, int count) = AccessorSpan(gltf, accessor, 4);
        var values = new float[count][];
        for (int i = 0; i < count; i++)
        {
            values[i] = new float[4];
            for (int c = 0; c < 4; c++)
                values[i][c] = BitConverter.ToSingle(bin, offset + (i * 4 + c) * 4);
        }
        return values;
    }

    private static (int Offset, int Count) AccessorSpan(JsonElement gltf, int accessor, int components)
    {
        JsonElement a = gltf.GetProperty("accessors")[accessor];
        JsonElement bv = gltf.GetProperty("bufferViews")[a.GetProperty("bufferView").GetInt32()];
        int offset = (bv.TryGetProperty("byteOffset", out JsonElement bvo) ? bvo.GetInt32() : 0)
            + (a.TryGetProperty("byteOffset", out JsonElement ao) ? ao.GetInt32() : 0);
        Assert.Equal(5126, a.GetProperty("componentType").GetInt32());   // FLOAT; nothing here is quantised
        Assert.Equal(components == 1 ? "SCALAR" : "VEC4", a.GetProperty("type").GetString());
        return (offset, a.GetProperty("count").GetInt32());
    }

    private static string GreyboxPath() =>
        Path.Combine(FindRepoRoot(), "assets", "creatures", "boxkid", "BoxKid.glb");

    private static JsonElement ReadGltfJson(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        int offset = 12;
        while (offset + 8 <= bytes.Length)
        {
            int length = BitConverter.ToInt32(bytes, offset);
            if (BitConverter.ToUInt32(bytes, offset + 4) == 0x4E4F534A)
            {
                string json = System.Text.Encoding.UTF8.GetString(bytes, offset + 8, length);
                return JsonDocument.Parse(json).RootElement.Clone();
            }
            offset += 8 + length;
        }
        Assert.Fail($"{path} contains no glTF JSON chunk");
        return default;
    }

    private static byte[] ReadBinChunk(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        int offset = 12;
        while (offset + 8 <= bytes.Length)
        {
            int length = BitConverter.ToInt32(bytes, offset);
            if (BitConverter.ToUInt32(bytes, offset + 4) == 0x004E4942)
            {
                var chunk = new byte[length];
                Array.Copy(bytes, offset + 8, chunk, 0, length);
                return chunk;
            }
            offset += 8 + length;
        }
        Assert.Fail($"{path} contains no glTF BIN chunk");
        return Array.Empty<byte>();
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "project.godot")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}

/// <summary>
/// <b>The clip library's names, lengths, loop modes and channel axes, checked against the shipped
/// bytes.</b>
///
/// <para><b>Why this file exists at all.</b> An unresolved <c>AnimationPlayer</c> track is silent in
/// Godot — that is ANIM-M0 §3.1(b)'s whole argument against a runtime name table, and it applies to
/// clip NAMES just as much as to node paths. A clip renamed in Blender produces a body that resolves
/// every joint and stands perfectly still, which is indistinguishable from a clip that was keyed
/// wrong. This turns that into a red test with no engine in the room.</para>
/// </summary>
public class AvatarClipContractTests
{
    [Fact]
    public void EveryClipTheCodeNames_IsInTheShippedFile()
    {
        HashSet<string> shipped = ShippedClipNames();
        var missing = new List<string>();
        foreach (string clip in AvatarClipNames.All)
        {
            if (!shipped.Contains(clip))
                missing.Add(clip);
        }
        Assert.True(missing.Count == 0,
            $"BoxKid.glb is missing clip(s) the code plays: {string.Join(", ", missing)}. " +
            $"It carries: {string.Join(", ", shipped)}");
    }

    [Fact]
    public void EveryClipTheFileShips_IsOneTheCodeKnowsAbout()
    {
        // The other direction, and it is not symmetry for its own sake: an authored clip nothing
        // plays is exactly what Puffling.glb has had for months (ANIM-M0 §1.4 — five shipped
        // animations that have never played once). This is what would have caught it.
        var orphaned = new List<string>();
        foreach (string clip in ShippedClipNames())
        {
            if (clip != "RESET" && Array.IndexOf(AvatarClipNames.All, clip) < 0)
                orphaned.Add(clip);
        }
        Assert.True(orphaned.Count == 0,
            $"BoxKid.glb ships clip(s) nothing in the code ever plays: {string.Join(", ", orphaned)}. " +
            "Either wire them up or retire them — a dead clip in a shipped asset is what " +
            "Puffling.glb has carried since it was authored.");
    }

    [Theory]
    [InlineData(AvatarClipNames.Idle, 4.0f)]
    [InlineData(AvatarClipNames.Walk, 0.8f)]
    [InlineData(AvatarClipNames.Run, 0.5333f)]
    [InlineData(AvatarClipNames.JumpLaunch, 0.2f)]
    [InlineData(AvatarClipNames.JumpAir, 0.6f)]
    [InlineData(AvatarClipNames.JumpLand, 0.2667f)]
    [InlineData(AvatarClipNames.Skid, 0.3667f)]
    [InlineData(AvatarClipNames.HoldNetSwing, 0.4667f)]
    [InlineData(AvatarClipNames.KnockOut, 0.6f)]
    [InlineData(AvatarClipNames.Stagger, 0.5f)]
    public void EachClipIsTheLengthTheStateMachineAssumes(string clip, float seconds)
    {
        // The state machine retires Jump_Launch and Jump_Land on their own clip lengths, and the seek
        // maths clamps one-shots to theirs. A re-export that halves a length changes when a state
        // ends, silently.
        Assert.Equal(seconds, ShippedClipLength(clip), precision: 3);
    }

    [Fact]
    public void TheLimbsAreKeyedInPitchAndTheTrunkInYaw_WhichIsWhatLetsTheRuntimeCompose()
    {
        // The composition rule the whole layering rests on, asserted rather than assumed. ThighL/R and
        // ArmL/R are pure X (pitch) — read back as an angle and fed through LimbIk. Waist and Head are
        // pure Y (yaw) — and the runtime writes Waist.Rotation.X with `with { X = ... }`, preserving
        // Y. Two components, no overlap. An authored roll or an authored waist PITCH would silently
        // become a second writer on a channel that already has one (ANIM-M2 §8: the runtime's waist
        // pitch already exceeds the seam budget at full lean).
        foreach ((string clip, string node, int axis) in EnumerateRotationChannels())
        {
            (float x, float y, float z) = MaxAbsComponents(clip, node);
            if (node is "ThighL" or "ThighR" or "ArmL" or "ArmR")
            {
                Assert.True(y < 1e-4f && z < 1e-4f,
                    $"{clip}/{node} is keyed off the pitch axis (|y| {y:E2}, |z| {z:E2}); the runtime " +
                    "reads Rotation.X as a hip/shoulder angle and would silently drop the rest");
            }
            else if (node is "Waist" or "Head")
            {
                Assert.True(x < 1e-4f && z < 1e-4f,
                    $"{clip}/{node} is keyed off the yaw axis (|x| {x:E2}, |z| {z:E2}); the runtime " +
                    "writes Waist.Rotation.X itself and an authored pitch would be a second writer " +
                    "on a channel already over ANIM-M2's seam budget");
            }
            else
            {
                Assert.Fail($"{clip} keys '{node}', which is not one of the six joints the code reads");
            }
            _ = axis;
        }
    }

    [Fact]
    public void NoClipCarriesATranslationOrScaleChannel()
    {
        // Authoring rule 1. A translation track is also what synthesised a 74.8 mm phantom rest
        // offset on Body during ANIM-M2's export — it shipped a wrong .glb once and the contract test
        // is what caught it.
        foreach (JsonElement clip in Animations().EnumerateArray())
        {
            string name = clip.GetProperty("name").GetString() ?? "?";
            foreach (JsonElement ch in clip.GetProperty("channels").EnumerateArray())
            {
                string path = ch.GetProperty("target").GetProperty("path").GetString() ?? "?";
                Assert.True(path == "rotation",
                    $"{name} carries a '{path}' channel; the library is rotation-only");
            }
        }
    }

    [Fact]
    public void TheUpperBodyOverridesCarryNoLegChannel()
    {
        // What makes them layerable at all. A "Hold_" clip that keys a leg is a full-body cycle
        // wearing an override's name, and the track filter would not save it.
        foreach ((string clip, string node, int _) in EnumerateRotationChannels())
        {
            if (!clip.StartsWith("Hold_", StringComparison.Ordinal)
                && !clip.StartsWith("Carry_", StringComparison.Ordinal))
            {
                continue;
            }
            Assert.True(node is not ("ThighL" or "ThighR"),
                $"{clip} keys {node}; an upper-body override may not own the legs");
        }
    }

    /// <summary>
    /// <b>No clip keys a FOOT — the ankle is the runtime's, exactly as the knee is (ANIM-M2b).</b>
    ///
    /// <para>This is the same class of guard as "no clip keys a shin", and it exists for a stronger
    /// reason than tidiness. The angle that holds a sole flat is <c>-(hip + knee)</c>, and the knee
    /// is whatever <c>LimbIk.Solve</c> produced on THIS frame out of the jump tuck and the landing
    /// absorb. A clip author cannot know that number, so a hand-keyed ankle is wrong by construction
    /// on every frame the knee is not straight — and worse, it would be a second writer on the node
    /// <c>AvatarVisual.SolveLeg</c> writes, which is the defect the whole clip migration exists to
    /// end.</para>
    ///
    /// <para>The generator's own bootstrap refuses to key these too
    /// (<c>author_greybox_clips.py</c>'s <c>forbidden_objects</c>), but the .blend is authoritative
    /// and hand-editable — so the check that matters is the one against the shipped bytes.</para>
    /// </summary>
    [Fact]
    public void NoClipKeysAFoot_BecauseTheAnkleIsTheSolvers()
    {
        foreach ((string clip, string node, int _) in EnumerateRotationChannels())
        {
            Assert.True(node is not ("FootL" or "FootR" or "ShinL" or "ShinR"),
                $"{clip} keys {node}. LimbIk owns the knee and the ankle; a keyed one is a second " +
                "writer on a node AvatarVisual.SolveLeg writes every frame, and an authored ankle " +
                "cannot know the knee angle it would have to cancel.");
        }
    }

    // --- the reader --------------------------------------------------------------------------------

    private static IEnumerable<(string Clip, string Node, int Axis)> EnumerateRotationChannels()
    {
        JsonElement gltf = Gltf();
        JsonElement nodes = gltf.GetProperty("nodes");
        foreach (JsonElement clip in gltf.GetProperty("animations").EnumerateArray())
        {
            string name = clip.GetProperty("name").GetString() ?? "?";
            foreach (JsonElement ch in clip.GetProperty("channels").EnumerateArray())
            {
                JsonElement target = ch.GetProperty("target");
                if (target.GetProperty("path").GetString() != "rotation")
                    continue;
                JsonElement node = nodes[target.GetProperty("node").GetInt32()];
                yield return (name, node.GetProperty("name").GetString() ?? "?", 0);
            }
        }
    }

    private static (float X, float Y, float Z) MaxAbsComponents(string clipName, string nodeName)
    {
        JsonElement gltf = Gltf();
        byte[] bin = Bin();
        JsonElement nodes = gltf.GetProperty("nodes");
        float mx = 0f, my = 0f, mz = 0f;
        foreach (JsonElement clip in gltf.GetProperty("animations").EnumerateArray())
        {
            if (clip.GetProperty("name").GetString() != clipName)
                continue;
            foreach (JsonElement ch in clip.GetProperty("channels").EnumerateArray())
            {
                JsonElement target = ch.GetProperty("target");
                if (target.GetProperty("path").GetString() != "rotation")
                    continue;
                if (nodes[target.GetProperty("node").GetInt32()].GetProperty("name").GetString() != nodeName)
                    continue;
                JsonElement sampler = clip.GetProperty("samplers")[ch.GetProperty("sampler").GetInt32()];
                JsonElement a = gltf.GetProperty("accessors")[sampler.GetProperty("output").GetInt32()];
                JsonElement bv = gltf.GetProperty("bufferViews")[a.GetProperty("bufferView").GetInt32()];
                int offset = (bv.TryGetProperty("byteOffset", out JsonElement bvo) ? bvo.GetInt32() : 0)
                    + (a.TryGetProperty("byteOffset", out JsonElement ao) ? ao.GetInt32() : 0);
                int count = a.GetProperty("count").GetInt32();
                for (int i = 0; i < count; i++)
                {
                    mx = Math.Max(mx, Math.Abs(BitConverter.ToSingle(bin, offset + (i * 4 + 0) * 4)));
                    my = Math.Max(my, Math.Abs(BitConverter.ToSingle(bin, offset + (i * 4 + 1) * 4)));
                    mz = Math.Max(mz, Math.Abs(BitConverter.ToSingle(bin, offset + (i * 4 + 2) * 4)));
                }
            }
        }
        return (mx, my, mz);
    }

    private static HashSet<string> ShippedClipNames()
    {
        var names = new HashSet<string>();
        foreach (JsonElement a in Animations().EnumerateArray())
            names.Add(a.GetProperty("name").GetString() ?? "?");
        return names;
    }

    private static float ShippedClipLength(string clipName)
    {
        JsonElement gltf = Gltf();
        foreach (JsonElement clip in gltf.GetProperty("animations").EnumerateArray())
        {
            if (clip.GetProperty("name").GetString() != clipName)
                continue;
            float max = 0f;
            foreach (JsonElement sampler in clip.GetProperty("samplers").EnumerateArray())
            {
                JsonElement input = gltf.GetProperty("accessors")[sampler.GetProperty("input").GetInt32()];
                max = Math.Max(max, input.GetProperty("max")[0].GetSingle());
            }
            return max;
        }
        Assert.Fail($"BoxKid.glb carries no clip named '{clipName}'");
        return 0f;
    }

    private static JsonElement Animations() => Gltf().GetProperty("animations");

    private static JsonElement Gltf()
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(
            Root(), "assets", "creatures", "boxkid", "BoxKid.glb"));
        int offset = 12;
        while (offset + 8 <= bytes.Length)
        {
            int length = BitConverter.ToInt32(bytes, offset);
            if (BitConverter.ToUInt32(bytes, offset + 4) == 0x4E4F534A)
            {
                return JsonDocument.Parse(
                    System.Text.Encoding.UTF8.GetString(bytes, offset + 8, length)).RootElement.Clone();
            }
            offset += 8 + length;
        }
        Assert.Fail("BoxKid.glb contains no glTF JSON chunk");
        return default;
    }

    private static byte[] Bin()
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(
            Root(), "assets", "creatures", "boxkid", "BoxKid.glb"));
        int offset = 12;
        while (offset + 8 <= bytes.Length)
        {
            int length = BitConverter.ToInt32(bytes, offset);
            if (BitConverter.ToUInt32(bytes, offset + 4) == 0x004E4942)
            {
                var chunk = new byte[length];
                Array.Copy(bytes, offset + 8, chunk, 0, length);
                return chunk;
            }
            offset += 8 + length;
        }
        Assert.Fail("BoxKid.glb contains no glTF BIN chunk");
        return Array.Empty<byte>();
    }

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "project.godot")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
