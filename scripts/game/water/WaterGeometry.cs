using Godot;

namespace Sail.Game.Water;

/// <summary>
/// Every number the water contract runs on, and the pure functions over them. No node, no
/// scene, no networking, no RNG and no frame-rate dependence beyond the <c>dt</c> handed in —
/// which is exactly what makes the whole state machine xUnit-testable without an engine and
/// what makes client prediction, reconciliation replay and server authority all land on the
/// identical answer from the identical inputs.
///
/// <b>The authority is</b> <c>docs/superpowers/specs/2026-08-08-lake-water-contract-design.md</c>
/// (§3 geometry, §4 states, §5 the cold clock, §6 sputter-out, §7 Soaked). Where that spec
/// names a value, it is reproduced here verbatim; where it left one to the packet, the value
/// is stated in the member's own doc comment along with why it was picked.
/// </summary>
public static class WaterGeometry
{
    // --- Geometry (spec §3.1) ------------------------------------------------------------

    /// <summary>The water surface plane. Everything else about submersion is derived from it.
    ///
    /// <b>This constant is duplicated, deliberately and dangerously.</b> The source of truth
    /// is <c>WATER_Y</c> in <c>tools/dev/camp_sites.gd</c>, which is GDScript and is owned by
    /// packet W1 (the generator authors the lakebed and the visual plane from it). C# cannot
    /// read a GDScript <c>const</c> without instantiating the script, and the state machine
    /// needs the number on every tick, so it is mirrored here.
    ///
    /// Silent drift between the two breaks the state machine invisibly — the bed would slope
    /// under a surface the code thinks is somewhere else, and a player would swim in air or
    /// walk under water with no error anywhere. <c>WaterConstantSourceTests</c> parses the
    /// <c>.gd</c> file (and the spec) and fails if they disagree, so the drift becomes a red
    /// test instead of a mystery.</summary>
    public const float WaterY = -0.58f;

    /// <summary>The shoreline: world x at or below this is lake. Mirrors <c>LAKE_X_MAX</c> in
    /// <c>camp_sites.gd</c> (unchanged by the water contract — spec §3.1 keeps it as
    /// <c>SHORE_X</c>). Depth at exactly this x is 0.02 m, i.e. firmly <see cref="WaterState.Dry"/>,
    /// so the hard lateral cut at the shoreline introduces no state discontinuity.</summary>
    public const float ShoreX = -45.0f;

    /// <summary>Outer edge of the walkable shelf (spec §3.1). Read by nothing in this file —
    /// stated so the shelf's identity lives next to the rest of the water numbers rather than
    /// only inside W1's generator.</summary>
    public const float ShelfX = -50.0f;

    /// <summary>Bottom of the drop-off (spec §3.1). Same reason as <see cref="ShelfX"/>.</summary>
    public const float DropX = -62.0f;

    /// <summary>Half the map's extent; mirrors <c>CampSites.MAP_HALF</c>. Used only to keep a
    /// shore-recovery point inside the world.</summary>
    public const float MapHalf = 150.0f;

    // --- State thresholds (spec §4) --------------------------------------------------------

    /// <summary>Depth at which <see cref="WaterState.Dry"/> becomes <see cref="WaterState.Wading"/>.
    /// The spec's table reads "Dry ≤ 0.2 m", so 0.2 exactly is Dry — the boundary is inclusive
    /// on the shallow side (MECHANICS-BIBLE §1: state the inclusive/exclusive bound rather than
    /// letting it fall out of the code).</summary>
    public const float WadeDepthM = 0.2f;

    /// <summary>Depth at which <see cref="WaterState.Wading"/> becomes <see cref="WaterState.Swimming"/>.
    /// The spec's table reads "Wading 0.2 – 1.1 m", so 1.1 exactly is still Wading.</summary>
    public const float SwimDepthM = 1.1f;

    /// <summary>The hysteresis deadband (spec §4). A player standing at exactly a threshold —
    /// which is the common case, because the bed is a smooth ramp and the avatar settles onto
    /// it — must not strobe between two states at 60 Hz. Deepening requires crossing
    /// <c>threshold + this</c>; shallowing requires crossing <c>threshold − this</c>, so the
    /// band is 0.1 m wide and the player must genuinely move to change state.</summary>
    public const float HysteresisM = 0.05f;

