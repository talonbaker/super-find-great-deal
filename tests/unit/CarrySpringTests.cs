using Godot;
using MpFoundation.Game.Sandbox.Feel;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// The carry spring's behaviour, without an engine (CARRY-1).
///
/// <para><see cref="CarrySpring"/> is the one piece of the R.E.P.O. carry that BOTH the feel lab
/// and the networked holder run, so a regression in it is a regression in two places at once —
/// and it is pure arithmetic over <c>Vector3</c>/<c>Basis</c>, which plain xUnit can drive at any
/// timestep for any number of ticks. The scene suites prove the carry replicates; these prove the
/// maths under it cannot blow up, cannot drift, and does not secretly depend on frame rate.</para>
/// </summary>
public class CarrySpringTests
{
    private const float Dt60 = 1f / 60f;

    private static CarrySpring Fresh() => new();

    /// <summary>Run the spring toward a fixed anchor and return where it ends up.</summary>
    private static Vector3 Settle(CarrySpring spring, Vector3 anchor, int ticks, float dt = Dt60)
    {
        Transform3D t = default;
        for (int i = 0; i < ticks; i++)
            t = spring.Step(dt, anchor, Basis.Identity, 0.1f);
        return t.Origin;
    }

    [Fact]
    public void Seed_StartsAtTheItem_NotAtTheHand()
    {
        // THE DOCUMENTED ANTI-POP. Seeding at the anchor teleports the item into the hand on the
        // grab frame and throws away the one frame in which the grab is readable, so the very
        // first thing this type must guarantee is that a freshly seeded spring is still AT the
        // item and not yet at the hand.
        var spring = Fresh();
        var item = new Transform3D(Basis.Identity, new Vector3(3f, 0.5f, -2f));
        var hand = new Vector3(0f, 1.2f, 0f);
        spring.Seed(item, hand);
        Assert.Equal(item.Origin, spring.Position);
        Assert.True(spring.Position.DistanceTo(hand) > 3f,
            "a seeded spring must not have arrived at the hand before a single tick has run");
    }

    [Fact]
    public void Converges_OnAStationaryHand()
    {
        var spring = Fresh();
        var hand = new Vector3(1f, 1.1f, 0.5f);
        spring.Seed(new Transform3D(Basis.Identity, new Vector3(4f, 0f, 4f)), hand);
        Vector3 end = Settle(spring, hand, ticks: 180); // three seconds
        Assert.True(end.DistanceTo(hand) < 0.005f,
            $"a hand that never moves must be caught up with; ended {end.DistanceTo(hand):0.0000} m short");
        Assert.True(spring.LagTo(hand) < 0.005f);
    }

    [Theory]
    // The response dial's full authored range (Interactor.LightResponse / HeavyResponse), at a
    // 60 Hz tick and at a pathological one. The naive spring integration blows up as omega*dt
    // approaches 1 — and it does not blow up quietly, it produces NaN a frame later and the
    // object is simply gone. This is the property that lets the dials be exposed at all.
    [InlineData(40f, Dt60)]
    [InlineData(40f, 0.25f)]
    [InlineData(1f, Dt60)]
    [InlineData(40f, 0.5f)]
    public void StaysFinite_AtEveryResponseAndTimestep(float omega, float dt)
    {
        var spring = Fresh();
        spring.LightResponse = omega;
        spring.HeavyResponse = omega;
        var hand = new Vector3(0.5f, 1.2f, -0.4f);
        spring.Seed(new Transform3D(Basis.Identity, Vector3.Zero), hand);
        for (int i = 0; i < 600; i++)
        {
            // A hand that jumps around as hard as a reconciliation snap ever could.
            Vector3 jitter = new(Mathf.Sin(i * 1.7f) * 2f, Mathf.Cos(i * 0.9f), Mathf.Sin(i * 0.3f) * 2f);
            Transform3D t = spring.Step(dt, hand + jitter, Basis.Identity, 1f);
            Assert.True(t.Origin.IsFinite(), $"position went non-finite at tick {i} (omega {omega}, dt {dt})");
            Assert.True(t.Basis.Determinant() > 0.5f, $"basis degenerated at tick {i}");
        }
    }

