using System;
using Godot;
using Sail.Game.Run;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// <b>W7-4 — the drowned body that went to the sky.</b> Talon, 2026-08-30 note 6:
/// <i>"When the player 'drowns', the body floats and spirals upward, to the surface, into the sky,
/// it looks like a tornado took them up."</i>
///
/// <para>These prove the three rules <see cref="DeathBeatPose"/> declares, exhaustively and
/// without a scene: the beat never trends upward, it never spins, and it arrives at a pose and
/// holds it. They are here rather than in the scene suite for the reason <c>DrowningClockTests</c>
/// gives: the interesting failure is arithmetic, and arithmetic is worth proving to the boundary
/// in the tier that can afford to sample a thousand points.</para>
///
/// <para><b>The number these replace.</b> The beat this file's subject supersedes lofted the body
/// <c>4.2 * sin(u * pi)</c> metres and rotated it <c>1.5</c> turns in X plus <c>3.0</c> in Y — a
/// 4.20 m peak and 4.5 whole revolutions. Both literals are asserted against below as the thing
/// that must not come back, so a future "let's make death read bigger" pass trips a test rather
/// than a playtest.</para>
/// </summary>
public class DeathBeatPoseTests
{
    private static readonly RespawnCause[] AllCauses =
    {
        RespawnCause.Unknown, RespawnCause.OffTheEdge, RespawnCause.Void, RespawnCause.Drowned,
    };

    /// <summary>Every cause, 1001 samples across the whole beat: the vertical term never exceeds
    /// the flat-lie clearance. This is the assertion that would have caught the shipped defect —
    /// the old curve's peak was 4.20 m, twenty-three times this bound.</summary>
    [Fact]
    public void NoCauseEverRisesAboveTheFlatLieClearance()
    {
        foreach (RespawnCause cause in AllCauses)
        {
            float peak = float.NegativeInfinity;
            for (int i = 0; i <= 1000; i++)
            {
                float y = DeathBeatPose.Sample(cause, i / 1000f).Offset.Y;
                if (y > peak) peak = y;
            }

            Assert.True(peak <= DeathBeatPose.MaxRiseM + 1e-4f,
                $"{cause} rises {peak:F3} m, past the {DeathBeatPose.MaxRiseM:F3} m bound. "
                + "The beat W7-4 replaced peaked at 4.200 m and that is what Talon saw.");
        }
    }

    /// <summary>A drowning has no upward term at all. The lake keeps what it took: the one thing
    /// Talon's note asks for by name is that the body stops going up, and for the cause he was
    /// actually watching the answer is not "less" but "none".</summary>
    [Fact]
    public void DrowningNeverRisesAtAll()
    {
        for (int i = 0; i <= 1000; i++)
        {
            float y = DeathBeatPose.Sample(RespawnCause.Drowned, i / 1000f).Offset.Y;
            Assert.True(y <= 0f, $"a drowning rose {y:F4} m at u={i / 1000f:F3}; it must only sink.");
        }

        Assert.Equal(-(double)DeathBeatPose.DrownSinkM, DeathBeatPose.Sample(RespawnCause.Drowned, 1f).Offset.Y, 4);
    }

    /// <summary>Total rotation is a topple, not a spiral. The old beat spent 4.5 whole
    /// revolutions; the bound here is 0.35.</summary>
    [Fact]
    public void EveryCauseTopplesRatherThanSpins()
    {
        foreach (RespawnCause cause in AllCauses)
        {
            float turns = DeathBeatPose.RevolutionsFor(cause);
            Assert.True(turns <= DeathBeatPose.MaxRevolutions,
                $"{cause} spends {turns:F3} turns, past the {DeathBeatPose.MaxRevolutions:F2} bound "
                + "(the beat W7-4 replaced spent 4.500).");
            Assert.True(turns > 0f, $"{cause} does not move at all; a dead body still has to fall over.");
        }
    }

    /// <summary>Rule 1: the pose ARRIVES. Everything from <see cref="DeathBeatPose.SettleU"/> to
    /// the end of the beat is bit-identical, so there is a real hold before the respawn.
    ///
    /// <para>This is the rule that actually explains Talon's "into the sky": the old curve's
    /// descent existed but was never on screen, because the arc returned to rest on the same frame
    /// the body teleported away. A beat that holds cannot be caught mid-excursion by its own
    /// respawn no matter how the two clocks drift.</para></summary>
    [Fact]
    public void ThePoseArrivesBeforeTheBeatEndsAndDoesNotMoveAgain()
    {
        foreach (RespawnCause cause in AllCauses)
        {
            DeathBeatPose.Pose settled = DeathBeatPose.Sample(cause, DeathBeatPose.SettleU);
            for (int i = 0; i <= 200; i++)
            {
                float u = DeathBeatPose.SettleU + (1f - DeathBeatPose.SettleU) * (i / 200f);
                DeathBeatPose.Pose p = DeathBeatPose.Sample(cause, u);
                Assert.Equal((double)settled.Offset.Y, p.Offset.Y, 5);
                Assert.Equal((double)settled.Rotation.X, p.Rotation.X, 5);
                Assert.Equal((double)settled.Rotation.Z, p.Rotation.Z, 5);
            }

            Assert.True(DeathBeatPose.SettleU < 1f, "the hold has to be visible, so settling must precede the end.");
        }
    }

