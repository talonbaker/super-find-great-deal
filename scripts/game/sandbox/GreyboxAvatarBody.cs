using System.Collections.Generic;
using Godot;

namespace MpFoundation.Game.Sandbox;

/// <summary>
/// <b>The greybox player, built from code: the same gumdrop the authored asset is.</b> No
/// <c>.glb</c>, no import, no asset directory, nothing for the pipeline to validate — the same
/// approach a since-removed code-built creature body already took.
///
/// <para><b>Two live jobs, and neither is "the character".</b> Since INTEG-1 the body a player
/// is has not come out of this file. <b>BODY-1 (2026-08-28) moved it again:</b> it was
/// <c>assets/creatures/greybox/Greybox.glb</c> — the gumdrop — and that file is now DELETED. A
/// player is <see cref="AvatarVisual.ClassicGreyboxAvatarKey"/>, which index 0 of the roster and
/// <see cref="AvatarVisual.PreferredAvatarKey"/> now both resolve to. What is left for this file is
/// (1) the load-failure fallback — <see cref="AvatarVisual.PrimitiveFallbackAvatarKey"/>, served
/// when ANY roster model fails to resolve — and (2) the engine-free fixture the xUnit suite reads
/// constants out of in a host with no Godot runtime in it. Both jobs are about <i>continuity</i>:
/// a failed load should degrade a player's body, never swap them for a different character.</para>
///
/// <para><b>WHAT AVATAR-5 CHANGED, AND WHY.</b> This body used to be RIG-1's box figure: a head
/// frustum on two trunk frustums with square-section limbs. The authored asset stopped being that
/// shape three iterations ago and Talon approved what it became — <i>"I love them"</i> — so the
/// fallback was rendering a <b>different character</b>, which made the picker's side-by-side rows
/// meaningless and would have made a load failure read as a stranger rather than as a downgrade.
/// Every dimension below is now the shipped asset's, taken from its own dimension block
/// (<c>assets/creatures/greybox/build_greybox.py</c>). <b>This file chooses no proportions</b>;
/// it mirrors them, and when that block moves this one follows it.</para>
///
/// <para><b>ONE PROFILE, REVOLVED — there is no head sphere and no waist pinch.</b> The trunk is a
/// single generatrix sampled from the flat underside to the crown: a quarter-circle fillet off the
/// underside disc, an optional straight flare, a near-vertical shoulder, and a sphere cap for the
/// crown. Every section meets the next with a vertical tangent, so the surface carries no crease
/// and <b>no step anywhere</b>. <c>Hips</c>, <c>Torso</c> and <c>Head</c> are three <i>slices</i> of
/// that one surface, cut at the joint heights the rig bends about — which is why
/// <c>AvatarVisual</c> declares this body's head <b>not</b> a distinct volume and does not paint a
/// value break across it (see <c>RosterRow.HeadIsDistinctVolume</c>).</para>
///
/// <para><b>What is approximated, deliberately.</b> A fallback has to be cheap and legible in code,
/// not byte-equal to a Blender export. Three simplifications, all silhouette-neutral: the limbs'
/// linear tapers carry no intermediate rings (with flat shading they would add triangles and
/// nothing else), the eyes are Godot's own <see cref="SphereMesh"/> rather than a hand-revolved
/// ball, and normals are computed per face rather than exported per corner. The <i>profile
/// itself</i> is not approximated — <see cref="TrunkRadius"/> is the asset's generatrix, section for
/// section, because a curve that only nearly matches is a second character with extra steps.</para>
///
/// <para><b>How it reaches the rig.</b> It emits the named parts <see cref="AvatarVisual"/>
/// harvests, at their authored placements, in the <b>rig's own space</b> (-Z forward, +X right,
/// y = 0 is the ground it stands on). The harvest path then consumes it byte-for-byte the way it
/// consumes a harvested creature: same reparents, same player tint, same rest-pose measurement. Nothing
/// downstream — not <see cref="AvatarProportions"/>, not the face mixer — knows a model was not
/// parsed from a file. That is the whole bet of this file: <i>satisfy the contract and the motion
/// comes for free.</i></para>
///
/// <para><b>NODE ORIGINS ARE JOINTS, and on a rig with sub-segments that is load-bearing.</b> Each
/// part that rotates is authored with its origin AT the joint it pivots about and its geometry
/// hanging off in local space — hip for the thigh, knee for the shin, shoulder for the upper arm,
/// elbow for the forearm, neck for the head. <c>AvatarVisual.TakePart</c> reparents with
/// <c>keepGlobalTransform</c>, so a shin harvested under its own thigh arrives with a local
/// position of exactly <c>(0, -thighLength, 0)</c> and rotates about the knee with no arithmetic at
/// the call site. A sub-segment authored centred on its own mesh would swing from its middle and
/// read as a floating stick.</para>
///
/// <para><b>No Z sign is mirrored, and this is settled by looking</b> (AVATAR-1, AVATAR-5). The
/// asset's glTF faces its face at +Z and <c>AvatarVisual.MirrorAboutY</c> negates its node
/// positions on import; this body has no exporter behind it and is authored directly in the rig's
/// space, so its face sits at <b>-Z</b> and its left limbs at <b>-X</b>. The two bodies agree in
/// engine — held items sit in front of both.</para>
///
/// <para><b>Absence is DECLARED, not discovered.</b> Eight of the harvested parts (Belly, Tail,
/// BackFiller, BlushL/R, Stem, LeafL/R) are things this body genuinely does not have. They are
/// emitted anyway, as <see cref="MeshInstance3D"/> nodes carrying no mesh — which is exactly
/// what <c>AvatarVisual.TakePart</c> substitutes for a part it cannot find, minus the
/// <c>GD.PushError</c>. That error is correct for a <c>.glb</c>, where a missing part means a
/// broken export; it is wrong here, where the part list is authored twenty lines up. Stating the
/// absence in the geometry keeps the distinction where it belongs — in this file, which knows
/// the answer — instead of silencing the error globally for every model in the roster.</para>
///
/// <para><b>Lit, not unshaded, and no emission.</b> The player takes real light like the whole-figure
/// bodies did. A creature body may carry a small emission so an undodgeable attack cannot come
/// out of pure black; a player has no such argument, and a self-lit player would quietly delete
/// the darkness canon fact 4 makes the game's central pressure. (Standing trap, recorded because
/// it has cost this repo a session: Godot's <i>unshaded</i> shading mode ignores EMISSION
/// entirely — anything glowing must write ALBEDO. Nothing here is unshaded, so it does not bite,
/// but the next person to reach for a glow should know.)</para>
/// </summary>
public static class GreyboxAvatarBody
{
    // --- The dimensions, and where they come from ---------------------------------------------
    //
    // EVERY NUMBER IN THIS SECTION IS THE AUTHORED ASSET'S. The source of truth is the dimension
    // block at the top of assets/creatures/greybox/build_greybox.py, which is what Talon reads and
    // edits; this file mirrors it so the fallback and the file stay the same character. A
    // divergence between them should be a VISIBLE difference in the picker's side-by-side rows
    // (AvatarVisual.RosterEntries carries "greybox" and "greybox_primitive" next to each other for
    // exactly that reason) rather than a silent one.
    //
    // HEIGHT IS THE ONE NUMBER WITH REACH BEYOND EITHER BODY. AvatarProportions derives the
    // capsule, the eyeline, the aim ray, the carry and stow anchors, the nameplate and the camera
    // focus from the MEASURED bounds of whatever is on screen — and CATCH-1 derived the
    // floating-creature heights from those same proportions, so a cast change could not silently put
    // the catchable creatures out of reach. Nothing below is fed to AvatarProportions as a literal:
    // it measures the vertices this file emits.

