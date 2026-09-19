using System.Collections.Generic;
using Godot;
using MpFoundation.Game.World;

namespace MpFoundation.Game.Sight;

/// <summary>
/// One light source, reduced to the only two things sight cares about: where it is and how far its
/// light reaches. A plain value type with no owner and no identity — every light-owning system
/// projects its own records into this shape (<see cref="PlayerSightService.GatherLights"/>), and
/// from there on nothing downstream can tell one kind of light from another. That is deliberate:
/// canon fact 4 says sight is proximity to <i>light</i>, not proximity to a particular kind of
/// light, so a curve that could distinguish them would be a curve that could be given a per-source
/// exception.
///
/// <para><b><see cref="LitRadiusM"/> is light, not ward, and the two are not the same question.</b>
/// Ward answers "is this ground safe"; light answers "can I see". Sight is the second question, so
/// a source may genuinely help you see and still not make you safe.</para>
///
/// <para><b>Sanitised at construction, in the blind direction.</b> A NaN or a negative radius
/// becomes zero, which contributes nothing. There is no input to this struct that invents light.</para>
/// </summary>
public readonly struct LightSample
{
    /// <summary>World space. Only X and Z are read — see
    /// <see cref="PlayerSightCurve.HorizontalDistanceM"/> for why.</summary>
    public Vector3 Position { get; }

    /// <summary>Metres of ground this source lights. Zero for anything that is not currently
    /// producing light; such a sample is simply never added.</summary>
    public float LitRadiusM { get; }

    public LightSample(Vector3 position, float litRadiusM)
    {
        Position = position;
        LitRadiusM = float.IsNaN(litRadiusM) || litRadiusM < 0f ? 0f : litRadiusM;
    }

    public override string ToString() =>
        $"({Position.X:F1},{Position.Z:F1}) r={LitRadiusM:F2}m";
}

/// <summary>
/// <b>How far a player can see — canon fact 4, as arithmetic.</b> "Darkness is the pressure, and
/// vision is proximity-based. Away from light a player is near-blind... The closer you stand to a
/// light source, the further you can see into the dark; walking to the firelight's edge to check a
/// sound means losing your own sight to do it."
///
/// <para>Pure — a position, a list of lights and a clock band in, one number in metres out. No
/// scene tree, no <c>Node</c>, no Multiplayer API, and (the load-bearing part) <b>nothing
/// presentational</b>. The same seam <see cref="CyclePhase"/> gives <see cref="CycleDriver"/> and
/// <see cref="CycleBands"/> gives the sky, for the same reason: this is the part that has to be
/// provable by <c>dotnet test</c> rather than by standing in it.</para>
///
/// <para><b>THE PARITY LAW IS THE POINT OF THIS FILE, NOT A FOOTNOTE.</b> Canon fact 4 ends
/// "Gameplay visibility is server data, identical on every client (the parity law); rendered light
/// is presentation only." The failure that sentence exists to prevent is a player turning graphics
/// down to see further in the dark. So every input below is either replicated world state (a fire's
/// fuel-derived light radius, a stick's phase, an avatar's authoritative position, the replicated
/// phase clock) or a constant in this file. Nothing here samples a light, a shader, a camera, a
/// rendered frame or <see cref="MpFoundation.World.GraphicsQuality"/>, and <b>nothing here ever
/// may</b> — two peers on different graphics tiers necessarily return the same number because there
/// is no term in the arithmetic that could differ. <c>PlayerSightTests</c> pins that across all
/// three tiers, with a negative control that proves the comparison can detect a break.</para>
///
/// <para><b>The shape, in one line each.</b>
/// <list type="number">
/// <item>Every source publishes a lit radius. Standing at its centre grants
/// <see cref="SightAtSourceM"/>; at its edge it grants nothing, and the fall between the two is
/// linear (<see cref="FromSource"/>).</item>
/// <item>A point's night sight is the <b>maximum</b> over every source — union, not nearest
/// (<see cref="NightSightM"/>).</item>
/// <item>Day is not governed by firelight at all; the two sweeps interpolate between the two
/// regimes so nothing steps (<see cref="BlendForBand"/>).</item>
/// <item>With no light, no clock, or no sources at all, the answer is
/// <see cref="DarkFloorM"/> — never <see cref="DaylightSightM"/>.</item>
/// </list></para>
///
/// <para><b>Why linear rather than an eased or inverse-square falloff.</b> Linear means the light
/// is always telling the truth about how much reach it is granting, and a player who has learned
/// one light can estimate
/// the next. An inverse-square curve would also be physically prettier and gameplay-worse — it
/// collapses almost all of the useful gradient into the first couple of metres, so the entire
/// middle of a fire's pool would read as one flat "outside", and "walk to the edge and lose your
/// sight" would stop being a gradient the player can feel themselves paying.</para>
///
/// <para><b>What this deliberately does NOT do.</b> It does not render, dim, fog, or tint anything;
/// it produces the number a renderer will later be bound to. It has no line-of-sight term (no
/// raycasts, no occlusion): a wall between you and a fire does not currently darken you. That is a
/// known simplification, stated rather than hidden — occlusion is a per-peer raycast budget and a
/// second desync surface, and it is not needed to make the dark the pressure.</para>
/// </summary>
public static class PlayerSightCurve
{
    /// <summary><b>How far a player sees standing in full darkness, metres.</b> Near-blind, and
    /// deliberately not zero: canon fact 4 says "silhouettes against sky", not a black screen, and a
    /// literal zero is a divide-by, a degenerate frustum and a bug magnet everywhere downstream.
    ///
    /// <para><b>Why 3.0.</b> The player stands roughly 1.4 m tall, so three metres is about two
    /// body lengths — far enough to see the ground you are about to step on and a teammate at
    /// arm's reach, short enough that navigation is by memory and by sound rather than by looking.
    /// A value picked and stated; move this and nothing else moves with it.</para></summary>
    public const float DarkFloorM = 3.0f;

