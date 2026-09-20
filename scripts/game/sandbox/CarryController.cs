using System;
using Godot;

namespace MpFoundation.Game.Sandbox;

/// <summary>
/// Something an avatar can pick up, carry, and drop. Node classes (Carryable,
/// Megaphone) implement this; the logic layer only ever sees the interface, so
/// carry rules are testable headless with plain fakes.
/// </summary>
public interface ICarryable
{
    /// <summary>True while some controller is carrying this item.</summary>
    bool IsHeld { get; }

    /// <summary>Approximate mass, used for encumbrance. Must be &gt; 0.</summary>
    float MassKg { get; }

    void OnPickedUp(CarryController holder);
    void OnDropped();

    /// <summary>Dropped with a launch impulse. Impulse is world-space.</summary>
    void OnThrown(Vector3 impulse);
}

/// <summary>
/// Single-slot pick-up/carry/drop state machine. Pure logic: no scene tree, no
/// physics, no networking. The owning avatar funnels intents in; state changes
/// come out through the events. The future net layer replicates ownership by
/// calling these same methods on remote instances (see SESSION-REPORT seams).
/// </summary>
public sealed class CarryController
{
    /// <summary>Encumbrance per kg carried; tuned so a 4 kg crate costs ~20% speed.</summary>
    private const float SlowdownPerKg = 0.055f;
    private const float MinSpeedFactor = 0.55f;

    public ICarryable? Held { get; private set; }

    /// <summary>
    /// Where carried items should sit, in world space. Supplied by the owning avatar
    /// node; null in pure logic tests (items then simply track no transform).
    /// </summary>
    public Func<Transform3D>? AnchorProvider { get; init; }

    public event Action<ICarryable>? PickedUp;
    public event Action<ICarryable>? Dropped;
    public event Action<ICarryable, Vector3>? Thrown;

    /// <summary>Movement speed multiplier from carried weight (1 = unencumbered).</summary>
    public float SpeedFactor
    {
        get
        {
            ReleaseIfFreed();
            return Held == null ? 1f : ComputeSpeedFactor(Held.MassKg);
        }
    }

    /// <summary>The encumbrance formula in isolation, so the networked path
    /// (<c>SandboxAvatar.ServerTick</c>) can compute the SAME authoritative number this
    /// controller uses for local prediction, from server-owned holder state, instead of trusting
    /// a client-reported speed factor (see RISK-AUDIT-2026-07-12.md 4.1). <paramref
    /// name="massKg"/> must be &gt; 0 (see <see cref="ICarryable.MassKg"/>'s contract), but
    /// that contract is ENFORCED here rather than assumed: <c>MassKg</c> is scene-authorable
    /// (<c>Carryable</c> forwards Godot's <c>Mass</c>), so a designer typo reaches this
    /// formula directly with no layer in between.
    ///
    /// Unguarded, <c>1/(1 + mass*0.055)</c> has a pole at <c>mass == -18.18</c>: the
    /// denominator crosses zero, and <c>Mathf.Max</c> — which is <c>a &gt; b ? a : b</c> —
    /// then SELECTS the huge positive result instead of the floor, so a negative-mass prop
    /// makes the player *faster*. A NaN mass propagates straight through for the same
    /// reason (<c>0.55f &gt; NaN</c> is false, so the NaN is returned) and poisons the
    /// authoritative movement speed on the server.
    ///
    /// Invalid mass is treated as weightless — no encumbrance, never a speed-up, never
    /// non-finite. No logging: this runs every tick on both the prediction and the
    /// authoritative path, and a per-tick warning would be its own defect.</summary>
    public static float ComputeSpeedFactor(float massKg)
    {
        if (!float.IsFinite(massKg) || massKg <= 0f)
            return 1f;

        return Mathf.Max(MinSpeedFactor, 1f / (1f + massKg * SlowdownPerKg));
    }

    /// <summary>
    /// Attempts to pick up an item. Fails if the hands are full, the item is
    /// already held by someone else (first-grab-wins; the net layer will arbitrate
    /// this server-side later, but the local rule must exist regardless), or the
    /// item is a scene node that is dead or mid-deletion — QueueFree is deferred,
    /// so a coin a drain claimed THIS frame still looks valid, and grabbing it
    /// leaves the hands referencing a corpse one frame later.
    /// </summary>
    public bool TryPickUp(ICarryable item)
    {
        if (Held != null || IsDying(item) || item.IsHeld)
            return false;

        Held = item;
        item.OnPickedUp(this);
        PickedUp?.Invoke(item);
        return true;
    }

    private static bool IsDying(ICarryable item) =>
        item is GodotObject o
        && (!GodotObject.IsInstanceValid(o) || (o is Node n && n.IsQueuedForDeletion()));

    /// <summary>A held node freed under us (a drain claiming the coin the frame it was
    /// grabbed, a despawn race) must empty the hands instead of dangling. Swept from
    /// SpeedFactor because every simulating role reads it each tick — the hands can
    /// never dangle longer than one tick.</summary>
    private void ReleaseIfFreed()
    {
        if (Held is not GodotObject o || GodotObject.IsInstanceValid(o))
            return;
        ICarryable item = Held;
        Held = null;
        Dropped?.Invoke(item); // reverts voice range etc.; nobody dereferences the corpse
    }

    /// <summary>Drops the held item in place. False if not carrying anything.</summary>
    public bool Drop()
    {
        if (Held == null)
            return false;

        ICarryable item = Held;
        Held = null;
        item.OnDropped();
        Dropped?.Invoke(item);
        return true;
    }

    /// <summary>Drops the held item with a launch impulse. False if hands are empty.</summary>
    public bool Throw(Vector3 impulse)
    {
        if (Held == null)
            return false;

        ICarryable item = Held;
        Held = null;
        item.OnThrown(impulse);
        Thrown?.Invoke(item, impulse);
        return true;
    }
}
