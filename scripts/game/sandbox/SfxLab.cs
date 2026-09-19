using System;
using Godot;

namespace MpFoundation.Game.Sandbox;

public enum Sfx
{
    /// <summary>"No built-in sound" — used by presentation profiles whose response is
    /// particle-only or CustomSound-only. Keep first (0) so it is the default.</summary>
    // Ordinals are pinned: EventResponse.Sound serializes this enum as an int in every
    // presentation .tres, so a member removed from the middle (13/14 and 18-21 belonged to
    // since-removed items) leaves a gap
    // rather than shifting its neighbours. Append at 22.
    None = 0,
    Jump = 1,
    Land = 2,
    Bump = 3,
    Stumble = 4,
    Recover = 5,
    Pop = 6,
    Thunk = 7,
    Chirp = 8,
    Oof = 9,
    Step = 10,
    Squeak = 11,
    SqueakLand = 12,
    /// <summary>One fire crackle — a single short pop of filtered noise. Deliberately ONE
    /// crackle rather than a loop of fire: a sparse emitter fires it at randomised intervals with
    /// pitch jitter, which is what makes the fire sound different from one moment to the next
    /// without spending a continuous voice. See <c>SparseSfxEmitter</c>.
    ///
    /// Registered in THRILL-BIBLE.md §10's *wrong silence* row as the repo's first ambient-bed
    /// investment (Issue #152). The crackle STOPPING is deliberately not that device: the fire
    /// going out is a legible, caused, visible event, and §6.3 needs a withdrawal with no
    /// explanation. A cause defuses it.</summary>
    Crackle = 15,

    /// <summary>A short two-or-three-note bird trill for the woods, same sparse-emitter treatment
    /// as <see cref="Crackle"/>. Distinct from <see cref="Chirp"/>, which is a creature
    /// vocalisation owned by presentation profiles — this one is scenery.</summary>
    Birdsong = 16,

    /// <summary>A brief insect buzz that passes close and leaves — the bee/fly texture of a warm
    /// clearing. Sparse emitter, like <see cref="Crackle"/> and <see cref="Birdsong"/>.</summary>
    Buzz = 17,

    /// <summary><b>A goose honk</b> (HONK-1, 2026-09-04): the mic-less player's voice. Talon,
    /// wrapping the playtest — <i>"a simple 'sound' … which will act like a voice input from
    /// players who don't have microphones or don't want to speak … should sound like a goose
    /// honking."</i>
    ///
    /// <para>Appended at 22 because the ordinals above are pinned — see the note at the top of
    /// this enum. Synthesised rather than sampled, like every other member here, so it needs no
    /// licence story and Talon can retune it by editing seven numbers
    /// (<see cref="SfxLab.GooseHonkPcm"/>) instead of finding a new file.</para></summary>
    GooseHonk = 22,

    /// <summary><b>The triumph</b> (CELEBRATE-1, 2026-09-04): the level noticing that the last
    /// bubble is gone. Talon, wrapping the playtest — <i>"when they get 100 out of 100, I would
    /// like a small sound to play like a triumph sound"</i>. <b>Small</b> is the operative word
    /// and it is a constraint, not a hedge: this is a four-note glass arpeggio that is over in
    /// just over a second, not a fanfare.
    ///
    /// <para>Appended at 23 because the ordinals above are pinned — see the note at the top of
    /// this enum. Synthesised like every other member here, so it ships no audio file and Talon
    /// can retune it by editing the named constants beside
    /// <see cref="SfxLab.TriumphPcm"/>.</para></summary>
    Triumph = 23,

    // 24–35 reserved: Bang (DOOR-1), material voices (SFX-1)
    //
    // Those two lanes branched from the same base as CLOCK-1 and cannot see each other, so the
    // gap is deliberate and this block is a NO-OP at merge rather than a renumbering: DOOR-1 takes
    // 24 (Bang) and SFX-1 takes 25–35 (TinPick, TinClank, TinBuzz, TinTick, CardPick, CardThud,
    // CardSettle, ProducePick, ProduceThump, ProducePlop, Whoosh). The ordinals above are pinned
    // by EventResponse.Sound's .tres serialization — see the note at the top of this enum — so a
    // lane that compacted this gap would silently re-point every profile that referenced one.

    /// <summary><b>The round starting</b> (CLOCK-1, 2026-09-19): two rising notes, ~0.35 s, played
    /// flat on every peer at the Holding → Hiding edge. The one cue in the round's palette that is
    /// an invitation rather than a deadline, which is why it rises.</summary>
    ChimeUp = 36,

    /// <summary><b>One second of the last ten</b> (CLOCK-1): a 40 ms wood-block click, positional
    /// from every <c>RoundClock</c>, so a hider head-down in an aisle hears the room counting. The
    /// pitch bias for the last seconds is the CALLER's (see <c>RoundAudioCues.TickPitchBias</c>) —
    /// one recipe, played higher, rather than three recipes that could drift apart.</summary>
    Tick = 37,

    /// <summary><b>Confirm</b> (CLOCK-1): one note, ~0.2 s, flat. The hider saying "I am done"
    /// before the clock said it for them.</summary>
    Note = 38,

    /// <summary><b>The hide buzzer</b> (CLOCK-1): ~0.5 s, positional from every clock and flat on
    /// top. Named <c>RoundBuzz</c> and not <c>Buzz</c> — the packet's name — because
    /// <see cref="Buzz"/> (17) is already taken by the outdoor insect buzz and an enum cannot
    /// carry the name twice. See the handoff's ordinal table.</summary>
    RoundBuzz = 39,

    /// <summary><b>The grace buzzer</b> (CLOCK-1): lower and shorter than
    /// <see cref="RoundBuzz"/>, ~0.25 s. The hide was not retrievable at the buzzer and the loop
    /// bought the hider one 10 s extension; a shorter, lower buzz says "not yet" rather than
    /// "time".</summary>
    BuzzShort = 40,

    /// <summary><b>The seek timeout</b> (CLOCK-1): two blasts, ~0.5 s total. Deliberately the only
    /// cue in the palette that repeats itself — the seeker ran out of clock, and a single buzz is
    /// already spoken for by the hide.</summary>
    BuzzDouble = 41,

    /// <summary><b>The world going home</b> (CLOCK-1): a soft filtered-noise whoosh at the
    /// Tally → Holding reset edge, flat.
    ///
    /// <para><b>Named <c>ResetWhoosh</c> rather than <c>Whoosh</c> on purpose.</b> SFX-1 appends a
    /// general-purpose <c>Whoosh</c> at 35 on its own branch for a thrown prop's release, and two
    /// lanes that cannot see each other must not both try to own one name at one ordinal. The
    /// orchestrator may collapse this onto SFX-1's <c>Whoosh</c> at merge; until then this is the
    /// reset's own cue and nothing else reads it.</para></summary>
    ResetWhoosh = 42,
}

/// <summary>
/// The looping palette (packet 1f). Kept apart from <see cref="Sfx"/> on purpose — see
/// <see cref="SfxLab.GetLoop"/> for why a looping member inside the one-shot enum would be a trap
/// rather than a convenience.
/// </summary>
public enum SfxLoop
{
    /// <summary>A fire's continuous body: low gusting rumble plus sparse bright snaps, with
    /// 700–2200 Hz deliberately left clear for creature voices.
    ///
    /// This is the layer plan §1.5's darkness law actually rests on — the thing a blind player
    /// takes a bearing off. It does NOT replace <see cref="Sfx.Crackle"/>'s sparse near-field
    /// pops: the two are a body layer and a detail layer at different ranges, which is ordinary
    /// soundscape construction, and the sparse layer stays short-range so the two never pile up
    /// at the pit.
    ///
    /// THRILL-BIBLE §10 (*Position without judgment*) governs what it may MEAN: where a fire is
    /// and how far, never whether anything is standing next to it.</summary>
    FireBody,
}

/// <summary>
/// Procedurally synthesized cartoon SFX — tiny sine sweeps, thumps, and warbles baked
/// to 48 kHz AudioStreamWav at first use. No asset files, no licensing, and the whole
/// palette is tunable from one place. Playback jitters pitch per shot so repeated
/// hops/bumps never sound machine-gun identical (organic > sampled, at this fidelity).
///
/// Playback goes through a fixed POOL of AudioStreamPlayer3D nodes (Bible §4: pooled
/// audio path, never spawn-per-sound). The pool is bounded well under the §4 concurrent
/// cap; when every slot is busy the OLDEST shot is stolen — with 6 players' worth of
/// footsteps the ear can't tell, and the node count stays flat. Pool nodes live under
/// the scene root (reparenting on scene change is handled by re-anchoring lazily), and
/// route to a dedicated "Sfx" bus so gameplay sound can be mixed/ducked as one group.
///
/// <b>"The oldest shot is stolen" is true as of 2026-08-13, and was not before.</b> The sentence
/// above shipped with the class; the code underneath it stole <c>Pool[_next]</c>, a cursor that
/// advanced only when a steal happened — so after any free slot had been handed out (the common
/// case, since the free-list scan always prefers the lowest index) the cursor could point at the
/// NEWEST sound in the pool. The path was unreachable, so nothing had ever heard it be wrong, but
/// <c>WaterFx.TryTakeVoice</c> had already reasoned from the doc's version in writing its own
/// policy. Remote footsteps make overflow reachable for the first time, which promoted the
/// contradiction from latent to live, so it was resolved in the direction the doc states: the doc
/// is the correct spec, because the whole justification for stealing rather than refusing — "a
/// stolen one-shot is a footstep that lost its last few milliseconds" — is only true of the
/// oldest shot. Stealing the newest loses a whole sound, which is audible. See <see cref="Rent"/>.
/// </summary>
public static class SfxLab
{
    private const int SampleRate = 48000; // matches the project's pinned mix rate