    /// <summary>How far the feet sit below the surface while swimming. Must exceed
    /// <c>SwimDepthM + HysteresisM</c> (1.15) or holding this line would immediately drop the
    /// player back to Wading and strobe — 1.25 leaves 0.10 m of margin. With the avatar's
    /// ~1.15 m head height that puts the crown of the head at the waterline, which is what
    /// swimming looks like.</summary>
    public const float SwimSubmersionM = 1.25f;

    /// <summary>How fast the body converges on the swim line, m/s. Not buoyancy and not a
    /// force (spec §1.4 forbids both): a deterministic MoveToward, resolved by MoveAndSlide
    /// against the bed exactly like any other motion, so a shallow drop-off stops the descent
    /// with no special case.</summary>
    public const float SwimSettleRate = 4.0f;

    /// <summary>Where the swimming player's eye sits, relative to <see cref="WaterY"/>. Free
    /// prospect denial: from the water you cannot see over the water (spec §4).</summary>
    public const float SwimEyeAboveWaterM = 0.15f;

    // --- Movement modifiers (spec §4, §7) ---------------------------------------------------

    /// <summary>Move-speed multiplier while wading (spec §4).</summary>
    public const float WadeSpeedMul = 0.55f;

    /// <summary>Move-speed multiplier while swimming (spec §4).</summary>
    public const float SwimSpeedMul = 0.40f;

    /// <summary>
    /// Move-speed multiplier while Soaked (spec §7).
    ///
    /// <b>Soaked is a speed multiplier and nothing else. Do not grow it.</b> Packet W5's
    /// <c>/direct</c> pass put it through THRILL-BIBLE §8.7's permanent-safety gate — it is the
    /// first standing cost the map charges the player's body — and it passed <i>because</i> a
    /// speed multiplier is not damage. A stamina drain, a health component, a vision penalty or
    /// a second stacking debuff would each re-open that gate, and the next one to try would be
    /// doing it without the review that cleared this one. Adding any of them is a decision for
    /// Talon, not an implementation detail.
    /// </summary>
    public const float SoakedSpeedMul = 0.90f;

    // --- The cold clock (spec §5) ------------------------------------------------------------

    /// <summary>West of the roped swim area. Inside the rope the lake is nearly free; the rope
    /// is where the cold curve accelerates, which is the rope's whole mechanical job.</summary>
    public const float RopeX = -63.0f;

    /// <summary>Where the ramp ends and the cold is at its worst.</summary>
    public const float ColdRampEndX = -85.0f;

    /// <summary>Seconds of continuous swimming from chill 0 to 1, inside the rope.</summary>
    public const float TimeToFullInsideRopeSec = 180f;

    /// <summary>Seconds from 0 to 1 at the far end of the ramp (<see cref="ColdRampEndX"/>).</summary>
    public const float TimeToFullRampEndSec = 45f;

    /// <summary>Seconds from 0 to 1 beyond the ramp. Note the deliberate step from 45 to 20 at
    /// <see cref="ColdRampEndX"/> — the spec's table specifies a ramp that ends at 45 and a
    /// flat 20 past it, not a continuous curve. Kept as written: the discontinuity is in a rate,
    /// never in the chill value itself, so nothing the player can perceive jumps.</summary>
    public const float TimeToFullFarSec = 20f;

    /// <summary>Chill recovered per second in <see cref="WaterState.Wading"/> or
    /// <see cref="WaterState.Dry"/> — a full recovery takes 30 s from 1.0.</summary>
    public const float ChillRecoveryPerSec = 1f / 30f;

    // --- Sputter-out (spec §6) ----------------------------------------------------------------

    /// <summary>The swallow: how long the view sinks before the hard cut.</summary>
    public const float GoUnderSec = 1.5f;

    /// <summary>How fast the body sinks during <see cref="SputterPhase.GoingUnder"/>, m/s.
    /// Chosen so 1.5 s carries the eye about 1.8 m under the surface — unambiguously below it,
    /// and shallow enough that the bed catches it on the drop-off rather than in the abyss.</summary>
    public const float SinkRate = 1.2f;

    /// <summary>Prone-at-the-shore recovery. Control returns at the END of this, once.</summary>
    public const float RecoverSec = 2.0f;

    /// <summary>How far east of <see cref="ShoreX"/> a recovery lands. Far enough onto the bank
    /// that the player is unambiguously out of the water (depth at ShoreX is only 0.02 m, so
    /// landing exactly on the line would be legal but would read as still standing in the lake)
    /// and near enough that the shore is where you woke up.</summary>
    public const float ShoreInsetM = 1.5f;

