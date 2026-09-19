using Godot;

namespace MpFoundation.Dev.Playground;

/// <summary>
/// <b>THE SCRAMBLE — the course you climb, and the one that is allowed to be a place.</b>
///
/// <para><b>Why a third course.</b> <see cref="CalibrationCourse"/> measures and
/// <see cref="FlowCourse"/> flows; between them they cover "does the movement do what the spec
/// says" and "does a route read". Neither asks the question Talon put next, which is whether a
/// player will climb something for no reason except that it is there:</para>
///
/// <para><i>"a literal pile of cubes arranged in different ways, different rotations, different
/// sizes... testing how well the player can climb up and grab an item at the top... I want this
/// very grand and extremely large and something that looms over the player."</i></para>
///
/// <para><b>And the criticism that produced it.</b> An earlier version of this geometry, built in
/// the shader lab, was seven zones each isolating one variable. Talon's verdict: <i>"you built this
/// playground more like a lab playground wherein the fun has sort of been taken away and replaced
/// with clinical stuffy study of what we want to find out which is what is fun like a robot."</i>
/// That is a fair description of a control group, and a control group is the correct instrument for
/// a calibration course and the wrong one for this. So this course deliberately gives up the
/// property the other two keep: nothing here isolates a variable, the placement is jittered rather
/// than measured, and there are many routes, none of them marked.</para>
///
/// <para><b>THE STAIR IS THE ONE PROMISE.</b> Everything else in the pile is a maybe — the spine can
/// dead-end, a rubble line can cliff out, and that is wanted, because a player finding their own way
/// up is the entire point. But somebody who can SEE the reward has to be able to reach it by some
/// route or the course is a tease. The spiral stair's rises are held under the jump apex the whole
/// way, so there is always a line that goes, hidden inside a pile that makes no promises.</para>
///
/// <para><b>Scale is the feature.</b> Roughly 24 m to the summit against a 1.20 m body — twenty body
/// heights. Neither other course has any vertical scale at all, and scale is most of what separates
/// "an obstacle" from "somewhere to go".</para>
/// </summary>
public partial class ScrambleCourse : MovementCourse
{
    public override string CourseName => "Scramble";

    /// <summary>On the approach plaza, far enough out that the pile is a silhouette before it is a
    /// climbing problem. Dropping the player at the foot of it wastes the one thing this course has
    /// that the others do not.</summary>
    public override Vector3 SpawnPointLocal => SpawnLocal;

    /// <summary>
    /// <b>Look at the pile (MOVE-5i).</b> MOVE-5h arrived here and found itself facing bare grey
    /// plane to the horizon: nothing carried a facing, so the camera kept whatever yaw it happened
    /// to hold, and yaw 0 points at <c>-Z</c> — away from a pile that sits north-west of this spawn.
    /// A course whose first frame is empty ground reads as a broken level, and this one has exactly
    /// one thing worth looking at.
    /// </summary>
    public override Vector3? SpawnLookAtLocal => PileCentreLocal;

    /// <summary>The spawn, as a constant the arithmetic around it can be checked against with no
    /// running engine — <c>MovementCourseSelectionTests</c> holds this and
    /// <see cref="PileCentreLocal"/> against <see cref="MovementCourse.YawTowards"/> to prove the
    /// arrival heading really does point into the pile's quadrant.</summary>
    public static readonly Vector3 SpawnLocal = new(26f, 1.2f, 22f);

    /// <summary>The pile's own axis: every generator below scatters around <c>(3, ·, 2)</c>, and the
    /// stack, the stair and the summit all sit on it. Named at eye height rather than at ground
    /// level so the arrival frame is looking <i>up</i> the thing — the looming is the course.</summary>
    public static readonly Vector3 PileCentreLocal = new(3f, 7f, 2f);

    /// <summary>Ground-to-summit. Everything else is derived from it.</summary>
    private const float PeakM = 24f;

    // --- The apron, as numbers rather than as literals inside one call --------------------------
    //
    // MOVE-5i needs the parapet to sit exactly on the apron's rim, and an apron whose floor and
    // whose wall disagree by half a metre is a strip of floor you can still walk off. One set of
    // numbers, two builders.

    private const float ApronCentreXM = 10f;
    private const float ApronCentreZM = 8f;

    /// <summary>Half the apron's side, so it spans x -29..49 and z -31..47 about the centre.</summary>
    private const float ApronHalfM = 39f;

