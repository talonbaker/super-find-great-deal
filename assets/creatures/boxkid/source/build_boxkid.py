#!/usr/bin/env python
"""Builds BOXKID -- the boxy player body -- and writes boxkid.blend and BoxKid.glb.

RUN IT LIKE THIS (Blender 5.0 on this machine; --background needs no display):

    blender.exe --background --python assets/creatures/boxkid/build_boxkid.py -- --force
    blender.exe --background --python assets/creatures/boxkid/build_boxkid.py -- --export-only

  ...where blender.exe is "C:\\Program Files\\Blender Foundation\\Blender 5.0\\blender.exe".
  The `--` is Blender's own separator; everything after it is this script's. The flags mean
  exactly what they mean in build_greybox.py, and THE PRECEDENCE RULE is the same one:
  boxkid.blend is AUTHORITATIVE, this script is a BOOTSTRAP, --force destroys hand-keyed
  animation, --export-only re-exports the .blend as it stands.

WHY THIS SCRIPT EXISTS. BT-7. Talon, 2026-08-27: "Simple boxy figure: box body, two box arms,
two box legs, box head." And, on why it is an asset rather than code geometry (program Q3):
"A code-generated greybox body risks quietly violating the same 'physically present,
editor-openable' spirit ... BoxKid is trivial to make (it's just boxes) and gives you a real,
versioned, reusable asset instead of a throwaway."

WHAT BODY-3 CHANGED, AND IT CHANGED EVERY NUMBER (2026-08-29). Talon, after playing the second
bubble-test pass: "I really like how the 'graybox' model looks and feels, I don't like how 'box
kid' model looks and feels. Please fix this so box kid model looks exactly like how graybox is
as the only different is the box kid is a physical glb and the graybox is generated. Please make
this change using the exact dimentions of the graybox to make box kid better."

  THE GREYBOX HE MEANS IS THE CLASSIC ONE -- scripts/game/sandbox/ClassicGreyboxAvatarBody.cs,
  roster row `greybox_classic`, display name "Greybox". It is the body MOVE-3g recovered at his
  own request ("bring back the original graybox player character. The one that was before the
  gumdrop") and it is NOT `greybox_primitive`, which is the gumdrop he rejected.

  AND THAT IS WHY THIS BODY WAS WRONG. BT-7 conformed BoxKid to the two PINNED numbers (crown
  1.200, eye 1.040) and then took every remaining joint height from build_greybox.py -- the
  GUMDROP's block. Measured through GreyboxPlayerLab against the classic greybox, that put the
  hip 108 mm too high, the leg 108 mm too long, the trunk 100 mm too wide and 50 mm too shallow,
  the shoulders 83 mm too far out, and the upper-arm/forearm ratio inverted. The two pinned
  numbers matched perfectly; nothing else did. A body can be exactly the right height and still
  be a different character, and it was.

  SO THE JOINT BLOCK IS THIS FILE'S NOW, and it is the CLASSIC greybox's, measured off that
  body's own emitted vertices rather than copied off its constants. Only CROWN_Y and EYE_Y are
  still imported from build_greybox: they are the two pinned numbers, the classic agrees with
  them to the millimetre, and AvatarProportions derives the collision capsule and the aim ray's
  origin from them. See THE JOINT BLOCK below for what each number is and where it was measured.

  THE CLIP LIBRARY SCALES OFF THE LEG, so it could not be left alone. author_greybox_clips.py
  computes LEG_M = gx.HIP_JOINT_Y at import time and derives stride length, stance reach and the
  leg-swing cap from it; a 0.548 m gait on a 0.440 m leg is a planted foot that skates. main()
  therefore assigns this file's HIP_JOINT_Y onto the gx module immediately BEFORE importing that
  module, which is the only point at which the value is read. That patch is deliberate, narrow
  and commented at the call site -- see main().

WHAT "SIX BOXES" MEANS IN A FILE THAT HAS SEVENTEEN. The silhouette is six volumes -- head,
body, two arms, two legs. The rig contract AvatarVisual harvests (and GreyboxAssetContractTests
pins) needs sixteen named parts, because the body has to bend at the waist, the elbow, the knee
and the ankle and a single box cannot. So: the body is Hips + Torso split at the waist joint,
each arm is Arm + Forearm split at the elbow, each leg is Thigh + Shin + Foot split at the knee
and the ankle, and the head carries EyeL/EyeR/Mouth.

  BOXES IS NO LONGER LITERALLY TRUE, AND THAT IS THE POINT OF BODY-3. The trunk and the head are
  rectangular FRUSTA now, because the classic greybox's are: its hips taper out from 0.145 to
  0.150 half-X on the way up, its head tapers IN from 0.175 to 0.166. The eyes are spheres
  because its eyes are spheres. Standing still it reads as the greybox, which is the ask; the
  name stays BoxKid because the roster key rides the wire (SandboxAvatar.AvatarKey) and renaming
  a key is a migration, not a rename.

  THE ONE PLACE THIS BODY STILL DIFFERS FROM THE CLASSIC GREYBOX, STATED RATHER THAN HIDDEN: it
  has an ANKLE AND A FOOT and the classic does not. That is not a leftover -- it is ANIM-M2b's
  landed mechanism, LimbIk.LevelAnkle levels the sole about a pivot that has to be at y = 0, and
  GreyboxAssetContractTests pins all three facts (ankle origin on the ground, foot geometry
  standing above it, shin no longer reaching the floor). Deleting the foot would red a test this
  role may not edit and would silently re-open the foot-slip error that test exists to close. So
  the foot survives with the SHIN'S OWN CROSS-SECTION and no toe: shin and foot together occupy
  exactly the volume the classic greybox's shin occupies, 0.000..0.242 at half-extents
  0.050 x 0.055, and the ankle is a seam inside an outline that is the greybox's to the
  millimetre. It costs nothing in silhouette and keeps a mechanism Talon's note did not ask about.
"""