    /// <summary>Ground to the crown, metres. <b>PINNED</b>, on both bodies: the collision capsule,
    /// the aim ray, the camera framing, the nameplate and the float heights all follow it, and
    /// <c>GreyboxAssetContractTests</c> fails if the shipped <c>.glb</c> ever drifts from it.
    ///
    /// <para>1.20 m is a decision rather than a measurement: it sits inside the read of the whole-figure
    /// blockout that was the player before GREY-1 (crown 1.241 m), so framing, collision and reach
    /// Talon has already played against do not jump; and it stays a stylised child rather than a
    /// person — a real 8-11 year old is 1.30-1.45 m and the house style is stubby (the original
    /// blob crown was 0.93 m).</para></summary>
    public const float CrownM = 1.20f;

    /// <summary><b>Where this body's eyes are, and therefore where its aim ray leaves from</b> —
    /// 1.04 m. <b>PINNED</b>, and no shape iteration has moved it.
    ///
    /// <para><b>Public because the swing hoop is centred on it and floating-creature geometry has
    /// to be checked against it</b> (CONV-2, 2026-08-16). <c>AvatarProportions.PlayerEyeHeightM</c>
    /// is 0.995 and describes a since-removed whole-figure blockout, which is no longer
    /// <see cref="AvatarVisual.PreferredAvatarKey"/> — so anything reasoning about where a level
    /// swing actually sweeps must read THIS, not that. Exposed as a measurement, not as a knob:
    /// nothing writes it, and it moves only when the greybox's eyes do.</para></summary>
    public const float EyeY = 1.04f;

    // --- The joints. Each is the world height of a pivot; the part that rotates about it is
    //     authored with its origin there and its geometry hanging off in local space.

    /// <summary>Where the thighs hang from. Above <see cref="BodyHemY"/>, so the hip joint is
    /// inside the trunk and the leg emerges from under the body rather than beside it.</summary>
    private const float HipJointY = 0.548f;

    /// <summary>Thigh/shin split — half way down the leg, which is what makes the knee legible as
    /// a knee rather than as a kink.</summary>
    private const float KneeJointY = 0.274f;

    /// <summary>THE NODE SEAM between <c>Hips</c> and <c>Torso</c>, and the pivot the whole upper
    /// body bends about. See <see cref="WaistY"/>.</summary>
    private const float WaistJointY = 0.532f;

    /// <summary>THE NODE SEAM between <c>Torso</c> and <c>Head</c>, and the <c>Head</c> node's
    /// origin. It is a <i>rig</i> height, not a shape feature: the surface does not change here
    /// (the dome's own equator is 6 mm above it, at <see cref="DomeEquatorY"/>) and nothing in the
    /// silhouette marks it. That is precisely why <c>AvatarVisual</c> must not paint a value break
    /// across it for this body — see <c>RosterRow.HeadIsDistinctVolume</c>.</summary>
    private const float NeckJointY = 0.920f;

    /// <summary>Where the upper arms hang from — the height the arm leaves the trunk.</summary>
    private const float ShoulderJointY = 0.796f;