    /// <summary>
    /// <b>How tall the apron's parapet stands (MOVE-5i).</b> Matched to <c>FlowCourse</c>'s
    /// <c>OverlookBackWall</c>, which is the only edge device either older course builds and is
    /// there for exactly this reason — "behind the spawn, so a player who turns the wrong way is
    /// told so immediately". Well over the 1.534 m measured standing-jump apex, so it is a boundary
    /// rather than a kerb to hop.
    /// </summary>
    private const float ParapetHeightM = 4.5f;

    /// <summary>Thickness of the parapet, matching <c>OverlookBackWall</c>'s. It stands <i>inside</i>
    /// the apron's rim rather than outside it, so the wall is something the apron holds up.</summary>
    private const float ParapetThicknessM = 1f;

    /// <summary>Rise per stair tread. Comfortably under the ~1.60 m jump apex so the guaranteed
    /// route survives a scuffed landing rather than needing a clean one.</summary>
    private const float TreadRiseM = 0.93f;

    /// <summary>Spacing between treads ALONG THE SPIRAL, in metres — not in degrees.
    ///
    /// <para>A constant angle step is the obvious way to build a spiral and it is wrong: at a wide
    /// radius the treads end up metres apart in a straight line, and the jump arc is long past its
    /// apex by the time it has travelled that far. Built that way in the lab, this stair's second
    /// step came out 1.49 m beyond reach while every tread looked identical. Deriving the angle from
    /// a fixed chord keeps every tread the same easy hop apart whatever the radius.</para></summary>
    private const float TreadGapM = 2.9f;

    private readonly RandomNumberGenerator _rng = new();

    protected override void Build()
    {
        // FIXED SEED. A jittered pile that re-rolls every launch is a pile nobody can file a bug
        // against — "the block near the top" has to mean the same block tomorrow. The randomness is
        // for the LOOK, not for variety between sessions.
        _rng.Seed = 20260827;

        BuildApron();
        BuildApronParapet();
        BuildSpine();
        BuildStack();
        float summitAngle = BuildStair();
        BuildRubble();
        BuildLeaningSlabs();
        BuildSummitReward(summitAngle);
    }

    /// <summary>The plaza you arrive on. Grey, flat and boring on purpose: it is the thing the pile
    /// is measured against, and a player needs somewhere to stand still and look up from.</summary>
    private void BuildApron()
    {
        Block(this, "Apron", new Vector3(ApronCentreXM, -0.5f, ApronCentreZM),
            new Vector3(ApronHalfM * 2f, 1f, ApronHalfM * 2f), Palette.Ground);
    }

    /// <summary>
    /// <b>The apron's rim, so the plaza stops being a hole (MOVE-5i).</b> MOVE-5h walked ~23 m from
    /// the spawn, straight off the apron's near edge, and fell until the harness's void floor caught
    /// them at <b>38 m/s</b> 25 m down — the same catch the capture's own tap beat records running
    /// into once, off the calibration course, before it was rewritten to stand still.
    ///
    /// <para><b>Four walls, following the precedent rather than inventing a third answer.</b> The
    /// playground is a dev harness and does not want a designed boundary, so this is not a
    /// designed one: it is <c>FlowCourse.OverlookBackWall</c>'s device — an
    /// <see cref="Palette.Obstacle"/> wall that says "not that way" in geometry — run round all four
    /// sides of the only course whose spawn is far enough out to reach an edge before it reaches
    /// anything else. The other two courses' answer at an edge is that their recovery floor is
    /// bigger than the space a player uses; this course's apron already is, and the wall is what
    /// makes the last 23 m of it land somewhere.</para>
    ///
    /// <para>The four overlap at the corners on purpose. Every other generator in this file
    /// intersects its neighbours; boxes that share a volume are free, and a mitre is not.</para>
    /// </summary>
    private void BuildApronParapet()
    {
        const float inset = ApronHalfM - ParapetThicknessM * 0.5f;
        const float span = ApronHalfM * 2f;
        float y = ParapetHeightM * 0.5f;

        Block(this, "ParapetNorth", new Vector3(ApronCentreXM, y, ApronCentreZM - inset),
            new Vector3(span, ParapetHeightM, ParapetThicknessM), Palette.Obstacle);
        Block(this, "ParapetSouth", new Vector3(ApronCentreXM, y, ApronCentreZM + inset),
            new Vector3(span, ParapetHeightM, ParapetThicknessM), Palette.Obstacle);
        Block(this, "ParapetWest", new Vector3(ApronCentreXM - inset, y, ApronCentreZM),
            new Vector3(ParapetThicknessM, ParapetHeightM, span), Palette.Obstacle);
        Block(this, "ParapetEast", new Vector3(ApronCentreXM + inset, y, ApronCentreZM),
            new Vector3(ParapetThicknessM, ParapetHeightM, span), Palette.Obstacle);
    }