import os
import sys

import bpy

# build_greybox lives in the sibling asset folder and is the source of every pivot below.
# Importing it is safe: its own `if __name__ == "__main__"` guard means an import never builds.
_GREYBOX_DIR = os.path.normpath(
    os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "greybox"))
if _GREYBOX_DIR not in sys.path:
    sys.path.insert(0, _GREYBOX_DIR)
import build_greybox as gx      # noqa: E402  (the path has to go in first)


# ==============================================================================================
# THE JOINT BLOCK (BODY-3). Metres, y up, +z is the way the face looks, +x is the figure's LEFT
# -- the same authoring space build_greybox.py writes in, turned into Godot's by gx.to_blender().
#
# EVERY NUMBER HERE WAS MEASURED, NOT COPIED. The source is the CLASSIC greybox
# (scripts/game/sandbox/ClassicGreyboxAvatarBody.cs) as GreyboxPlayerLab reports it -- off that
# body's own emitted vertices in engine, which is the only reading that survives the difference
# between what a constant says and what a body is. The lab run is
# docs/qa/BODY-3/before-greybox.log; its "RIG-1 measured geometry" block is the column below.
#
# TWO NUMBERS ARE STILL IMPORTED, and only two: gx.CROWN_Y (1.200) and gx.EYE_Y (1.040). They are
# PINNED -- AvatarProportions sizes the collision capsule off the measured crown and takes the aim
# ray's origin from the eye centre -- and the classic greybox already agrees with both exactly, so
# importing them states the shared constraint rather than duplicating a number.
# ==============================================================================================

NECK_JOINT_Y = 0.880          # THE NODE SEAM between Torso and Head, and the Head node's origin.
                              # Measured: the classic's Head spans 0.880..1.200, its Torso
                              # 0.550..0.880. Was gx.NECK_JOINT_Y = 0.920.
WAIST_JOINT_Y = 0.550         # THE NODE SEAM between Hips and Torso, and the pivot the whole
                              # upper body bends about. Measured: Hips top / Torso bottom.
                              # AvatarVisual's harvest path places its waist at the harvested
                              # Hips AABB's top edge, so the two bodies bend at the same height
                              # only if this IS that edge. Was gx.WAIST_JOINT_Y = 0.532.
SHOULDER_JOINT_Y = 0.820      # Measured: the classic's ArmL spans 0.620..0.868, and 0.868 is the
                              # shoulder plus its 0.048 rise. Was gx.SHOULDER_JOINT_Y = 0.796.
ELBOW_JOINT_Y = 0.620         # upper-arm/forearm split. Was gx.ELBOW_JOINT_Y = 0.640.
WRIST_Y = 0.440               # the bottom of the forearm. Measured: ForearmL 0.440..0.646.
                              # Upper arm 0.200 + forearm 0.180 = arm 0.380 -- the human ratio,
                              # and the INVERSE of what BoxKid had (0.156 + 0.284).
HIP_JOINT_Y = 0.440           # where the thighs hang from, and the single most load-bearing
                              # number in this file: AvatarVisual reads the thigh node's rest Y
                              # as the leg length, the gait scales off it, and
                              # author_greybox_clips.LEG_M IS it. Was gx.HIP_JOINT_Y = 0.548 --
                              # 108 mm of leg BoxKid had and the greybox never did.
KNEE_JOINT_Y = 0.220          # thigh/shin split. Exactly half the leg, which is what the
                              # contract test requires and what makes the knee read as a knee.
                              # Was gx.KNEE_JOINT_Y = 0.274.
ANKLE_JOINT_Y = 0.0           # THE ANKLE, ON THE GROUND ON PURPOSE (ANIM-M2b). LimbIk.LevelAnkle
                              # levels the sole about this point and the levelling is exact only
                              # at zero. Unchanged, and see the module docstring for why the foot
                              # survives a conform to a body that has none.