    /// <summary>Upper-arm/forearm split. Deliberately high — just above the trunk's widest line —
    /// so the elbow is outside the body's own shadow and a bend can be seen.</summary>
    private const float ElbowJointY = 0.640f;

    /// <summary>The bottom end of the forearm post, BEFORE its rounded tip.</summary>
    private const float WristY = 0.160f;

    // --- THE GUMDROP. One profile curve, revolved, from the flat underside to the crown. There is
    //     no head sphere and no waist section; see TrunkRadius.

    /// <summary>The underside of the body. Lower = shorter legs showing.</summary>
    private const float BodyHemY = 0.527f;

    /// <summary>The gumdrop's half-width at its WIDEST.</summary>
    private const float BodyRadius = 0.282f;

    /// <summary>The height at which the trunk reaches <see cref="BodyRadius"/> — the single number
    /// that decides whether the figure reads as a gumdrop or as a barrel.</summary>
    private const float BodyWidestY = 0.612f;

    /// <summary>Half-width of the flat disc the body sits on.</summary>
    private const float UndersideRadius = 0.197f;

    /// <summary>Radius of the rounded corner between that flat underside and the flank.</summary>
    private const float BottomFillet = 0.085f;

    /// <summary>THE DOME-versus-CONE KNOB: the radius of the sphere cap that is the crown. A length
    /// you can point at in the silhouette rather than an exponent — AVATAR-1D replaced a
    /// superellipse with it precisely because no exponent could separate "flat at the crown" from
    /// "a tight corner at the shoulder".</summary>
    private const float DomeCapRadius = 0.274f;

    // --- The legs: round posts under the body, tapering toward points.

    private const float LegSpacingX = 0.190f;
    private const float ThighRadius = 0.052f;   // the leg at the hip — its thickest point
    private const float KneeRadius = 0.042f;    // the thigh where it meets the knee
    private const float ShinRadius = 0.039f;    // the shin at the TOP of its geometry
    private const float ToeRadius = 0.016f;     // the leg where it meets the ground, and its cap

    /// <summary>How far the shin reaches up past the knee, into the thigh, so a bent knee shows a
    /// joint rather than a gap.</summary>
    private const float ShinOverlap = 0.022f;

    // --- The arms: long, THIN, and HANGING, close in against the trunk's flank.
    //
    // Two X values rather than one, because the arm is not vertical: the shoulder has to be far
    // enough IN that the arm leaves the trunk instead of floating beside it, and the forearm far
    // enough OUT that the hand end clears the widest part of the body.

    private const float ShoulderX = 0.310f;
    private const float ForearmX = 0.324f;
    private const float UpperArmRadius = 0.040f;
    private const float ElbowRadius = 0.037f;
    private const float ForearmRadius = 0.034f;
    private const float ArmTipRadius = 0.013f;

    /// <summary>How far the upper arm reaches up past the shoulder, into the dome.</summary>
    private const float ArmOverlap = 0.012f;

    /// <summary>How far the forearm reaches up past the elbow, into the upper arm.</summary>
    private const float ForearmOverlap = 0.026f;

    // --- The face. The eye centres are the aim origin, so their height is load-bearing; the mouth
    //     is a bar. Both ride the HEAD node, not the torso: the waist bends the upper body, and a
    //     face parented to the lower torso would stay behind while the head leaned away from it.

    private const float EyeRadius = 0.046f;
    private const float EyeSpreadX = 0.078f;
    private const float EyeForwardZ = 0.232f;
    private const float MouthY = 0.942f;
    private const float MouthForwardZ = 0.267f;
    private const float MouthHalfWidth = 0.055f;
    private const float MouthHalfHeight = 0.012f;
    private const float MouthHalfDepth = 0.012f;

    // --- Tessellation. Faceted is on-style (ART-BIBLE §6); smooth is not.

    private const int BodySegments = 16;    // radial segments around the trunk and the head
    private const int FilletRings = 6;      // rings up the bottom fillet, sampled BY ANGLE
    private const int ShoulderRings = 4;    // rings across the near-vertical shoulder
    private const int DomeRings = 13;       // rings up the sphere cap, sampled BY ANGLE
    private const int LimbSegments = 10;    // radial segments around each limb post
    private const int LimbCapRings = 3;     // rings in the rounded dome that closes a limb end
    private const int EyeSegments = 10;
    private const int EyeRings = 6;

    // --- Derived, so a moved knob drags its dependants with it and the two can never disagree.

    /// <summary>Where the bottom corner stops curving.</summary>
    private const float FilletTopY = BodyHemY + BottomFillet;

    /// <summary>The trunk's radius at <see cref="FilletTopY"/>.</summary>
    private const float WallRadius = UndersideRadius + BottomFillet;

    /// <summary>Where the sphere cap reaches its own full radius. Six millimetres above
    /// <see cref="NeckJointY"/> — the dome and the <c>Head</c> node very nearly coincide, which is
    /// why that node seam has nothing in the surface to hide behind.</summary>
    private const float DomeEquatorY = CrownM - DomeCapRadius;

