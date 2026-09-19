using Godot;

namespace MpFoundation.Dev.Playground;

/// <summary>
/// <b>The playable half: is there any fun in here?</b> Not a measurement rig — a place to move
/// through, with lines to read, momentum to keep and things to try that nobody specified.
///
/// <para><b>The route — "The Ridge Run", one lap.</b> You start high on the Overlook, sprint off
/// its front edge, and everything after that is downhill and forward until the tower. Drop-In:
/// three descending slabs whose gaps grow (3.6 m free, 5.0 m and 5.6 m sprint-only) — falling
/// buys airtime, so speed you brought off the deck is speed that clears them. Weave: a wide
/// straight with four posts down the middle; threading them is shorter than going round, and it
/// is the only place on the lap that asks you to turn at full speed instead of jump. Turn Deck:
/// the corner, taken fast, onto the Gauntlet — two 2.6 m pads with 4.6 m gaps and a 3.6 m lateral
/// stagger, too short to rebuild sprint on, so the chain either survives or it does not. The Leap
/// off the second pad is 4.2 m out and 1.2 m up: sprint reaches 4.7 m, jog reaches 3.0 m, and
/// that is the whole point of the course. Then the tower spirals up six ledges to the Summit at
/// 10.8 m, where you can look back down the entire lap. Home is the Ridge: a descending run of
/// slabs at the north edge, with the Lip in it, and a long causeway back onto the Overlook.</para>
///
/// <para><b>Two lines, deliberately.</b> The Gauntlet and the Leap are the fast line to the
/// tower's fourth ledge; the Safe Line causeway walks to the tower's foot and takes the spiral
/// from the bottom. Both arrive. Only one of them is worth bragging about.</para>
///
/// <para><b>The overshoot beat is the Ridge Lip</b> (marked "EASE OFF"): a 2.2 m pad 3.8 m out
/// from a descending 6 m run. A held sprint jump travels about 6.5 m from there and sails clean
/// over it; the answers are to cut the jump short or to lean on the air brake, because the brake
/// sheds some speed and never all of it. Overshooting costs you the ridge and a climb back up
/// the west stair — it does not cost you the session.</para>
///
/// <para><b>Numbers this is built to</b> (measured off the shipped motor, MOVE-3): held sprint
/// jump 6.192 m flat, apex 1.534 m, airtime 0.717 s; jog tap 1.710 m; sprint 8.64 m/s, jog
/// 5.40 m/s. Landing costs no horizontal speed, so a chain keeps what it started with, and the
/// air can never give you speed the ground did not. Gaps are therefore sized in three bands:
/// under 3.8 m is free at jog, 4.0–6.0 m is sprint-only, over 6.2 m is a bug report.</para>
///
/// <para><b>MOVE-8 (2026-08-28): the paragraph above is STALE and the geometry with it.</b>
/// Talon's ruling moved <c>MotorTuning.Default</c>; the arc is now held sprint 4.053 m flat /
/// apex 1.407 m / airtime 0.667 s, jog tap 0.887 m, sprint 6.08 m/s, jog 3.80 m/s, and a
/// traditional double jump reaches 2.410 m / 6.485 m. Live values come from
/// <see cref="MpFoundation.Net.MotorArc"/>, which is where any new consumer reads them. The three
/// gap bands above therefore no longer describe this course: what was "free at jog" is now at or
/// past a sprint jump. Not re-spaced here — re-spacing greybox geometry is outside MOVE-8's
/// scope — but marked rather than left silent, per MOVE-8 scope item 4.</para>
///
/// <para><b>Recovery.</b> A floor runs under the whole lap and four stairs climb off it — beside
/// the Overlook, into the Turn Deck, up the tower's south face, and up to the causeway. Falling
/// is a detour, never a walk of shame.</para>
///
/// <para>Every standable and blocking surface in here goes through
/// <see cref="MovementCourse.Block"/>, which builds mesh, body and collider together. Nothing is
/// hand-rolled and nothing is visual-only.</para>
/// </summary>
public partial class FlowCourse : MovementCourse
{
    public override string CourseName => "Flow";

    /// <summary>On the Overlook, eight metres back from its front edge, with a wall close behind.
    /// Facing down-course you are looking at the green first slab and the whole descending line
    /// past it; facing the other way you are looking at a wall from four metres, which is the
    /// cheapest possible way to say "that way".</summary>
    public override Vector3 SpawnPointLocal => new(0f, 7.2f, 12f);