LEG_SPACING_X = 0.110         # half the distance between the two legs. Measured: the classic's
                              # ShinL spans x -0.160..-0.060, centre -0.110. Was 0.190.
MOUTH_Y = 0.965               # the mouth bar's centre. Measured: Mouth spans 0.953..0.977.
                              # Was gx.MOUTH_Y = 0.942.

# ==============================================================================================
# THE DIMENSION BLOCK. The extents that hang off those joints -- again the classic greybox's,
# again measured. Half-extents, so 0.175 means a part 0.350 m across.
#
# THE TRUNK AND THE HEAD ARE FRUSTA, NOT BOXES. The classic's hips widen 0.145 -> 0.150 on the way
# up to the waist, its torso 0.150 -> 0.158 on the way up to the neck, and its head narrows
# 0.175 -> 0.166 on the way up to the crown. Those five millimetres per section are the whole
# difference between "a young figure with a waist" and "a slab", and they are the reason this file
# grew a frustum() helper. Every seam is flush by construction: each pair of numbers below is
# written once and read by both parts that meet there.
# ==============================================================================================

# --- The hips. 0.420 up to the waist seam. Its BOTTOM half-X (0.145) is 20 mm INSIDE the legs'
#     outer edge (0.165), which is what deletes the hem -- the trunk does not overhang the legs.
TRUNK_BOTTOM_Y = 0.420
HIPS_BOTTOM_HALF_X = 0.145
HIPS_BOTTOM_HALF_Z = 0.140

# --- The waist seam. ONE pair of numbers, read by the hips' top and the torso's bottom, so the
#     seam cannot open: whatever these are, both sides are them.
WAIST_HALF_X = 0.150
WAIST_HALF_Z = 0.145

# --- The torso's top, at the neck seam. Wider than the waist: the chest is a shade broader,
#     which is what makes the waist legible as a waist rather than as a crease.
TORSO_TOP_HALF_X = 0.158
TORSO_TOP_HALF_Z = 0.150

# --- The head. 0.880 -> 1.200 (the pinned crown), 27% of the figure, and WIDER at its base than
#     the torso is at its top -- 0.175 against 0.158. That step at the neck is deliberate: this
#     head is its own volume (the roster row declares HeadIsDistinctVolume) and ART-BIBLE 3's
#     tonal band lands on it.
HEAD_BOTTOM_HALF_X = 0.175
HEAD_BOTTOM_HALF_Z = 0.165
HEAD_TOP_HALF_X = 0.166
HEAD_TOP_HALF_Z = 0.157

# --- The arms. Straight untapered columns, split at the elbow, each reaching a little ABOVE its
#     own pivot so the joint is not a visible seam when it bends.
ARM_HALF_X = 0.052
ARM_HALF_Z = 0.054
FOREARM_HALF_X = 0.047        # narrower, so it nests inside the upper arm instead of clipping.
FOREARM_HALF_Z = 0.049
ARM_RISE = 0.048              # how far the upper arm reaches above the shoulder pivot.
FOREARM_RISE = 0.026          # ...and the forearm above the elbow.
ARM_X = 0.175                 # the shoulder axis. NOT derived from the trunk half-width the way
                              # BoxKid's used to be: at 0.175 against a 0.150 waist the arm's
                              # inner face sits 27 mm proud of the trunk, and the classic's
                              # 0.052 half-width leaves 7 cm of limb outside the body even after
                              # ANIM-1's carry pose translates the node 0.16 m toward the midline.

# --- The legs. Straight columns, split at the knee, standing on the ankle.
THIGH_HALF_X = 0.055
THIGH_HALF_Z = 0.060
SHIN_HALF_X = 0.050           # narrower than the thigh, so it nests at the knee.
SHIN_HALF_Z = 0.055
SHIN_RISE = 0.022             # how far the shin reaches above the knee pivot.

# --- The foot. See the module docstring: the classic greybox has none, this body keeps one
#     because ANIM-M2b's ankle levelling and GreyboxAssetContractTests both require it, and it is
#     made invisible by giving it the SHIN'S cross-section and no toe. Shin (0.024..0.242) and
#     foot (0.000..0.030) together occupy exactly the volume the classic's shin occupies
#     (0.000..0.242), so the leg's outline is the greybox's and the ankle is a seam inside it.
FOOT_HEIGHT = 0.030           # sole to instep. Its SOLE IS THE GROUND: the lowest vertex on this
                              # body is y = 0, which the contract test pins to 1 mm.
FOOT_OVERLAP = 0.006          # how far the shin's bottom reaches DOWN into the foot, so an ankle
                              # pitch cannot open a gap. The shin must still clear the floor by
                              # more than the test's 5 mm tolerance; 24 mm does.
SHIN_BOTTOM_Y = FOOT_HEIGHT - FOOT_OVERLAP        # 0.024

