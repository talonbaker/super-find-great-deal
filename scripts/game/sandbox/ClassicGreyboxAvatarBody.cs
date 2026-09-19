using Godot;

namespace MpFoundation.Game.Sandbox;

/// <summary>
/// <b>The greybox player: a head, a two-part torso, arms with elbows and legs with knees, built
/// from code.</b> No <c>.glb</c>, no import, no asset directory, nothing for the pipeline to
/// validate — the same approach a since-removed code-built creature body already took.
///
/// <para><b>WHAT THIS FILE IS, AND WHY IT IS A SECOND COPY</b> (MOVE-3g, 2026-08-26). Talon:
/// <i>"bring back the original graybox player character. The one that was before the gumdrop."</i>
/// This is that body, recovered verbatim from <c>GreyboxAvatarBody.cs</c> as it stood at
/// <b><c>ea812e03</c></b> — the last revision before <c>1be54a07</c> ("the code catches up to the
/// approved gumdrop", 2026-08-20) rebuilt that file around AVATAR-1C/1D's single revolved profile.
/// The only edits are the type name, the root node name and <see cref="SourceLabel"/>; every
/// dimension, every mesh and every part placement below is the recovered file's.
///
/// <para><b>It does NOT replace <see cref="GreyboxAvatarBody"/>.</b> That file is the live
/// load-failure fallback (<see cref="AvatarVisual.PrimitiveFallbackAvatarKey"/>) and the fixture
/// the xUnit suite reads in an engine-free host; overwriting it would have moved the shipped body
/// under everyone else's feet. The two live side by side as two roster rows —
/// <c>greybox_primitive</c> is the gumdrop-shaped fallback, <c>greybox_classic</c> is this one —
/// and they are two bodies, not two spellings of one. <b>Neither is
/// <see cref="AvatarVisual.PreferredAvatarKey"/>:</b> the played body is still the authored
/// <c>Greybox.glb</c>.</para>
///
/// <para><b>The head here really is its own volume</b>, unlike both gumdrop bodies: the head band
/// (0.88 → 1.20) is a separate frustum sitting on a narrower torso, with a visible step at the
/// neck. So this row declares <c>HeadIsDistinctVolume: true</c> and takes ART-BIBLE §3's tonal
/// band, which is the value break AVATAR-5 had to switch OFF for a continuous surface.</para>
///
/// <para><b>Why it exists</b> (Talon, 2026-08-16): the running and everything else feels wrong
/// and he cannot tell how the <i>animation</i> is going to feel. A primitive answers that
/// question today. If the motion feels right, upgrading the graphics later is easy; if it feels
/// wrong, no amount of model fidelity saves it. <b>This is not a cast decision</b> — the real
/// cast (<c>pendling</c>, <c>banneret</c>) is specced, unbuilt, and a hand-build in Blender.</para>
///
/// <para><b>WHAT RIG-1 CHANGED, AND WHY IT WAS NOT OPTIONAL</b> (Talon, 2026-08-17). The first cut
/// of this body was one trapezoid spanning y 0.30 → 1.20 — <b>75% of the whole figure in a single
/// frustum</b> — whose bottom half-extents were 0.24 X against a leg whose outer edge was 0.175.
/// That is a <b>6.5 cm overhang per side</b>: the legs sat physically <i>inside</i> the body's
/// silhouette, with only 0.30 m of leg (25% of the figure) below the hem. Talon's read was exact:
/// <i>"a large trapezoid shape that's blocking, looking like a dress being draped over the body."</i>
/// A knee added under that hem would have been a joint nobody could see, which is why the
/// re-proportioning came FIRST and the joint second.
///
/// <para>The figure now reads roughly as thirds — head 27% / torso 38% / legs 35% — inside a
/// <b>pinned</b> height envelope: <see cref="CrownM"/> 1.20 m and <see cref="EyeY"/> 1.04 m have not
/// moved, because the collision capsule, the aim ray, the camera focus, the nameplate, the
/// the floating-creature heights and CONV-4's capture volume were all derived from them. The hips are
/// <i>narrower</i> than the legs' outer edge, so the legs read beside the silhouette instead of under
/// it, and the knee at y 0.22 sits in open air.</para></para>
///
/// <para><b>Extremely simple is still the specification.</b> Head, two torso segments, arms with
/// elbows, legs with knees, eyes, mouth. No tail, sprout, blush, hair, ears, fingers or clothing.
/// Every extra shape is noise against the signal this body exists to carry. The five parts RIG-1
/// added are not decoration: each one is a joint the animation layer needs somewhere to put.</para>
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
/// elbow for the forearm. <c>AvatarVisual.TakePart</c> reparents with <c>keepGlobalTransform</c>, so
/// a shin harvested under its own thigh arrives with a local position of exactly
/// <c>(0, -thighLength, 0)</c> and rotates about the knee with no arithmetic at the call site. A
/// sub-segment authored centred on its own mesh would swing from its middle and read as a floating
/// stick — which is exactly what the arms did before they were given a shoulder origin.</para>
///
/// <para><b>Absence is DECLARED, not discovered.</b> Eight of the harvested parts (Belly, Tail,
/// BackFiller, BlushL/R, Stem, LeafL/R) are things this body genuinely does not have. They are
/// emitted anyway, as <see cref="MeshInstance3D"/> nodes carrying no mesh — which is exactly
/// what <c>AvatarVisual.TakePart</c> substitutes for a part it cannot find, minus the
/// <c>GD.PushError</c>. That error is correct for a <c>.glb</c>, where a missing part means a
/// broken export; it is wrong here, where the part list is authored twenty lines up. Stating the
/// absence in the geometry keeps the distinction where it belongs — in this file, which knows
/// the answer — instead of silencing the error globally for every model in the roster.
/// <b><c>Hips</c> was on that list until RIG-1 and is now real geometry</b>: it is the lower torso
/// the legs hang off, so the torso split needed no new part name.</para>
///
/// <para><b>Lit, not unshaded, and no emission.</b> The player takes real light like the whole-figure
/// bodies did. A creature body may carry a small emission so an undodgeable attack cannot come
/// out of pure black; a player has no such argument, and a self-lit player would quietly delete
/// the darkness canon fact 4 makes the game's central pressure. (Standing trap, recorded because
/// it has cost this repo a session: Godot's <i>unshaded</i> shading mode ignores EMISSION
/// entirely — anything glowing must write ALBEDO. Nothing here is unshaded, so it does not bite,
/// but the next person to reach for a glow should know.)</para>
/// </summary>
public static class ClassicGreyboxAvatarBody
{
    // --- The dimensions, and why each one is that number --------------------------------------
    //
    // HEIGHT IS THE ONE NUMBER WITH REACH BEYOND THIS FILE. AvatarProportions derives the
    // capsule, the eyeline, the aim ray, the carry and stow anchors, the nameplate and the
    // camera focus from the MEASURED bounds of whatever is on screen — and CATCH-1 derived the
    // floating-creature heights from those same proportions, so that a cast change could not
    // silently put the catchable creatures out of reach. Nothing below is fed to
    // AvatarProportions as a literal: it measures the vertices this file emits.
    //
    // 1.20 m crown, chosen (not inherited) for three reasons:
    //   1. A swing at head height should be natural. The whole-figure blockout that was the player
    //      before GREY-1 measures crown 1.241 m / eye 0.995 m, and 1.20 m sits inside that read, so
    //      the camera framing, collision and reach Talon has ALREADY played against do not jump
    //      when the default body changes. A primitive that also moved every derived dimension would
    //      confound the feel question it exists to answer.
    //   2. It is deliberately NOT that blockout's 1.241 m. That number is a measurement artifact of
    //      one blockout; this one is a decision, and a decision can be argued with.
    //   3. It stays a stylised child rather than a person: a real 8-11 year old is 1.30-1.45 m,
    //      and the house style is stubby (the original blob crown was 0.93 m). 1.20 m is between them
    //      and nearer the taller end, because arms and legs need somewhere to be.
    //
    // RIG-1 DID NOT TOUCH IT, AND MUST NOT. Every band below was re-authored inside this envelope:
    // the three masses sum to exactly 1.20 and the eyes land at 1.04 without being placed there —
    // a head spanning 0.88 → 1.20 puts its centre at 1.04 on its own.