    /// <summary>The vertical term is monotone for every cause — it goes one way and stops. A
    /// round trip is what let the old beat be read as an ascent: the player saw the up half, the
    /// respawn ate the down half, and "the body went up and never came back" is the honest
    /// description of what was actually rendered.</summary>
    [Fact]
    public void TheVerticalTermIsMonotone()
    {
        foreach (RespawnCause cause in AllCauses)
        {
            float sign = MathF.Sign(DeathBeatPose.Sample(cause, 1f).Offset.Y);
            float prev = 0f;
            for (int i = 1; i <= 1000; i++)
            {
                float y = DeathBeatPose.Sample(cause, i / 1000f).Offset.Y;
                Assert.True((y - prev) * sign >= -1e-5f,
                    $"{cause} reverses direction at u={i / 1000f:F3} ({prev:F4} -> {y:F4}).");
                prev = y;
            }
        }
    }

    /// <summary>The beat starts at rest, so the frame a body dies on is not a jump cut.</summary>
    [Fact]
    public void EveryCauseStartsFromRest()
    {
        foreach (RespawnCause cause in AllCauses)
        {
            DeathBeatPose.Pose p = DeathBeatPose.Sample(cause, 0f);
            Assert.Equal(Vector3.Zero, p.Offset);
            Assert.Equal(Vector3.Zero, p.Rotation);
        }
    }

    /// <summary>Out-of-range and non-finite beat time are clamped rather than extrapolated. A
    /// long frame must not overshoot the pose, and a NaN must never reach a transform — a NaN
    /// transform silently removes a whole subtree from the renderer, which looks exactly like the
    /// body being taken.</summary>
    [Theory]
    [InlineData(1.5f)]
    [InlineData(9f)]
    [InlineData(float.PositiveInfinity)]
    public void OvershootClampsToTheTerminalPose(float u)
    {
        foreach (RespawnCause cause in AllCauses)
        {
            DeathBeatPose.Pose end = DeathBeatPose.Sample(cause, 1f);
            DeathBeatPose.Pose over = DeathBeatPose.Sample(cause, u);
            Assert.Equal((double)end.Offset.Y, over.Offset.Y, 5);
            Assert.Equal((double)end.Rotation.X, over.Rotation.X, 5);
        }
    }

    /// <summary>NaN and any time BEFORE the beat are rest. Note the asymmetry with the theory
    /// above and that it is deliberate: negative infinity is a time before the beat and belongs
    /// here, positive infinity is a time after it and belongs there. Treating every non-finite
    /// value as rest is what the first cut did, and it would have stood a dead body back on its
    /// feet on any frame long enough to overflow the ratio.</summary>
    [Fact]
    public void NanAndNegativeBeatTimeAreRestNotGarbage()
    {
        foreach (RespawnCause cause in AllCauses)
        {
            foreach (float u in new[] { float.NaN, -1f, float.NegativeInfinity })
            {
                DeathBeatPose.Pose p = DeathBeatPose.Sample(cause, u);
                Assert.True(float.IsFinite(p.Offset.Y) && float.IsFinite(p.Rotation.X)
                            && float.IsFinite(p.Rotation.Z),
                    $"{cause} at u={u} produced a non-finite pose.");
                Assert.Equal(0d, p.Offset.Y, 5);
            }
        }
    }

    /// <summary>Determinism, stated as the multiplayer property it actually is: the pose is a pure
    /// function of the replicated cause and the normalised beat time, so two peers handed the same
    /// pair compute the same transform bit for bit. Nothing here samples local physics, a frame
    /// counter, or a clock.</summary>
    [Fact]
    public void ThePoseIsAPureFunctionOfCauseAndTime()
    {
        foreach (RespawnCause cause in AllCauses)
        {
            for (int i = 0; i <= 100; i++)
            {
                float u = i / 100f;
                DeathBeatPose.Pose a = DeathBeatPose.Sample(cause, u);
                DeathBeatPose.Pose b = DeathBeatPose.Sample(cause, u);
                Assert.Equal(a.Offset, b.Offset);
                Assert.Equal(a.Rotation, b.Rotation);
            }
        }
    }

    /// <summary>Drowning is told apart from every other cause by what the body does, not only by
    /// the log line — §4.1's legibility condition applied to a death: a watcher on the bank can
    /// see that the swimmer drowned.</summary>
    [Fact]
    public void ADrowningLooksDifferentFromAFall()
    {
        float drowned = DeathBeatPose.Sample(RespawnCause.Drowned, 1f).Offset.Y;
        float fell = DeathBeatPose.Sample(RespawnCause.OffTheEdge, 1f).Offset.Y;
        Assert.True(drowned < 0f && fell >= 0f,
            $"drowned={drowned:F3} fell={fell:F3}: the two causes must not be the same beat.");
    }
}