    /// <summary><b>Metres of sight granted per metre of a source's own lit radius, at its
    /// centre.</b> The single dial that decides how much a fire is worth. 1.75 was picked against the
    /// shipped light radii rather than in the abstract:
    /// <code>
    ///   source                     lit radius   sight at its centre
    ///   fire pit, full pile          18.0 m           34.5 m
    ///   fire pit, last embers         2.5 m            7.4 m
    ///   rock ring, full pile         14.0 m           27.5 m
    ///   rock ring, nearly out         4.0 m           10.0 m
    ///   indoor hearth, full pile     12.0 m           24.0 m
    ///   handheld light                4.0 m           10.0 m
    ///   nothing                          —             3.0 m
    /// </code>
    /// Read against the original level's geography: from a roaring pit a landmark 18-23 m out
    /// was visible and the trailheads (r = 40-50 m) were not, so a well-fed fire made the base
    /// legible and never the treeline. That is the whole of canon fact 2's "light is safety"
    /// expressed as something the player perceives without a HUD.</summary>
    public const float SightPerLitMetre = 1.75f;

    /// <summary><b>Absolute ceiling on a night-time sight range, metres.</b> A backstop, not an
    /// everyday clamp: it only binds for a source whose lit radius exceeds
    /// (36 - 3) / 1.75 = 18.9 m, and the largest light in the game is the fire pit's 18 m, so
    /// nothing in shipped play reaches it. It exists so that a future light source with a large
    /// radius cannot silently hand out a sight range that makes the night stop being the pressure.
    /// If a later light legitimately needs to beat this, that is a design call, not a clamp to
    /// raise in passing.</summary>
    public const float MaxSightM = 36.0f;

    /// <summary><b>How far a player sees in daylight, metres.</b> Requirement: day is not governed
    /// by firelight — a fire at noon must change nothing about what you can see. 80 m clears the
    /// camp's r = 50 m cleared core and its treeline in every direction, so by day the map is simply
    /// legible; it is comfortably above <see cref="MaxSightM"/>, which is what makes the dusk sweep
    /// always a loss of sight and never a gain.</summary>
    public const float DaylightSightM = 80.0f;

