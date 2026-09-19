namespace MpFoundation.Game.Sandbox;

/// <summary>
/// The concurrent-3D-voice budget, in one place, as arithmetic a test can check.
///
/// Until packet 1f this number lived as a sentence in three class doc comments
/// (<see cref="SfxLab"/>, <c>SparseSfxEmitter</c>, <c>ChillChatter</c>), all three agreeing and
/// none of them checkable. That was survivable while every consumer was a one-shot renting from
/// one pool. It stops being survivable the moment a second pool exists that holds voices OPEN,
/// because then the ceiling can be exceeded by a configuration change rather than by a bug —
/// somebody raises <see cref="SfxLab.LoopPoolSize"/> by three, every individual class doc is
/// still true, and the mix quietly runs over budget on a full lobby at night.
///
/// So the sentence became this class, and <c>BlindNightSelfTest</c> asserts it.
///
/// <b>The arithmetic, in full.</b>
/// <code>
///   5   VoiceSpeaker      one AudioStreamPlayer3D per REMOTE peer, Protocol.MaxPlayers - 1
/// + 14  SfxLab one-shots  the pooled positional one-shot slots (PoolSize)
/// +  5  SfxLab loops      the looping partition this packet adds (LoopPoolSize)
/// ────
///  24   of a documented ceiling of 24
/// </code>
///
/// It lands exactly on the ceiling, which is deliberate and is the reason
/// <see cref="SfxLab.LoopPoolSize"/> is 5 rather than the 6 the plan's `[playtest: K=6]` might
/// suggest. §15's K=6 is the **impostor-glow** threshold — a draw-call budget. The audible
/// cutoff falls out of a **voice** budget, and the two are different budgets that happen to be
/// described by one letter. Five is what was left after the speakers and the one-shot pool were
/// paid for, and the alternative was taking a slot back off the one-shot pool — which is the pool
/// creature voices live in, and plan §7.3 ("audible presence before visible presence in the
/// dark") is the requirement that pool exists to serve. Shrinking it to widen the fire's audible
/// horizon would have traded the thing the darkness law is FOR against the thing it is ABOUT.
///
/// <b>What is NOT in this sum, and why.</b> Non-positional <c>AudioStreamPlayer</c>s —
/// <c>AmbientBed</c>'s two loop layers, <c>ChillChatter</c>, <c>VoiceCapture</c>'s mic,
/// <see cref="SfxLab.PlayUi"/>'s fire-and-forget UI shots. They are outside the 3D ceiling
/// entirely, which is precisely why the bed was allowed to be continuous at all.
///
/// <b>What this sum is NOT.</b> It is a reservation count, not a measurement: it counts slots
/// that MAY be sounding, not slots that are. Nothing here has been profiled on the GTX 970
/// Forward+ floor, and node count and CPU cost remain separate budgets of which only this one is
/// written down. See <see cref="SfxLab.Live3DVoices"/> for the runtime figure, which is what
/// Phase 5 will actually defend.
/// </summary>
public static class AudioVoiceBudget
{
    /// <summary>The written ceiling. Raising it requires a profile and a stated reason, not a
    /// preference — see the class doc.</summary>
    public const int Ceiling = 24;

    /// <summary>One <c>VoiceSpeaker</c> per remote peer at a full lobby. Six players means five
    /// remote, and the local player never gets one.</summary>
    public const int VoiceSpeakers = Protocol.MaxPlayers - 1;

    public const int OneShotSlots = SfxLab.PoolSize;

    public const int LoopSlots = SfxLab.LoopPoolSize;

    /// <summary>Everything that may hold a 3D voice, added up.</summary>
    public const int Reserved = VoiceSpeakers + OneShotSlots + LoopSlots;

    /// <summary>Headroom left under the ceiling. Zero is legal; negative is a bug the self-test
    /// fails on.</summary>
    public const int Headroom = Ceiling - Reserved;

    /// <summary>Looping voices held back from the fires for creature use. <b>Zero today, and the
    /// zero is a decision rather than an oversight.</b>
    ///
    /// No creature exists yet, and whether a threat's voice even wants a *sustained* layer is
    /// `/direct` + `sound-threat-audio`'s call at each creature's own packet — a growl bed needs
    /// one, a bark does not, and reserving a slot against a shape nobody has specified would take
    /// a fire off the night to hold a seat for a sound that may never sit in it.
    ///
    /// <b>But the collision is real and it is coming</b>, so the seam is written here rather than
    /// discovered later: the loop partition is fully spent on fires, headroom under the ceiling is
    /// zero, and the first creature that wants a continuous positional voice has exactly two
    /// honest options — raise this number (which lowers <see cref="AudibleFireCap"/> by the same
    /// amount, one constant, no redesign) or raise <see cref="Ceiling"/> with a profile behind it.
    /// It may not simply take a sixth loop voice, because there is not one.</summary>
    public const int CreatureLoopReserve = 0;

    /// <summary>How many fires may be audible at once. Derived from the loop partition rather
    /// than declared independently, so the audible-fire cap and the voice budget can never
    /// disagree — a K larger than the pool would not be a louder night, it would be a fire that
    /// silently failed to get a voice and a rule the player cannot learn (§7.2).</summary>
    public const int AudibleFireCap = LoopSlots - CreatureLoopReserve;
}
