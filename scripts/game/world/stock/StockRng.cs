namespace MpFoundation.Game.World.Stock;

/// <summary>
/// <b>The deterministic stream every stock layout is drawn from — SplitMix64, seeded from a
/// string.</b>
///
/// <para><b>Why not <c>System.Random</c>.</b> Its algorithm is explicitly documented as an
/// implementation detail and it has changed between .NET versions. This layout is BAKED into a
/// scene file by a generator and then re-derived by <c>dotnet test</c> to prove the bake did not
/// drift (see <c>ShelfStockBakeTests</c>); a generator and a checker that disagree because the
/// runtime was upgraded would be a red nobody could explain. SplitMix64 is eleven lines, has no
/// state beyond a <c>ulong</c>, and gives the same stream on every runtime and every machine
/// forever.</para>
///
/// <para><b>Why the seed is a string.</b> Every bay's fill is seeded from that bay's NODE PATH
/// (<c>Aisle0_Bay2</c>), so the sixteen bays get sixteen different pictures from one algorithm,
/// and moving a bay in the editor without renaming it keeps its stock. That is also the trap
/// <c>.claude/rules/godot-scenes.md</c> names three times over — an <c>[Export]</c> on a nested
/// instance line is silently dropped on this build, a node NAME is native and survives — so the
/// identity this reads is the one kind that cannot go missing.</para>
/// </summary>
public struct StockRng
{
    private ulong _state;

    /// <summary>FNV-1a over the seed string's UTF-16 code units. Not a hash anybody has to trust
    /// cryptographically — it has to be stable, and a hand-written one is stable in a way
    /// <c>string.GetHashCode</c> explicitly is not (randomised per process since .NET Core).</summary>
    public static ulong SeedFrom(string s)
    {
        ulong h = 14695981039346656037UL;
        foreach (char c in s)
        {
            h ^= c;
            h *= 1099511628211UL;
        }
        // A zero state is legal for SplitMix64 but makes the first draw predictable; nudge it.
        return h == 0 ? 0x9E3779B97F4A7C15UL : h;
    }

    public StockRng(string seed) => _state = SeedFrom(seed);

    public StockRng(ulong seed) => _state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;

    /// <summary>The next 64 bits.</summary>
    public ulong Next()
    {
        _state += 0x9E3779B97F4A7C15UL;
        ulong z = _state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>A value in [0, exclusiveMax). Plain modulo: the bias at these ranges (every
    /// bound here is under 64) is ~2^-58 and this is a shelf, not a lottery.</summary>
    public int NextInt(int exclusiveMax) =>
        exclusiveMax <= 1 ? 0 : (int)(Next() % (ulong)exclusiveMax);

    /// <summary>A value in [inclusiveMin, inclusiveMax].</summary>
    public int NextRange(int inclusiveMin, int inclusiveMax) =>
        inclusiveMin + NextInt(inclusiveMax - inclusiveMin + 1);

    /// <summary>A fair coin.</summary>
    public bool NextBool() => (Next() & 1UL) != 0UL;

    /// <summary>A float in [-half, +half]. Used only for the per-instance jitter that keeps a
    /// mound from reading as a lattice.</summary>
    public float NextJitter(float half) =>
        half <= 0f ? 0f : (float)((Next() / (double)ulong.MaxValue) * 2.0 - 1.0) * half;
}