    /// <summary>Height of the waist joint, metres — i.e. where this body's trunk geometry is split,
    /// and the pivot the upper body bends about.
    ///
    /// <para><b>It names the <c>Hips</c>/<c>Torso</c> NODE SEAM, not a shape feature.</b> The trunk
    /// is one revolved profile; nothing narrows here and nothing creases here. The seam exists
    /// because the rig has to bend somewhere, and 0.532 m is where the authored asset cuts it.</para>
    ///
    /// <para><b>Nothing in the engine reads it, deliberately.</b> <c>AvatarVisual</c> places its
    /// waist pivot off the <i>measured</i> top of whatever lower torso it harvested, so a
    /// hand-authored model with a waist at a different height bends at its own seam and needs no
    /// code change. This constant is published for the opposite direction: it is the number the
    /// authored <c>Greybox.glb</c> has to match if the asset and this fallback are to stay in
    /// lockstep.</para></summary>
    public const float WaistY = WaistJointY;

    /// <summary>What <c>AvatarVisual</c> reports in a missing-part error for this body. It is not
    /// a path and must never look like one — there is no file, and a session has been lost before
    /// to a plausible-looking path that did not exist.</summary>
    public const string SourceLabel = "(code-built greybox primitive — GreyboxAvatarBody.cs)";

    /// <summary>Face colour for the eyes and the mouth. Never player-tinted: the harvest path
    /// reparents these AS IS, exactly as it leaves a harvested creature's authored eye colours alone, so
    /// every greybox reads as the same little face regardless of which palette colour it was
    /// dealt.
    ///
    /// <para><b>The asset's <c>FACE_COLOUR</c> is (0.09, 0.09, 0.12) in glTF's LINEAR space; a
    /// <see cref="StandardMaterial3D"/>'s <c>AlbedoColor</c> is sRGB.</b> Writing the same three
    /// numbers into both therefore does NOT produce the same face — measured off two captures of
    /// this exact pose, the code-built face rendered (37, 38, 46) against the imported one's
    /// (80, 81, 97), i.e. roughly half as bright. The conversion is applied here rather than the
    /// converted numbers being typed in, so the line still reads as "the asset's face colour" when
    /// that colour next changes.</para></summary>
    private static readonly Color FaceColor = new Color(0.09f, 0.09f, 0.12f).LinearToSrgb();

