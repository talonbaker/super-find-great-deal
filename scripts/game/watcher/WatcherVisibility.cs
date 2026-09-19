using System.Collections.Generic;
using Godot;
using MpFoundation.Game.Contracts;

namespace MpFoundation.Game.Watcher;

/// <summary>Stance, worst-concealing last. Ordering is the contract, not the numbers.</summary>
public enum ExposureStance
{
    Prone = 0,
    Crouched = 1,
    Standing = 2,
}

/// <summary>How much of itself a body is advertising through movement.</summary>
public enum ExposureMotion
{
    Still = 0,
    Walking = 1,
    Running = 2,
}

/// <summary>
/// A stand-in producer for <see cref="IVisibilityScore"/>, so R3 can be tested and driven in a
/// lab before the avatar-side stance machine exists. <b>Provisional by design</b> — when the
/// real producer lands (the stance/motion authority on the player), this class is deleted and
/// the watcher is pointed at that instead. The watcher itself only ever sees the interface, so
/// that swap touches nothing in <see cref="WatcherBrain"/>.
///
/// <para><b>One reading of the interface is decided here and needs to survive consolidation:
/// the score is EXPOSURE, not luminance.</b> The interface's own words are "0.0 = effectively
/// unseen, 1.0 = fully exposed", and it lists "inside lit ground vs outside" as a contributor
/// without giving that term a sign. Both signs are defensible — a player in the firelight is
/// certainly the brightest object on the map — but read as luminance the whole mechanic
/// inverts: the highest scorer would permanently be somebody sitting safely at the fire, the
/// watcher would stare at the camp all night, and the one thing it is built to do (pick out
/// whoever is out in the dark) never happens. So lit ground <i>attenuates</i> here: standing in
/// the fire's protection is the least exposed a player can be, because the watcher may not go
/// there. Whoever builds the real producer has to agree with this or the watcher goes blind.</para>
///
/// <para>The "doe run" is not implemented anywhere and is not supposed to be: standing up and
/// running is simply the highest score this table can produce, so attention transfers to that
/// player and off their friends as a consequence of the arithmetic. That is the whole reason
/// target selection was routed through a single number.</para>
/// </summary>
public static class WatcherVisibility
{
    // Prone is not "half of crouched" — it is the difference between a shape and a texture, and
    // the ladder is deliberately steep at that end so the panic drop is worth pressing.
    private static float StanceTerm(ExposureStance s) => s switch
    {
        ExposureStance.Prone => 0.12f,
        ExposureStance.Crouched => 0.45f,
        _ => 1.00f,
    };

    private static float MotionTerm(ExposureMotion m) => m switch
    {
        ExposureMotion.Still => 0.55f,
        ExposureMotion.Walking => 0.80f,
        _ => 1.00f,
    };

    /// <summary>See the class remarks: lit ground is protection, so it attenuates.</summary>
    private static float ExposureTerm(bool insideLitGround) => insideLitGround ? 0.15f : 1.00f;

    /// <summary>The composed score, clamped to the interface's stated [0,1] range.</summary>
    public static float Score(ExposureStance stance, ExposureMotion motion, bool insideLitGround)
        => Mathf.Clamp(StanceTerm(stance) * MotionTerm(motion) * ExposureTerm(insideLitGround), 0f, 1f);
}

/// <summary>
/// A mutable table of per-peer exposure implementing <see cref="IVisibilityScore"/>: the lab's
/// and the tests' way of saying "this player just stood up" without an avatar in the scene.
/// </summary>
public sealed class WatcherVisibilityTable : IVisibilityScore
{
    private readonly Dictionary<int, float> _scores = new();

    /// <summary>Sets a peer's score from a stance/motion/ground description.</summary>
    public void Set(int peerId, ExposureStance stance, ExposureMotion motion, bool insideLitGround)
        => _scores[peerId] = WatcherVisibility.Score(stance, motion, insideLitGround);

    /// <summary>Sets a peer's score directly, for tests that care about a boundary value rather
    /// than about a posture.</summary>
    public void SetRaw(int peerId, float score) => _scores[peerId] = Mathf.Clamp(score, 0f, 1f);

    public void Remove(int peerId) => _scores.Remove(peerId);

    /// <summary>An unknown peer is unseen, not invisible-by-error: 0 is the honest answer and it
    /// keeps a mid-session join from being instantly interesting.</summary>
    public float VisibilityFor(int peerId) => _scores.TryGetValue(peerId, out float v) ? v : 0f;
}