    protected override void Build()
    {
        BuildFloor();
        BuildOverlook();
        BuildDropIn();
        BuildWeave();
        BuildGauntlet();
        BuildSafeLine();
        BuildTower();
        BuildReturnRidge();
        BuildCauseway();
        BuildRecoveryStairs();
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>A horizontal slab specified by the height of its <i>top</i> surface, because that
    /// is the only number a jump cares about. Delegates to <see cref="MovementCourse.Block"/>.</summary>
    private StaticBody3D Slab(string name, float cx, float cz, float sx, float sz, float topY,
        Color colour, float thickness = 0.8f)
        => Block(this, name, new Vector3(cx, topY - thickness * 0.5f, cz),
            new Vector3(sx, thickness, sz), colour);

    /// <summary>A post to weave around rather than clear. Sits on the surface at
    /// <paramref name="baseY"/>.</summary>
    private StaticBody3D Post(string name, float cx, float cz, float baseY, float height,
        float side = 1.6f)
        => Block(this, name, new Vector3(cx, baseY + height * 0.5f, cz),
            new Vector3(side, height, side), Palette.Obstacle);

    /// <summary>One solid step of a recovery stair: a box from just above the floor up to
    /// <paramref name="topY"/>, so steps abut rather than stack.</summary>
    private StaticBody3D Riser(string name, float cx, float cz, float sx, float sz, float topY)
    {
        const float floorTop = 0.02f;
        float height = topY - floorTop;
        return Block(this, name, new Vector3(cx, floorTop + height * 0.5f, cz),
            new Vector3(sx, height, sz), Palette.Obstacle);
    }

    private void Sign(string text, float x, float y, float z, float size = 0.6f)
        => Marker(this, text, new Vector3(x, y, z), size);

    // ------------------------------------------------------------------ floor

    /// <summary>Catches every fall. Its top sits 2 cm above zero rather than exactly on it, so if
    /// the harness scene ever grows a ground plane of its own the two do not z-fight; 2 cm is not
    /// a step anything can trip on.</summary>
    private void BuildFloor()
        => Slab("RecoveryFloor", 16f, -15.5f, 74f, 69f, 0.02f, Palette.Ground, 1f);

    // --------------------------------------------------------------- the start

    private void BuildOverlook()
    {
        Block(this, "OverlookPlinth", new Vector3(0f, 3.11f, 10f), new Vector3(10f, 6.18f, 10f),
            Palette.Obstacle);
        Slab("OverlookDeck", 0f, 10f, 12f, 12f, 7f, Palette.Platform);

        // Behind the spawn, so a player who turns the wrong way is told so immediately.
        Block(this, "OverlookBackWall", new Vector3(0f, 9.25f, 15.5f), new Vector3(12f, 4.5f, 1f),
            Palette.Obstacle);

        Sign("RIDGE RUN", 0f, 10f, 15f, 0.9f);
        Sign("SPRINT OFF THIS EDGE", 0f, 8.4f, 5f, 0.7f);
    }

    // ------------------------------------------------------------- the drop-in

    /// <summary>Three descending slabs, gaps 3.6 / 5.0 / 5.6 m with a 1.4 m drop on each. The drop
    /// buys about 0.11 s of extra airtime, which is what turns a 6.19 m flat sprint jump into a
    /// 7.14 m one — so the ladder escalates past jog range (4.47 m with the same assist) while
    /// staying inside sprint range the whole way down. The first one is free on purpose: the
    /// opening three seconds of a session should be a landing, not a fall.</summary>
    private void BuildDropIn()
    {
        Slab("DropIn1", 0f, -2.1f, 10f, 5f, 5.6f, Palette.Reachable);
        Slab("DropIn2", 0f, -12.1f, 10f, 5f, 4.2f, Palette.Platform);
        Slab("DropIn3", 0f, -22.7f, 10f, 5f, 2.8f, Palette.Platform);

        Sign("COMMIT", 0f, 5.4f, -12.1f);
        Sign("KEEP IT", 0f, 4f, -22.7f);
    }

    // --------------------------------------------------------------- the weave

    /// <summary>The one stretch that asks for a turn instead of a jump. Threading the posts is the
    /// short way through; running the outside is free and costs you three or four metres of the
    /// run-up the Gauntlet is about to want.</summary>
    private void BuildWeave()
    {
        Slab("WeaveRun", 0f, -32.2f, 16f, 14f, 2.8f, Palette.Platform);
        Post("WeavePost1", -3f, -27.5f, 2.8f, 3.2f);
        Post("WeavePost2", 3f, -31f, 2.8f, 3.2f);
        Post("WeavePost3", -3f, -34.5f, 2.8f, 3.2f);
        Post("WeavePost4", 3f, -38f, 2.8f, 3.2f);

        Sign("WEAVE - KEEP THE SPEED", 0f, 4.8f, -26f, 0.7f);

        // The corner. Wide enough to take at sprint, and the only way onto the Gauntlet.
        Slab("TurnDeck", 12f, -32.2f, 8f, 14f, 2.8f, Palette.Platform);
    }

    // ------------------------------------------------------ the momentum chain

    /// <summary>Two pads, 2.6 and 3.0 m deep, 4.6 m apart with a 3.6 m lateral stagger — 5.84 m of
    /// true diagonal, inside a sprint's 6.19 m and well outside a jog's 3.87 m. A pad that short is
    /// crossed in three tenths of a second, which is nowhere near enough ground to rebuild sprint
    /// on: a standing start over 3.0 m reaches about 7.3 m/s, and 7.3 m/s clears neither the next
    /// gap nor The Leap off the end. Arrive fast or do not arrive.</summary>
    private void BuildGauntlet()
    {
        Slab("Gauntlet1", 21.9f, -34f, 2.6f, 5f, 2.8f, Palette.Platform);
        Slab("Gauntlet2", 29.3f, -30.4f, 3f, 5f, 2.8f, Palette.Platform);

        Sign("NO BRAKES", 18f, 4.4f, -32.2f, 0.7f);
    }

    /// <summary>The other way to the tower, for a player who is not landing the Gauntlet yet or
    /// who simply wants to look at the thing. Continuous, flat, and slow — it arrives at the
    /// bottom of the spiral instead of a third of the way up it.</summary>
    private void BuildSafeLine()
    {
        Slab("SafeCauseway", 26f, -22.7f, 22f, 5f, 2.8f, Palette.Platform);
        Slab("SafeSpur", 35.5f, -26.6f, 3f, 2.8f, 2.8f, Palette.Platform);

        Sign("SAFE LINE", 20f, 4.4f, -22.7f);
    }

    // --------------------------------------------------------------- the tower

    /// <summary>Six ledges wrapping a solid core, each 1.2 m above the last — inside the 1.534 m
    /// apex, so the spiral climbs at any speed and never gates on a run-up. The fast line skips
    /// the first two: The Leap lands on Ledge1 at 4.0 m, 4.2 m out and 1.2 m up from the second
    /// Gauntlet pad. Rising eats distance, so sprint reaches 4.7 m there and jog reaches 3.0 m;
    /// it is the one jump on the lap that a full sprint is strictly required for, and it is
    /// reachable only by a Gauntlet chain that never stopped. Overshooting it is harmless — the
    /// core wall is what you hit.</summary>
    private void BuildTower()
    {
        Slab("Leap_Ledge1", 36.5f, -32f, 3f, 8f, 4f, Palette.Reachable);
        Sign("THE LEAP", 32.9f, 5.6f, -30.4f, 0.8f);

        Block(this, "TowerCore", new Vector3(42f, 5.01f, -32f), new Vector3(8f, 9.98f, 8f),
            Palette.Obstacle);

        Slab("Ledge2", 40.5f, -37.5f, 11f, 3f, 5.2f, Palette.Platform);
        Slab("Ledge3", 47.5f, -33.5f, 3f, 11f, 6.4f, Palette.Platform);
        Slab("Ledge4", 42f, -26.5f, 14f, 3f, 7.6f, Palette.Platform);
        Slab("Ledge5", 33.5f, -30.5f, 3f, 11f, 8.8f, Palette.Platform);
        Slab("Ledge6", 40.5f, -39f, 17f, 6f, 10f, Palette.Platform);
        Slab("Summit", 42f, -32f, 8f, 8f, 10.8f, Palette.Reachable);

        Sign("SPIRAL UP", 36.5f, 5.2f, -34f);
        Sign("SUMMIT - LOOK BACK", 42f, 12.2f, -32f, 0.9f);
    }

    // ---------------------------------------------------------- the way home

    /// <summary>A descending run along the north edge, wide and fast — the lap's victory lap, with
    /// the whole course to your right and eight metres of air under it. One thing in it bites.
    ///
    /// <para><b>The Lip.</b> A 2.2 m pad, 3.8 m out from a 6 m run that is dropping 0.6 m. Held to
    /// full apex at sprint that jump travels roughly 6.5 m and lands a metre past the far edge.
    /// The air brake is 30% of ground deceleration — 6.3 m/s², enough to shed about 1.8 m of that
    /// over the flight and no more — so braking alone lands you on it with nothing spare, and
    /// cutting the jump early is the cleaner answer. It is the only place on the lap where more
    /// speed is the wrong call, which is the point of putting it here rather than early.</para></summary>
    private void BuildReturnRidge()
    {
        Slab("Ridge1", 38f, -45f, 10f, 5f, 10f, Palette.Platform);
        Slab("Ridge2", 26.1f, -45f, 7f, 5f, 9.4f, Palette.Platform);
        Slab("Ridge3", 15f, -45f, 6f, 5f, 8.8f, Palette.Platform);
        Slab("RidgeLip", 7.1f, -45f, 2.2f, 5f, 8.2f, Palette.Reachable);
        Slab("Ridge5", -0.8f, -45f, 5.6f, 5f, 7.6f, Palette.Platform);

        Sign("RIDGE HOME", 38f, 11.4f, -45f, 0.8f);
        Sign("EASE OFF", 7.1f, 9.6f, -45f, 0.7f);
    }

    /// <summary>The last stretch, and it opens with a sting: 4.4 m across and 0.8 m up off the end
    /// of the ridge, which sprint makes at 5.3 m and jog misses at 3.3 m. After that it is a
    /// long high straight with three posts in it, dropping gently onto the Overlook so the lap
    /// closes where it started and the next one can begin without a single step backwards.</summary>
    private void BuildCauseway()
    {
        Slab("Causeway1", -12f, -35f, 8f, 20f, 8.4f, Palette.Platform);
        Slab("Causeway2", -12f, -15f, 8f, 20f, 8f, Palette.Platform);
        Slab("Causeway3", -12f, 3f, 8f, 16f, 7.6f, Palette.Platform);
        Slab("ReturnBridge", -7f, 8f, 2f, 6f, 7f, Palette.Reachable);

        Post("CausewayPost1", -14f, -20f, 8f, 2.8f, 1.4f);
        Post("CausewayPost2", -10f, -12f, 8f, 2.8f, 1.4f);
        Post("CausewayPost3", -14f, 0f, 7.6f, 2.8f, 1.4f);

        Sign("HOME", -12f, 9.2f, 6f, 0.8f);
    }

    // ------------------------------------------------------------- recovery

    /// <summary>Four stairs off the recovery floor, one per stretch of the lap, so no fall is more
    /// than a short jog from a way back up. Deliberately dull: they are plumbing, not a line.</summary>
    private void BuildRecoveryStairs()
    {
        // Beside the Overlook, up to the deck.
        Riser("StairA1", 9f, -0.5f, 4f, 3f, 1.2f);
        Riser("StairA2", 9f, 2.5f, 4f, 3f, 2.4f);
        Riser("StairA3", 9f, 5.5f, 4f, 3f, 3.6f);
        Riser("StairA4", 9f, 8.5f, 4f, 3f, 4.8f);
        Riser("StairA5", 9f, 11.5f, 4f, 3f, 6f);
        Riser("StairA6", 8.5f, 14.5f, 5f, 3f, 7f);

        // Into the Turn Deck, for a fall out of the weave or the Gauntlet's first gap.
        Riser("StairC1", 11f, -22.4f, 6f, 2.8f, 1.4f);
        Riser("StairC2", 11f, -24.5f, 6f, 1.4f, 2.8f);

        // Up the tower's south face, onto Ledge4.
        Riser("StairB1", 43f, -14.2f, 4f, 2.4f, 1.28f);
        Riser("StairB2", 43f, -16.6f, 4f, 2.4f, 2.56f);
        Riser("StairB3", 43f, -19f, 4f, 2.4f, 3.84f);
        Riser("StairB4", 43f, -21.4f, 4f, 2.4f, 5.12f);
        Riser("StairB5", 43f, -23.8f, 4f, 2.4f, 6.4f);

        // Up to the causeway, for a fall off the Lip.
        Riser("StairD1", -18f, -35f, 4f, 2f, 1.4f);
        Riser("StairD2", -18f, -37f, 4f, 2f, 2.8f);
        Riser("StairD3", -18f, -39f, 4f, 2f, 4.2f);
        Riser("StairD4", -18f, -41f, 4f, 2f, 5.6f);
        Riser("StairD5", -18f, -43f, 4f, 2f, 7f);
        Riser("StairD6", -18f, -45f, 4f, 2f, 8.4f);
    }
}
