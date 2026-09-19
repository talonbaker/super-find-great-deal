using Godot;
using MpFoundation.Game.Sandbox;

namespace MpFoundation.Game.World;

/// <summary>
/// Fires one short <see cref="Sfx"/> one-shot at randomised intervals from its own position — the
/// a fire's crackle, birdsong in the woods, an insect passing through a clearing.
///
/// WHY SPARSE ONE-SHOTS RATHER THAN A LOOPING BED. Two reasons, one budgetary and one about how it
/// sounds. Budget: the written ceiling is ≤24 concurrent 3D voices, and 5 voice speakers + SfxLab's
/// 14-slot one-shot pool already spend 19 of them, so continuous positional layers are the scarcest
/// thing in the mix. A sparse emitter spends NO continuous voice — it borrows a pool slot for a
/// fraction of a second and gives it straight back. Sound: a loop is identical every pass and the
/// ear stops hearing it, whereas randomised spacing plus per-shot pitch jitter means the fire is
/// audibly different from one moment to the next, which is the whole point of the layer.
///
/// RECONCILED WITH THE BED (2026-08-08). The paragraph above reads as an argument against a
/// continuous layer; it is not, and the arithmetic is worth restating because it decided how
/// <see cref="AmbientBed"/> was allowed to land. The ≤24 ceiling counts concurrent 3D voices —
/// <c>AudioStreamPlayer3D</c> — and the bed's two continuous layers are plain
/// <c>AudioStreamPlayer</c>s, non-positional by construction. They cost zero 3D voices, so the
/// post-merge count is still 5 speakers + 14 pool slots = 19 of 24, exactly as before. What the
/// bed does spend is pool CONTENTION, not slots: its sparse positional event layer rents from
/// the same 14, which is why a world that places these emitters switches it off (they own that
/// layer there). Nothing continuous ever entered
/// the pool, which is the part of the argument that was never negotiable.
///
/// NEVER holds a pool slot open. SfxLab.Rent selects on <c>!p.Playing</c>, so anything continuous
/// rented from that pool would never be returned — it would permanently shrink the one-shot pool or
/// be stolen mid-sound by the next footstep. Every emission here is a genuine one-shot, which is
/// exactly what that pool is for.
///
/// Deterministic by seed: two emitters given the same seed produce the same interval sequence,
/// because the spacing comes from <see cref="SparseSfxSchedule.NextIntervalSec"/> — a pure static
/// function a headless test can assert without an audio device. That is deliberate; this repo has
/// no audio test of any kind, and a scheduler is the one part of an audio system a machine can
/// actually check.
///
/// Positional and client-local. Ambience is scenery, not a game event: every peer runs its own
/// emitters over its own copy of the world, so nothing here touches the wire. That also means it
/// must never carry information a player could act on — see LEVEL-BIBLE §8.1, audio alone is never
/// a sufficient cue.
/// </summary>
public partial class SparseSfxEmitter : Node3D
{
    /// <summary>Which one-shot to fire.</summary>
    [Export] public Sfx Kind { get; set; } = Sfx.Crackle;

    /// <summary>Shortest and longest gap between emissions, seconds. The gap is redrawn after
    /// every shot, so the layer never settles into a countable rhythm.</summary>
    [Export] public float MinIntervalSec { get; set; } = 0.45f;

    [Export] public float MaxIntervalSec { get; set; } = 1.6f;

    [Export] public float VolumeDb { get; set; } = -14f;

    /// <summary>Per-shot pitch variation. Higher than SfxLab's default: the same crackle at the
    /// same pitch twice in a row is the thing that gives a synthesised layer away.</summary>
    [Export] public float PitchJitter { get; set; } = 0.22f;

    [Export] public float MaxDistance { get; set; } = 30f;

    /// <summary>Metres of random scatter around this node per shot. A fire is a volume, not a
    /// point; scattering the source stops repeated crackles arriving from one exact spot.</summary>
    [Export] public float ScatterRadiusM { get; set; }

    /// <summary>Seed for both the interval sequence and the scatter. Distinct per emitter so two
    /// fires never crackle in lockstep.</summary>
    [Export] public int Seed { get; set; } = 1;

    /// <summary>Which mix bus to fire into. Defaults to <see cref="AudioBuses.Scenery"/> —
    /// scenery is not gameplay feedback, and a mix where the birds cannot be turned down without
    /// also turning down every footstep is not a mix. Falls back to SfxLab's own "Sfx" bus if the
    /// named one does not exist (see <see cref="SfxLab.PlayStream3D"/>).</summary>
    [Export] public string Bus { get; set; } = AudioBuses.Scenery;