    /// <summary>One-shot pool size. §4 budgets ≤24 concurrent 3D players INCLUDING the 5 voice
    /// speakers and ambience; 14 one-shot slots keeps the worst case comfortably inside.
    ///
    /// Public since packet 1f so <see cref="AudioVoiceBudget"/> can add it up rather than restate
    /// it — the whole budget now lands exactly on the ceiling, and a number that load-bearing
    /// should not be private to the class that happens to spend it. **Deliberately NOT reduced to
    /// make room for the looping partition:** this is the pool creature voices live in, and plan
    /// §7.3 is the reason it exists.</summary>
    public const int PoolSize = 14;

    /// <summary>Looping partition size, and therefore the audible-fire cap
    /// (<see cref="AudioVoiceBudget.AudibleFireCap"/>). Five is what the ≤24 ceiling had left
    /// after 5 voice speakers and the 14 one-shot slots — see <see cref="AudioVoiceBudget"/> for
    /// the full sum and for why this is not the plan's rendering-side K.</summary>
    public const int LoopPoolSize = 5;

    /// <summary>Name of the SFX mix bus (created lazily, routed to Master).</summary>
    public const string Bus = "Sfx";

    private static readonly System.Collections.Generic.Dictionary<Sfx, AudioStreamWav> Cache = new();
    private static readonly System.Collections.Generic.Dictionary<SfxLoop, AudioStreamWav> LoopCache = new();
    private static readonly Random Rng = new();

    private static readonly System.Collections.Generic.List<AudioStreamPlayer3D> Pool = new();

    /// <summary>Rent order, index-parallel to <see cref="Pool"/>: a monotonically increasing stamp
    /// written every time a slot is handed out. The smallest stamp is the oldest sound, which is
    /// the steal victim.
    ///
    /// A counter rather than <c>Time.GetTicksMsec()</c> deliberately — it is exact (two shots in
    /// the same millisecond still have a defined order, so a full-pool frame has no ties to break
    /// arbitrarily), it needs no clock, and it makes <see cref="OldestRentedIndex"/> a pure
    /// function the xUnit suite can hammer without an engine.</summary>
    private static readonly System.Collections.Generic.List<ulong> PoolRentedAt = new();

    private static ulong _rentCounter;

    /// <summary>The looping partition. Separate LIST, not a separate system: same class, same bus
    /// discipline, same anchoring and reanchoring, same budget — one path, as plan §15 requires.
    /// What differs is the lifecycle, and it has to, because the one-shot pool's free-list test is
    /// <c>!p.Playing</c> and a continuous layer is <c>Playing</c> forever. A loop rented from
    /// <see cref="Pool"/> would therefore never be returned: it would permanently shrink the
    /// one-shot pool, or be stolen mid-sound by the next footstep. Two partitions with explicit
    /// acquire/release is the smallest correct shape, and it is what lets the sum in
    /// <see cref="AudioVoiceBudget"/> be a fact rather than a hope.</summary>
    private static readonly System.Collections.Generic.List<AudioStreamPlayer3D> LoopPool = new();
    private static readonly System.Collections.Generic.HashSet<AudioStreamPlayer3D> LoopsInUse = new();

    /// <summary>Fire-and-forget non-positional one-shot for UI/front-door beats (menus,
    /// splash) — outside the 3D pool entirely, since there's no world position to attach
    /// to. A plain AudioStreamPlayer frees itself when done.</summary>
    /// <param name="pitchBias">Added to the pitch scale BEFORE the jitter (CLOCK-1, 2026-09-19).
    /// It is a deliberate, caller-chosen transposition — the round's last three ticks rise — as
    /// distinct from <paramref name="pitchJitter"/>, which is per-shot randomness whose whole job
    /// is to be unnoticed. Defaulted to zero, so no existing call site moves by a cent.</param>
    public static void PlayUi(Sfx kind, float volumeDb = -6f, float pitchJitter = 0f,
        float pitchBias = 0f)
    {
        SceneTree? tree = Engine.GetMainLoop() as SceneTree;
        Node? root = tree?.Root;
        if (root == null)
            return;
        EnsureBus();
        var player = new AudioStreamPlayer
        {
            Stream = Get(kind),
            Bus = Bus,
            VolumeDb = volumeDb,
            PitchScale = 1f + pitchBias + (float)(Rng.NextDouble() * 2 - 1) * pitchJitter,
        };
        root.AddChild(player);
        player.Finished += player.QueueFree;
        player.Play();
    }

    /// <summary>Pooled positional one-shot for callers with their own streams (LabAmbience
    /// et al) — the single §4-compliant playback path for ALL gameplay one-shots.
    ///
    /// <paramref name="bus"/> defaults to this class's own "Sfx" bus, which is where gameplay
    /// feedback belongs. Scenery passes <c>AudioBuses.Scenery</c> instead, so the world's
    /// ambience can be trimmed, ducked or turned down as a group without dragging every
    /// footstep and shutter click with it. The bus is set per playback for the same reason
    /// <see cref="AudioStreamPlayer3D.VolumeDb"/> is: a pool slot is borrowed, not owned, and
    /// the next caller may want a different one.
    ///
    /// <para><paramref name="unitSize"/> is null by default and the pool slot then keeps whatever
    /// the engine's own default is — so no existing call site's falloff changes by a decibel.
    /// It exists for HONK-1: a honk stands in for proximity VOICE, so it has to attenuate on
    /// voice's curve (<c>VoiceConfig.ProximityUnitSize</c>) rather than on the one-shot pool's,
    /// and a caller that must match another system's falloff needs both halves of the curve, not
    /// just the cutoff. Deliberately nullable rather than defaulted to a literal 10: writing the
    /// engine default down here would be a second copy of a number this file does not own.</para>
    ///
    /// <para><paramref name="pitchBias"/> is CLOCK-1's (2026-09-19), on the same "additive, zero
    /// by default, nobody else moves" contract as <paramref name="unitSize"/>: a deliberate
    /// transposition chosen by the caller (the round's last three ticks rise) as opposed to the
    /// per-shot randomness <paramref name="pitchJitter"/> exists to hide.</para></summary>
    public static void PlayStream3D(Node parent, Vector3 globalPos, AudioStream stream,
        float volumeDb = -6f, float pitchJitter = 0.08f, float maxDistance = 40f,
        string? bus = null, float? unitSize = null, float pitchBias = 0f)
    {
        AudioStreamPlayer3D? player = Rent(parent);
        if (player == null)
            return; // no valid tree to play into (headless teardown race) — sound is skippable
        // Fall back rather than assign a name the mixer does not know: a bus that has not been
        // created yet (boot order, a lab scene loaded directly) would otherwise make Godot spam
        // an error per shot and drop the sound entirely, which is a far worse failure than
        // playing it on the default bus.
        player.Bus = bus != null && AudioServer.GetBusIndex(bus) >= 0 ? bus : Bus;
        player.Stream = stream;
        player.VolumeDb = volumeDb;
        player.PitchScale = 1f + pitchBias + (float)(Rng.NextDouble() * 2 - 1) * pitchJitter;
        player.MaxDistance = maxDistance;
        // ALWAYS written, never conditionally: a pool slot is borrowed, and a setting left behind
        // by the previous borrower is a falloff curve the next caller never asked for. The
        // "unchanged" case therefore restores the engine's own default rather than skipping the
        // write — see PoolDefaultUnitSize for where that number comes from.
        player.UnitSize = unitSize ?? _poolDefaultUnitSize ?? player.UnitSize;
        player.GlobalPosition = globalPos;
        player.Play();
    }

    /// <summary>The engine's own <see cref="AudioStreamPlayer3D.UnitSize"/> default, sampled once
    /// off the first pool node <see cref="Rent"/> constructs. READ rather than typed: a literal
    /// here would be a second copy of a number this file does not own, and the day Godot changed
    /// it every one-shot in the game would quietly move on the distance curve. Null only before
    /// the pool has ever grown a node, which cannot happen on the line that reads it — every
    /// caller comes through <see cref="Rent"/> first — so the third fallback there is a
    /// no-op write, not a policy.</summary>
    private static float? _poolDefaultUnitSize;

