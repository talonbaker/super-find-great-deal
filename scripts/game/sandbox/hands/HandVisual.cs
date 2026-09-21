using Godot;

namespace MpFoundation.Game.Sandbox.Hands;

/// <summary>
/// <b>The placeholder hand, and the ONE place a real one replaces it</b> (HANDS-1, 2026-09-20).
///
/// <para>Talon, 2026-09-20: <i>"Don't make any low-poly assets. I will find or purchase or
/// otherwise acquire those myself. We just need to understand if this works as a concept;
/// details later."</i> So a hand is a <see cref="BoxMesh"/> in two proportions — a flatter slab
/// for the open hand, a squarer one for the closed fist — and nothing lands under
/// <c>assets/models/</c>. Grip is that size swap and nothing else: no finger rig, no IK, no
/// animation clip (program RIDE-1 §2.5).</para>
///
/// <para><b>What a real mesh has to be authored to.</b> One <see cref="MeshInstance3D"/> per hand
/// and one place that sets its mesh, so dropping a hand model in is an edit to
/// <see cref="Open"/>/<see cref="Closed"/> and nothing in the reach logic moves. The axes are the
/// contract: <b>+Y is the back of the hand</b> (the palm faces −Y), <b>−Z is the direction the
/// fingers point</b>, and the origin sits in the middle of the palm — which is the point
/// <see cref="FirstPersonHands"/> puts on the grab point. Same convention
/// <c>HandReach.Orient</c> builds a basis for.</para>
/// </summary>
public static class HandVisual
{
    /// <summary>
    /// <b>The render layer both hands live on, exclusively.</b>
    ///
    /// <para>18, one below <c>AvatarVisual.FirstPersonHiddenLayer</c> (19) and two below
    /// <c>OwnBodyRenderLayer</c> (20), so the three "who may see this" layers read as a block and
    /// none of them collides with the 1–3 range the world, props and effects use.</para>
    ///
    /// <para><b>The hands must never be on 19</b>, which is the one layer the first-person lens
    /// DROPS — that layer exists to take the player's own body out of their own eyes, and a hand
    /// that joined it would be invisible to the only camera that is supposed to see it.
    /// <c>AvatarVisual.HideFromFirstPerson</c> walks the visual rig's subtree and cannot reach
    /// these (they hang off the LENS, not off the rig), but the self-test asserts it rather than
    /// trusting the tree shape.</para>
    ///
    /// <para><b>Other players never see them at all</b>, and that needs no layer: the hands are
    /// built only where a <c>FirstPersonCamera</c> is built, which is the one body this process
    /// drives. There is nothing to replicate and nothing to hide (ruling R5).</para></summary>
    public const int RenderLayer = 18;

    /// <summary>The open hand: a flat slab, wider than it is thick, fingers out.</summary>
    public static readonly Vector3 OpenSizeM = new(0.085f, 0.030f, 0.115f);

    /// <summary>...and the closed one: shorter and thicker, so the silhouette change reads as a
    /// fist closing rather than as the hand shrinking. Same width, deliberately — a hand does not
    /// get narrower when it grips.</summary>
    public static readonly Vector3 ClosedSizeM = new(0.085f, 0.068f, 0.082f);

    /// <summary>
    /// The placeholder's colour. A warm tone rather than the avatar's own body colour, which is
    /// per-player and would make one player's hands read as a wall: this is a stand-in for a mesh
    /// Talon supplies, and it only has to be legible while he judges whether a hand at the grab
    /// point reads right.
    ///
    /// <para><b>Light, and that was measured rather than chosen.</b> The first capture run used a
    /// mid brown (0.80, 0.62, 0.50) and the two hands gripping a crate's side faces came out the
    /// same value as the crate's own shadowed face — a picture in which the concept under
    /// judgement is invisible. This is well above every prop shade in the room (the crate's lit
    /// face, the cardboard, the bins) and well above the floor.</para></summary>
    public static readonly Color PlaceholderColour = new(0.95f, 0.80f, 0.68f);

    /// <summary>Builds one hand: a <see cref="MeshInstance3D"/> on <see cref="RenderLayer"/> and
    /// nothing else. The caller owns where it goes.</summary>
    public static MeshInstance3D Build(string name)
    {
        var mesh = new MeshInstance3D
        {
            Name = name,
            Mesh = Open(),
            // No shadow: the hands hang 0.45 m from the lens inside the holder's own capsule, and
            // a shadow caster there rakes the whole room from the player's own light.
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = PlaceholderColour,
                Roughness = 0.85f,
                // The lens has no CameraAttributes (FirstPersonCamera deliberately ships none), so
                // nothing blurs the near field; this is only here so a future one cannot.
                DisableReceiveShadows = true,
                // ...and a floor of its own light. MEASURED, twice: a lit-only hand 0.45 m from
                // the eye sits inside the holder's own capsule, where almost nothing in this
                // deliberately dim supermarket reaches it, and the first two capture runs came
                // back with the hands rendering the same near-black brown as the crate's shadowed
                // face -- a photograph in which the thing under judgement is invisible. Raising
                // the ALBEDO did not fix it, because the problem was the lighting and not the
                // colour. A low emission keeps the shape shading (so the fist still reads as a
                // solid) while guaranteeing the hand is legible in any room, which is the whole
                // job of a placeholder Talon is going to replace.
                EmissionEnabled = true,
                Emission = PlaceholderColour,
                EmissionEnergyMultiplier = 0.45f,
            },
        };
        mesh.Layers = 0;
        mesh.SetLayerMaskValue(RenderLayer, true);
        return mesh;
    }

    /// <summary>The open-hand mesh. <b>The single place a real open-hand model replaces the
    /// primitive.</b></summary>
    public static Mesh Open() => new BoxMesh { Size = OpenSizeM };

    /// <summary>The closed-hand mesh. <b>The single place a real fist model replaces the
    /// primitive.</b></summary>
    public static Mesh Closed() => new BoxMesh { Size = ClosedSizeM };
}