    /// <summary>Sight granted by standing at the exact centre of a source with this lit radius.
    /// Linear in the radius, floored at <see cref="DarkFloorM"/> (a source that lights nothing
    /// grants nothing) and capped at <see cref="MaxSightM"/>.
    ///
    /// <para>Defined for hostile input rather than asserting (MECHANICS-BIBLE §6): NaN and negative
    /// both read as no light at all.</para></summary>
    public static float SightAtSourceM(float litRadiusM)
    {
        if (float.IsNaN(litRadiusM) || litRadiusM <= 0f)
            return DarkFloorM;
        return Mathf.Min(DarkFloorM + SightPerLitMetre * litRadiusM, MaxSightM);
    }

    /// <summary>How much of a source's grant survives at this distance: 1 at its centre, 0 at its
    /// edge and beyond, linear in between. Continuous everywhere, including across the edge, so no
    /// single step can change a player's sight by a jump.
    ///
    /// <para>A source with no radius reaches nothing at any distance, including at distance
    /// zero — an extinguished fire you are standing on top of is not light.</para></summary>
    public static float Reach01(float horizontalDistanceM, float litRadiusM)
    {
        if (float.IsNaN(litRadiusM) || litRadiusM <= 0f)
            return 0f;
        if (float.IsNaN(horizontalDistanceM) || horizontalDistanceM <= 0f)
            return 1f;
        if (horizontalDistanceM >= litRadiusM)
            return 0f;
        return 1f - horizontalDistanceM / litRadiusM;
    }

    /// <summary>Sight granted by one source alone, at this distance. Never below
    /// <see cref="DarkFloorM"/>: a light can only ever add.</summary>
    public static float FromSource(float horizontalDistanceM, float litRadiusM) =>
        Mathf.Lerp(DarkFloorM, SightAtSourceM(litRadiusM), Reach01(horizontalDistanceM, litRadiusM));

    /// <summary><b>Night sight at a point: the UNION over every source, not the nearest one.</b>
    /// (Plan §6.3, "overlapping wards: fine, union".)
    ///
    /// <para>Nearest-source is the weaker rule and it is wrong in a way a player would feel: a
    /// guttering loose burn at your feet would mask the roaring pit eight metres away that is
    /// genuinely reaching you, so dropping a stick would make you <i>blinder</i>. Taking the maximum
    /// grant instead means a point sees as far as the best light that reaches it, and adding a light
    /// can never reduce anyone's sight.</para>
    ///
    /// <para>Not a sum, either. Two fires side by side do not let you see twice as far; they light a
    /// wider shape. Summing would also make a pile of cheap sticks beat the central fire, which would
    /// break canon fact 3 outright — the central fire keeps its weight.</para>
    ///
    /// <para><b>Degrades to blind.</b> An empty list, a list of dead sources, or a point outside
    /// every radius all return <see cref="DarkFloorM"/>. There is no input that returns more than
    /// the floor without a live source within a real radius of the point — the direction a light
    /// query must be wrong in if it is wrong at all.</para></summary>
    public static float NightSightM(Vector3 at, IReadOnlyList<LightSample>? lights)
    {
        float best = DarkFloorM;
        if (lights == null)
            return best;
        for (int i = 0; i < lights.Count; i++)
        {
            LightSample s = lights[i];
            if (s.LitRadiusM <= 0f)
                continue;
            float granted = FromSource(HorizontalDistanceM(s.Position, at), s.LitRadiusM);
            if (granted > best)
                best = granted;
        }
        return best;
    }