    /// <summary>
    /// The spine — a fallen wall, scrambled ALONG rather than climbed. Big blocks, low, wandering
    /// across the footprint at a lazy diagonal, each leaning into the next.
    /// </summary>
    private void BuildSpine()
    {
        var cursor = new Vector3(-13f, 0f, -9f);
        float heading = 0.9f;

        for (int i = 0; i < 15; i++)
        {
            float t = i / 14f;
            float w = _rng.RandfRange(2.6f, 5.2f) * Mathf.Lerp(1.15f, 0.72f, t);
            float h = _rng.RandfRange(1.6f, 3.4f);

            var body = Block(this, $"SpineBlock{i:00}", cursor + new Vector3(0f, h * 0.5f, 0f),
                new Vector3(w, h, w * _rng.RandfRange(0.7f, 1.35f)),
                i % 3 == 0 ? Palette.Platform : Palette.Obstacle);
            body.Rotation = new Vector3(_rng.RandfRange(-0.22f, 0.22f),
                heading + _rng.RandfRange(-0.5f, 0.5f), _rng.RandfRange(-0.20f, 0.20f));

            heading += _rng.RandfRange(-0.42f, 0.42f);
            cursor += new Vector3(Mathf.Sin(heading), 0f, -Mathf.Cos(heading))
                * (w * _rng.RandfRange(0.62f, 0.95f));
        }
    }

    /// <summary>
    /// The stack — the vertical mass, and the thing that actually looms. Blocks shrink with height
    /// and lean further off true the higher they go, so the silhouette tapers and the top reads as
    /// precarious.
    ///
    /// <para>They INTERSECT deliberately. Overlapping boxes throw out ledges, notches and chimneys
    /// at every scale, and not one of them had to be authored — which is the cheapest way there is
    /// to get geometry that does not feel designed.</para>
    /// </summary>
    private void BuildStack()
    {
        for (int i = 0; i < 20; i++)
        {
            float t = i / 19f;
            float y = 1.4f + t * (PeakM - 3f);
            float w = Mathf.Lerp(6.4f, 2.2f, t) * _rng.RandfRange(0.78f, 1.18f);
            float lean = 0.10f + t * 0.30f;
            var off = new Vector3(_rng.RandfRange(-1f, 1f), 0f, _rng.RandfRange(-1f, 1f))
                * (2.6f + t * 2.2f);

            var body = Block(this, $"StackBlock{i:00}", new Vector3(3f, y, 2f) + off,
                new Vector3(w, w * _rng.RandfRange(0.55f, 1.05f), w * _rng.RandfRange(0.7f, 1.2f)),
                i % 4 == 1 ? Palette.Obstacle : Palette.Platform);
            body.Rotation = new Vector3(_rng.RandfRange(-lean, lean),
                _rng.RandfRange(0f, Mathf.Tau), _rng.RandfRange(-lean, lean));
        }
    }

    /// <summary>
    /// The stair — the guaranteed route, and <b>the only thing in this course wearing the accent
    /// colour</b>. Returns the angle the spiral finished at, so the reward can sit over the last
    /// tread rather than over a recomputed guess that drifts the moment the spacing changes.
    /// </summary>
    private float BuildStair()
    {
        float angle = 0.55f;

        for (int i = 0; i < 23; i++)
        {
            float radius = Mathf.Lerp(13.5f, 3.4f, i / 22f);
            angle += TreadGapM / Mathf.Max(radius, 1f);
            float y = 0.9f + i * TreadRiseM;

            var body = Block(this, $"Tread{i:00}",
                new Vector3(3f + Mathf.Cos(angle) * radius, y, 2f + Mathf.Sin(angle) * radius),
                new Vector3(_rng.RandfRange(2.8f, 3.8f), 1.1f, _rng.RandfRange(2.8f, 3.8f)),
                Palette.Route);
            body.Rotation = new Vector3(_rng.RandfRange(-0.10f, 0.10f),
                angle + _rng.RandfRange(-0.3f, 0.3f), _rng.RandfRange(-0.10f, 0.10f));
            body.SetMeta("tread", i);
        }

        return angle;
    }