    /// <summary>Fallback ground height for a recovery point when no physics space is available
    /// to raycast against (headless logic tests, a world with no terrain). The live path in
    /// <see cref="WaterService"/> always raycasts first; this is only so the pure function is
    /// total. Sits just above the shore bed, so gravity settles the last centimetres.</summary>
    public const float ShoreFallbackY = -0.35f;

    // --- Soaked (spec §7) -----------------------------------------------------------------------

    /// <summary>Seconds inside a heat source's warmth to dry off.</summary>
    public const float SoakedDryOffSec = 20f;

    /// <summary>A heat source's warmth radius — its second job after light (spec §7). Wider than
    /// an interact reach (3.0 m): you dry off by sitting near the fire, not by standing close
    /// enough to touch it. 4.5 m is a first-pass tuning value, picked so a small group can all
    /// be inside it at once.
    ///
    /// <para><b>Not a live radius.</b> The fire system that once derived its own fuel-scaled
    /// warmth ward from this floor has been removed; <c>WaterService</c> answers warmth through an
    /// injected probe and nothing in this build supplies one. This survives as the documented
    /// floor for whatever heat source comes next.</para></summary>
    public const float WarmthRadiusM = 4.5f;

    /// <summary>Noise multiplier applied to a Soaked player's footsteps. <b>Hook point only —
    /// nothing consumes this.</b> The creature sim that would read it does not exist, and the
    /// shared noise channel (INTERACTION-BIBLE §9.3) has no implementation on this trunk. Stated
    /// as a named constant with one reader (<see cref="WaterService.FootstepNoiseMultiplierFor"/>)
    /// so the hook is discoverable, exactly the discipline M5's dock creak already uses.</summary>
    public const float SoakedFootstepNoiseMul = 1.35f;

    // --- Drowning (WATER-3, Talon 2026-08-29) ---------------------------------------------------

    /// <summary>
    /// Depth at which a player is <b>submerged</b>: out of their depth, with the crown of the head
    /// at or under the waterline.
    ///
    /// <para>Deliberately the SAME number as the state machine's Swimming entry
    /// (<see cref="SwimDepthM"/> + <see cref="HysteresisM"/> = 1.15), and derived from it rather
    /// than typed, so "submerged" and <see cref="WaterState.Swimming"/> can never disagree about
    /// the same body. <see cref="SwimSubmersionM"/> (1.25) holds a swimmer 0.10 m deeper than this
    /// line, which is what puts the crown at the surface — so a swimmer is submerged for the whole
    /// time they are swimming, which is exactly the reading Talon asked for: being in the water
    /// over your head is what kills you, wading is not.</para>
    /// </summary>
    public const float SubmergedDepthM = SwimDepthM + HysteresisM;

    /// <summary>
    /// Seconds continuously submerged before a player drowns.
    ///
    /// <para><b>Talon, 2026-08-29:</b> <i>"the water should kill the player after about three
    /// seconds"</i>. Taken as 3.0 exactly rather than escalated as a value question. Canon settles
    /// the rest: drowning is a <b>death with a respawn</b>, not a faint (the PLAYTEST-2 ruling), and
    /// under the caveman register a comic drowning is in bounds — the beat is
    /// <c>RespawnService.ComicDeathBeat</c>'s loft-and-spin, no gore and nobody taken.</para>
    ///
    /// <para><b>This makes the chill sputter-out (§5/§6) unreachable in any world that runs the
    /// drowning timer</b> — 3 s beats 20-180 s by two orders of magnitude. The sputter is left
    /// standing rather than deleted: it is still the camp world's contract (camp builds no
    /// <c>RespawnService</c>, so camp never drowns anyone), and the two water labs drive it
    /// directly. Noted as a consequence rather than discovered later.</para>
    /// </summary>
    public const float DrownAfterSec = 3.0f;

    // --- The lake's footprint (WATER-3) ---------------------------------------------------------