# --- The face. The eyes are SPHERES, because the classic's are (Godot SphereMesh, radius 0.046,
#     10 radial segments, 6 rings -- mirrored here segment for segment). They stand proud of the
#     head's front plane and are meant to: that is the greybox's face, and matching it is the ask.
EYE_RADIUS = 0.046
EYE_SEGMENTS = 10
EYE_RINGS = 6
EYE_SPREAD_X = 0.078          # measured: EyeL centre x -0.078. Unchanged -- it was already the
                              # greybox's, which is why the two bodies' eyes lined up even while
                              # nothing else did.
EYE_Z = 0.152                 # measured: EyeL centre z -0.152 in rig space.
MOUTH_HALF_X = 0.055
MOUTH_HALF_Y = 0.012
MOUTH_HALF_Z = 0.012
MOUTH_Z = 0.166               # measured: the mouth bar's centre, 0.166 forward.

# --- The palette. Two materials, one per unique colour (ART-BIBLE 4.2). The runtime overrides
# the body one per player (AvatarVisual.BuildAuthoredRig tints every mesh but the face), so
# these are what the .blend and the editor show, not what the game shows.
BODY_COLOUR = gx.BODY_COLOUR
FACE_COLOUR = gx.FACE_COLOUR
BODY_MATERIAL = "BoxKidBody"
FACE_MATERIAL = "BoxKidFace"


def slab(half_x, half_z, y_bottom, y_top, origin_y, centre_z=0.0):
    """One axis-aligned box spanning [y_bottom, y_top], local to a node whose origin is origin_y.

    The two heights are WORLD heights -- the dimension block is written in them, because every
    number it has to agree with (a joint, the crown, the ground) is a world height. This turns
    them into the node-local numbers gx.make_box wants, so no line in the block above has to
    hold a subtraction in its head."""
    half_y = 0.5 * (y_top - y_bottom)
    centre_y = 0.5 * (y_top + y_bottom) - origin_y
    return gx.make_box(half_x, half_y, half_z, centre_y, centre_z)


def frustum(y_bottom, y_top, bottom_half_x, bottom_half_z, top_half_x, top_half_z, origin_y):
    """A rectangular frustum -- a box whose top and bottom rectangles are sized independently.

    BODY-3 added it, and it is a straight port of ClassicGreyboxAvatarBody.Frustum: same six
    quads, same corner order, same authoring in absolute heights. gx has make_box (no taper) and
    make_taper (round, radius-based); neither can express "0.145 half-X at the bottom, 0.150 at
    the top, and a different pair on Z", which is what the classic greybox's three trunk sections
    are and therefore what this body has to be.

    Winding is not hand-worked: gx.add_part runs bmesh.ops.recalc_face_normals over the result, so
    the six faces come out consistently outward however they were written. That matters -- the
    classic's own Frustum computes each face's direction for exactly this reason, and the one
    open front-faces-invisible defect in this repo is a hand-wound quad that got it backwards."""
    b = [(-bottom_half_x, y_bottom - origin_y, -bottom_half_z),
         (bottom_half_x, y_bottom - origin_y, -bottom_half_z),
         (bottom_half_x, y_bottom - origin_y, bottom_half_z),
         (-bottom_half_x, y_bottom - origin_y, bottom_half_z)]
    t = [(-top_half_x, y_top - origin_y, -top_half_z),
         (top_half_x, y_top - origin_y, -top_half_z),
         (top_half_x, y_top - origin_y, top_half_z),
         (-top_half_x, y_top - origin_y, top_half_z)]
    faces = [(0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7),
             (4, 5, 6, 7), (3, 2, 1, 0)]
    return b + t, faces


