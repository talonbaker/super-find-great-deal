namespace MpFoundation.Game.Sandbox;

/// <summary>
/// <b>What a prop is made of</b> (SFX-1, 2026-09-19) — and therefore what it sounds like when it
/// is picked up, struck, set down or thrown.
///
/// <para><b>This is deliberately not on the wire.</b> Sound plays client-locally, on every peer,
/// out of the replicated prop state the server already sends: <c>PropManager.ApplyPropState</c>
/// runs identically everywhere and calls <c>Carryable.OnPickedUp</c>/<c>OnDropped</c>/
/// <c>OnPlaced</c>, which fire the presentation events. Every peer instanced the same authored
/// scene, so every peer independently arrives at the same material. Putting a cosmetic tag on the
/// wire would buy nothing and cost a protocol version.</para>
///
/// <para><b>Ordinals are a .tres serialization contract</b>, exactly like
/// <see cref="Sfx"/> and <c>ActorEvent</c>: this is an <c>[Export]</c> on <c>Carryable</c> and is
/// written into every prop prefab as an int. Append only; never reorder.</para>
///
/// <para><b>Wood is 0 because it is the no-change default.</b> A prop that says nothing about its
/// material keeps the shared <c>assets/items/default/prop_presentation.tres</c> — the
/// <c>Pop</c>/<c>Thunk</c> pair the crate and the ball have always had — so nothing that existed
/// before this packet changes sound.</para>
/// </summary>
public enum PropMaterial
{
    /// <summary>The default: the crate, the ball, and anything that has not said otherwise.
    /// Keeps <c>assets/items/default/prop_presentation.tres</c>.</summary>
    Wood = 0,

    /// <summary>A can. Thin shell, inharmonic, rings. <c>assets/items/tin/</c>.</summary>
    Tin = 1,

    /// <summary>A cereal box. Damped panel with something shifting inside; never rings.
    /// <c>assets/items/cardboard/</c>.</summary>
    Cardboard = 2,

    /// <summary>Fruit and veg. Soft, organic, absorbs the hit entirely.
    /// <c>assets/items/produce/</c>.</summary>
    Produce = 3,
}
