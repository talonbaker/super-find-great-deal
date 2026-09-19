using MpFoundation.Game.World;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// The repo's first audio-side test. It cannot hear anything — no headless run can — so it asserts
/// the one part of an ambient layer a machine genuinely can: that the scheduler behind
/// <see cref="SparseSfxEmitter"/> is deterministic per seed, stays inside its stated bounds, and
/// cannot be talked into a gap that would fire every frame and drain SfxLab's shared one-shot pool
/// (which would take every other sound in the game down with it, not just the ambience).
/// </summary>
public class SparseSfxScheduleTests
{
    private const int Steps = 500;

    [Fact]
    public void SameSeed_ProducesIdenticalSequence()
    {
        for (ulong step = 0; step < Steps; step++)
        {
            Assert.Equal(
                SparseSfxSchedule.NextIntervalSec(101, step, 0.2f, 1.5f),
                SparseSfxSchedule.NextIntervalSec(101, step, 0.2f, 1.5f));
        }
    }

    [Fact]
    public void DifferentSeeds_Diverge()
    {
        // Two emitters placed in the same world must not crackle in lockstep. Not asserting every
        // step differs (two hashes may legitimately collide on one step); asserting the sequences
        // are not the same sequence.
        int differing = 0;
        for (ulong step = 0; step < Steps; step++)
        {
            if (SparseSfxSchedule.NextIntervalSec(101, step, 0.2f, 1.5f)
                != SparseSfxSchedule.NextIntervalSec(102, step, 0.2f, 1.5f))
            {
                differing++;
            }
        }
        Assert.True(differing > Steps * 0.9, $"seeds 101/102 produced the same gap on too many steps ({Steps - differing} of {Steps})");
    }

    [Fact]
    public void EverySample_StaysWithinBounds()
    {
        const float min = 0.2f, max = 1.5f;
        for (ulong step = 0; step < Steps; step++)
        {
            float v = SparseSfxSchedule.NextIntervalSec(7, step, min, max);
            Assert.InRange(v, min, max);
        }
    }

    [Fact]
    public void SequenceActuallyVaries_NotAConstant()
    {
        // A hash that avalanches badly can return a near-constant, which would make the layer a
        // metronome — audibly worse than a loop, and invisible to a bounds check.
        var seen = new System.Collections.Generic.HashSet<float>();
        for (ulong step = 0; step < Steps; step++)
            seen.Add(SparseSfxSchedule.NextIntervalSec(7, step, 0.2f, 1.5f));
        Assert.True(seen.Count > Steps / 2, $"only {seen.Count} distinct gaps in {Steps} steps — the spacing is barely varying");
    }

    [Fact]
    public void AdjacentSteps_AreNotMonotonic()
    {
        // Guards the specific bug the avalanche exists to prevent: a weak (seed + step) hash makes
        // consecutive gaps drift steadily, so the layer develops an accelerating rhythm that a
        // bounds check and a distinctness check both pass.
        int rises = 0;
        float prev = SparseSfxSchedule.NextIntervalSec(7, 0, 0.2f, 1.5f);
        for (ulong step = 1; step < Steps; step++)
        {
            float v = SparseSfxSchedule.NextIntervalSec(7, step, 0.2f, 1.5f);
            if (v > prev)
                rises++;
            prev = v;
        }
        // A well-mixed sequence rises about half the time; a monotonic one rises ~always or ~never.
        Assert.InRange(rises, (int)(Steps * 0.3), (int)(Steps * 0.7));
    }

    [Theory]
    [InlineData(float.NaN, 1.5f)]
    [InlineData(float.PositiveInfinity, 1.5f)]
    [InlineData(-1f, 1.5f)]
    [InlineData(0.2f, float.NaN)]
    [InlineData(0.2f, -1f)]
    [InlineData(float.NaN, float.NaN)]
    public void DegenerateBounds_NeverProduceANonFiniteOrNegativeGap(float min, float max)
    {
        // The failure this prevents is not a wrong sound, it's a starved pool: a NaN or negative
        // gap means the timer is always elapsed, so the emitter fires every single frame.
        for (ulong step = 0; step < 64; step++)
        {
            float v = SparseSfxSchedule.NextIntervalSec(7, step, min, max);
            Assert.True(float.IsFinite(v), $"non-finite gap {v} from min={min} max={max}");
            Assert.True(v >= SparseSfxSchedule.MinFloorSec,
                $"gap {v} from min={min} max={max} is below the {SparseSfxSchedule.MinFloorSec}s floor that keeps the pool alive");
        }
    }

    [Fact]
    public void MaxBelowMin_IsTreatedAsWrittenBackwards_NotAsANegativeRange()
    {
        for (ulong step = 0; step < 64; step++)
        {
            float v = SparseSfxSchedule.NextIntervalSec(7, step, 1.5f, 0.2f);
            Assert.InRange(v, 0.2f, 1.5f);
        }
    }

    [Fact]
    public void EqualBounds_ReturnExactlyThatValue()
    {
        for (ulong step = 0; step < 64; step++)
            Assert.Equal(0.75f, SparseSfxSchedule.NextIntervalSec(7, step, 0.75f, 0.75f));
    }

    [Fact]
    public void NegativeSeeds_StillDecorrelate()
    {
        // Guards the sign-extension trap: casting a negative int straight to ulong yields a
        // mostly-ones value, which collapses small negative seeds onto near-identical sequences.
        int differing = 0;
        for (ulong step = 0; step < Steps; step++)
        {
            if (SparseSfxSchedule.NextIntervalSec(-1, step, 0.2f, 1.5f)
                != SparseSfxSchedule.NextIntervalSec(-2, step, 0.2f, 1.5f))
            {
                differing++;
            }
        }
        Assert.True(differing > Steps * 0.9, $"negative seeds -1/-2 collided on too many steps ({Steps - differing} of {Steps})");
    }

    [Fact]
    public void Unit01_StaysInZeroToOne()
    {
        for (ulong step = 0; step < Steps; step++)
        {
            float u = SparseSfxSchedule.Unit01(12345, step);
            Assert.InRange(u, 0f, 0.9999999f);
        }
    }
}