    /// <summary>An idle pool player (preferring free ones, else stealing the oldest),
    /// anchored under the current scene tree root so scene changes can't orphan-leak it.</summary>
    private static AudioStreamPlayer3D? Rent(Node context)
    {
        if (!GodotObject.IsInstanceValid(context) || !context.IsInsideTree())
            return null;
        Node anchor = context.GetTree().CurrentScene ?? context.GetTree().Root;

        // Drop pool entries whose node died with a previous scene. Index-parallel with
        // PoolRentedAt, so the two lists are pruned together — a List.RemoveAll on one of them
        // alone would silently shift every stamp onto the wrong slot.
        for (int i = Pool.Count - 1; i >= 0; i--)
        {
            if (GodotObject.IsInstanceValid(Pool[i]))
                continue;
            Pool.RemoveAt(i);
            PoolRentedAt.RemoveAt(i);
        }

        // Prefer a slot that is not currently playing.
        for (int i = 0; i < Pool.Count; i++)
        {
            if (Pool[i].Playing)
                continue;
            PoolRentedAt[i] = ++_rentCounter;
            return Reanchor(Pool[i], anchor);
        }
        if (Pool.Count < PoolSize)
        {
            EnsureBus();
            var fresh = new AudioStreamPlayer3D { Name = $"SfxPool{Pool.Count}", Bus = Bus };
            _poolDefaultUnitSize ??= fresh.UnitSize; // before any borrower has touched it
            anchor.AddChild(fresh);
            Pool.Add(fresh);
            PoolRentedAt.Add(++_rentCounter);
            return fresh;
        }
        // All busy: steal the genuinely oldest sound. See the class doc for why this is oldest
        // rather than the round-robin cursor it used to be.
        int victim = OldestRentedIndex(PoolRentedAt);
        if (victim < 0)
            return null;
        OneShotSteals++;
        PoolRentedAt[victim] = ++_rentCounter;
        Pool[victim].Stop();
        return Reanchor(Pool[victim], anchor);
    }

    /// <summary>Index of the slot rented longest ago, or -1 for an empty pool. Pure, and public
    /// for exactly that reason: the steal policy is the one part of the pool a Godot-free test can
    /// reach, and it is the part that was wrong for the whole life of the class.</summary>
    public static int OldestRentedIndex(System.Collections.Generic.IReadOnlyList<ulong> rentedAt)
    {
        if (rentedAt == null || rentedAt.Count == 0)
            return -1;
        int best = 0;
        for (int i = 1; i < rentedAt.Count; i++)
        {
            if (rentedAt[i] < rentedAt[best])
                best = i;
        }
        return best;
    }

    private static AudioStreamPlayer3D Reanchor(AudioStreamPlayer3D p, Node anchor)
    {
        if (p.GetParent() != anchor)
        {
            p.GetParent()?.RemoveChild(p);
            anchor.AddChild(p);
        }
        return p;
    }

    // --- The looping partition (packet 1f) ------------------------------------------------
    //
    // THE VOICE-STEAL POLICY, STATED ONCE AND DELIBERATELY DIFFERENT PER PARTITION.
    //
    //   One-shots: steal the oldest (Rent, above). A stolen one-shot is a footstep that lost its
    //   last few milliseconds in a mix already carrying thirteen others; nobody can hear the
    //   theft, and the alternative — refusing — is a footstep that never plays at all, which IS
    //   audible. Note that the justification depends on the victim being the OLDEST: it was
    //   written here while the code stole a round-robin cursor's target, which can be the newest
    //   sound in the pool, and losing a whole sound is not the same trade at all. Corrected
    //   2026-08-13 — the policy stated here is now the policy implemented.
    //
    //   Loops: NEVER steal. Refuse, and return null.
    //
    // The asymmetry is the point. Stealing a continuous voice means cutting a sustained layer
    // dead mid-note, and a player mid-fade is the most attractive steal target in the pool
    // precisely because it is quietest — so a stealing loop pool would preferentially destroy the
    // fades that exist to stop the cull being audible. Worse, an uncaused cut in the ambient bed
    // is THRILL-BIBLE §6.3's wrong-silence device, which is `fresh`, is the strongest entry in
    // that register, and may only be spent by a directed beat through `/direct`. A pool that
    // steals loops would fire it several times a night, by accident, for free. See §10's
    // wrong-silence row, which now records this refusal explicitly.
    //
    // Reclamation is therefore the CALLER's, and it is a two-step: fade the loop out, then
    // release it. FireAudioDirector does exactly that, which is why a rank swap takes a fade
    // rather than a frame.

    /// <summary>Takes a voice from the looping partition, or returns null if all
    /// <see cref="LoopPoolSize"/> are in use (this pool never steals — see the policy note
    /// above). The returned node is the caller's until <see cref="ReleaseLoop"/>: the caller owns
    /// its position and its volume, and the pool owns its existence and its bus.
    ///
    /// Starts silent at -80 dB and at a random offset into the stream. Both matter. Silent,
    /// because a loop that begins at full level has cut IN, which is the same audible event as
    /// cutting out and is no more caused. Offset, because five fires sharing one cached buffer
    /// would otherwise phase-lock and read as one enormous fire rather than five.</summary>
    public static AudioStreamPlayer3D? AcquireLoop(Node context, AudioStream stream,
        string? bus = null, float maxDistance = 60f, float unitSize = 8f,
        float panningStrength = 1.4f, float pitchJitter = 0.06f)
    {
        if (!GodotObject.IsInstanceValid(context) || !context.IsInsideTree())
            return null;
        Node anchor = context.GetTree().CurrentScene ?? context.GetTree().Root;

        LoopPool.RemoveAll(p => !GodotObject.IsInstanceValid(p));
        LoopsInUse.RemoveWhere(p => !GodotObject.IsInstanceValid(p));

        AudioStreamPlayer3D? player = null;
        foreach (AudioStreamPlayer3D p in LoopPool)
        {
            if (!LoopsInUse.Contains(p))
            {
                player = p;
                break;
            }
        }
        if (player == null)
        {
            if (LoopPool.Count >= LoopPoolSize)
                return null; // budget reached: refuse, never steal
            EnsureBus();
            player = new AudioStreamPlayer3D { Name = $"SfxLoop{LoopPool.Count}", Bus = Bus };
            anchor.AddChild(player);
            LoopPool.Add(player);
        }

        Reanchor(player, anchor);
        player.Bus = bus != null && AudioServer.GetBusIndex(bus) >= 0 ? bus : Bus;
        player.Stream = stream;
        player.VolumeDb = SilentDb;
        player.MaxDistance = maxDistance;
        // UnitSize and PanningStrength are the two dials that decide whether a blind player can
        // BEAR a sound rather than merely hear it (plan §1.5). Larger UnitSize keeps a distant
        // fire present instead of collapsing it into the noise floor; PanningStrength above 1
        // exaggerates the inter-aural difference, which is the only directional information a
        // stereo headphone mix carries. Values are starting points — nothing here has been mixed
        // by ear, let alone on the floor spec.
        player.UnitSize = unitSize;
        player.PanningStrength = panningStrength;
        player.PitchScale = 1f + (float)(Rng.NextDouble() * 2 - 1) * pitchJitter;
        LoopsInUse.Add(player);

        // GetLength(), not Data.Length/MixRate: AudioStreamWav.Data marshals the ENTIRE PCM
        // buffer out of the engine on every access, so deriving the duration from it would copy
        // ~1.3 MB per acquire — five of them on a single rank shuffle — to learn one float.
        double length = stream.GetLength();
        player.Play(length > 0.0 ? (float)(Rng.NextDouble() * length) : 0f);
        return player;
    }

    /// <summary>Hands a looping voice back. Idempotent and null-safe: releasing twice, or
    /// releasing a node that was never acquired, is a no-op rather than a corruption of the
    /// in-use set — a double release that silently freed somebody else's voice would present as
    /// a fire that goes quiet for no reason, which is the one failure this whole packet is
    /// arranged to prevent.</summary>
    public static void ReleaseLoop(AudioStreamPlayer3D? player)
    {
        if (player == null || !GodotObject.IsInstanceValid(player))
        {
            LoopsInUse.RemoveWhere(p => !GodotObject.IsInstanceValid(p));
            return;
        }
        if (!LoopsInUse.Remove(player))
            return;
        player.Stop();
        player.Stream = null;
        player.VolumeDb = SilentDb;
    }

    /// <summary>True silence for a 3D player. Godot treats -80 dB as the practical floor and
    /// every fade in this packet lands on it exactly rather than approaching it.</summary>
    public const float SilentDb = -80f;

    // --- Instrumentation (Issue #182: Phase 5 cannot defend what it cannot see) -------------

    /// <summary>Looping voices currently held. Never exceeds <see cref="LoopPoolSize"/>.</summary>
    public static int ActiveLoops
    {
        get
        {
            LoopsInUse.RemoveWhere(p => !GodotObject.IsInstanceValid(p));
            return LoopsInUse.Count;
        }
    }