    /// <summary>
    /// Builds the primitive and hands it back detached, for the caller to <c>AddChild</c> and
    /// harvest — the same shape as <c>PackedScene.Instantiate&lt;Node3D&gt;()</c>, so the branch
    /// that chooses between them is one expression rather than two code paths.
    ///
    /// <para>Authored in the RIG's own space, so unlike a glTF instance it needs no 180-degree yaw
    /// correction and no mirror. Those exist to undo a Blender export convention; there is no
    /// exporter here.</para>
    ///
    /// <para><b>Everything is emitted FLAT, as a sibling of everything else.</b> The hierarchy —
    /// head and arms under a waist, shins under thighs, forearms under upper arms — is assembled by
    /// <c>AvatarVisual.BuildAppearance</c>, because that is the one place that knows how to do it for
    /// a harvested <c>.glb</c> as well, and a second assembler here would be a second thing to keep
    /// in step.</para>
    /// </summary>
    public static Node3D Build()
    {
        var root = new Node3D { Name = "GreyboxPrimitive" };

        // --- The trunk, in three slices of ONE revolved profile, cut at the joints the rig bends
        //     about. Each node's origin IS its joint; the geometry is authored in that local space.
        Part(root, "Hips", TrunkSlice(BodyHemY, WaistJointY, HipJointY),
            new Vector3(0f, HipJointY, 0f));

        Part(root, "Torso", TrunkSlice(WaistJointY, NeckJointY, WaistJointY),
            new Vector3(0f, WaistJointY, 0f));

        // The head slice closes at a POINT rather than a flat lid — the profile's radius really
        // does reach zero at the crown, and a flat cap there would put a disc where the silhouette
        // has a curve.
        MeshInstance3D head = Part(root, "Head", TrunkSlice(NeckJointY, CrownM, NeckJointY),
            new Vector3(0f, NeckJointY, 0f));

        // --- Mouth: a child of the HEAD mesh, not a sibling. The harvest contract does not name
        //     Mouth, so a top-level Mouth would be left behind and freed with the leftover
        //     scaffolding — invisible, and it would look like the mouth was never built. Riding the
        //     head is also simply correct: it is a face, and the head is what a face is on.
        head.AddChild(new MeshInstance3D
        {
            Name = "Mouth",
            Mesh = Box(MouthHalfWidth, MouthHalfHeight, MouthHalfDepth),
            Position = new Vector3(0f, MouthY - NeckJointY, -MouthForwardZ),
            MaterialOverride = FaceMaterial(),
        });

        // --- Eyes. Real geometry, and that matters beyond looks: AvatarProportions measures the
        //     eyeline off these very meshes (AvatarVisual.EyePartCentre returns null for anything
        //     that is not a real mesh, precisely so a character's aim ray can never silently be
        //     placed on the floor), and AvatarVisual.Animate Y-scales them for the blink.
        var eyeMesh = new SphereMesh
        {
            Radius = EyeRadius,
            Height = EyeRadius * 2f,
            RadialSegments = EyeSegments,
            Rings = EyeRings,
        };
        Part(root, "EyeL", eyeMesh, new Vector3(-EyeSpreadX, EyeY, -EyeForwardZ), FaceMaterial());
        Part(root, "EyeR", eyeMesh, new Vector3(EyeSpreadX, EyeY, -EyeForwardZ), FaceMaterial());

        // --- Arms, in two segments. ArmL on -X and ArmR on +X: facing -Z with +Y up, the
        //     character's left IS -X, and the carry/aim offsets in AvatarVisual.Animate confirm it —
        //     both push their arm toward the midline (+0.16 for L, -0.16 for R), which is a carry
        //     gesture only if the arms start on the outside of the body they belong to.
        //
        //     ArmL/ArmR keep their names and are the UPPER arm; ForearmL/R hang off them at the
        //     elbow. That is a deliberate continuation of the vocabulary compromise recorded under
        //     the legs below — renaming them would touch the harvest path, SandboxAvatar, the net
        //     mount and the face binding for zero gain.
        Part(root, "ArmL", UpperArmMesh(), new Vector3(-ShoulderX, ShoulderJointY, 0f));
        Part(root, "ArmR", UpperArmMesh(), new Vector3(ShoulderX, ShoulderJointY, 0f));
        Part(root, "ForearmL", ForearmMesh(), new Vector3(-ForearmX, ElbowJointY, 0f));
        Part(root, "ForearmR", ForearmMesh(), new Vector3(ForearmX, ElbowJointY, 0f));

        // --- Legs, in two segments, under the contract's FOOT names. The contract has no leg: it
        //     has FootL/FootR, and those are the nodes the step animation lifts. Calling a leg a
        //     foot is a vocabulary compromise with the original harvested rig, made deliberately and recorded
        //     here rather than solved by renaming a part half the codebase reads. FootL/FootR are
        //     specifically the THIGH; ShinL/ShinR hang off them at the knee. THIS BODY HAS NO FEET.
        //
        //     AND THE SHIPPED .GLB DIVERGED FROM THIS ON 2026-08-21 (ANIM-M2b), DELIBERATELY.
        //     Greybox.glb now names its thighs ThighL/ThighR and carries real FootL/FootR feet under
        //     Talon's ruling. This file is NOT renamed with it, and that is exactly why the rename
        //     was cheap: the .glb is served by AvatarVisual's Build.AuthoredRig path and this
        //     primitive by Build.PrimitiveParts, and the load-failure fallback swaps the ROW as well
        //     as the geometry — so the two dialects never meet in one body. The harvested part rigs
        //     still speak this older one. docs/BLENDER-EXPORT.md carries the
        //     migration note and the condition under which the old dialect ends.
        Part(root, "FootL", ThighMesh(), new Vector3(-LegSpacingX, HipJointY, 0f));
        Part(root, "FootR", ThighMesh(), new Vector3(LegSpacingX, HipJointY, 0f));
        Part(root, "ShinL", ShinMesh(), new Vector3(-LegSpacingX, KneeJointY, 0f));
        Part(root, "ShinR", ShinMesh(), new Vector3(LegSpacingX, KneeJointY, 0f));

        // --- Parts this body does not have. See the class doc: declared absent, not discovered
        //     absent. Stem carries the sprout pivot and is placed at the crown so that the
        //     (empty, invisible) sprout node hangs off somewhere sane rather than the floor.
        Absent(root, "Belly");
        Absent(root, "Tail");
        Absent(root, "BackFiller");
        Absent(root, "BlushL");
        Absent(root, "BlushR");
        Absent(root, "Stem", new Vector3(0f, CrownM, 0f));
        Absent(root, "LeafL", new Vector3(0f, CrownM, 0f));
        Absent(root, "LeafR", new Vector3(0f, CrownM, 0f));

        return root;
    }

    // --- the profile ----------------------------------------------------------------------------

    /// <summary>
    /// The gumdrop's half-width at height <paramref name="y"/> — <b>the ONE curve <c>Hips</c>,
    /// <c>Torso</c> AND <c>Head</c> all sample</b>, and a direct port of
    /// <c>build_greybox.py</c>'s <c>trunk_radius</c>.
    ///
    /// <para>Four sections, bottom to top: the quarter-circle fillet off the flat underside, an
    /// optional straight-walled flare (zero height when the fillet reaches the widest line, which
    /// is the case today), the near-vertical SHOULDER, and then the SPHERE CAP that is the crown.
    /// Every boundary is tangent-vertical, so the surface has no crease and NO STEP anywhere.
    /// <b>There is no waist section and no head sphere</b>; if you are looking for the place to
    /// reintroduce either, there isn't one.</para>
    /// </summary>
    private static float TrunkRadius(float y)
    {
        if (y <= BodyHemY)
            return UndersideRadius;
        if (y < FilletTopY)
        {
            float drop = FilletTopY - y;
            return UndersideRadius + Mathf.Sqrt(Mathf.Max(0f, BottomFillet * BottomFillet - drop * drop));
        }
        if (y < BodyWidestY)
        {
            float u = (y - FilletTopY) / (BodyWidestY - FilletTopY);
            return WallRadius + (BodyRadius - WallRadius) * Smoothstep(u);
        }
        if (y < DomeEquatorY)
        {
            // The shoulder. Vertical tangent at BOTH ends — at the widest point it meets the
            // fillet, at the equator it meets the cap — so it can carry the last few millimetres
            // of width without putting a crease at either end.
            float u = (DomeEquatorY - y) / (DomeEquatorY - BodyWidestY);
            return DomeCapRadius + (BodyRadius - DomeCapRadius) * Smoothstep(u);
        }
        if (y < CrownM)
        {
            float rise = y - DomeEquatorY;
            return Mathf.Sqrt(Mathf.Max(0f, DomeCapRadius * DomeCapRadius - rise * rise));
        }
        return 0f;
    }