    /// <summary>Silences this emitter after dark. Off by default, because most scenery is not
    /// time-of-day dependent and a fire's crackle explicitly is not — the fire's own lit
    /// state owns that (see <see cref="Active"/>).
    ///
    /// It exists for the layer that genuinely is: birdsong and warm-clearing insects are DAY
    /// sounds, and a bird trilling at 3 a.m. is the same class of bug as an unlit pit that
    /// crackles — a legible statement that the world's own state machine is lying. This is a
    /// crossfade rather than a subtraction: as the day scenery falls, <see cref="AmbientBed"/>'s
    /// night layer is rising on the same clock, so the mix never drops toward nothing. It is
    /// deliberately NOT THRILL-BIBLE §6.3's wrong-silence device, which requires a withdrawal
    /// with no explanation; dusk is an explanation, and a cause defuses it.
    ///
    /// Gates in ADDITION to <see cref="Active"/>, never instead of it, so nothing here becomes a
    /// second writer of a flag another system owns.</summary>
    [Export] public bool DaytimeOnly { get; set; }

    /// <summary>Whether this emitter is currently sounding. Exists because scenery audio is not
    /// always unconditional: an unlit fire must be SILENT, so whatever owns the fire's lit
    /// state owns this flag. Flipping it false stops emissions without tearing the node down, and
    /// the interval sequence resumes from where it left off rather than restarting — a fire relit
    /// does not begin its crackle pattern from the top.
    ///
    /// The clock does not advance while inactive, deliberately: a fire lit after ten minutes of
    /// darkness should not fire a backlog of crackles on its first frame.</summary>
    [Export] public bool Active { get; set; } = true;

    private ulong _step;
    private double _untilNext;
    private RandomNumberGenerator _scatterRng = null!;

    /// <summary>The gap before emission number <paramref name="step"/>. Delegates to
    /// <see cref="SparseSfxSchedule"/>, which is where the math lives and where it is tested — see
    /// that class for why it is not a member of this node.</summary>
    private float NextInterval(ulong step) =>
        SparseSfxSchedule.NextIntervalSec(Seed, step, MinIntervalSec, MaxIntervalSec);

    /// <summary>True when the day scenery should be quiet. Holds at DAY (audible) until the
    /// clock is synced, matching every other consumer of <see cref="CycleDriver"/> — an emitter
    /// that went silent while waiting for the server would make a fresh join sound like night.
    /// Shares <see cref="AmbientBedMath.NightWeight"/>'s bands rather than inventing a second
    /// set, so the day scenery fades out exactly as the night bed fades in.</summary>
    private bool SilencedByNight =>
        DaytimeOnly
        && CycleDriver.Instance is { Synced: true } driver
        && AmbientBedMath.NightWeight(driver.Phase) >= NightSilenceWeight;

    /// <summary>Half weight — the midpoint of the dusk crossfade, which is where the night bed
    /// has taken over enough to carry the mix on its own.</summary>
    private const float NightSilenceWeight = 0.5f;

    public override void _Ready()
    {
        AudioBuses.EnsureLayout(); // idempotent; no boot-order guarantee against the world script
        _scatterRng = new RandomNumberGenerator { Seed = (ulong)Seed };
        // Stagger the first shot by a full interval so several emitters spawned on the same frame
        // do not all fire together on frame one and read as a single event.
        _untilNext = NextInterval(_step);
    }

    public override void _Process(double delta)
    {
        if (!Active || SilencedByNight)
            return;
        _untilNext -= delta;
        if (_untilNext > 0)
            return;

        _step++;
        _untilNext = NextInterval(_step);

        Vector3 at = GlobalPosition;
        if (ScatterRadiusM > 0f)
        {
            at += new Vector3(
                _scatterRng.RandfRange(-ScatterRadiusM, ScatterRadiusM),
                _scatterRng.RandfRange(-ScatterRadiusM * 0.35f, ScatterRadiusM * 0.35f),
                _scatterRng.RandfRange(-ScatterRadiusM, ScatterRadiusM));
        }
        SfxLab.PlayStream3D(this, at, SfxLab.Get(Kind), VolumeDb, PitchJitter, MaxDistance, Bus);
    }
}