    /// <summary>
    /// <b>A lake, as a finite volume.</b> Before WATER-3 the lake was a half-plane — every point
    /// with <c>x &lt;= ShoreX</c> at any z, any distance west and any depth was "in water" — so a
    /// player who walked off the western edge of the map kept swimming forever in nothing
    /// (Talon, 2026-08-29: <i>"like floating in air invisible water"</i>). A half-plane cannot be
    /// bounded by tightening one number; it needs a shape, and this is that shape.
    ///
    /// <para><b>Plan bounds plus an optional radius plus a floor.</b> The box alone describes the
    /// camp lake (a strip of the camp map west of the shoreline); the radius describes the bubble
    /// test's round lake, whose rendered surface really is a disc. The <b>floor</b> is the third
    /// bound and the one that is easy to forget: a lake has a bottom, so a body below the deepest
    /// authored bed has clipped through the world and must <i>fall</i>, not swim.</para>
    ///
    /// <para>A value type with no engine dependency, so the whole predicate stays xUnit-provable
    /// exactly like the rest of this file.</para>
    /// </summary>
    public readonly struct LakeFootprint
    {
        /// <summary>Western plan bound (inclusive).</summary>
        public readonly float MinX;

        /// <summary>Eastern plan bound (inclusive) — the shoreline, for a lake that has one.</summary>
        public readonly float MaxX;

        /// <summary>Southern plan bound (inclusive).</summary>
        public readonly float MinZ;

        /// <summary>Northern plan bound (inclusive).</summary>
        public readonly float MaxZ;

        /// <summary>Centre of the radial test, when <see cref="RadiusM"/> is positive.</summary>
        public readonly float CentreX;

        /// <summary>Centre of the radial test, when <see cref="RadiusM"/> is positive.</summary>
        public readonly float CentreZ;

        /// <summary>Radius of the round lake, metres. Non-positive means "box only".</summary>
        public readonly float RadiusM;

        /// <summary>World y below which this is no longer the lake but the inside of the map.
        /// Set below the deepest authored lakebed, never at it — see the two footprints
        /// below.</summary>
        public readonly float FloorY;

        private LakeFootprint(float minX, float maxX, float minZ, float maxZ,
                              float centreX, float centreZ, float radiusM, float floorY)
        {
            MinX = minX; MaxX = maxX; MinZ = minZ; MaxZ = maxZ;
            CentreX = centreX; CentreZ = centreZ; RadiusM = radiusM; FloorY = floorY;
        }

        /// <summary>An axis-aligned lake: a rectangle in plan with a floor under it.</summary>
        public static LakeFootprint Box(float minX, float maxX, float minZ, float maxZ, float floorY)
            => new(minX, maxX, minZ, maxZ, 0f, 0f, 0f, floorY);

        /// <summary>A round lake. The plan box is the disc's own bounding square, so
        /// <see cref="ContainsPlan"/> can reject the common case with two compares before it
        /// squares anything.</summary>
        public static LakeFootprint Disc(float centreX, float centreZ, float radiusM, float floorY)
            => new(centreX - radiusM, centreX + radiusM, centreZ - radiusM, centreZ + radiusM,
                   centreX, centreZ, radiusM, floorY);

        /// <summary>Inside the lake's lateral footprint, at any height. Bounds are inclusive on
        /// both sides (MECHANICS-BIBLE §1: state the bound rather than letting it fall out of the
        /// code); a non-finite coordinate is outside, never NaN-propagating.</summary>
        public bool ContainsPlan(Vector3 p)
        {
            if (!float.IsFinite(p.X) || !float.IsFinite(p.Z))
                return false;
            if (p.X < MinX || p.X > MaxX || p.Z < MinZ || p.Z > MaxZ)
                return false;
            if (RadiusM <= 0f)
                return true;
            float dx = p.X - CentreX, dz = p.Z - CentreZ;
            return dx * dx + dz * dz <= RadiusM * RadiusM;
        }

        /// <summary>Inside the lake's whole volume: the footprint in plan, and at or above the
        /// floor. Below the floor a body is inside the map, not in the lake, and falls.</summary>
        public bool Contains(Vector3 p)
            => float.IsFinite(p.Y) && p.Y >= FloorY && ContainsPlan(p);
    }

    /// <summary>
    /// The camp lake (world "camp", and the fallback for any world that does not name its own).
    ///
    /// <para><b>Measured from the map, not invented:</b> east edge at <see cref="ShoreX"/> (−45,
    /// <c>camp_sites.LAKE_X_MAX</c>); west, north and south edges at the map boundary
    /// (<see cref="MapHalf"/> = 150, <c>camp_sites.MAP_HALF</c>) because west of the shoreline the
    /// camp terrain really is lake all the way to the edge of the world; floor at −8.0, which is
    /// 1.5 m under <c>camp_sites.BED_DEEP</c> (−6.50), the deepest bed camp authors.</para>
    ///
    /// <para>Inside the camp map this is <b>identical</b> to the old half-plane, which is why camp,
    /// the two water labs and <c>WaterSelfTest</c> are unchanged by WATER-3. What it removes is the
    /// infinite part — the ground that is not camp.</para>
    /// </summary>
    public static readonly LakeFootprint CampLake =
        LakeFootprint.Box(-MapHalf, ShoreX, -MapHalf, MapHalf, -8.0f);