def build():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    body_mat = gx.make_material(BODY_MATERIAL, BODY_COLOUR)
    face_mat = gx.make_material(FACE_MATERIAL, FACE_COLOUR)

    # ==========================================================================================
    # THE RIG BLOCK. Identical in shape to build_greybox.py's -- read that one for WHY each node
    # is where it is; this comment records only that it is deliberately the same tree:
    #
    #   Rig
    #   +- Pose            runtime-owned (lean/roll/hop). Never keyed in a clip.
    #   |  +- Body         runtime-owned (squash-and-stretch scale). Never keyed in a clip.
    #   |     +- Hips      lower trunk slab, at the hip joint
    #   |     +- Waist     the trunk seam and the bend pivot
    #   |        +- Torso     upper trunk slab (origin AT the seam)
    #   |        +- Head      -> EyeL, EyeR, Mouth
    #   |        +- ArmL      -> ForearmL
    #   |        +- ArmR      -> ForearmR
    #   +- ThighL          -> ShinL -> FootL
    #   +- ThighR          -> ShinR -> FootR
    #
    # THE LEGS HANG OFF Rig, NOT OFF Body AND NOT OFF Pose, and that is RIG-1's invariant, not a
    # tidiness choice: outside Body's scale so a squash cannot lift the feet off the ground, and
    # outside Pose's lean so a leaning body does not tip its own legs over.
    # ==========================================================================================
    rig = gx.add_joint("Rig", (0.0, 0.0, 0.0), None)
    pose = gx.add_joint("Pose", (0.0, 0.0, 0.0), rig)
    body = gx.add_joint("Body", (0.0, 0.0, 0.0), pose)

    # THE BODY, LOWER HALF. Origin at the hip joint, geometry from the trunk's underside up to
    # the waist seam. A FRUSTUM since BODY-3: it widens 0.145 -> 0.150 on the way up, which is the
    # classic greybox's hips. Its bottom half-X is 20 mm INSIDE the legs' outer edge, so the trunk
    # does not overhang them and the legs read beside the silhouette rather than under it.
    gx.add_part(
        "Hips",
        frustum(TRUNK_BOTTOM_Y, WAIST_JOINT_Y,
                HIPS_BOTTOM_HALF_X, HIPS_BOTTOM_HALF_Z, WAIST_HALF_X, WAIST_HALF_Z,
                HIP_JOINT_Y),
        (0.0, HIP_JOINT_Y, 0.0), body, body_mat)

    waist = gx.add_joint("Waist", (0.0, WAIST_JOINT_Y, 0.0), body)

    # THE BODY, UPPER HALF. Origin IS the seam, so it starts at its own zero. It reads the SAME
    # pair of half-extents at its bottom that the hips read at their top, so the seam is flush by
    # construction -- and the contract test that pins it can only fail if somebody edits one of
    # those two numbers into a third.
    gx.add_part(
        "Torso",
        frustum(WAIST_JOINT_Y, NECK_JOINT_Y,
                WAIST_HALF_X, WAIST_HALF_Z, TORSO_TOP_HALF_X, TORSO_TOP_HALF_Z,
                WAIST_JOINT_Y),
        (0.0, 0.0, 0.0), waist, body_mat)

    # THE HEAD. A frustum whose TOP is the pinned crown and whose BOTTOM is the neck seam, so it
    # SITS ON the torso rather than overlapping down past it the way BoxKid's cube did. It is
    # wider at its base than the torso is at its top -- that step is what makes it its own volume.
    head = gx.add_part(
        "Head",
        frustum(NECK_JOINT_Y, gx.CROWN_Y,
                HEAD_BOTTOM_HALF_X, HEAD_BOTTOM_HALF_Z, HEAD_TOP_HALF_X, HEAD_TOP_HALF_Z,
                NECK_JOINT_Y),
        (0.0, NECK_JOINT_Y - WAIST_JOINT_Y, 0.0), waist, body_mat)

    # THE FACE. Children of the head so it turns with it, and on the face material so the
    # runtime's per-player tint (which skips exactly these three) leaves them alone.
    #
    # THE EYES ARE SPHERES (BODY-3), not the flat panels BoxKid had: the classic greybox's are
    # Godot SphereMesh at radius 0.046 with 10 radial segments and 6 rings, and gx.make_sphere
    # takes the same two counts. gx.EYE_Y is PINNED -- it is the height the aim ray leaves from
    # and what AvatarProportions reads back off the model -- so the eye CENTRE is placed at it and
    # the sphere is authored about its own origin, exactly as the classic places its SphereMesh.
    for name, sign in (("EyeL", 1.0), ("EyeR", -1.0)):
        gx.add_part(
            name, gx.make_sphere(EYE_RADIUS, 0.0, EYE_SEGMENTS, EYE_RINGS),
            (sign * EYE_SPREAD_X, gx.EYE_Y - NECK_JOINT_Y, EYE_Z),
            head, face_mat)
    gx.add_part(
        "Mouth", gx.make_box(MOUTH_HALF_X, MOUTH_HALF_Y, MOUTH_HALF_Z, 0.0, 0.0),
        (0.0, MOUTH_Y - NECK_JOINT_Y, MOUTH_Z), head, face_mat)

    # THE ARMS. Two straight columns against the trunk's flanks, split at the elbow, each reaching
    # a little above its own pivot so the joint is a nested seam rather than a gap when it bends.
    # Upper arm 0.200, forearm 0.180 -- the human ratio, and the reverse of what this body had.
    for arm_name, forearm_name, sign in (("ArmL", "ForearmL", 1.0), ("ArmR", "ForearmR", -1.0)):
        arm = gx.add_part(
            arm_name,
            slab(ARM_HALF_X, ARM_HALF_Z,
                 ELBOW_JOINT_Y, SHOULDER_JOINT_Y + ARM_RISE, SHOULDER_JOINT_Y),
            (sign * ARM_X, SHOULDER_JOINT_Y - WAIST_JOINT_Y, 0.0), waist, body_mat)
        gx.add_part(
            forearm_name,
            slab(FOREARM_HALF_X, FOREARM_HALF_Z,
                 WRIST_Y, ELBOW_JOINT_Y + FOREARM_RISE, ELBOW_JOINT_Y),
            (0.0, ELBOW_JOINT_Y - SHOULDER_JOINT_Y, 0.0), arm, body_mat)

    # THE LEGS: ThighL -> ShinL -> FootL, and the R side. The names are ANIM-M2b's and they are
    # load-bearing -- AvatarClipDirector resolves ThighL/ThighR by name, and Foot* means a FOOT
    # on this rig, not the grandfathered thigh it used to mean on the -ufflings. (The classic
    # greybox speaks the OTHER dialect: it has no ThighL at all and its FootL IS the thigh. That
    # is not a discrepancy to fix here -- the two dialects live on two different code paths in
    # AvatarVisual, and this body is on the authored one.)
    #
    # The thigh's top is AT the hip joint and 20 mm inside the trunk (the trunk's underside is
    # 0.420, the hip is 0.440), which is what lets the hip rotate without opening a hole.
    for thigh_name, shin_name, foot_name, sign in (("ThighL", "ShinL", "FootL", 1.0),
                                                   ("ThighR", "ShinR", "FootR", -1.0)):
        thigh = gx.add_part(
            thigh_name,
            slab(THIGH_HALF_X, THIGH_HALF_Z, KNEE_JOINT_Y, HIP_JOINT_Y, HIP_JOINT_Y),
            (sign * LEG_SPACING_X, HIP_JOINT_Y, 0.0), rig, body_mat)
        shin = gx.add_part(
            shin_name,
            slab(SHIN_HALF_X, SHIN_HALF_Z,
                 SHIN_BOTTOM_Y, KNEE_JOINT_Y + SHIN_RISE, KNEE_JOINT_Y),
            (0.0, KNEE_JOINT_Y - HIP_JOINT_Y, 0.0), thigh, body_mat)
        # THE FOOT. Its origin is the ANKLE and the ankle is on the ground, so this node's local
        # offset is the whole of the shin's length and its sole sits at exactly y = 0. BODY-3 gave
        # it the SHIN'S cross-section and took its toe away: shin and foot together now occupy
        # exactly the volume the classic greybox's shin occupies, so the ankle is a seam inside the
        # greybox's own leg outline rather than a boot on the end of it.
        gx.add_part(
            foot_name,
            gx.make_box(SHIN_HALF_X, 0.5 * FOOT_HEIGHT, SHIN_HALF_Z, 0.5 * FOOT_HEIGHT, 0.0),
            (0.0, ANKLE_JOINT_Y - KNEE_JOINT_Y, 0.0), shin, body_mat)

    warning = bpy.data.texts.new("READ-ME-FIRST")
    warning.write(WARNING_TEXT)
    return rig


