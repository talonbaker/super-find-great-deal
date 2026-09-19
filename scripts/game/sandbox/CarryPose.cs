namespace MpFoundation.Game.Sandbox;

/// <summary>
/// <b>The two ways a body holds a thing</b> (CARRY-1, 2026-08-17). Talon's own words:
/// <i>"they should have two distinct means of carrying something. If it's something like a net, this
/// should be clear they're holding an object with a handle… like how a person holds a bat or a
/// sword. There is another kind of holding an object which is like the player character is holding a
/// big pot like their arms are out and they are holding it out in front of them."</i>
///
/// <para><b>This is not a new distinction — it is the data model's, finally reaching the body.</b>
/// The carry registers have separated the hand from the arms since 2026-08-08 (an earlier register's
/// doc: <i>"Firewood is therefore carried in the ARMS rather than in a slot"</i>), and canon fact 10 records the arms as
/// "a separate register and not one of the three". The visual layer collapsed both into one boolean;
/// this enum is that boolean widened to the shape the registers already had.</para>
///
/// <para><b>It names a POSE, never a rule about what may be carried.</b> Which register an item routes
/// to is <c>PropManager.RequestGrab</c>'s policy and stays there — the same separation
/// <c>ToolStance</c> keeps between naming a state and authoring a pose for it.</para>
/// </summary>
public enum CarryPose : byte
{
    /// <summary>Nothing in the hand and nothing in the arms. Both arms swing with the gait.</summary>
    None = 0,

    /// <summary><b>A handle, gripped in one hand</b> — the net, a bat, a sword, a camera. The tool
    /// hand reaches to where the tool actually hangs (the carry mount) and <b>the free arm keeps
    /// swinging with the gait</b>, because a person carrying a bat still swings the other arm and
    /// that asymmetry is most of what makes the walk read as natural.</summary>
    Handle = 1,

    /// <summary><b>An armful, supported by both arms</b> — the firewood, a crate, a big pot. Both
    /// hands go forward and slightly apart, under and either side of the load, and <b>both</b> gait
    /// swings are suppressed, because both arms are genuinely occupied.</summary>
    Armful = 2,
}