    /// <summary>Ground to the crown, metres. See the block above for the reasoning — this is the
    /// number the whole dimension cascade follows, and it is <b>pinned</b>.</summary>
    public const float CrownM = 1.20f;

    // --- THE THREE MASSES ---------------------------------------------------------------------
    //
    // Talon's ask, verbatim: "more uniform, stylized proportions, closer to a young or adolescent
    // build rather than adult proportions, where the head and body read as slightly larger relative
    // to the limbs, giving the character a cuter, softer silhouette rather than a bottom heavy
    // trapezoid."
    //
    // So the flare is GONE and the direction is reversed where it survives at all: the hips are the
    // narrowest part of the trunk, there is a waist, and the chest is a shade broader than the waist.
    // That is a young figure rather than a dumpling, and it is what makes the waist joint legible as
    // a joint instead of as a crease in a dress. ART-BIBLE §6 asks for "big head / big defining
    // feature, small limbs"; the head is 27% of the figure and 0.35 m across, wider than the chest,
    // which is that instruction in numbers.

    /// <summary>Where the head starts. 0.88 → 1.20 is 0.32 m, 27% of the figure — the "cute" signal,
    /// and it puts the eye band dead centre.</summary>
    private const float HeadBottomY = 0.88f;
    private const float HeadBottomHalfX = 0.175f;
    private const float HeadBottomHalfZ = 0.165f;
    private const float HeadTopHalfX = 0.166f;
    private const float HeadTopHalfZ = 0.157f;