    /// <summary>One-shot slots currently sounding. Note this is genuinely live — it counts
    /// <c>Playing</c>, not slots allocated — so it is the only figure here that reflects load
    /// rather than reservation.</summary>
    public static int ActiveOneShots
    {
        get
        {
            int n = 0;
            foreach (AudioStreamPlayer3D p in Pool)
            {
                if (GodotObject.IsInstanceValid(p) && p.Playing)
                    n++;
            }
            return n;
        }
    }

    /// <summary>Nodes actually allocated in each partition — how much of the reservation has been
    /// realised. The pools grow lazily, so early in a session these sit below their caps.</summary>
    public static int AllocatedOneShotSlots => Pool.Count;

    public static int AllocatedLoopSlots => LoopPool.Count;

    /// <summary>How many times a full one-shot pool has had a sound taken off it since the last
    /// <see cref="ResetPoolsForTest"/>. Zero for the whole life of the class until remote
    /// footsteps made overflow reachable, which is exactly why it is now worth counting: a steal
    /// is not an error, but a mix that steals routinely has quietly stopped being able to promise
    /// that the cue you needed was the one that played.</summary>
    public static long OneShotSteals { get; private set; }

    /// <summary>The one-shot pool's nodes in slot order. Test-fixture plumbing only, and it exists
    /// because the obvious alternative does not work: <see cref="ResetPoolsForTest"/> frees slots
    /// with <c>QueueFree</c>, which is deferred, so a synchronous self-test that looked pool nodes
    /// up by name would find the PREVIOUS check's dying nodes — Godot uniquifies the new ones'
    /// names behind its back. That failure looks exactly like a steal-policy bug.</summary>
    public static System.Collections.Generic.IReadOnlyList<AudioStreamPlayer3D> OneShotSlotsForTest
        => Pool;

    /// <summary>Live 3D voices this class is responsible for, right now: sounding one-shots plus
    /// held loops. Deliberately excludes <c>VoiceSpeaker</c>'s five, which this class cannot see
    /// and does not own — <see cref="AudioVoiceBudget"/> adds those in.</summary>
    public static int Live3DVoices => ActiveOneShots + ActiveLoops;

    /// <summary>Highest <see cref="Live3DVoices"/> observed since the last
    /// <see cref="ResetPeak"/>. Sampled by callers rather than continuously, because the
    /// interesting figure is the peak under a bench run and nothing should pay for a per-frame
    /// max it never reads.</summary>
    public static int PeakLive3DVoices { get; private set; }

    /// <summary>Samples <see cref="Live3DVoices"/> into <see cref="PeakLive3DVoices"/> and
    /// returns it.</summary>
    public static int SamplePeak()
    {
        int now = Live3DVoices;
        if (now > PeakLive3DVoices)
            PeakLive3DVoices = now;
        return now;
    }

    public static void ResetPeak() => PeakLive3DVoices = 0;

    /// <summary>Drops every pooled node and clears both partitions. Test-fixture plumbing only —
    /// it exists so a self-test can measure a partition from a known-empty state instead of
    /// inheriting whatever a previous check left behind, which is how a budget assertion ends up
    /// passing for the wrong reason.</summary>
    public static void ResetPoolsForTest()
    {
        foreach (AudioStreamPlayer3D p in Pool)
        {
            if (GodotObject.IsInstanceValid(p))
                p.QueueFree();
        }
        foreach (AudioStreamPlayer3D p in LoopPool)
        {
            if (GodotObject.IsInstanceValid(p))
                p.QueueFree();
        }
        Pool.Clear();
        PoolRentedAt.Clear();
        LoopPool.Clear();
        LoopsInUse.Clear();
        _rentCounter = 0;
        OneShotSteals = 0;
        ResetPeak();
    }

    private static void EnsureBus()
    {
        if (AudioServer.GetBusIndex(Bus) >= 0)
            return;
        int idx = AudioServer.BusCount;
        AudioServer.AddBus(idx);
        AudioServer.SetBusName(idx, Bus);
        AudioServer.SetBusSend(idx, "Master");
    }

    public static AudioStreamWav Get(Sfx kind)
    {
        if (Cache.TryGetValue(kind, out AudioStreamWav? cached))
            return cached;

        float[] samples = kind switch
        {
            Sfx.None => new float[SampleRate / 100], // 10 ms of silence; never played by Presentation, defensive only
            Sfx.Jump => EffortGrunt(0.14f),
            Sfx.Land => Thump(0.12f, 85f, noise: 0.5f),
            Sfx.Bump => Bonk(0.11f, 175f),
            Sfx.Stumble => Warble(0.38f, 520f, 180f),
            Sfx.Recover => Sweep(0.09f, 350f, 780f),
            Sfx.Pop => Sweep(0.06f, 420f, 950f),
            Sfx.Thunk => Thump(0.15f, 110f, noise: 0.3f),
            Sfx.Chirp => TwoNote(0.16f, 620f, 830f),
            Sfx.Oof => Oof(0.16f),
            Sfx.Step => Thump(0.05f, 150f, noise: 0.6f),
            // Dog-chew-toy squeak: short, high, rising "eek". Higher f0/f1 than the old
            // low honk, so it reads as a rubber-toy squeak and carries at a subtle volume.
            Sfx.Squeak => Squeak(0.05f, 1500f, 2050f),
            Sfx.SqueakLand => Squeak(0.07f, 1250f, 1650f),
            Sfx.Crackle => Crackle(0.07f),
            Sfx.Birdsong => Birdsong(0.30f),
            Sfx.Buzz => Buzz(0.45f),
            Sfx.GooseHonk => GooseHonkPcm(),
            Sfx.Triumph => TriumphPcm(),
            // The round's palette (CLOCK-1). Every one of these is public and returns raw PCM for
            // the same reason the two above are: the Godot-free unit suite asserts each one is
            // non-silent, finite and under 0 dBFS without an audio device.
            Sfx.ChimeUp => ChimeUpPcm(),
            Sfx.Tick => TickPcm(),
            Sfx.Note => NotePcm(),
            Sfx.RoundBuzz => RoundBuzzPcm(),
            Sfx.BuzzShort => BuzzShortPcm(),
            Sfx.BuzzDouble => BuzzDoublePcm(),
            Sfx.ResetWhoosh => ResetWhooshPcm(),
            _ => Sweep(0.05f, 400f, 400f),
        };

        var stream = ToWav(samples);
        Cache[kind] = stream;
        return stream;
    }

    /// <summary>The looping palette, rendered once and cached, with <c>LoopMode.Forward</c> set —
    /// which <see cref="ToWav"/> deliberately never does, and which is the single reason
    /// <see cref="Sfx"/> could not carry a sustained layer.
    ///
    /// A separate enum from <see cref="Sfx"/>, and that is a safety property rather than
    /// tidiness: <see cref="Sfx"/> is what <c>[Export]</c>ed emitter fields and presentation
    /// profiles select from, and a looping member visible there could be handed to
    /// <see cref="PlayStream3D"/>. That call would put a never-ending stream into the one-shot
    /// pool, whose free-list test is <c>!p.Playing</c> — the slot would never come back, the pool
    /// would shrink by one permanently, and nothing would report it. Two enums make that
    /// unsayable instead of merely discouraged.</summary>
    public static AudioStreamWav GetLoop(SfxLoop kind)
    {
        if (LoopCache.TryGetValue(kind, out AudioStreamWav? cached))
            return cached;

        float[] samples = kind switch
        {
            SfxLoop.FireBody => FireBody(),
            _ => FireBody(),
        };

        var stream = ToWavLooping(samples);
        LoopCache[kind] = stream;
        return stream;
    }