    /// <summary>
    /// The rubble — what makes this a tangle instead of three sculptures.
    ///
    /// <para>Scattered without a plan, at every size, at every angle, through the whole volume. Some
    /// lands against the stack and becomes a hold; some jams two blocks apart and becomes a tunnel;
    /// some is just a rock. <b>The point is that nobody decided which.</b> Places that were designed
    /// read as designed, and a player can feel the difference between a route they were handed and
    /// one they found.</para>
    /// </summary>
    private void BuildRubble()
    {
        for (int i = 0; i < 46; i++)
        {
            float d = _rng.RandfRange(0f, 15.5f);
            float a = _rng.RandfRange(0f, Mathf.Tau);
            // Weighted low: a pile is widest at the bottom, and rubble at head height is where the
            // scrambling actually happens.
            float y = Mathf.Pow(_rng.Randf(), 1.9f) * (PeakM * 0.78f) + 0.6f;
            float w = _rng.RandfRange(1.1f, 4.6f);

            var body = Block(this, $"Rubble{i:00}",
                new Vector3(3f + Mathf.Cos(a) * d, y, 2f + Mathf.Sin(a) * d),
                new Vector3(w, w * _rng.RandfRange(0.5f, 1.3f), w * _rng.RandfRange(0.6f, 1.4f)),
                (i % 3) switch { 0 => Palette.Platform, 1 => Palette.Obstacle, _ => Palette.Ground });
            body.Rotation = new Vector3(_rng.RandfRange(0f, Mathf.Tau),
                _rng.RandfRange(0f, Mathf.Tau), _rng.RandfRange(0f, Mathf.Tau));
        }
    }

    /// <summary>Three enormous slabs canted over the approach. Pure theatre, and worth it: without
    /// something huge overhead the pile reads as tall rather than as looming, and looming is the
    /// thing this course has that the other two do not.</summary>
    private void BuildLeaningSlabs()
    {
        for (int i = 0; i < 3; i++)
        {
            float a = 2.1f + i * 1.9f;
            var body = Block(this, $"Slab{i}",
                new Vector3(3f + Mathf.Cos(a) * 10f, 5.5f + i * 3.4f, 2f + Mathf.Sin(a) * 10f),
                new Vector3(_rng.RandfRange(6.5f, 9.5f), _rng.RandfRange(1.4f, 2.4f),
                    _rng.RandfRange(4.5f, 7f)), Palette.Obstacle);
            body.Rotation = new Vector3(_rng.RandfRange(-0.5f, -0.2f), a,
                _rng.RandfRange(-0.35f, 0.35f));
        }
    }

    /// <summary>
    /// The reward: a floating, wobbling bubble at the summit, and a crystal cluster under it.
    ///
    /// <para><b>The bubble wobbles on its own.</b> <c>bubble_film</c> displaces its own surface and
    /// reads its radius out of <c>MODEL_MATRIX</c> to decide how much — below its onset it is a hard
    /// little bead, above its full threshold it deforms completely. At 1.15 m this one is far past
    /// that, so there is nothing to author. What the animation adds is the thing the shader cannot
    /// know: that the whole object is FLOATING rather than sitting on a shelf.</para>
    ///
    /// <para><b>An AnimationPlayer, not <c>_Process</c>.</b> One less behaviour to think about when
    /// this course is dropped somewhere else, and it keeps the drift as data rather than code.</para>
    /// </summary>
    private void BuildSummitReward(float summitAngle)
    {
        var at = new Vector3(3f + Mathf.Cos(summitAngle) * 3.4f,
            0.9f + 23f * TreadRiseM + 2.6f, 2f + Mathf.Sin(summitAngle) * 3.4f);

        var bubble = IridescentProps.Bubble(this, "SummitBubble", at, 1.15f);
        IridescentProps.Float(this, bubble.Name, at);

        // A crystal cluster on the last tread, so the summit is somewhere rather than a spot.
        for (int i = 0; i < 3; i++)
        {
            float a = summitAngle + 1.4f + i * 2.1f;
            IridescentProps.Crystal(this, $"SummitCrystal{i}",
                at + new Vector3(Mathf.Cos(a) * 1.7f, -2.2f, Mathf.Sin(a) * 1.7f),
                _rng.RandfRange(0.35f, 0.55f), _rng);
        }
    }
}