WARNING_TEXT = (
    "boxkid.blend is AUTHORITATIVE. build_boxkid.py is a BOOTSTRAP.\n"
    "\n"
    "GEOMETRY is generated. Move a vertex here and a --force run throws it away; change a\n"
    "number in THE DIMENSION BLOCK in build_boxkid.py instead.\n"
    "\n"
    "ANIMATION ACTIONS are hand-editable and they SURVIVE, because a bare run refuses to\n"
    "overwrite this file at all. Re-export with:\n"
    "    blender --background --python assets/creatures/boxkid/build_boxkid.py -- --export-only\n"
    "\n"
    "THE JOINT HEIGHTS ARE THE CLASSIC GREYBOX'S (BODY-3, 2026-08-29). They were MEASURED off\n"
    "ClassicGreyboxAvatarBody.cs's emitted vertices in engine, not copied off its constants, and\n"
    "they live in THE JOINT BLOCK in build_boxkid.py. Talon's instruction was that this body\n"
    "look exactly like the greybox and differ from it only by being a file. Two numbers are\n"
    "still imported from build_greybox.py -- CROWN_Y and EYE_Y -- because they are pinned and\n"
    "shared. Move any of them and the clip library's gait stops matching this leg.\n"
)


# --- The self-check ---------------------------------------------------------------------------