    /// <summary>Renders the fire's continuous body — the layer a blind player navigates by.
    ///
    /// <b>Two layers, and the frequency split between them is a direction, not a taste.</b>
    /// The night mix reserves 700–2200 Hz for creature voices,
    /// because THRILL-BIBLE §7.3 needs a threat audible THROUGH the night mix and a bed that
    /// ducked to make room would have announced the threat (§8.3). So this recipe is built to
    /// straddle that band rather than fill it:
    ///
    ///   • <b>Body</b> — a four-pole lowpassed noise rumble under ~180 Hz, gusting slowly. This
    ///     is what carries distance; low frequencies survive foliage and air absorption, which is
    ///     also why a real fire is audible across a clearing as a rumble long before it is
    ///     audible as crackles.
    ///   • <b>Snaps</b> — sparse, very short bursts of fast-decaying resonances placed ABOVE
    ///     2.6 kHz. Fires genuinely do this, so the direction and the physics agree for once: the
    ///     bright snap is the fire's identity and it sits well clear of anything with a throat.
    ///
    /// The gap between them is the hole a growl goes in.
    ///
    /// Seeded and deterministic. Five fires share this one cached buffer and decorrelate through
    /// per-emitter pitch jitter and a random start offset (see <see cref="AcquireLoop"/>) rather
    /// than through five renders, which would cost five megabytes to solve a problem two floats
    /// already solve.</summary>
    private static float[] FireBody()
    {
        var rng = new Random(31337);
        // Four cascaded one-poles: 6 dB/oct each, 24 dB/oct together. One pole alone would leave
        // the vocal band only ~8 dB down, which is not a hole, it is a dip.
        float b0 = 0, b1 = 0, b2 = 0, b3 = 0;
        const float bodyK = 0.022f; // ~170 Hz at 48 kHz

        // Snap state, carried across samples in the closure.
        int snapLeft = 0;
        float f1 = 0, f2 = 0, f3 = 0, snapT = 0;
        const float SnapsPerSecond = 6.5f;
        float snapChance = SnapsPerSecond / SampleRate;

        return LoopSplice.Render(SampleRate, LoopBodySeconds, LoopBodyOverlapSeconds, (t, u) =>
        {
            float white = (float)(rng.NextDouble() * 2 - 1);
            b0 += bodyK * (white - b0);
            b1 += bodyK * (b0 - b1);
            b2 += bodyK * (b1 - b2);
            b3 += bodyK * (b2 - b3);
            // Two incommensurate gusts, so the swell never settles into a countable period.
            float gust = 0.72f + 0.18f * Mathf.Sin(Mathf.Tau * 0.037f * t)
                               + 0.10f * Mathf.Sin(Mathf.Tau * 0.023f * t + 2.1f);
            // The cascade costs a lot of amplitude; 6.5x brings the rumble back to a usable level while
            // leaving real headroom — LoopSplice clamps at +/-1, so a hot recipe clips permanently.
            float body = b3 * 6.5f * gust;

            if (snapLeft <= 0 && rng.NextDouble() < snapChance)
            {
                snapLeft = (int)(SampleRate * 0.012f); // 12 ms: a snap, not a tap
                snapT = 0f;
                f1 = 2600f + (float)rng.NextDouble() * 3400f;
                f2 = 2600f + (float)rng.NextDouble() * 3400f;
                f3 = 2600f + (float)rng.NextDouble() * 3400f;
            }
            float snap = 0f;
            if (snapLeft > 0)
            {
                snapLeft--;
                snapT += 1f / SampleRate;
                // ~3 ms decay. Fast enough to read as a snap; slow enough that the resonance
                // stays near its centre frequency instead of smearing back down into the band
                // this recipe exists to keep clear.
                float env = Mathf.Exp(-snapT / 0.003f);
                snap = (Mathf.Sin(Mathf.Tau * f1 * snapT)
                      + Mathf.Sin(Mathf.Tau * f2 * snapT)
                      + Mathf.Sin(Mathf.Tau * f3 * snapT)) * env * 0.075f;
            }

            return body + snap;
        });
    }

    /// <summary>14 seconds. Long enough that a player standing at a fire for a minute does not
    /// hear the same crackle land twice in the same place; short enough that the cached PCM is
    /// ~1.3 MB rather than several.</summary>
    private const float LoopBodySeconds = 14f;

    private const float LoopBodyOverlapSeconds = 2f;

    // --- Synthesis recipes ---------------------------------------------------------

    /// <summary>Soft little grunt of effort — a low voiced "hup" that pushes up then settles.
    /// Replaces the springy boing for jumps: same beat, far less shouty over and over.</summary>
    private static float[] EffortGrunt(float seconds)
    {
        var rng = new Random(9001);
        float lp = 0;
        return Render(seconds, (t, u) =>
        {
            // Quick upward push in the first half, then a small settle: the shape of effort.
            float rise = EaseOut(Mathf.Min(u * 2f, 1f));
            float settle = u > 0.5f ? Mathf.Lerp(1f, 0.86f, (u - 0.5f) * 2f) : 1f;
            float freq = Mathf.Lerp(150f, 205f, rise) * settle;
            float phase = Mathf.Tau * freq * t;
            float voice = Mathf.Sin(phase) + 0.4f * Mathf.Sin(2f * phase) + 0.15f * Mathf.Sin(3f * phase);
            float white = (float)(rng.NextDouble() * 2 - 1);
            lp += 0.10f * (white - lp); // breathy edge
            return (voice * 0.5f + lp * 0.2f) * Envelope(u, attack: 0.01f, curve: 1.8f) * 0.5f;
        });
    }

    /// <summary>Soft landing/drop thud: low sine with pitch sag plus filtered noise puff.</summary>
    private static float[] Thump(float seconds, float baseFreq, float noise)
    {
        var rng = new Random(1234);
        float lp = 0;
        return Render(seconds, (t, u) =>
        {
            float freq = baseFreq * (1f - 0.35f * u);
            float tone = Mathf.Sin(Mathf.Tau * freq * t);
            float white = (float)(rng.NextDouble() * 2 - 1);
            lp += 0.08f * (white - lp); // cheap one-pole lowpass = "puff" not "hiss"
            return (tone * 0.7f + lp * noise) * Envelope(u, attack: 0.005f, curve: 2.2f) * 0.8f;
        });
    }

    /// <summary>Head-bonk: mid sine that sags in pitch with a hard attack.</summary>
    private static float[] Bonk(float seconds, float baseFreq)
    {
        return Render(seconds, (t, u) =>
        {
            float freq = baseFreq * (1f - 0.3f * u);
            float phase = Mathf.Tau * freq * t;
            float body = Mathf.Sin(phase) + 0.25f * Mathf.Sin(3f * phase) * (1f - u);
            return body * Envelope(u, attack: 0.004f, curve: 2.5f) * 0.7f;
        });
    }

    /// <summary>Comedy slide-whistle-down for stumbles.</summary>
    private static float[] Warble(float seconds, float f0, float f1)
    {
        return Render(seconds, (t, u) =>
        {
            float freq = Mathf.Lerp(f0, f1, u) * (1f + 0.09f * Mathf.Sin(t * 55f));
            return Mathf.Sin(Mathf.Tau * freq * t) * Envelope(u, attack: 0.02f, curve: 1.2f) * 0.45f;
        });
    }

    /// <summary>Quick rising chirp (pickup pop, recovery).</summary>
    private static float[] Sweep(float seconds, float f0, float f1)
    {
        return Render(seconds, (t, u) =>
            Mathf.Sin(Mathf.Tau * Mathf.Lerp(f0, f1, u) * t) * Envelope(u, attack: 0.01f, curve: 1.8f) * 0.5f);
    }

    /// <summary>Tiny sagging vowel-ish grunt — the comedy "oof" for bumps.</summary>
    private static float[] Oof(float seconds)
    {
        var rng = new Random(777);
        float lp = 0;
        return Render(seconds, (t, u) =>
        {
            float freq = Mathf.Lerp(240f, 150f, u); // dropping pitch = deflating
            float phase = Mathf.Tau * freq * t;
            float voice = Mathf.Sin(phase) + 0.5f * Mathf.Sin(2f * phase) + 0.2f * Mathf.Sin(3f * phase);
            float white = (float)(rng.NextDouble() * 2 - 1);
            lp += 0.12f * (white - lp); // breathy edge
            return (voice * 0.55f + lp * 0.25f) * Envelope(u, attack: 0.015f, curve: 1.6f) * 0.6f;
        });
    }

    /// <summary>Two-note "ta-da" chirp for special pickups.</summary>
    private static float[] TwoNote(float seconds, float fA, float fB)
    {
        return Render(seconds, (t, u) =>
        {
            float freq = u < 0.45f ? fA : fB;
            return Mathf.Sin(Mathf.Tau * freq * t) * Envelope(u, attack: 0.01f, curve: 1.5f) * 0.45f;
        });
    }

    /// <summary>One fire crackle: a single tiny pop of band-limited noise. Short on purpose —
    /// the fire's character comes from a sparse emitter's randomised spacing and pitch jitter
    /// across many of these, not from any one of them being interesting.</summary>
    private static float[] Crackle(float seconds)
    {
        var rng = new Random(5521);
        float lp = 0, hp = 0;
        return Render(seconds, (t, u) =>
        {
            float white = (float)(rng.NextDouble() * 2 - 1);
            lp += 0.45f * (white - lp);   // tame the hiss
            hp = lp - hp * 0.05f;         // keep it thin, so it reads as a snap not a thud
            return hp * Envelope(u, attack: 0.008f, curve: 5f) * 0.5f;
        });
    }

    /// <summary>A little three-note bird trill, each note shorter and higher than the last, with
    /// a gap between them. Scenery, not a creature voice — see <see cref="Sfx.Birdsong"/>.</summary>
    private static float[] Birdsong(float seconds)
    {
        return Render(seconds, (t, u) =>
        {
            // Three notes inside the clip, each occupying its own third, sounding for the first
            // ~55% of its slot so there is audible air between them.
            int note = u < 0.33f ? 0 : (u < 0.66f ? 1 : 2);
            float local = (u - note * 0.33f) / 0.33f;
            if (local > 0.55f)
                return 0f;
            float baseFreq = note switch { 0 => 2300f, 1 => 2750f, _ => 3100f };
            // Each note itself slides up a little — a chirp, not a beep.
            float freq = baseFreq * Mathf.Lerp(1f, 1.12f, local / 0.55f);
            return Mathf.Sin(Mathf.Tau * freq * t) * Envelope(local / 0.55f, attack: 0.08f, curve: 2f) * 0.3f;
        });
    }

