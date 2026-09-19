using Godot;

namespace MpFoundation.Game.World;

/// <summary>
/// The interval math behind <see cref="SparseSfxEmitter"/>, extracted as a pure static class so a
/// headless test can reach it.
///
/// Deliberately NOT a member of the emitter: the emitter is a <see cref="Node3D"/>, and this
/// repo's Godot-free xUnit tier can only touch plain managed types (Godot value structs and
/// <see cref="Mathf"/>), never a node. Same reason and same shape as <c>CyclePhase.FromElapsed</c>
/// — a decision lifted out of a node purely so a machine can check it. That matters more here than
/// usual: this repo currently has NO audio test of any kind, and a scheduler is the one part of an
/// audio system a headless run can actually verify. It cannot hear a mix; it can prove that the
/// same seed produces the same sequence, and that no input produces a gap that would drain the
/// one-shot pool.
/// </summary>
public static class SparseSfxSchedule
{
    /// <summary>Floor applied when a caller passes a nonsensical minimum. Small enough to be
    /// inaudible as a change in character, large enough that a misconfigured emitter cannot fire
    /// every frame and starve SfxLab's shared one-shot pool.</summary>
    public const float MinFloorSec = 0.05f;

    /// <summary>
    /// The gap before emission number <paramref name="step"/>, as a pure function of
    /// <paramref name="seed"/> and <paramref name="step"/> — replayable from any step without
    /// having generated the ones before it, which is what makes the sequence assertable.
    ///
    /// Always returns a finite value in <c>[min, max]</c>, both ends inclusive. Degenerate inputs
    /// are corrected rather than propagated, because each failure mode here is silent and nasty: a
    /// negative or NaN gap would fire every frame and drain the shared pool (taking every other
    /// sound in the game with it), and a max below the min would otherwise produce a negative
    /// range. A max below the min is treated as the caller having written the pair backwards and is
    /// swapped, which is the only interpretation that preserves their evident intent.
    /// </summary>
    public static float NextIntervalSec(int seed, ulong step, float minSec, float maxSec)
    {
        if (!float.IsFinite(minSec) || minSec < 0f)
            minSec = MinFloorSec;
        if (!float.IsFinite(maxSec) || maxSec < 0f)
            maxSec = minSec;
        if (maxSec < minSec)
            (minSec, maxSec) = (maxSec, minSec);
        if (Mathf.IsEqualApprox(minSec, maxSec))
            return minSec;

        return Mathf.Lerp(minSec, maxSec, Unit01(seed, step));
    }

    /// <summary>A deterministic value in <c>[0,1)</c> from the (seed, step) pair. splitmix64-style
    /// avalanche: cheap, and it decorrelates adjacent steps, which a plain
    /// <c>seed + step</c> hash does not — without that, consecutive gaps drift monotonically and
    /// the layer develops an audible accelerating rhythm.</summary>
    public static float Unit01(int seed, ulong step)
    {
        // Cast through uint first: a negative seed sign-extends to a mostly-ones ulong, which
        // collapses the useful entropy of small negative seeds into near-identical values.
        ulong h = (ulong)(uint)seed * 0x9E3779B97F4A7C15UL ^ (step + 1UL) * 0xBF58476D1CE4E5B9UL;
        h ^= h >> 30; h *= 0xBF58476D1CE4E5B9UL;
        h ^= h >> 27; h *= 0x94D049BB133111EBUL;
        h ^= h >> 31;
        return (h >> 11) * (1.0f / (1UL << 53));
    }
}