    /// <summary>0..1 with zero slope at BOTH ends, so two profile sections meet without a
    /// crease.</summary>
    private static float Smoothstep(float u)
    {
        u = Mathf.Clamp(u, 0f, 1f);
        return u * u * (3f - 2f * u);
    }

    /// <summary>
    /// Ring heights for the slice of the trunk between two y values, sampled PER SECTION so the
    /// fillet gets fillet rings and the cap gets dome rings no matter where a node seam falls —
    /// and the seam itself is always a ring, on both sides of it.
    ///
    /// <para>The CAP and the FILLET are sampled BY ANGLE rather than by height. Both turn most of
    /// their corner in a small slice of their height, and uniform height sampling spends every ring
    /// on the straight part: it leaves the crown a cone and the bottom turn a faceted ledge.</para>
    /// </summary>
    private static List<float> TrunkRingHeights(float yBottom, float yTop)
    {
        var heights = new SortedSet<float> { yBottom, yTop };

        void Add(float y)
        {
            if (y > yBottom + 1e-6f && y < yTop - 1e-6f)
                heights.Add(y);
        }

        // The bottom fillet, by angle: 0 is the hem (surface horizontal), 90 degrees is the widest
        // line (surface vertical).
        for (int i = 0; i <= FilletRings; i++)
        {
            float phi = 0.5f * Mathf.Pi * i / FilletRings;
            Add(FilletTopY - BottomFillet * Mathf.Cos(phi));
        }
        // The FLARE — the straight-walled section between the fillet's top and the widest line —
        // gets no rings of its own, and needs none: it is a cone in profile, and a flat-shaded cone
        // is exactly as round with two rings as with five. Its boundaries are already rings, from
        // the two loops either side of this comment. (The asset spends FLARE_RINGS there; on the
        // shipped numbers that section has zero height anyway, because the fillet reaches the
        // widest line on its own.)
        //
        // The shoulder, by height — it is nearly straight, so the rings are there to exist.
        if (DomeEquatorY - BodyWidestY > 1e-6f)
        {
            for (int i = 0; i <= ShoulderRings; i++)
                Add(BodyWidestY + (DomeEquatorY - BodyWidestY) * i / ShoulderRings);
        }
        // The sphere cap, by angle: 0 is the equator (surface vertical), 90 degrees is the crown
        // (surface horizontal). The last ring stops short of the pole; the apex closes it as a point.
        for (int i = 0; i < DomeRings; i++)
            Add(DomeEquatorY + DomeCapRadius * Mathf.Sin(0.5f * Mathf.Pi * i / DomeRings));

        return new List<float>(heights);
    }

    /// <summary>
    /// One slice of the revolved trunk, from <paramref name="yBottom"/> to
    /// <paramref name="yTop"/>, authored in the local space of a node whose origin sits at
    /// <paramref name="originY"/>.
    ///
    /// <para>A slice that reaches the crown closes at a point; every other end gets a flat cap.
    /// Those internal caps are never seen from outside — the seams are flush by construction —
    /// but they keep each part a closed volume, so a bent waist cannot show the inside of the
    /// body through its own seam.</para>
    /// </summary>
    private static ArrayMesh TrunkSlice(float yBottom, float yTop, float originY)
    {
        List<float> heights = TrunkRingHeights(yBottom, yTop);
        var profile = new List<Vector2>(heights.Count);
        foreach (float y in heights)
        {
            float radius = TrunkRadius(y);
            // The crown's ring radius is zero; it is the apex, not a ring, and a zero-radius ring
            // would emit a band of degenerate quads under it.
            if (radius <= 1e-5f)
                continue;
            profile.Add(new Vector2(y - originY, radius));
        }

        bool apexTop = yTop >= CrownM - 1e-5f;
        return Revolve(profile, BodySegments,
            apexTopY: apexTop ? yTop - originY : null,
            apexBottomY: null);
    }

    // --- the limbs ------------------------------------------------------------------------------
    //
    // Each mesh is authored in its OWN pivot's local space — see the class doc's note on node
    // origins being joints. The numbers are differences of the absolute joint heights above, so
    // moving a joint moves the geometry with it and the two can never disagree.
    //
    // A limb is a tapered post, rounded where its end is FREE and flat where it is buried in the
    // part above it. The taper carries no intermediate rings: it is linear and flat-shaded, so a
    // ring in the middle of it would add triangles and change nothing.

    /// <summary>Shoulder to elbow. Rounded at the top — it pushes up into the dome and the shoulder
    /// has to read as a ball rather than a cut pipe — and rounded at the elbow.</summary>
    private static ArrayMesh UpperArmMesh() => Post(
        yTop: ArmOverlap, rTop: UpperArmRadius,
        yBottom: ElbowJointY - ShoulderJointY, rBottom: ElbowRadius,
        roundTop: true, roundBottom: true);

    /// <summary>Elbow to the tip. Rounded at both ends — the top overlaps up into the upper arm so
    /// a bent elbow shows a joint rather than a gap, and the bottom is the hand end.</summary>
    private static ArrayMesh ForearmMesh() => Post(
        yTop: ForearmOverlap, rTop: ForearmRadius,
        yBottom: WristY - ElbowJointY, rBottom: ArmTipRadius,
        roundTop: true, roundBottom: true);