    /// <summary>An insect passing close: a buzzy two-harmonic tone whose pitch wavers, swelling
    /// in and back out so it reads as something flying past rather than sitting on the mic.</summary>
    private static float[] Buzz(float seconds)
    {
        return Render(seconds, (t, u) =>
        {
            // Wavering pitch — a steady tone reads as a machine, not an insect.
            float freq = 220f + 18f * Mathf.Sin(Mathf.Tau * 7.5f * t);
            float phase = Mathf.Tau * freq * t;
            float tone = Mathf.Sin(phase) + 0.45f * Mathf.Sin(2f * phase);
            // Fly-past: rise and fall rather than Envelope's decay-from-the-start.
            float swell = Mathf.Sin(Mathf.Pi * Mathf.Clamp(u, 0f, 1f));
            return tone * swell * 0.16f;
        });
    }

    private static float[] Squeak(float seconds, float f0, float f1)
    {
        return Render(seconds, (t, u) =>
        {
            float freq = Mathf.Lerp(f0, f1, EaseOut(u));
            float phase = Mathf.Tau * freq * t;
            // A little extra odd-harmonic gives the rubbery chew-toy edge (vs a pure sine
            // whistle). Fades over the note so the tail is clean.
            float body = Mathf.Sin(phase) + 0.45f * Mathf.Sin(3f * phase) * (1f - u);
            return body * Envelope(u, attack: 0.008f, curve: 3.0f) * 0.35f;
        });
    }

    // --- The goose (HONK-1, 2026-09-04) ---------------------------------------------------------
    //
    // EVERY NUMBER BELOW IS A KNOB, and they are named constants rather than literals inside the
    // lambda for exactly that reason: Talon asked for "a goose honking", picked no numbers, and
    // said retuning by hand is his. DECISION-LOG.md carries the same list with the reasoning.
    //
    // WHAT MAKES IT A GOOSE AND NOT THE TWO THINGS IT COULD EASILY BE:
    //   • Not a CAR HORN. A horn is two steady pitches held flat for half a second. The goose is
    //     a PITCH ARC — up fast, held, then a longer fall (HonkRise/HonkFall below). A flat honk
    //     reads as traffic no matter what the timbre is, which is why the arc is the first knob.
    //   • Not a DUCK. A quack is ~0.15 s of broadband rasp that falls off a cliff. The goose is
    //     nearly three times longer, has a real tonal core, and RISES before it falls.
    // The remaining character is timbre: a 1/n harmonic stack (a sawtooth's recipe) rather than a
    // sine, because a goose's syrinx is a reed, not a flute — and the stack DARKENS across the
    // call, which is the open "aa" closing into "onk". An unchanging timbre is what makes a
    // synthesised call read as an instrument.

    /// <summary>Total call length. Long enough to be a goose rather than a duck, short enough that
    /// two players honking over each other stays legible.</summary>
    private const float HonkSeconds = 0.42f;

    /// <summary>The pitch arc, in Hz: where the call starts, the pitch it shouts at, where it
    /// lands. A Canada goose's honk sits in this band; higher reads as a toy, lower as a foghorn.</summary>
    private const float HonkStartHz = 300f;
    private const float HonkPeakHz = 470f;
    private const float HonkEndHz = 250f;

    /// <summary>Normalized times: the rise finishes at <see cref="HonkRiseEnd"/> and the fall
    /// begins at <see cref="HonkFallStart"/>. The fall is deliberately longer than the rise —
    /// "ah-RONK", not "RONK-ah".</summary>
    private const float HonkRiseEnd = 0.14f;
    private const float HonkFallStart = 0.42f;

    /// <summary>The bleat: a few percent of pitch wobble. Without it the harmonic stack is a
    /// kazoo. Fast enough to read as roughness in the throat rather than as vibrato.</summary>
    private const float HonkBleatHz = 38f;
    private const float HonkBleatDepth = 0.025f;

    /// <summary>How many harmonics the reed stack carries. Six is where the brassy body lives;
    /// far more starts to hiss on a small speaker.</summary>
    private const int HonkHarmonics = 7;

    /// <summary>Breath. A goose moves air; a stack with none is a synth patch. Fades across the
    /// call because the air is spent by the end of it.</summary>
    private const float HonkBreathMix = 0.18f;

    /// <summary>Attack, as a fraction of the call (~13 ms). A honk ARRIVES — it does not fade in —
    /// but a truly instant edge clicks.</summary>
    private const float HonkAttackU = 0.03f;

    /// <summary>Output trim. Set so the rendered buffer peaks well under full scale: the clamp in
    /// <see cref="Render"/> would otherwise hard-clip the harmonic stack, which is audible as a
    /// buzz and is not the roughness we want. <c>GooseHonkPeak</c> in the unit suite pins it.</summary>
    private const float HonkGain = 0.80f;

    /// <summary>Renders the goose. <b>Public and returning raw PCM</b> so the Godot-free unit suite
    /// can assert the arc and the headroom without an audio device — <see cref="Render"/>,
    /// <see cref="Envelope"/> and <c>Mathf</c> are all pure. That is the only reason this one is
    /// not private like its neighbours.</summary>
    public static float[] GooseHonkPcm()
    {
        var rng = new Random(20260904); // fixed seed: the honk must be the same honk every session
        float lp = 0;
        return Render(HonkSeconds, (t, u) =>
        {
            float freq;
            if (u < HonkRiseEnd)
                freq = Mathf.Lerp(HonkStartHz, HonkPeakHz, EaseOut(u / HonkRiseEnd)); // "ah" — up fast
            else if (u < HonkFallStart)
                freq = HonkPeakHz;                                                    // the held shout
            else
                freq = Mathf.Lerp(HonkPeakHz, HonkEndHz, (u - HonkFallStart) / (1f - HonkFallStart)); // "onk"
            freq *= 1f + HonkBleatDepth * Mathf.Sin(Mathf.Tau * HonkBleatHz * t);

            float phase = Mathf.Tau * freq * t;
            float bright = Mathf.Lerp(1f, 0.35f, u); // the vowel closing
            float body = 0f;
            for (int n = 1; n <= HonkHarmonics; n++)
            {
                float gain = 1f / n;
                if (n >= 2)
                    gain *= bright;
                body += gain * Mathf.Sin(n * phase);
            }
            body *= 0.45f;

            float white = (float)(rng.NextDouble() * 2 - 1);
            lp += 0.20f * (white - lp); // one-pole lowpass: breath, not hiss
            body += lp * HonkBreathMix * (1f - u);

            // Not Envelope(): that decays from the first sample, and this call has to HOLD before
            // it falls or the shout is over before the pitch arc gets to the top of itself.
            float env = u < HonkAttackU
                ? u / HonkAttackU
                : (u < HonkFallStart ? 1f : Mathf.Pow(1f - (u - HonkFallStart) / (1f - HonkFallStart), 1.4f));
            return body * env * HonkGain;
        });
    }

    // --- The triumph (CELEBRATE-1, 2026-09-04) --------------------------------------------------
    //
    // EVERY NUMBER BELOW IS A KNOB, named for the same reason the goose's are: Talon asked for
    // "a small sound ... like a triumph sound", picked no numbers, and retuning by hand is his.
    // DECISION-LOG.md carries the same list with the reasoning.
    //
    // WHAT MAKES IT A SMALL TRIUMPH AND NOT THE TWO THINGS IT COULD EASILY BE:
    //   • Not a FANFARE. A fanfare is brass, loud, and long enough that the player stops playing
    //     to listen to it. The standing constraint on this packet is Talon's own reverted-juice
    //     history, so the whole shape is bounded first: four notes, <see cref="TriumphSeconds"/>
    //     total, and each note's ring is allowed to overlap the next rather than being held.
    //   • Not a COIN PICKUP. A pickup is one or two notes with no tail — it says "you got a
    //     thing", not "that was the last one". Four notes climbing a major triad and landing an
    //     octave above the root is the smallest shape that reads as an arrival rather than as
    //     another tick of the same counter the player has heard ninety-nine times.
    // The timbre is GLASS, deliberately: a sine fundamental with two quiet upper partials and a
    // slow beat between two detuned copies. Soap film, not brass — the level is made of bubbles,
    // and the one sound it makes when they are gone should be made of the same stuff.

    /// <summary>Total length, including the last note's tail. Comfortably under the ~2 s the
    /// packet's restraint constraint allows the whole celebration.</summary>
    private const float TriumphSeconds = 1.15f;

    /// <summary>The four notes, in Hz: a G-major triad climbing to the octave (G5 B5 D6 G6).
    /// Major rather than a fifth-only shape because the third is what makes it read as pleased
    /// rather than as an alarm; the octave landing is what makes the fourth note an ending.</summary>
    private static readonly float[] TriumphNotesHz = { 784.0f, 987.8f, 1174.7f, 1568.0f };