    /// <summary>The waist: where the upper torso meets the lower one, and the pivot the forward/back
    /// bend happens about. <b><c>AvatarVisual</c> does NOT read this</b> — it measures the harvested
    /// lower torso's own top edge and places its waist node there, so an authored <c>.glb</c> whose
    /// waist sits somewhere else bends at its own seam rather than at this body's. See
    /// <see cref="WaistY"/>.</summary>
    private const float TorsoBottomY = 0.55f;
    private const float TorsoBottomHalfX = 0.150f;
    private const float TorsoBottomHalfZ = 0.145f;
    private const float TorsoTopHalfX = 0.158f;
    private const float TorsoTopHalfZ = 0.150f;

    /// <summary>The lower torso / pelvis. Promoted from a declared-absent empty mesh to real
    /// geometry by RIG-1: it is what the legs now hang off, so the torso split cost no new part
    /// name. Its BOTTOM half-X (0.145) is the number acceptance criterion 1 turns on — it is
    /// <b>2 cm narrower</b> than the legs' outer edge (0.165), which is what deletes the hem.</summary>
    private const float HipsBottomY = 0.42f;
    private const float HipsBottomHalfX = 0.145f;
    private const float HipsBottomHalfZ = 0.140f;
    private const float HipsTopHalfX = 0.150f;
    private const float HipsTopHalfZ = 0.145f;

    /// <summary>Height of the waist joint, metres — i.e. where this body's trunk geometry is split.
    ///
    /// <para><b>Nothing in the engine reads it, deliberately.</b> <c>AvatarVisual</c> places its waist
    /// pivot off the <i>measured</i> top of whatever lower torso it harvested, so a hand-authored
    /// model with a waist at a different height bends at its own seam and needs no code change. This
    /// constant is published for the opposite direction: it is the number an authored
    /// <c>Greybox.glb</c> has to match if the asset and this fallback primitive are to stay in
    /// lockstep, and a divergence between them should be a visible difference in a capture rather than
    /// a silent one.</para></summary>
    public const float WaistY = TorsoBottomY;