    /// <summary>Hip to knee. FLAT at the top — that end is inside the trunk — and rounded at the
    /// knee.</summary>
    private static ArrayMesh ThighMesh() => Post(
        yTop: 0f, rTop: ThighRadius,
        yBottom: KneeJointY - HipJointY, rBottom: KneeRadius,
        roundTop: false, roundBottom: true);

    /// <summary>Knee to the ground. Flat at the top (buried in the thigh, reaching
    /// <see cref="ShinOverlap"/> up past the knee) and rounded at the toe, whose pole lands exactly
    /// on y = 0 so the body stands on the floor rather than in it.</summary>
    private static ArrayMesh ShinMesh() => Post(
        yTop: ShinOverlap, rTop: ShinRadius,
        yBottom: -KneeJointY + ToeRadius, rBottom: ToeRadius,
        roundTop: false, roundBottom: true);

    /// <summary>A tapered round post along +Y, optionally closed at either end with a rounded cap
    /// whose radius is that end's own radius — so the cap is tangent to the post and the join is
    /// invisible. A rounded end extends the mesh by that radius beyond
    /// <paramref name="yTop"/>/<paramref name="yBottom"/>.</summary>
    private static ArrayMesh Post(
        float yTop, float rTop, float yBottom, float rBottom, bool roundTop, bool roundBottom)
    {
        var profile = new List<Vector2>();

        if (roundBottom)
        {
            // Quarter circle from just off the pole up to the post's bottom ring. The pole itself
            // is the apex, not a ring, so the loop stops one short of it.
            for (int i = LimbCapRings - 1; i >= 1; i--)
            {
                float a = 0.5f * Mathf.Pi * i / LimbCapRings;
                profile.Add(new Vector2(yBottom - rBottom * Mathf.Sin(a), rBottom * Mathf.Cos(a)));
            }
        }

        profile.Add(new Vector2(yBottom, rBottom));
        profile.Add(new Vector2(yTop, rTop));

        if (roundTop)
        {
            for (int i = 1; i < LimbCapRings; i++)
            {
                float a = 0.5f * Mathf.Pi * i / LimbCapRings;
                profile.Add(new Vector2(yTop + rTop * Mathf.Sin(a), rTop * Mathf.Cos(a)));
            }
        }

        return Revolve(profile, LimbSegments,
            apexTopY: roundTop ? yTop + rTop : null,
            apexBottomY: roundBottom ? yBottom - rBottom : null);
    }

    // --- part helpers -------------------------------------------------------------------------

    private static MeshInstance3D Part(Node3D parent, string name, Mesh mesh, Vector3 position) =>
        Part(parent, name, mesh, position, null);

    private static MeshInstance3D Part(
        Node3D parent, string name, Mesh mesh, Vector3 position, Material? material)
    {
        var node = new MeshInstance3D { Name = name, Mesh = mesh, Position = position };
        if (material != null)
            node.MaterialOverride = material;
        parent.AddChild(node);
        return node;
    }

    /// <summary>A part the contract knows about that this body genuinely does not have. A
    /// <see cref="MeshInstance3D"/> with no mesh: harvested and reparented like any other, tinted
    /// like any other, and drawn by nobody. See the class doc for why it is emitted at all rather
    /// than left for the harvest path to miss.</summary>
    private static void Absent(Node3D parent, string name) => Absent(parent, name, Vector3.Zero);

    private static void Absent(Node3D parent, string name, Vector3 position) =>
        parent.AddChild(new MeshInstance3D { Name = name, Position = position });

    /// <summary>Eyes and mouth get their own material rather than sharing one, because the
    /// harvest path is free to set <c>MaterialOverride</c> on anything it reparents and a shared
    /// instance would let one part's tint follow the other. Two tiny StandardMaterial3Ds is not a
    /// budget anyone is counting (ART-BIBLE §4.2's ceiling is on UNIQUE materials, and these are
    /// identical in every field).</summary>
    private static StandardMaterial3D FaceMaterial() => new()
    {
        AlbedoColor = FaceColor,
        Roughness = 0.7f,
        Metallic = 0f,
    };

    // --- geometry -------------------------------------------------------------------------------

    /// <summary>
    /// A solid of revolution about the Y axis, from a bottom-to-top list of <c>(y, radius)</c>
    /// rings — <c>X</c> is the height, <c>Y</c> is the radius.
    ///
    /// <para><paramref name="apexTopY"/> / <paramref name="apexBottomY"/> close that end with a
    /// single point at that height instead of a flat disc. An end left open gets the disc, so every
    /// mesh this returns is a closed volume — an open end would show the inside of the body the
    /// first time a joint bent far enough to expose it.</para>
    ///
    /// <para>Normals are per face: the style is faceted (ART-BIBLE §6) and the authored asset is
    /// flat-shaded too, so a smooth-normal primitive would read as a different material even at an
    /// identical silhouette.</para>
    /// </summary>
    private static ArrayMesh Revolve(
        IReadOnlyList<Vector2> profile, int segments, float? apexTopY, float? apexBottomY)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        for (int r = 0; r + 1 < profile.Count; r++)
        {
            float y0 = profile[r].X, r0 = profile[r].Y;
            float y1 = profile[r + 1].X, r1 = profile[r + 1].Y;
            for (int s = 0; s < segments; s++)
            {
                Vector3 a0 = Ring(r0, y0, s, segments);
                Vector3 a1 = Ring(r0, y0, s + 1, segments);
                Vector3 b0 = Ring(r1, y1, s, segments);
                Vector3 b1 = Ring(r1, y1, s + 1, segments);
                Quad(st, Outward(s, segments), a0, a1, b1, b0);
            }
        }