    /// <summary>Seconds between note onsets. Fast enough to be one gesture rather than four
    /// events; slow enough that each note is individually audible.</summary>
    private const float TriumphNoteGapSec = 0.115f;

    /// <summary>How long each note rings before it is inaudible. Longer than the gap ON PURPOSE:
    /// the notes overlap into a chord rather than being played staccato, which is what turns four
    /// pings into one arrival.</summary>
    private const float TriumphRingSec = 0.62f;

    /// <summary>The last note rings this much longer than its siblings — the only asymmetry in
    /// the recipe, and it is what makes the phrase land instead of stop.</summary>
    private const float TriumphFinalRingBonusSec = 0.28f;

    /// <summary>Per-note attack, seconds (~6 ms). A struck glass has an edge; a truly instant one
    /// clicks.</summary>
    private const float TriumphAttackSec = 0.006f;

    /// <summary>Decay shape. Above 1 the note falls away faster at the start than at the end,
    /// which is what a struck resonant body does.</summary>
    private const float TriumphDecayCurve = 2.6f;

    /// <summary>The two quiet upper partials, relative to the fundamental. Glass, not organ: keep
    /// these small or the arpeggio turns into a church.</summary>
    private const float TriumphPartial2 = 0.30f;
    private const float TriumphPartial3 = 0.10f;

    /// <summary>Detune of the shimmer copy, as a fraction of the note's own frequency. The beat
    /// this produces (a few Hz) is the whole difference between "a sine" and "a struck
    /// object".</summary>
    private const float TriumphDetune = 0.004f;

    /// <summary>Output trim. Set so the rendered buffer peaks well under full scale even where
    /// all four notes' tails overlap — <see cref="Render"/>'s clamp would otherwise flatten the
    /// chord's peak, which is audible as a crunch. <c>TriumphPeak</c> in the unit suite pins
    /// it.</summary>
    private const float TriumphGain = 0.42f;

    /// <summary>Renders the triumph. <b>Public and returning raw PCM</b> for the same reason
    /// <see cref="GooseHonkPcm"/> is: the Godot-free unit suite asserts the shape and the
    /// headroom without an audio device.
    ///
    /// <para>Note that this is NOT built on <see cref="Envelope"/> — that envelope decays from
    /// the first sample of the WHOLE buffer, and this buffer holds four notes that each need
    /// their own. Each note is summed in with its own local envelope, which is also what lets the
    /// tails overlap into a chord (see <see cref="TriumphRingSec"/>).</para></summary>
    public static float[] TriumphPcm()
    {
        return Render(TriumphSeconds, (t, _) =>
        {
            float sum = 0f;
            for (int n = 0; n < TriumphNotesHz.Length; n++)
            {
                float onset = n * TriumphNoteGapSec;
                float age = t - onset;
                if (age < 0f)
                    continue;
                float ring = TriumphRingSec + (n == TriumphNotesHz.Length - 1 ? TriumphFinalRingBonusSec : 0f);
                if (age >= ring)
                    continue;

                float hz = TriumphNotesHz[n];
                float phase = Mathf.Tau * hz * age;
                float shimmer = Mathf.Tau * hz * (1f + TriumphDetune) * age;
                float voice = 0.5f * (Mathf.Sin(phase) + Mathf.Sin(shimmer))
                              + TriumphPartial2 * Mathf.Sin(2f * phase)
                              + TriumphPartial3 * Mathf.Sin(3f * phase);

                float u = age / ring;
                float attack = age < TriumphAttackSec ? age / TriumphAttackSec : 1f;
                sum += voice * attack * Mathf.Pow(1f - u, TriumphDecayCurve);
            }
            return sum * TriumphGain;
        });
    }

    // --- The round's palette (CLOCK-1, 2026-09-19) ----------------------------------------------
    //
    // SEVEN RECIPES, ONE JOB: make the countdown audible from inside an aisle. The design is
    // PROPOSAL-2026-09-19-SOUND-STATES.md §3.2; the edge -> cue mapping is engine-free in
    // RoundAudioCues and is NOT restated here — this file only knows how each sound is made.
    //
    // EVERY NUMBER IS A KNOB, named, for the same reason the goose's and the triumph's are: Talon
    // picked none of them and retuning is his. All seven are public and return raw PCM so the
    // Godot-free unit suite can assert the headroom (RoundClockTests) without an audio device.
    //
    // THE PALETTE HAS A SHAPE, and it is deliberate: the two invitations (ChimeUp, Note) are
    // clean sines that RISE, and the three deadlines (RoundBuzz, BuzzShort, BuzzDouble) are the
    // same low inharmonic buzz at three lengths. A player should not have to learn six sounds —
    // they have to learn "bright = you may, low = you must", which is one bit, and then the
    // LENGTH says which deadline it was.

    /// <summary>Total length of the start chime, including the second note's tail.</summary>
    private const float ChimeSeconds = 0.35f;

    /// <summary>The two notes, in Hz: G5 then D6, a rising fifth. A fifth rather than an octave
    /// because an octave is the triumph's ending shape and this is a beginning.</summary>
    private const float ChimeNoteAHz = 784.0f;
    private const float ChimeNoteBHz = 1174.7f;

    /// <summary>When the second note starts. Short enough that the two read as one gesture.</summary>
    private const float ChimeNoteBOnsetSec = 0.12f;

    /// <summary>How long each note rings. Longer than the gap, so the two overlap into an
    /// interval rather than being two separate pings.</summary>
    private const float ChimeRingSec = 0.22f;

    /// <summary>Per-note attack (~4 ms) and decay shape — struck glass, like the triumph.</summary>
    private const float ChimeAttackSec = 0.004f;
    private const float ChimeDecayCurve = 2.2f;

    /// <summary>The one quiet upper partial. Keep it small or the chime turns into an organ.</summary>
    private const float ChimePartial2 = 0.22f;

    /// <summary>Output trim, set so the overlap of the two tails stays well under full scale —
    /// <see cref="Render"/>'s clamp would otherwise flatten the peak into a crunch.</summary>
    private const float ChimeGain = 0.46f;

    /// <summary>Renders the round-start chime. Two rising notes; see the block comment above.</summary>
    public static float[] ChimeUpPcm() => Render(ChimeSeconds, (t, _) =>
    {
        float sum = 0f;
        for (int n = 0; n < 2; n++)
        {
            float onset = n == 0 ? 0f : ChimeNoteBOnsetSec;
            float age = t - onset;
            if (age < 0f || age >= ChimeRingSec)
                continue;
            float hz = n == 0 ? ChimeNoteAHz : ChimeNoteBHz;
            float phase = Mathf.Tau * hz * age;
            float voice = Mathf.Sin(phase) + ChimePartial2 * Mathf.Sin(2f * phase);
            sum += voice * Envelope(age / ChimeRingSec, ChimeAttackSec / ChimeRingSec, ChimeDecayCurve);
        }
        return sum * ChimeGain;
    });

    /// <summary>The packet's 40 ms. A tick longer than this stops being a click and starts being
    /// a note, and ten of them in ten seconds would then be a melody.</summary>
    private const float TickSeconds = 0.040f;

    /// <summary>The two resonances of the block, in Hz. DELIBERATELY NOT HARMONIC (2100/1400 is
    /// 1.5, close to a fifth, but the decays differ) — a struck piece of wood is inharmonic, and
    /// two partials an exact octave apart read as a tuned instrument instead.</summary>
    private const float TickLowHz = 1400f;
    private const float TickHighHz = 2100f;

    /// <summary>How fast each resonance dies. High: the whole point of a click.</summary>
    private const float TickDecayCurve = 5.0f;

    /// <summary>The noise transient's share of the first few milliseconds — the stick hitting the
    /// wood. Without it the tick is a beep.</summary>
    private const float TickNoiseMix = 0.55f;
    private const float TickNoiseSec = 0.004f;

    /// <summary>
    /// Sub-millisecond onset ramp, and it is not a softening.
    ///
    /// <para><b>Found by the headroom test, which is why it is here.</b> The first draft had no
    /// ramp at all — a click should start instantly — and rendered a buffer whose first sample
    /// was 0.137. A buffer that begins at a non-zero value is a step discontinuity on top of the
    /// sound, which the mixer reproduces as a second, different click every single time the
    /// sample plays, and 0.8 ms at 48 kHz is 38 samples: inaudible as an attack, sufficient to
    /// start at zero. Every other recipe here already had an attack and this one did not.</para>
    /// </summary>
    private const float TickAttackSec = 0.0008f;

    /// <summary>Output trim.</summary>
    private const float TickGain = 0.62f;

    /// <summary>Renders one wood-block tick. Seeded: every tick in a round is the same tick, and
    /// the variation the ear wants comes from the pool's pitch jitter and the caller's pitch bias
    /// (<c>RoundAudioCues.TickPitchBias</c>) rather than from a different render.</summary>
    public static float[] TickPcm()
    {
        var rng = new Random(20260919);
        return Render(TickSeconds, (t, u) =>
        {
            float body = Mathf.Sin(Mathf.Tau * TickLowHz * t)
                         + 0.7f * Mathf.Sin(Mathf.Tau * TickHighHz * t);
            body *= 0.5f;
            float noise = t < TickNoiseSec
                ? (float)(rng.NextDouble() * 2 - 1) * TickNoiseMix * (1f - t / TickNoiseSec)
                : 0f;
            float attack = t < TickAttackSec ? t / TickAttackSec : 1f;
            return (body + noise) * attack * Mathf.Pow(1f - u, TickDecayCurve) * TickGain;
        });
    }