    /// <summary>
    /// The bubble test's lake (world "bubbletest"), <b>derived from what GreenHills.tscn actually
    /// renders</b> rather than from the half-plane that happened to cover it.
    ///
    /// <para>The rendered surface is <c>assets/environment/bubbletest/green/lake_surface.res</c>: a
    /// 96-segment fan built by <c>tools/dev/bubbletest_bake_green.gd</c> about the GreenHills
    /// section origin, which sits at <c>BubbleTestLayout.GreenAnchor</c> (−95, 0, 0). Its radius is
    /// the y = <see cref="WaterY"/> contour of the baked bowl plus the 0.6 m the mesh is buried
    /// into the bank. <see cref="BubbleTestLakeRadiusM"/> is that measured radius; the check that
    /// keeps the two agreeing is in <c>BubbleTestSelfTest.CheckLakeBounds</c>, which reads the
    /// shipped mesh's world AABB — so a re-bake that moves the shoreline is a red test rather than
    /// a swimmable strip of nothing.</para>
    ///
    /// <para>Floor at −6.0: 2.15 m under the bake's <c>BASIN_FLOOR</c> (−3.85) and 2.0 m under
    /// <c>BubbleTestLayout.LakeBedMinY</c> (−4), so the sputter-out's 1.8 m sink from the swim line
    /// (to −3.63) still resolves inside the lake and only a body that has left the world falls out
    /// the bottom.</para>
    /// </summary>
    public static readonly LakeFootprint BubbleTestLake =
        LakeFootprint.Disc(BubbleTestLakeCentreX, BubbleTestLakeCentreZ, BubbleTestLakeRadiusM, -6.0f);

    /// <summary>
    /// <b>The bubble test's SECOND water body: the moat under the blue tower's climb</b>
    /// (EGG-2, Talon's addendum §7 — <i>"One platforming tower currently has a safe floor below
    /// its jumping section, so falling is consequence-free — too easy. Replace that floor with
    /// the same standard drowns-on-touch water used elsewhere in the level"</i>).
    ///
    /// <para><b>Why this is a second FOOTPRINT and not a second system.</b> "The same standard
    /// drowns-on-touch water" is a precise instruction: the moat has to drown through
    /// <c>DrowningClock</c> on <c>RespawnService</c>'s own scan, exactly as green's lake does,
    /// and that clock's whole predicate is <see cref="IsSubmerged"/> against a lake footprint. A
    /// trigger volume in <c>BluePrecision.tscn</c> that killed people would have been a second
    /// drowning authority with its own timing, its own edge cases and its own bugs — which is the
    /// shape of defect <c>DrowningClock</c>'s own class doc exists to argue against. One more row
    /// in this file is the whole change.</para>
    ///
    /// <para><b>The numbers, and where each comes from.</b> Plan x 63..113 and z −18..13 is the
    /// hole cut in <c>BluePrecision</c>'s ground plate (section-local x −32..18, z −18..13 against
    /// a <see cref="BubbleTestLayout"/> anchor of (95, 0, 0)), sized to hold the fall shadow of
    /// everything above the tier-1 approach — the spiral pads reach section-local x −28.2..0.0 and
    /// z −11.6..12.2, the ladder ledges z −16.8..−13.2, and the balance beam out to x +15.75.
    /// The basin's bed is authored at y −3.0, so a body on it sits 2.42 m under
    /// <see cref="WaterY"/> — comfortably past <see cref="SubmergedDepthM"/> (1.15), which is what
    /// makes the moat lethal rather than merely wet. <b>Floor at −5.0</b>: 1.5 m under the
    /// authored bed and 2.0 m under the slab's own underside, so only a body that has clipped out
    /// of the world falls out the bottom, per the rule the two footprints above already follow.
    /// </para>
    ///
    /// <para><b>It has no shore, and that is the design rather than an oversight.</b> The basin is
    /// vertical-walled: a swimmer floats at <see cref="SwimLineY"/>, 1.83 m below the apron, and
    /// <see cref="JumpAllowed"/> is false while Swimming, so there is no climbing out anywhere
    /// along it. Green's lake is the opposite by design (its bake asserts a walk-out gradient
    /// under the 32° limit). Talon's words for this one are "falling means landing in water and
    /// drowning"; the cost is a respawn at the hub and the climb again, which is the fall costing
    /// the climb — the thing §7 says is missing today.</para>
    /// </summary>
    public static readonly LakeFootprint BluePrecisionMoat =
        LakeFootprint.Box(63f, 113f, -18f, 13f, -5.0f);