    // --- THE LEGS -----------------------------------------------------------------------------
    //
    // Legs run from the ground to y 0.44, PAST the hips' bottom edge at 0.42. The 2 cm overlap is
    // the hips' skirt hiding the hip seam, and it is all that is left of what used to be a 6 cm
    // overlap under a 0.24-wide hem. It is small on purpose: the overlap that mattered before was
    // covering a body that swung 15 cm forward of its own hips at a sprint lean, and MOVE-1 fixed
    // that at the source by pivoting the pose at the hip instead of at the feet.
    //
    // 0.42 m of leg is now visible — 35% of the figure, against 25% before — and the knee at 0.22 m
    // sits in open air rather than 6 cm under a hem. That is the whole of Talon's "I would like to
    // see the bend and snap and landing".

    private const float LegTopY = 0.44f;         // the hip joint
    private const float KneeY = 0.22f;           // half way down: thigh 0.22, shin 0.22
    private const float ThighHalfX = 0.055f;
    private const float ThighHalfZ = 0.060f;
    private const float ShinHalfX = 0.050f;
    private const float ShinHalfZ = 0.055f;
    private const float LegSpacingX = 0.11f;     // outer edge 0.11 + 0.055 = 0.165

    /// <summary>How far the shin's mesh rises ABOVE the knee, so a bent knee shows a joint rather
    /// than a gap. Narrower than the thigh (0.050 vs 0.055) so it nests inside instead of clipping
    /// through — the same trick the shoulder has always used.</summary>
    private const float ShinRise = 0.022f;

    // --- THE ARMS -----------------------------------------------------------------------------
    //
    // Shoulder 0.82, elbow 0.62, wrist 0.44 — upper arm 0.20, forearm 0.18, which is the human
    // ratio and reads as an arm rather than as two equal sticks.
    //
    // THE OLD SHOULDER CONSTRAINT IS GONE, and the comment it used to carry with it. Shoulders were
    // pinned to 1.00 m because "there is no head on this body, so the face sits on the upper torso,
    // and shoulders authored above the eyes read as arms growing out of the character's temples."
    // There IS a head now, spanning 0.88 → 1.20, so the constraint dissolves: shoulders at 0.82
    // against eyes at 1.04 clear it by 22 cm.
    //
    // 0.175 rather than the 0.155 RIG-1's packet proposed, and the reason is measured rather than
    // aesthetic. At 0.155 the arm's inner face sits 5 cm inside the torso surface, leaving under
    // 5 cm of limb proud of the body — and worse, the carry pose translates the arm node +0.16 m
    // toward the midline (ANIM-1's absolute, blob-calibrated offsets), which at 0.155 puts the
    // shoulder at x = -0.005: across the midline, on the wrong side of its own body. 0.175 keeps it
    // at +0.015 and leaves 7 cm of arm outside the torso. The cost is a wider measured silhouette
    // and therefore a smaller drop in the derived capsule radius; that trade is stated in RIG-1's
    // report rather than taken silently.
    private const float ShoulderY = 0.82f;
    private const float ElbowY = 0.62f;
    private const float WristY = 0.44f;
    private const float ShoulderX = 0.175f;
    private const float ArmHalfX = 0.052f;
    private const float ArmHalfZ = 0.054f;
    private const float ForearmHalfX = 0.047f;
    private const float ForearmHalfZ = 0.049f;

    /// <summary>How far each limb's mesh rises above its own pivot, so the joint is not a visible
    /// seam when it bends.</summary>
    private const float ArmRise = 0.048f;
    private const float ForearmRise = 0.026f;

    // --- THE FACE -----------------------------------------------------------------------------
    // Eyes and mouth ride the HEAD node now (RIG-1), not the torso. That is not tidiness: the waist
    // bends the upper body, and a face parented to the lower torso would stay behind while the head
    // it belongs to leaned away from it.