    /// <summary>Confirm, in one note. 0.2 s per the packet.</summary>
    private const float NoteSeconds = 0.20f;

    /// <summary>A5. One octave under the chime's second note, so Confirm answers the start rather
    /// than competing with it.</summary>
    private const float NoteHz = 880.0f;

    private const float NoteAttackSec = 0.005f;
    private const float NoteDecayCurve = 2.4f;
    private const float NotePartial2 = 0.18f;
    private const float NoteGain = 0.50f;

    /// <summary>Renders the Confirm note.</summary>
    public static float[] NotePcm() => Render(NoteSeconds, (t, u) =>
    {
        float phase = Mathf.Tau * NoteHz * t;
        float voice = Mathf.Sin(phase) + NotePartial2 * Mathf.Sin(2f * phase);
        return voice * Envelope(u, NoteAttackSec / NoteSeconds, NoteDecayCurve) * NoteGain;
    });

    /// <summary>The buzzer's fundamental. Low enough to be felt rather than heard past a wall,
    /// which is the whole point of putting it on the clocks.</summary>
    private const float BuzzFundamentalHz = 150f;

    /// <summary>How many harmonics the buzz carries. A buzzer is a reed, not a sine: the odd
    /// harmonics are what make it unpleasant, which is the job.</summary>
    private const int BuzzHarmonics = 7;

    /// <summary>The amplitude tremolo, in Hz. This is the difference between "a low tone" and "a
    /// BUZZER" — a mechanical buzzer's armature chatters, and this is that chatter.</summary>
    private const float BuzzChatterHz = 32f;

    /// <summary>How deep the chatter cuts, 0..1. At 1.0 the sound gates fully off between
    /// chatters and reads as a rattle rather than a buzz.</summary>
    private const float BuzzChatterDepth = 0.45f;

    /// <summary>Attack and release, seconds. Both short — a buzzer has no bloom — but not zero,
    /// which clicks.</summary>
    private const float BuzzEdgeSec = 0.012f;

    /// <summary>Output trim. Seven harmonics sum well past unity before this.</summary>
    private const float BuzzGain = 0.40f;

    /// <summary>The hide buzzer's length (packet: 0.5 s).</summary>
    private const float RoundBuzzSeconds = 0.50f;

    /// <summary>The grace buzzer: shorter AND lower, both, so it is distinguishable from the hide
    /// buzzer in a room where the player is not looking at anything.</summary>
    private const float BuzzShortSeconds = 0.25f;
    private const float BuzzShortHz = 104f;

    /// <summary>The seek timeout: two blasts with a gap. Total stays inside half a second so it
    /// does not overlap the tally the phase change is about to put on the clocks.</summary>
    private const float BuzzDoubleBlastSec = 0.19f;
    private const float BuzzDoubleGapSec = 0.08f;

    /// <summary>One buzzer blast, sampled at time <paramref name="age"/> into a blast of
    /// <paramref name="length"/> seconds at <paramref name="hz"/>. Shared by all three buzzers so
    /// the family cannot drift apart into three unrelated noises.</summary>
    private static float BuzzBlast(float age, float length, float hz)
    {
        if (age < 0f || age >= length)
            return 0f;
        float body = 0f;
        for (int n = 1; n <= BuzzHarmonics; n++)
            body += Mathf.Sin(n * Mathf.Tau * hz * age) / n;
        float chatter = 1f - BuzzChatterDepth * 0.5f * (1f - Mathf.Cos(Mathf.Tau * BuzzChatterHz * age));
        float edge = Mathf.Min(age, length - age) / BuzzEdgeSec;
        return body * chatter * Mathf.Clamp(edge, 0f, 1f);
    }

    /// <summary>Renders the hide buzzer.</summary>
    public static float[] RoundBuzzPcm() => Render(RoundBuzzSeconds,
        (t, _) => BuzzBlast(t, RoundBuzzSeconds, BuzzFundamentalHz) * BuzzGain);

    /// <summary>Renders the grace buzzer — shorter and lower than the hide buzzer.</summary>
    public static float[] BuzzShortPcm() => Render(BuzzShortSeconds,
        (t, _) => BuzzBlast(t, BuzzShortSeconds, BuzzShortHz) * BuzzGain);

    /// <summary>Renders the seek-timeout buzzer: the same blast, twice.</summary>
    public static float[] BuzzDoublePcm() =>
        Render(BuzzDoubleBlastSec * 2f + BuzzDoubleGapSec, (t, _) =>
        {
            float a = BuzzBlast(t, BuzzDoubleBlastSec, BuzzFundamentalHz);
            float b = BuzzBlast(t - (BuzzDoubleBlastSec + BuzzDoubleGapSec), BuzzDoubleBlastSec,
                BuzzFundamentalHz);
            return (a + b) * BuzzGain;
        });

    /// <summary>The reset whoosh's length. Long enough to be a movement, short enough that the
    /// holding room is quiet again before anybody reaches the Start button.</summary>
    private const float WhooshSeconds = 0.40f;

    /// <summary>The band the whoosh sweeps through, in Hz. It rises and falls rather than only
    /// rising: the world is going HOME, not launching.</summary>
    private const float WhooshLowHz = 300f;
    private const float WhooshPeakHz = 1500f;

    /// <summary>Resonance of the one-pole band. Higher is more "jet", lower is more "air"; this
    /// is deliberately near the air end, because the loudest thing in the reset should be the
    /// props landing, not the transition.</summary>
    private const float WhooshBandwidth = 0.35f;

    private const float WhooshGain = 0.55f;

    /// <summary>Renders the reset whoosh: white noise through a one-pole band whose centre
    /// sweeps up and back. Seeded, so the reset sounds the same every round.</summary>
    public static float[] ResetWhooshPcm()
    {
        var rng = new Random(20260920);
        float lp = 0f;
        float bp = 0f;
        return Render(WhooshSeconds, (_, u) =>
        {
            // A half-sine sweep: 0 -> 1 -> 0 over the whole buffer.
            float centre = Mathf.Lerp(WhooshLowHz, WhooshPeakHz, Mathf.Sin(Mathf.Pi * u));
            float f = Mathf.Clamp(centre / (SampleRate * 0.5f), 0.001f, 0.9f);
            float white = (float)(rng.NextDouble() * 2 - 1);
            lp += f * (white - lp);
            bp += f * WhooshBandwidth * (lp - bp);
            // Band-passed: the low-passed signal minus its own slower copy.
            float band = lp - bp;
            // Soft both ends — a whoosh that starts at full level is a click.
            float env = Mathf.Sin(Mathf.Pi * u);
            return band * env * WhooshGain;
        });
    }

    // NOTE (Issue #152): this branch originally carried its own Crackle() recipe, written because
    // #152's brief stated Sfx.Crackle already existed when it did not. PR #154 landed the real one
    // (above), so the duplicate was deleted rather than reconciled — two crackle recipes cannot
    // both be the fire's sound, and #154's is the one with a unit-tested schedule behind it.

    // --- Plumbing --------------------------------------------------------------------

    private static float[] Render(float seconds, Func<float, float, float> sample)
    {
        int count = (int)(SampleRate * seconds);
        var data = new float[count];
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)SampleRate;
            float u = i / (float)count; // normalized 0..1 progress
            data[i] = Mathf.Clamp(sample(t, u), -1f, 1f);
        }
        return data;
    }

    /// <summary>Attack then power-curve decay; u is normalized progress.</summary>
    private static float Envelope(float u, float attack, float curve)
    {
        float a = u < attack ? u / attack : 1f;
        return a * Mathf.Pow(1f - u, curve);
    }

    private static float EaseOut(float u) => 1f - (1f - u) * (1f - u);

    /// <summary>Same PCM as <see cref="ToWav"/>, with the loop points set to cover the whole
    /// buffer. Separate method rather than a bool parameter on <see cref="ToWav"/> so that no
    /// existing one-shot call site can grow a looping variant by accident — every one-shot in
    /// this class must stay non-looping or it holds a pool slot forever.</summary>
    internal static AudioStreamWav ToWavLooping(float[] samples)
    {
        AudioStreamWav wav = ToWav(samples);
        wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
        wav.LoopBegin = 0;
        wav.LoopEnd = samples.Length;
        return wav;
    }

    private static AudioStreamWav ToWav(float[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short s = (short)(samples[i] * short.MaxValue);
            bytes[i * 2] = (byte)(s & 0xFF);
            bytes[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
        }
        return new AudioStreamWav
        {
            Data = bytes,
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = SampleRate,
            Stereo = false,
        };
    }
}
