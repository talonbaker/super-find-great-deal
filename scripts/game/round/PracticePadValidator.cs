using Godot;
using MpFoundation.Game.Props;
using MpFoundation.Game.Sandbox.Feel;

namespace MpFoundation.Game.Round;

/// <summary>
/// <b>The practice corner's snap pad, made real on the server</b> (HOLD-1, 2026-09-19): a player
/// who puts something down on the pad learns that placing can SNAP, which is the one carry verb
/// the rest of the holding room cannot teach.
///
/// <para><b>Why a validator at all, when <see cref="InteractionSlot"/> already exists.</b>
/// CARRY-1's handoff is explicit: <c>InteractionSlot</c> is a CLIENT-SIDE lab type and the
/// server knows nothing about it, so a pad that only existed as a slot would snap in the feel
/// lab and do nothing whatsoever in a networked session. "The honest shape is a validator that
/// reads the pads the server already knows about and returns
/// <c>PlacementDecision.Snap(pad.RestPose(intended))</c>" — that sentence is this class. The
/// slot node is still what carries the radius, the rest height and the ring a player can see;
/// this only asks it the question on the authoritative side.</para>
///
/// <para><b>It is a pass-through everywhere else</b>, and that is not politeness — it is what
/// keeps every other lane's placement byte-for-byte unchanged. Outside the pad's own catch
/// volume this returns the intended transform untouched, which is the identity case
/// <see cref="PlacementDecision.Allow"/> exists for. It refuses nothing, ever: a practice pad
/// that could say no would be teaching a rule the game does not have.</para>
///
/// <para><b><see cref="Next"/> is here because two lanes want one slot.</b>
/// <c>PropManager.PlacementValidator</c> is a single reference, and TASK-1 registers one for the
/// tower pads. Rather than leave INT-1 to invent a composition, this one chains: it answers for
/// its own pads and hands everything else to the next validator in the line. Wiring TASK-1's in
/// behind this is then one assignment rather than a design decision taken during a merge.</para>
/// </summary>
public sealed class PracticePadValidator : IPlacementValidator
{
    /// <summary>The validator to ask when no practice pad claims this placement. Null means
    /// "free placement", which is the shipped default (<see cref="IPlacementValidator"/>).</summary>
    public IPlacementValidator? Next { get; set; }

    /// <summary>
    /// The pads this validator owns, resolved once by the caller from the world scene.
    ///
    /// <para><b>Not <c>InteractionSlot</c>'s static registry.</b> That registry holds every slot
    /// in the tree, including the feel lab's, and a server-side rule should own the list of
    /// things it enforces rather than inheriting whatever happens to be loaded. It is also the
    /// difference between a validator that can be unit-reasoned about and one whose behaviour
    /// depends on which scene was opened last.</para>
    /// </summary>
    public System.Collections.Generic.IReadOnlyList<InteractionSlot> Pads { get; init; } =
        System.Array.Empty<InteractionSlot>();

    /// <inheritdoc/>
    public PlacementDecision Validate(NetworkedProp prop, Transform3D intended, int holderPeerId)
    {
        InteractionSlot? pad = PadFor(intended.Origin);
        if (pad == null)
            return Next?.Validate(prop, intended, holderPeerId)
                   ?? PlacementDecision.Allow(intended);

        // RestPose is the slot's own answer to "yes, but there", including whether it also
        // straightens the object (SnapRotation). The server re-runs placement integrity on the
        // moved transform afterwards — PropManager does that for every validator that moves a
        // placement, so a pad cannot snap a crate into the table it sits on.
        return PlacementDecision.Snap(pad.RestPose(intended));
    }

    /// <summary>
    /// The pad claiming this release point, or null.
    ///
    /// <para><b>A vertical cylinder, not a sphere</b>, which is <see cref="InteractionSlot"/>'s
    /// own measured shape and its header explains why at length: the hand carries at chest
    /// height and a pad sits at waist height, so a spherical catch volume large enough to reach
    /// the hand is also large enough to swallow the floor beside the table. Horizontal distance
    /// against <c>Radius</c>, vertical against <c>VerticalReach</c>, and the release must be
    /// ABOVE the pad rather than under it.</para>
    ///
    /// <para>Linear over a handful of pads, for the reason the slot registry is linear: this is
    /// asked once, at the instant of a release, not every frame.</para>
    /// </summary>
    private InteractionSlot? PadFor(Vector3 at)
    {
        InteractionSlot? best = null;
        float bestSq = float.MaxValue;
        foreach (InteractionSlot pad in Pads)
        {
            if (!GodotObject.IsInstanceValid(pad) || !pad.IsInsideTree() || !pad.IsFree)
                continue;
            Vector3 p = pad.GlobalPosition;
            float dy = at.Y - p.Y;
            if (dy < -0.05f || dy > pad.VerticalReach)
                continue;
            float dx = at.X - p.X;
            float dz = at.Z - p.Z;
            float flatSq = dx * dx + dz * dz;
            if (flatSq > pad.Radius * pad.Radius || flatSq >= bestSq)
                continue;
            bestSq = flatSq;
            best = pad;
        }
        return best;
    }
}