def self_check():
    """Re-measures the built scene against the contract the rest of the game reads off it.

    Same job as build_greybox.self_check(): a bad number in the block fails HERE, next to the
    number, rather than three suites downstream."""
    bpy.context.view_layer.update()
    parts = {o.name: gx.world_bounds(o) for o in gx.mesh_objects()}
    crown = max(b[3] for b in parts.values())
    floor = min(b[2] for b in parts.values())
    eye_centre = 0.5 * (parts["EyeL"][2] + parts["EyeL"][3])

    required = ("Hips", "Torso", "Head", "Mouth", "EyeL", "EyeR",
                "ArmL", "ArmR", "ForearmL", "ForearmR",
                "ThighL", "ThighR", "ShinL", "ShinR", "FootL", "FootR")
    missing = [n for n in required if n not in parts]
    joints = {o.name for o in bpy.data.objects if o.type == "EMPTY"}
    missing_joints = [n for n in ("Rig", "Pose", "Body", "Waist") if n not in joints]

    # THE WAIST SEAM. Y-flushness is measured; the two rims being the SAME rectangle is a source
    # identity rather than a measurement -- both parts read WAIST_HALF_X/Z, one constant twice --
    # because an AABB cannot report a frustum's top ring separately from its bottom one. The X
    # extents therefore deliberately are NOT compared here the way they were when both halves
    # were the same untapered slab: hips max-X is now the waist (0.150) and torso max-X is the
    # neck (0.158), and asserting those equal would fail on a body that is correct.
    hips, torso, head = parts["Hips"], parts["Torso"], parts["Head"]
    seam_flush = (abs(hips[3] - torso[2]) < 1e-6 and abs(hips[3] - WAIST_JOINT_Y) < 1e-6)

    # THE HEM, and it is the criterion RIG-1 re-proportioned the whole figure around: the trunk's
    # underside must sit strictly INSIDE the legs' outer edge, or the legs disappear under a skirt.
    # GreyboxAssetContractTests asserts the same inequality off the shipped bytes.
    hips_bottom_half_x = HIPS_BOTTOM_HALF_X
    leg_outer_half_x = LEG_SPACING_X + max(THIGH_HALF_X, SHIN_HALF_X)

    # The arm nests INSIDE the torso wall rather than butting against it -- the classic greybox's
    # authored nesting, and what keeps a shoulder from opening a wedge when the arm swings. The
    # wall is the torso's own half-X at shoulder height, interpolated up the frustum.
    torso_t = (SHOULDER_JOINT_Y - WAIST_JOINT_Y) / (NECK_JOINT_Y - WAIST_JOINT_Y)
    torso_half_x_at_shoulder = WAIST_HALF_X + torso_t * (TORSO_TOP_HALF_X - WAIST_HALF_X)
    arm_tuck = torso_half_x_at_shoulder - (ARM_X - ARM_HALF_X)

    thigh_len = parts["ThighL"][3] - parts["ThighL"][2]
    shin_span = (KNEE_JOINT_Y + SHIN_RISE) - SHIN_BOTTOM_Y

    checks = [
        ("every contract part is present",
         not missing, "all sixteen" if not missing else "MISSING: " + ", ".join(missing)),
        ("every rig joint is present",
         not missing_joints, "Rig/Pose/Body/Waist"
         if not missing_joints else "MISSING: " + ", ".join(missing_joints)),
        ("the crown is the pinned height",
         abs(crown - gx.CROWN_Y) < 1e-4, f"{crown:.4f} m (pinned {gx.CROWN_Y:.3f})"),
        ("the sole is on the ground",
         abs(floor) < 1e-4, f"lowest vertex y = {floor:.4f} m"),
        ("the eye centre is the pinned aim height",
         abs(eye_centre - gx.EYE_Y) < 1e-4, f"{eye_centre:.4f} m (pinned {gx.EYE_Y:.3f})"),
        ("the waist seam is flush",
         seam_flush, f"Hips top {hips[3]:.4f} = Torso bottom {torso[2]:.4f} = the waist joint"),
        ("the neck seam is flush",
         abs(torso[3] - head[2]) < 1e-6 and abs(head[2] - NECK_JOINT_Y) < 1e-6,
         f"Torso top {torso[3]:.4f} = Head bottom {head[2]:.4f} = the neck joint"),
        ("the head sits on the trunk and is its own volume",
         HEAD_BOTTOM_HALF_X > TORSO_TOP_HALF_X,
         f"head {2 * HEAD_BOTTOM_HALF_X:.3f} m across at the neck vs torso "
         f"{2 * TORSO_TOP_HALF_X:.3f} m"),
        ("there is no hem -- the trunk is inside the legs",
         hips_bottom_half_x < leg_outer_half_x,
         f"hips half-X {hips_bottom_half_x:.4f} m < leg outer {leg_outer_half_x:.4f} m "
         f"({1000.0 * (leg_outer_half_x - hips_bottom_half_x):.1f} mm of leg proud)"),
        ("the leg is split evenly at the knee",
         abs((HIP_JOINT_Y - KNEE_JOINT_Y) - (KNEE_JOINT_Y - ANKLE_JOINT_Y)) < 1e-6,
         f"thigh {HIP_JOINT_Y - KNEE_JOINT_Y:.3f} m = shin {KNEE_JOINT_Y - ANKLE_JOINT_Y:.3f} m"),
        ("the thigh hangs the whole way from hip to knee",
         abs(thigh_len - (HIP_JOINT_Y - KNEE_JOINT_Y)) < 1e-6,
         f"{thigh_len:.3f} m"),
        ("the arm is longer above the elbow than below it",
         (SHOULDER_JOINT_Y - ELBOW_JOINT_Y) > (ELBOW_JOINT_Y - WRIST_Y),
         f"upper arm {SHOULDER_JOINT_Y - ELBOW_JOINT_Y:.3f} m, "
         f"forearm {ELBOW_JOINT_Y - WRIST_Y:.3f} m"),
        ("the foot swallows the shin's bottom",
         parts["FootL"][3] > parts["ShinL"][2] + 1e-6,
         f"foot top {parts['FootL'][3]:.3f} m > shin bottom {parts['ShinL'][2]:.3f} m"),
        ("the shin is clear of the floor by more than the contract's tolerance",
         parts["ShinL"][2] > 0.005 + 1e-6,
         f"shin bottom {parts['ShinL'][2]:.4f} m (bar: > 0.0050 m), span {shin_span:.3f} m"),
        ("the foot hides inside the shin's own cross-section",
         abs(parts["FootL"][1] - parts["ShinL"][1]) < 1e-6
         and abs(parts["FootL"][5] - parts["ShinL"][5]) < 1e-6,
         f"foot {2 * SHIN_HALF_X:.3f} x {2 * SHIN_HALF_Z:.3f} m = the shin's, and no toe"),
        ("the arm nests inside the torso wall",
         arm_tuck > 0.0,
         f"{1000.0 * arm_tuck:.1f} mm tucked in at the shoulder"),
    ]
    print("")
    print("  BOXKID -- measured off the built scene")
    ok = True
    for label, passed, detail in checks:
        print(f"  [{'PASS' if passed else 'FAIL'}] {label}: {detail}")
        ok = ok and passed
    return ok


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    blend_path = os.path.join(here, "boxkid.blend")
    glb_path = os.path.join(here, "BoxKid.glb")
    args = gx.script_args()
    force = "--force" in args
    export_only = "--export-only" in args

    if export_only:
        if not os.path.exists(blend_path):
            print(f"REFUSING: --export-only needs {blend_path}, and it does not exist.")
            print("          Run without any flag to bootstrap it.")
            sys.exit(2)
        bpy.ops.wm.open_mainfile(filepath=blend_path)
        gx.export_glb(glb_path)
        print(f"  re-exported {glb_path} from the .blend as it stands (no rebuild)")
        return

    if os.path.exists(blend_path) and not force:
        print(f"REFUSING to overwrite {blend_path}.")
        print("  That file is AUTHORITATIVE. It holds hand-editable animation actions that this")
        print("  script does not know about and cannot recover, and a rebuild destroys them.")
        print("")
        print("  Rebuild the geometry from THE DIMENSION BLOCK:")
        print("      ... --python assets/creatures/boxkid/build_boxkid.py -- --force")
        print("  Re-export the .blend as it stands:")
        print("      ... --python assets/creatures/boxkid/build_boxkid.py -- --export-only")
        sys.exit(2)

    build()
    ok = self_check()

    # THE CLIP LIBRARY IS THE GREYBOX'S, RUN AGAINST THIS SCENE. Not a copy of it: that module
    # keys nothing but ROTATIONS on six nodes (ThighL, ThighR, ArmL, ArmR, Waist, Head) whose
    # rest positions it snapshots off whatever scene is open. So the same fourteen clips bind to
    # this body verbatim, and AvatarClipDirector -- which refuses a model missing any of them --
    # is satisfied.
    #
    # BUT THE GAIT SCALES OFF THE LEG, AND BODY-3 SHORTENED THE LEG BY 108 mm. That module's
    # LEG_M is `gx.HIP_JOINT_Y`, evaluated ONCE at ITS import, and everything downstream of it --
    # step length, stance reach, the leg-swing cap, the duty factor -- is derived from that one
    # number. Left alone it would author a 0.548 m stride onto a 0.440 m leg, and the tell is a
    # planted foot that skates a few millimetres every step: exactly the silent failure the old
    # "import the pivots, never copy them" rule existed to prevent, arriving from the other
    # direction now that the pivots are this body's own.
    #
    # So the assignment below is not a shortcut around that rule, it is that rule still being
    # obeyed with one module in between. It is deliberately on the line before the import, which
    # is the only moment at which the value is read, and it is narrow: HIP_JOINT_Y and nothing
    # else. gx's own module-scope constants were all evaluated at ITS import and are unaffected;
    # nothing in this process builds the greybox.
    gx.HIP_JOINT_Y = HIP_JOINT_Y
    import author_greybox_clips
    ok = author_greybox_clips.author(verbose=True, force=True) and ok

    gx.save_blend(blend_path)
    gx.export_glb(glb_path)
    print(f"  wrote {blend_path}")
    print(f"  wrote {glb_path}")
    if not ok:
        sys.exit(1)


if __name__ == "__main__":
    main()