    /// <summary>GreenHills' section anchor x — the centre of the round lake. Mirrors
    /// <c>BubbleTestLayout.GreenAnchor</c>, which cannot be referenced here without pointing the
    /// water contract at a level (<c>BubbleTestLayout</c> already reads this file, and the
    /// dependency runs that way round on purpose). <c>BubbleTestLayoutTests</c> fails if they
    /// drift.</summary>
    public const float BubbleTestLakeCentreX = -95f;

    /// <summary>GreenHills' section anchor z. Same mirror, same guard.</summary>
    public const float BubbleTestLakeCentreZ = 0f;

    /// <summary>
    /// Radius of the bubble test's lake, metres. <b>Measured from the shipped mesh, in engine</b>:
    /// <c>--bubbletest-selftest</c> reports <c>lake_surface.res</c>'s world AABB as
    /// <c>(−111.96, −0.58, −16.96) .. (−78.04, −0.58, 16.96)</c> — 33.92 m across on both axes,
    /// i.e. a <b>16.96 m</b> radius about (−95, 0, 0), with the surface sitting exactly on
    /// <see cref="WaterY"/>. Rounded UP to 17.0 so the swimmable disc covers every rendered
    /// triangle rather than leaving a rim of visible water you cannot swim in.
    ///
    /// <para>The 4 cm the rounding adds is bank, not water: the bake buries the mesh edge 0.6 m
    /// into the shore precisely so the boundary lands in solid ground, and terrain at that radius
    /// is above <see cref="WaterY"/>, so nothing standing there can read as submerged anyway.</para>
    /// </summary>
    public const float BubbleTestLakeRadiusM = 17.0f;

    /// <summary>
    /// <b>Which lake the world currently being played has.</b> Set once per world build by
    /// <c>Gameplay.BuildWorld</c>, on the server and on every client alike, before any avatar
    /// exists — so it is a world constant from the motor's point of view and a reconciliation
    /// replay resolves the identical depth the live step did.
    ///
    /// <para>Defaults to <see cref="CampLake"/>: every world that does not name its own lake keeps
    /// exactly the behaviour it had before WATER-3, minus the infinite part.</para>
    /// </summary>
    public static LakeFootprint ActiveLake
    {
        get => ActiveWaters.Length > 0 ? ActiveWaters[0] : CampLake;
        set => ActiveWaters = new[] { value };
    }

    /// <summary>
    /// <b>Every water body the world currently being played has.</b> One for camp; two for the
    /// bubble test since EGG-2 gave the blue tower a moat (<see cref="BluePrecisionMoat"/>).
    ///
    /// <para><b><see cref="ActiveLake"/> is kept, and kept meaningful.</b> It reads the first
    /// body and its setter installs exactly one — so every existing caller and every test that
    /// says <c>ActiveLake = X</c> gets precisely the behaviour it had before, a single named lake
    /// and nothing else. That matters more than it looks: these are process-wide statics, and a
    /// test that set one lake must not silently inherit another world's second one.</para>
    ///
    /// <para>Set once per world build by <c>Gameplay.BuildWorld</c>, on the server and on every
    /// client alike, before any avatar exists — so it is still a world constant from the motor's
    /// point of view and a reconciliation replay still resolves the identical depth the live step
    /// did.</para></summary>
    public static LakeFootprint[] ActiveWaters { get; set; } = { CampLake };

    /// <summary>The lake a world id gets — the FIRST of its bodies. Kept for callers that mean
    /// "the named lake of this world" (the bubble test's self-test measures its disc against the
    /// rendered mesh); use <see cref="WatersForWorld"/> for "everything that can drown you".
    /// </summary>
    public static LakeFootprint LakeForWorld(string? worldId) =>
        worldId == BubbleTestWorldId ? BubbleTestLake : CampLake;