        Vector2 bottom = profile[0];
        Cap(st, bottom, segments, Vector3.Down, apexBottomY ?? bottom.X);

        Vector2 top = profile[profile.Count - 1];
        Cap(st, top, segments, Vector3.Up, apexTopY ?? top.X);

        return st.Commit();
    }

    /// <summary>Closes one end of a revolve: a fan from the rim to a tip on the axis. When the tip
    /// sits at the ring's own height this is a flat disc; when it sits beyond it, it is a cone —
    /// which, fed the ring a quarter-circle cap ends on, is the last facet of a dome.</summary>
    private static void Cap(SurfaceTool st, Vector2 ring, int segments, Vector3 outward, float tipY)
    {
        var tip = new Vector3(0f, tipY, 0f);
        for (int s = 0; s < segments; s++)
        {
            Vector3 a = Ring(ring.Y, ring.X, s, segments);
            Vector3 b = Ring(ring.Y, ring.X, s + 1, segments);
            Face(st, outward, a, b, tip);
        }
    }

    /// <summary>One point on a horizontal ring. Angle 0 is +X, turning toward +Z — the SAME phase
    /// the asset's own <c>_ring</c> uses. It matters at ten segments: a ring rotated by half a
    /// segment puts its widest vertices somewhere else, and the two bodies' measured half-widths
    /// (and therefore their derived capsules) would differ by millimetres for no reason anybody
    /// could find later.</summary>
    private static Vector3 Ring(float radius, float y, int segment, int segments)
    {
        float t = Mathf.Tau * (segment % segments) / segments;
        return new Vector3(radius * Mathf.Cos(t), y, radius * Mathf.Sin(t));
    }

    /// <summary>The outward direction at the middle of one radial segment — the "which way is out"
    /// hint <see cref="Face"/> resolves winding against.</summary>
    private static Vector3 Outward(int segment, int segments)
    {
        float t = Mathf.Tau * (segment + 0.5f) / segments;
        return new Vector3(Mathf.Cos(t), 0f, Mathf.Sin(t));
    }

    /// <summary>A box centred on its own origin. The mouth bar, and nothing else — the rest of this
    /// body is round.</summary>
    private static ArrayMesh Box(float halfX, float halfY, float halfZ)
    {
        Vector3 b0 = new(-halfX, -halfY, -halfZ);
        Vector3 b1 = new(halfX, -halfY, -halfZ);
        Vector3 b2 = new(halfX, -halfY, halfZ);
        Vector3 b3 = new(-halfX, -halfY, halfZ);
        Vector3 t0 = new(-halfX, halfY, -halfZ);
        Vector3 t1 = new(halfX, halfY, -halfZ);
        Vector3 t2 = new(halfX, halfY, halfZ);
        Vector3 t3 = new(-halfX, halfY, halfZ);

        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        Quad(st, Vector3.Forward, b0, b1, t1, t0);   // -Z, the face side
        Quad(st, Vector3.Back, b2, b3, t3, t2);      // +Z
        Quad(st, Vector3.Left, b3, b0, t0, t3);      // -X
        Quad(st, Vector3.Right, b1, b2, t2, t1);     // +X
        Quad(st, Vector3.Up, t0, t1, t2, t3);
        Quad(st, Vector3.Down, b3, b2, b1, b0);
        return st.Commit();
    }

    /// <summary>Emits one quad as two independently wound triangles, facing
    /// <paramref name="approxOutward"/>.</summary>
    private static void Quad(SurfaceTool st, Vector3 approxOutward, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        Face(st, approxOutward, a, b, c);
        Face(st, approxOutward, a, c, d);
    }

    /// <summary>
    /// Emits one triangle in whichever winding actually faces <paramref name="approxOutward"/>.
    ///
    /// <para><b>Why the direction is computed rather than hand-worked.</b> Godot's front faces are
    /// CLOCKWISE as seen from outside, so the front normal of <c>(v0,v1,v2)</c> is
    /// <c>(v2-v0) x (v1-v0)</c> — and getting that backwards on one face out of six produces a
    /// mesh that looks completely normal from most angles and has an invisible wall from one. That
    /// exact defect is open in this repo against the Postpile. A revolved body is hundreds of faces
    /// and therefore hundreds of chances to make it; deriving the winding is none.</para>
    /// </summary>
    private static void Face(SurfaceTool st, Vector3 approxOutward, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 normal = (c - a).Cross(b - a);
        if (normal.Dot(approxOutward) < 0f)
        {
            (b, c) = (c, b);
            normal = -normal;
        }
        normal = normal.LengthSquared() > 0f ? normal.Normalized() : approxOutward;
        Tri(st, normal, a, b, c);
    }

    private static void Tri(SurfaceTool st, Vector3 normal, Vector3 a, Vector3 b, Vector3 c)
    {
        st.SetNormal(normal);
        st.AddVertex(a);
        st.SetNormal(normal);
        st.AddVertex(b);
        st.SetNormal(normal);
        st.AddVertex(c);
    }
}