    /// <summary><b>Where this body's eyes are, and therefore where its aim ray leaves from</b> —
    /// 1.04 m, which is 0.867 of <see cref="CrownM"/>. <b>Pinned</b>, and RIG-1 did not move it: the
    /// head spans 0.88 → 1.20, so 1.04 is its exact centre and the eyes did not have to be dragged
    /// anywhere to stay put.
    ///
    /// <para><b>Public because the swing hoop is centred on it and floating-creature geometry has
    /// to be checked against it</b> (CONV-2, 2026-08-16). <c>AvatarProportions.PlayerEyeHeightM</c>
    /// is 0.995 and describes a since-removed whole-figure blockout, which is no longer
    /// <see cref="AvatarVisual.PreferredAvatarKey"/> — so anything reasoning about where a level
    /// swing actually sweeps must read THIS, not that. Exposed as a measurement, not as a knob:
    /// nothing writes it, and it moves only when the greybox's eyes do.</para></summary>
    public const float EyeY = 1.04f;
    private const float EyeX = 0.078f;
    private const float EyeZ = -0.152f;       // -Z is forward
    private const float EyeRadius = 0.046f;

    private const float MouthY = 0.965f;
    private const float MouthZ = -0.166f;
    private const float MouthHalfX = 0.055f;
    private const float MouthHalfY = 0.012f;
    private const float MouthHalfZ = 0.012f;

    /// <summary>What <c>AvatarVisual</c> reports in a missing-part error for this body. It is not
    /// a path and must never look like one — there is no file, and a session has been lost before
    /// to a plausible-looking path that did not exist.</summary>
    public const string SourceLabel = "(code-built CLASSIC greybox primitive — ClassicGreyboxAvatarBody.cs)";

    /// <summary>Face colour for the eyes and the mouth. Never player-tinted: the harvest path
    /// reparents these AS IS, exactly as it leaves a harvested creature's authored eye colours alone, so
    /// every greybox reads as the same little face regardless of which palette colour it was
    /// dealt.</summary>
    private static readonly Color FaceColor = new(0.09f, 0.09f, 0.12f);

    /// <summary>
    /// Builds the primitive and hands it back detached, for the caller to <c>AddChild</c> and
    /// harvest — the same shape as <c>PackedScene.Instantiate&lt;Node3D&gt;()</c>, so the branch
    /// that chooses between them is one expression rather than two code paths.
    ///
    /// <para>Authored in the RIG's own space, so unlike a glTF instance it needs no 180-degree
    /// yaw correction. That axis flip exists to undo a Blender export convention; there is no
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
        var root = new Node3D { Name = "ClassicGreyboxPrimitive" };

        // --- The trunk, in two segments split at the waist. Origins at the rig origin, geometry
        //     authored in place, so each harvested part lands at exactly the placement written
        //     above and the waist node can be dropped in at WaistY without moving anything.
        Part(root, "Hips", Frustum(
            HipsBottomY, TorsoBottomY,
            HipsBottomHalfX, HipsBottomHalfZ,
            HipsTopHalfX, HipsTopHalfZ));

        Part(root, "Torso", Frustum(
            TorsoBottomY, HeadBottomY,
            TorsoBottomHalfX, TorsoBottomHalfZ,
            TorsoTopHalfX, TorsoTopHalfZ));

        // --- The head. The one genuinely new BODY part name RIG-1 adds, and the part that makes
        //     the figure read as a figure: without it the eyes sat on the chest and the shoulders
        //     had to be authored under them.
        var head = Part(root, "Head", Frustum(
            HeadBottomY, CrownM,
            HeadBottomHalfX, HeadBottomHalfZ,
            HeadTopHalfX, HeadTopHalfZ));