    /// <summary>
    /// Every water body a world id gets. One place, so a new world — or a new pond in an existing
    /// one — is a row here rather than a silent inheritance of camp's shoreline.
    ///
    /// <para><b>Order is not arbitrary:</b> the named lake is first, because
    /// <see cref="ActiveLake"/> reads index 0 and every diagnostic that prints "the lake" means
    /// that one.</para></summary>
    public static LakeFootprint[] WatersForWorld(string? worldId) =>
        worldId == BubbleTestWorldId
            ? new[] { BubbleTestLake, BluePrecisionMoat }
            : new[] { CampLake };

    /// <summary>Mirrors <c>BubbleTestLayout.WorldId</c>, for the reason
    /// <see cref="BubbleTestLakeCentreX"/> gives: the water contract does not depend on a
    /// level.</summary>
    public const string BubbleTestWorldId = "bubbletest";

    // --- Pure functions --------------------------------------------------------------------------

    /// <summary>
    /// Submersion depth in metres at a world position: positive means that much water over the
    /// feet, non-positive means dry. Outside the lake — beyond its footprint in plan, or below its
    /// floor — the answer is always negative regardless of height, so a pit or a cellar elsewhere
    /// on the map can never read as lake, and neither can the empty air past the edge of the world:
    /// the water plane is a lake fact, not a global sea level.
    /// </summary>
    public static float DepthAt(Vector3 feetPosition)
    {
        // The DEEPEST answer any active body gives, which for disjoint bodies is simply "the one
        // you are in". Max rather than first-hit so an overlap — two ponds sharing an edge — can
        // never read as shallower than either of them, which is the direction that would silently
        // stop drowning somebody.
        float deepest = -1f;
        LakeFootprint[] waters = ActiveWaters;
        for (int i = 0; i < waters.Length; i++)
        {
            float d = DepthAt(feetPosition, waters[i]);
            if (d > deepest) deepest = d;
        }
        return deepest;
    }

    /// <summary>As <see cref="DepthAt(Vector3)"/>, against a named lake rather than the active one.
    /// This is the overload the tests use, so proving the arithmetic never has to mutate global
    /// state in a parallel xUnit run.</summary>
    public static float DepthAt(Vector3 feetPosition, in LakeFootprint lake)
        => lake.Contains(feetPosition) ? WaterY - feetPosition.Y : -1f;

    /// <summary>True if a world position is inside the lake's lateral footprint at all.</summary>
    public static bool InLakeRegion(Vector3 position)
    {
        LakeFootprint[] waters = ActiveWaters;
        for (int i = 0; i < waters.Length; i++)
            if (waters[i].ContainsPlan(position))
                return true;
        return false;
    }

    /// <summary>As <see cref="InLakeRegion(Vector3)"/>, against a named lake.</summary>
    public static bool InLakeRegion(Vector3 position, in LakeFootprint lake) => lake.ContainsPlan(position);

    /// <summary>
    /// The state machine itself (spec §4), as a total function of the previous state and the
    /// current depth. Every one of the nine (previous state × direction) cases has a defined
    /// answer, including the two that only a teleport can produce — dropped straight from Dry
    /// into deep water, and lifted straight from Swimming onto dry land.
    ///
    /// Hysteresis is asymmetric on purpose: deepening needs <c>threshold + band</c>, shallowing
    /// needs <c>threshold − band</c>. A player parked exactly on a threshold therefore keeps
    /// whatever state they arrived in and cannot strobe.
    /// </summary>
    public static WaterState Resolve(WaterState previous, float depth)
    {
        if (!float.IsFinite(depth))
            return WaterState.Dry;

        float deepenToWade = WadeDepthM + HysteresisM;   // 0.25
        float deepenToSwim = SwimDepthM + HysteresisM;   // 1.15
        float shallowToDry = WadeDepthM - HysteresisM;   // 0.15
        float shallowToWade = SwimDepthM - HysteresisM;  // 1.05

        switch (previous)
        {
            case WaterState.Dry:
                // A teleport (or a running jump off the dock) can skip Wading entirely.
                if (depth >= deepenToSwim) return WaterState.Swimming;
                if (depth >= deepenToWade) return WaterState.Wading;
                return WaterState.Dry;

            case WaterState.Wading:
                if (depth >= deepenToSwim) return WaterState.Swimming;
                if (depth <= shallowToDry) return WaterState.Dry;
                return WaterState.Wading;

            case WaterState.Swimming:
                // Lifted clean out (the shore recovery does exactly this) skips Wading.
                if (depth <= shallowToDry) return WaterState.Dry;
                if (depth <= shallowToWade) return WaterState.Wading;
                return WaterState.Swimming;

            default:
                // Unreachable through the enum, reachable through a corrupt wire byte.
                return WaterState.Dry;
        }
    }