    /// <summary><b>Day is not night, and the sweeps are ramps rather than steps.</b> Day holds at
    /// <see cref="DaylightSightM"/> regardless of every fire in the camp; night is whatever the
    /// lights grant; the dusk and dawn sweeps interpolate between the two across the sweep's own
    /// normalized progress.
    ///
    /// <para><b>Continuous at every boundary by construction</b>, which is the property that makes
    /// this correct rather than merely smooth-looking: at dusk's start progress is 0 and the blend
    /// is exactly the day value; at its end progress is 1 and it is exactly the night value, which
    /// is what the Night band then holds; dawn runs the same interval backwards and lands on the day
    /// value at the wrap, where the Day band picks it up. <see cref="CycleBands.GetBand"/> is
    /// exhaustive over [0,1) and reports progress inside the band, so there is no phase at which
    /// this function jumps.</para>
    ///
    /// <para>The dusk sweep is a fraction of the cycle (<see cref="CycleBands.DuskSweepWidth"/>),
    /// and across it the player goes from seeing the whole camp to seeing the pool their fire is
    /// holding. Nobody has to be told the night arrived — this curve is how they read it. The
    /// sweep's share of the cycle narrowed at CONST-1 (2026-08-21), so that transition now takes a
    /// smaller slice of the day than it used to. <b>Deliberately not stated in seconds:</b> the
    /// period is a tuning value, so a duration here would be true of exactly one configuration and
    /// would read to a later agent as a definition of dusk. It is not one.</para></summary>
    public static float BlendForBand(CycleBands.Band band, float bandProgress, float nightSightM)
    {
        float t = float.IsNaN(bandProgress) ? 0f : Mathf.Clamp(bandProgress, 0f, 1f);
        return band switch
        {
            CycleBands.Band.Day => DaylightSightM,
            CycleBands.Band.DuskSweep => Mathf.Lerp(DaylightSightM, nightSightM, t),
            CycleBands.Band.DawnSweep => Mathf.Lerp(nightSightM, DaylightSightM, t),
            _ => nightSightM,
        };
    }

    /// <summary>The whole model as one pure call: where the player is, what lights exist, and where
    /// the replicated clock is. This is what the server evaluates per peer per tick.</summary>
    public static float Resolve(Vector3 at, IReadOnlyList<LightSample>? lights,
        CycleBands.Band band, float bandProgress)
        => BlendForBand(band, bandProgress, NightSightM(at, lights));

    /// <summary>The same, from the clock's raw replicated pair — the exact derivation
    /// <c>PlayerSightService</c> runs, exposed so a headless test can assert it without a
    /// <c>Node</c>, the same seam <see cref="CyclePhase.FromElapsed"/> gives
    /// <see cref="CycleDriver"/>.</summary>
    public static float ResolveFromClock(Vector3 at, IReadOnlyList<LightSample>? lights,
        float phase, int cyclesElapsed)
    {
        CycleBands.Band band = CycleBands.GetBand(phase, cyclesElapsed, out float progress);
        return Resolve(at, lights, band, progress);
    }

    /// <summary><b>The whole server-side decision, clock guard included.</b> This is exactly what
    /// <c>PlayerSightService.ServerRecompute</c> evaluates per peer, extracted as a pure function so
    /// the guard is <i>provable</i> rather than merely written down in a Node nothing headless can
    /// instantiate.
    ///
    /// <para><b>An unsynced clock reads as blind, and that branch is the one worth staring at.</b>
    /// <see cref="CycleDriver"/>'s zero-initialized default is phase 0 — the middle of the Day band —
    /// so a build that simply read <c>Phase</c> without checking <see cref="CycleDriver.Synced"/>
    /// would hand every player <see cref="DaylightSightM"/> at midnight, on every client, for as long
    /// as the clock took to arrive. That is the inversion in its purest form: a default value that
    /// silently means the opposite of the truth. So there is no
    /// "reasonable guess" here — no clock, no sight.</para></summary>
    public static float ResolveForClock(Vector3 at, IReadOnlyList<LightSample>? lights,
        bool clockSynced, float phase, int cyclesElapsed)
        => clockSynced ? ResolveFromClock(at, lights, phase, cyclesElapsed) : DarkFloorM;

    /// <summary>Planar distance, metres. <b>Y is ignored</b>: a light lights the ground around it,
    /// and a spherical test would silently blind anyone standing on the smallest rise — or decide
    /// whether an indoor light counts by the floor's height.</summary>
    public static float HorizontalDistanceM(Vector3 a, Vector3 b)
    {
        float dx = a.X - b.X;
        float dz = a.Z - b.Z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }
}