        // --- Mouth: a child of the HEAD mesh, not a sibling. The harvest contract does not name
        //     Mouth, so a top-level Mouth would be left behind and freed with the leftover
        //     scaffolding — invisible, and it would look like the mouth was never built. Riding the
        //     head is also simply correct: it is a face, and the head is what a face is on.
        head.AddChild(new MeshInstance3D
        {
            Name = "Mouth",
            Mesh = Frustum(
                MouthY - MouthHalfY, MouthY + MouthHalfY,
                MouthHalfX, MouthHalfZ, MouthHalfX, MouthHalfZ),
            Position = new Vector3(0f, 0f, MouthZ),
            MaterialOverride = FaceMaterial(),
        });

        // --- Eyes. Real geometry, and that matters beyond looks: AvatarProportions measures the
        //     eyeline off these very meshes (AvatarVisual.EyePartCentre returns null for anything
        //     that is not a real mesh, precisely so a character's aim ray can never silently be
        //     placed on the floor), and AvatarVisual.Animate Y-scales them for the blink.
        var eyeMesh = new SphereMesh { Radius = EyeRadius, Height = EyeRadius * 2f, RadialSegments = 10, Rings = 6 };
        Part(root, "EyeL", eyeMesh, new Vector3(-EyeX, EyeY, EyeZ), FaceMaterial());
        Part(root, "EyeR", eyeMesh, new Vector3(EyeX, EyeY, EyeZ), FaceMaterial());

        // --- Arms, in two segments. ArmL on -X and ArmR on +X: facing -Z with +Y up, the
        //     character's left IS -X, and the carry/aim offsets in AvatarVisual.Animate confirm it —
        //     both push their arm toward the midline (+0.16 for L, -0.16 for R), which is a carry
        //     gesture only if the arms start on the outside of the body they belong to.
        //
        //     ArmL/ArmR keep their names and become the UPPER arm; ForearmL/R are new. That is a
        //     deliberate continuation of the vocabulary compromise recorded under the legs below —
        //     renaming them would touch the harvest path, SandboxAvatar, the net mount and the face
        //     binding for zero gain.
        Part(root, "ArmL", UpperArmMesh(), new Vector3(-ShoulderX, ShoulderY, 0f));
        Part(root, "ArmR", UpperArmMesh(), new Vector3(ShoulderX, ShoulderY, 0f));
        Part(root, "ForearmL", ForearmMesh(), new Vector3(-ShoulderX, ElbowY, 0f));
        Part(root, "ForearmR", ForearmMesh(), new Vector3(ShoulderX, ElbowY, 0f));

        // --- Legs, in two segments, under the contract's FOOT names. The contract has no leg: it
        //     has FootL/FootR, and those are the nodes the step animation lifts. Calling a leg a
        //     foot is a vocabulary compromise with the original harvested rig, made deliberately and recorded
        //     here rather than solved by renaming a part half the codebase reads. FootL/FootR are
        //     now specifically the THIGH; ShinL/ShinR are new and hang off them at the knee.
        Part(root, "FootL", ThighMesh(), new Vector3(-LegSpacingX, LegTopY, 0f));
        Part(root, "FootR", ThighMesh(), new Vector3(LegSpacingX, LegTopY, 0f));
        Part(root, "ShinL", ShinMesh(), new Vector3(-LegSpacingX, KneeY, 0f));
        Part(root, "ShinR", ShinMesh(), new Vector3(LegSpacingX, KneeY, 0f));

        // --- Parts this body does not have. See the class doc: declared absent, not discovered
        //     absent. Stem carries the sprout pivot and is placed at the crown so that the
        //     (empty, invisible) sprout node hangs off somewhere sane rather than the floor.
        //     Hips LEFT THIS LIST in RIG-1 — it is the lower torso now.
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

    // --- part helpers -------------------------------------------------------------------------

    private static MeshInstance3D Part(Node3D parent, string name, Mesh mesh) =>
        Part(parent, name, mesh, Vector3.Zero, null);

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