    /// <summary>Convenience: resolve straight from a world position, against
    /// <see cref="ActiveLake"/>.</summary>
    public static WaterState ResolveAt(WaterState previous, Vector3 feetPosition) =>
        Resolve(previous, DepthAt(feetPosition));

    /// <summary>As <see cref="ResolveAt(WaterState, Vector3)"/>, against a named lake.</summary>
    public static WaterState ResolveAt(WaterState previous, Vector3 feetPosition, in LakeFootprint lake) =>
        Resolve(previous, DepthAt(feetPosition, lake));

    /// <summary>True while a body is deep enough that the drowning clock should run — see
    /// <see cref="SubmergedDepthM"/>. Named rather than inlined so the rule has one reader and one
    /// place to be tested.</summary>
    public static bool IsSubmerged(Vector3 feetPosition) => DepthAt(feetPosition) >= SubmergedDepthM;

    /// <summary>As <see cref="IsSubmerged(Vector3)"/>, against a named lake.</summary>
    public static bool IsSubmerged(Vector3 feetPosition, in LakeFootprint lake) =>
        DepthAt(feetPosition, lake) >= SubmergedDepthM;

    /// <summary>
    /// Seconds of continuous swimming to take chill from 0 to 1 at a given world x (spec §5).
    /// A pure function of position with no RNG anywhere. Always finite and always strictly
    /// positive, which is the load-bearing half of the anti-unwinnable guarantee: from any
    /// position in the lake, chill reaches 1.0 in bounded time, so the sputter-out that returns
    /// a player to the shore is always eventually reachable.
    /// </summary>
    public static float TimeToFullChillSec(float x)
    {
        if (!float.IsFinite(x))
            return TimeToFullInsideRopeSec;
        if (x > RopeX)
            return TimeToFullInsideRopeSec;
        if (x < ColdRampEndX)
            return TimeToFullFarSec;
        // Ramped across the rope-to-ramp-end span. t = 0 at RopeX, 1 at ColdRampEndX.
        float t = (RopeX - x) / (RopeX - ColdRampEndX);
        return Mathf.Lerp(TimeToFullInsideRopeSec, TimeToFullRampEndSec, Mathf.Clamp(t, 0f, 1f));
    }

    /// <summary>Chill gained per second of swimming at a given x. The reciprocal of
    /// <see cref="TimeToFullChillSec"/>, exposed because the rate is what the integrator wants
    /// and because "is this strictly positive everywhere" is the property the anti-unwinnable
    /// test asserts.</summary>
    public static float ChillRatePerSec(float x) => 1f / TimeToFullChillSec(x);

    /// <summary>
    /// The nearest point on the shoreline to a position in the lake (spec §6.3) — nearest-shore
    /// rather than where-you-entered, because that is the only rule that still means something
    /// after a mid-lake capsize and because it can never teleport a player across the map.
    ///
    /// The shoreline is the line <c>x = ShoreX</c>, so the nearest point is directly east at the
    /// same z, inset onto the bank. The Y is a fallback: the live path in
    /// <see cref="WaterService"/> raycasts the real terrain and only falls back to this when
    /// there is no physics space (a pure-logic test).
    /// </summary>
    public static Vector3 NearestShorePoint(Vector3 from)
    {
        float z = float.IsFinite(from.Z) ? Mathf.Clamp(from.Z, -MapHalf + 2f, MapHalf - 2f) : 0f;
        return new Vector3(ShoreX + ShoreInsetM, ShoreFallbackY, z);
    }

    /// <summary>The world Y the feet should converge on while swimming.</summary>
    public static float SwimLineY => WaterY - SwimSubmersionM;

    /// <summary>The combined move-speed multiplier for a state, before the Soaked penalty.</summary>
    public static float SpeedMultiplierFor(WaterState state) => state switch
    {
        WaterState.Wading => WadeSpeedMul,
        WaterState.Swimming => SwimSpeedMul,
        _ => 1f,
    };

    /// <summary>Sprint is denied in any water at all (spec §4 denies it in both Wading and
    /// Swimming). Named rather than inlined so the rule has one reader.</summary>
    public static bool SprintAllowed(WaterState state) => state == WaterState.Dry;

    /// <summary>Jump is denied only while swimming — you can still hop about in the shallows,
    /// which is most of the day-register comedy (spec §2).</summary>
    public static bool JumpAllowed(WaterState state) => state != WaterState.Swimming;
}