    [Fact]
    public void Lag_IsBounded_AndDoesNotGrow_UnderASustainedWalk()
    {
        // The drift regression, in miniature: a hand travelling at a constant speed forever must
        // settle to a CONSTANT lag, not an accumulating one. This is the property Run-CarryDriftTest
        // measures live; asserting it here means a spring change is caught in three seconds of
        // xUnit rather than in twenty of a scene suite.
        var spring = Fresh();
        var hand = Vector3.Zero;
        spring.Seed(new Transform3D(Basis.Identity, hand), hand);
        const float speed = 3.6f; // the avatar's walk
        float early = 0f, late = 0f, peak = 0f;
        for (int i = 0; i < 600; i++)
        {
            hand += new Vector3(speed * Dt60, 0f, 0f);
            spring.Step(Dt60, hand, Basis.Identity, 0.1f);
            float lag = spring.LagTo(hand);
            if (i == 120) early = lag;
            if (i == 599) late = lag;
            if (i > 60 && lag > peak) peak = lag;
        }
        Assert.True(peak < 1.0f, $"steady-state lag {peak:0.000} m is not a carry, it is a tow rope");
        Assert.True(Mathf.Abs(late - early) < 0.005f,
            $"lag grew from {early:0.000} m to {late:0.000} m over eight seconds of walking — that is drift, not lag");
    }

    [Fact]
    public void Lag_IsFrameRateIndependent()
    {
        // A bare Lerp smooths twice as fast at 120 Hz as at 60, which would make every feel dial
        // secretly a function of the player's monitor. Same walk, two timesteps, same answer.
        float LagAt(float dt, int ticks)
        {
            var spring = Fresh();
            var hand = Vector3.Zero;
            spring.Seed(new Transform3D(Basis.Identity, hand), hand);
            for (int i = 0; i < ticks; i++)
            {
                hand += new Vector3(3.6f * dt, 0f, 0f);
                spring.Step(dt, hand, Basis.Identity, 0.1f);
            }
            return spring.LagTo(hand);
        }
        float at60 = LagAt(1f / 60f, 300);
        float at120 = LagAt(1f / 120f, 600);
        Assert.True(Mathf.Abs(at60 - at120) < 0.02f,
            $"60 Hz settled at {at60:0.000} m of lag and 120 Hz at {at120:0.000} m — the dials depend on frame rate");
    }

    [Fact]
    public void Heft_MakesAHeavyThingLagFurtherThanALightOne()
    {
        // The entire weight effect is the spring frequency, and nothing else: no animation, no
        // per-item authoring, no branch. If this ever stops being true, "heavy" has quietly become
        // a second system.
        float LagAtHeft(float heft)
        {
            var spring = Fresh();
            var hand = Vector3.Zero;
            spring.Seed(new Transform3D(Basis.Identity, hand), hand);
            for (int i = 0; i < 300; i++)
            {
                hand += new Vector3(3.6f * Dt60, 0f, 0f);
                spring.Step(Dt60, hand, Basis.Identity, heft);
            }
            return spring.LagTo(hand);
        }
        Assert.True(LagAtHeft(1f) > LagAtHeft(0f) * 2f,
            "a full-heft item must trail the hand visibly further than a weightless one");
    }

    [Fact]
    public void HeftOf_NormalisesAgainstTheOneMassReference()
    {
        var spring = Fresh(); // MassReference 10 kg
        Assert.Equal(0f, spring.HeftOf(0f));
        Assert.True(Mathf.Abs(spring.HeftOf(5f) - 0.5f) < 1e-4f);
        Assert.Equal(1f, spring.HeftOf(10f));
        Assert.Equal(1f, spring.HeftOf(1000f)); // clamped, never past full heft
    }

    [Fact]
    public void ItemVelocity_IsTheItemsOwn_NotTheHands()
    {
        // A release inherits the ITEM's velocity, which is the whole reason the lag is simulated
        // rather than faked with an offset: a heavy thing that trailed the hand leaves at the
        // speed it was actually travelling. On the first ticks after a grab the item has barely
        // started moving while the hand is already at full speed.
        var spring = Fresh();
        var hand = Vector3.Zero;
        spring.Seed(new Transform3D(Basis.Identity, hand), hand);
        for (int i = 0; i < 3; i++)
        {
            hand += new Vector3(3.6f * Dt60, 0f, 0f);
            spring.Step(Dt60, hand, Basis.Identity, 1f);
        }
        Assert.True(spring.ItemVelocity.Length() < 3.6f,
            "the item cannot already be travelling at hand speed three ticks into a heavy carry");
        Assert.True(spring.ItemVelocity.X > 0f, "it is nonetheless moving the way the hand went");
    }
}