    // Each mesh is authored in its OWN pivot's local space — see the class doc's note on node
    // origins being joints. The numbers are differences of the absolute band heights above, so
    // moving a joint moves the geometry with it and the two can never disagree.
    private static Mesh UpperArmMesh() =>
        Frustum(ElbowY - ShoulderY, ArmRise, ArmHalfX, ArmHalfZ, ArmHalfX, ArmHalfZ);

    private static Mesh ForearmMesh() =>
        Frustum(WristY - ElbowY, ForearmRise, ForearmHalfX, ForearmHalfZ, ForearmHalfX, ForearmHalfZ);

    private static Mesh ThighMesh() =>
        Frustum(KneeY - LegTopY, 0f, ThighHalfX, ThighHalfZ, ThighHalfX, ThighHalfZ);

    private static Mesh ShinMesh() =>
        Frustum(-KneeY, ShinRise, ShinHalfX, ShinHalfZ, ShinHalfX, ShinHalfZ);

    /// <summary>Eyes and mouth get their own material rather than sharing one, because the
    /// harvest path is free to set <c>MaterialOverride</c> on anything it reparents and a shared
    /// instance would let one part's tint follow the other. Two tiny StandardMaterial3Ds is not a
    /// budget anyone is counting (ART-BIBLE's ceiling is on UNIQUE materials, and these are
    /// identical in every field).</summary>
    private static StandardMaterial3D FaceMaterial() => new()
    {
        AlbedoColor = FaceColor,
        Roughness = 0.7f,
        Metallic = 0f,
    };

    // --- geometry -------------------------------------------------------------------------------

    /// <summary>
    /// A rectangular frustum — a box whose top and bottom rectangles are sized independently.
    /// One primitive covers every solid part of this body: the tapered trunk segments, the
    /// untapered limbs, and the flat mouth bar.
    ///
    /// <para>Built by hand rather than from <see cref="BoxMesh"/> because a box cannot taper, and
    /// rather than from a four-sided <see cref="CylinderMesh"/> because that gives a square
    /// cross-section on the diagonal and would need a 45-degree yaw the mouth would then inherit
    /// and have to undo.</para>
    /// </summary>
    private static ArrayMesh Frustum(
        float y0, float y1, float bottomHalfX, float bottomHalfZ, float topHalfX, float topHalfZ)
    {
        Vector3 b0 = new(-bottomHalfX, y0, -bottomHalfZ);
        Vector3 b1 = new(bottomHalfX, y0, -bottomHalfZ);
        Vector3 b2 = new(bottomHalfX, y0, bottomHalfZ);
        Vector3 b3 = new(-bottomHalfX, y0, bottomHalfZ);
        Vector3 t0 = new(-topHalfX, y1, -topHalfZ);
        Vector3 t1 = new(topHalfX, y1, -topHalfZ);
        Vector3 t2 = new(topHalfX, y1, topHalfZ);
        Vector3 t3 = new(-topHalfX, y1, topHalfZ);

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

    /// <summary>
    /// Emits one quad facing <paramref name="approxOutward"/>, in whichever winding actually
    /// faces that way.
    ///
    /// <para><b>Why the direction is computed rather than hand-worked.</b> Godot's front faces are
    /// CLOCKWISE as seen from outside, so the front normal of <c>(v0,v1,v2)</c> is
    /// <c>(v2-v0) x (v1-v0)</c> — and getting that backwards on one face out of six produces a
    /// mesh that looks completely normal from most angles and has an invisible wall from one.
    /// That exact defect is open in this repo against the Postpile. Six faces authored by hand is
    /// six chances to make it; deriving it is none.</para>
    /// </summary>
    private static void Quad(SurfaceTool st, Vector3 approxOutward, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        Vector3 normal = (c - a).Cross(b - a);
        if (normal.Dot(approxOutward) < 0f)
        {
            (b, d) = (d, b);
            normal = -normal;
        }
        normal = normal.LengthSquared() > 0f ? normal.Normalized() : approxOutward;
        Tri(st, normal, a, b, c);
        Tri(st, normal, a, c, d);
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
