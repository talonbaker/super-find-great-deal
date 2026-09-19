using Godot;
using MpFoundation;
using MpFoundation.Game.Sandbox;
using MpFoundation.Game.World;

namespace Sail.Game.Water.Fx;

/// <summary>
/// Packet W4: the listener that turns <see cref="WaterService"/>'s five replicated events into
/// splash particles and one-shots, and the one place the submersion filter is driven from.
///
/// <b>It is wired into the live game, not only into a lab.</b> <c>Gameplay._Ready</c> calls
/// <see cref="Attach"/> immediately after <c>ChillCueOverlay.Attach</c>, on the same line of
/// reasoning: both subscribe to <see cref="WaterService.Instance"/>, which does not exist until
/// the service has been added a few lines above. A player in a real session hears this; the lab
/// exists to photograph it, not to be the only thing that runs it.
///
/// <b>Client-local presentation over a server-owned event.</b> This class writes nothing,
/// replicates nothing, and never touches <see cref="WaterService"/>'s state. The
/// "exactly once, and every client in the same instant" half of spec §9.1 is not defended here
/// and does not need to be: W2 assigns the event kind on the server and broadcasts it, so by the
/// time this class sees an event the guarantee has already been made structurally. Predicting a
/// splash locally would have broken it, and that is the whole reason the contract is shaped the
/// way it is.
///
/// <b>The night difference is subtraction, and it arrives through one number.</b>
/// <c>AmbientBedMath.NightWeight(CycleDriver.Phase)</c> — the shipped bands, never a second set,
/// the same discipline <c>SparseSfxEmitter</c> holds. It reaches particle count, particle
/// lifetime and droplet brightness (emission AND albedo — see <see cref="SplashParticlePool"/>
/// for why an unshaded material needs both), and it reaches nothing else. Not size, not velocity,
/// and not the clip set: there is no night clip, no night sting and no second preset anywhere in
/// this packet (direction §5).
/// </summary>
public partial class WaterFx : Node
{
    /// <summary>The live listener, or null outside a gameplay session.</summary>
    public static WaterFx? Instance { get; private set; }

    // --- Instrumentation ------------------------------------------------------------------------
    //
    // Counters rather than assertions, in the shape
    // WaterService.TapeRuinCount already uses: a headless run cannot see a splash or hear a
    // one-shot, but it can prove how many of each were dispatched, how many the distance cull
    // dropped, and how many the voice budget refused. That is the half of this feature a machine
    // can actually check, so it is worth exposing properly.

    /// <summary>Water events received, all five kinds, all peers.</summary>
    public int EventsSeen { get; private set; }

    /// <summary>Bursts actually handed to the pool.</summary>
    public int BurstsEmitted { get; private set; }

    /// <summary>Bursts the 35 m cull refused (spec §9.1).</summary>
    public int BurstsCulled { get; private set; }

    /// <summary>One-shots actually played.</summary>
    public int VoicesPlayed { get; private set; }

    /// <summary>One-shots the water voice budget refused — the number that proves water cannot
    /// monopolise <c>SfxLab</c>'s shared 14 (see
    /// <see cref="WaterFxTuning.MaxConcurrentWaterVoices"/>).</summary>
    public int VoicesDeniedByBudget { get; private set; }

    /// <summary>One-shots the 45 m cull refused.</summary>
    public int VoicesCulled { get; private set; }

    // --- Local state --------------------------------------------------------------------------

    /// <summary>Busy-until ticks for water's self-imposed share of the shared one-shot pool. A
    /// fixed array rather than a list: the cap is a compile-time constant and an allocation-free
    /// governor is the whole point.</summary>
    private readonly ulong[] _voiceFreeAt = new ulong[WaterFxTuning.MaxConcurrentWaterVoices];

    private float _submersion;
    private float _nightWeight;

    /// <summary>The cold's audio channel. A child rather than a sibling so it lives and dies with
    /// this listener and cannot outlast the session that built it.</summary>
    private ChillChatter _chatter = null!;

    /// <summary>The chill audio channel, for the self-test. Null before <c>_Ready</c>.</summary>
    public ChillChatter? Chatter => _chatter;

    /// <summary>
    /// Attach to a scene root. No-op on a headless peer (nothing to see, nobody to hear it, and a
    /// dedicated server should not be synthesising five clips at world load) and no-op if one
    /// already exists — the same guard <c>ChillCueOverlay.Attach</c> and <c>BedSleepFade.Attach</c>
    /// use, for the same reason: a reconnect re-runs the gameplay setup path.
    /// </summary>
    public static void Attach(Node sceneRoot)
    {
        if (NetworkManager.Instance is { IsHeadless: true })
            return;
        if (Instance != null && IsInstanceValid(Instance))
            return;
        sceneRoot.AddChild(new WaterFx { Name = "WaterFx" });
    }

    public override void _Ready()
    {
        Instance = this;
        SubmersionFilter.Ensure();

        // The cold's third redundant channel, which ChillCueOverlay's own doc hands to this
        // packet by name. Non-positional and owner-only — see its class doc for why that is the
        // resolution to direction §10.1's flagged 5-free-slots-against-5-chatter-voices collision
        // rather than merely a cheaper version of it.
        _chatter = new ChillChatter { Name = "ChillChatter" };
        AddChild(_chatter);

        if (WaterService.Instance is { } water)
        {
            water.Entered += OnEntered;
            water.Exited += OnExited;
            water.Splash += OnSplash;
            water.WentUnder += OnWentUnder;
            water.Sputtered += OnSputtered;
        }
    }

    public override void _ExitTree()
    {
        if (WaterService.Instance is { } water)
        {
            water.Entered -= OnEntered;
            water.Exited -= OnExited;
            water.Splash -= OnSplash;
            water.WentUnder -= OnWentUnder;
            water.Sputtered -= OnSputtered;
        }
        // Hand the buses back. Not optional — see SubmersionFilter.Release.
        SubmersionFilter.Release();
        if (Instance == this)
            Instance = null;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (!float.IsFinite(dt) || dt <= 0f)
            return;

        UpdateNightWeight();
        UpdateSubmersion(dt);
    }

    // --- The night subtraction --------------------------------------------------------------

    private void UpdateNightWeight()
    {
        // Holds at DAY until the clock is synced, exactly as hard and no harder than every other
        // consumer of CycleDriver. A splash that came out night-dim on a fresh join, before the
        // server had said what time it was, would be the same class of bug as birdsong at 3 a.m.
        _nightWeight = CycleDriver.Instance is { Synced: true } driver
            ? AmbientBedMath.NightWeight(driver.Phase)
            : 0f;
        SplashParticlePool.SetNightLevel(_nightWeight);
    }

    // --- The submersion sweep -------------------------------------------------------------------

    /// <summary>
    /// Drives the filter from the LOCAL peer's authoritative sputter phase rather than from a
    /// timer started on the <c>WentUnder</c> event.
    ///
    /// <b>This deliberately differs from <c>ChillCueOverlay</c>'s veil</b>, which does use a local
    /// timer and documents why: the hard cut has to land on the exact frame even under loss. The
    /// trade goes the other way here. The phase is broadcast at 6 Hz, so an edge can arrive up to
    /// ~167 ms late — which is imperceptible inside a 1.5 s sweep — whereas the failure mode of an
    /// orphaned local timer is a permanently muffled game with nothing on screen to explain it.
    /// Reading the replicated phase every frame is self-correcting by construction: a dropped
    /// message, a late join mid-episode, or a disconnect all resolve to <c>None</c> and the filter
    /// opens on its own.
    ///
    /// <b>Only the local peer's phase counts.</b> Watching someone else go under does not put your
    /// own head underwater.
    /// </summary>
    private void UpdateSubmersion(float dt)
    {
        float target = 0f;
        if (WaterService.Instance is { Synced: true } water)
        {
            int me = Multiplayer?.MultiplayerPeer != null ? Multiplayer.GetUniqueId() : 1;
            // GoingUnder only. Recovering is prone at the shore, coughing — out of the water — so
            // the world comes back as the screen does, which is the shape of the release
            // direction §5 asks the day sputter to be.
            target = water.PhaseOf(me) == SputterPhase.GoingUnder ? 1f : 0f;
        }

        float seconds = target > _submersion
            ? WaterFxTuning.FilterCloseSec
            : WaterFxTuning.FilterOpenSec;
        _submersion = Mathf.MoveToward(_submersion, target, dt / Mathf.Max(0.0001f, seconds));
        SubmersionFilter.Apply(_submersion);
    }

    // --- The five events ---------------------------------------------------------------------

    private void OnEntered(WaterEvent e) => Dispatch(WaterEventKind.Entered, e);
    private void OnExited(WaterEvent e) => Dispatch(WaterEventKind.Exited, e);
    private void OnSplash(WaterEvent e) => Dispatch(WaterEventKind.Splash, e);
    private void OnWentUnder(WaterEvent e) => Dispatch(WaterEventKind.WentUnder, e);
    private void OnSputtered(WaterEvent e) => Dispatch(WaterEventKind.Sputtered, e);

    /// <summary>
    /// One event becomes at most one burst and at most one one-shot. Public so
    /// <see cref="WaterFxSelfTest"/> can drive the exact live path rather than a parallel copy of
    /// it — a test that exercises its own dispatcher proves nothing about the one players get.
    /// </summary>
    public void Dispatch(WaterEventKind kind, WaterEvent e)
    {
        EventsSeen++;
        if (!e.Position.IsFinite())
            return;

        // GetViewport().GetCamera3D() is how every distance-culled system in this repo resolves
        // the local viewpoint (the nameplates, the since-removed scenery LODs) — there is no
        // AvatarCamera helper and inventing one here would be a sixth pattern. The active Camera3D
        // is also Godot's default audio listener, so one position serves both culls honestly.
        Camera3D? cam = GetViewport()?.GetCamera3D();
        float distSq = cam != null
            ? cam.GlobalPosition.DistanceSquaredTo(e.Position)
            : 0f; // no camera yet (a self-test scene, one frame after load): do not cull

        DispatchParticles(kind, e, distSq);
        DispatchAudio(kind, e, distSq);
    }

    private void DispatchParticles(WaterEventKind kind, WaterEvent e, float distSq)
    {
        if (!WaterFxTuning.ParticlesVisibleAt(distSq))
        {
            BurstsCulled++;
            return;
        }
        SplashParticlePool.Burst(
            this,
            e.Position,
            kind,
            WaterFxTuning.BurstCount(kind, e.Speed, _nightWeight),
            WaterFxTuning.BurstLifetimeSec(kind, _nightWeight),
            WaterFxTuning.BurstVelocityMps(kind, e.Speed),
            WaterFxTuning.ParticleSizeM(kind));
        BurstsEmitted++;
    }

    private void DispatchAudio(WaterEventKind kind, WaterEvent e, float distSq)
    {
        if (!WaterFxTuning.AudibleAt(distSq))
        {
            VoicesCulled++;
            return;
        }
        AudioStreamWav clip = WaterSfx.For(kind);
        if (!TryTakeVoice((float)clip.GetLength()))
        {
            VoicesDeniedByBudget++;
            return;
        }
        // Sfx, never Ambient_Scenery. Direction §10.1 flags this as the routing mistake a builder
        // reading "water ambience" will make: the scenery lane is un-ducked and un-compressed for
        // sparse transients, and putting player-caused event feedback there also hides it behind
        // the player's Ambient slider, which is the wrong slider for a thing the player did.
        SfxLab.PlayStream3D(this, e.Position, clip,
            WaterFxTuning.VolumeDb(kind, e.Speed),
            WaterFxTuning.PitchJitter,
            WaterFxTuning.AudioMaxDistanceM,
            AudioBuses.Sfx);
        VoicesPlayed++;
    }

    /// <summary>
    /// Water's voice governor, and the answer to "how can water not starve proximity voice".
    ///
    /// The structural half is that it cannot reach voice at all: <c>VoiceSpeaker</c> owns one
    /// <c>AudioStreamPlayer3D</c> per remote peer, outside <c>SfxLab</c> entirely, and every water
    /// sound goes through <c>SfxLab</c>'s fixed 14-slot pool. Water cannot take a speaker's node
    /// and it adds no continuous emitter, so the concurrent-3D-voice count after this packet is
    /// still 5 + 14 = 19 of the written 24.
    ///
    /// This method is the second half: the steal policy INSIDE the pool. SfxLab's own policy is
    /// oldest-first across all 14, which is right for footsteps and wrong for a
    /// six-player thrash — six players flailing at 0.45 s intervals could hold every slot and
    /// steal the shutter click, the match strike and every footstep in the camp. So water caps
    /// itself at four, refuses the fifth outright, and never steals: a denied splash simply does
    /// not sound. Refusing is strictly better than stealing here, because the thing a fifth
    /// simultaneous splash would have stolen is a sound somebody was listening to, and the thing
    /// it loses is one splash out of five that were all happening at once.
    ///
    /// <b>Footnote, 2026-08-13.</b> This paragraph was written from SfxLab's class doc, and until
    /// that date the doc and the code disagreed: the code stole a round-robin cursor's target,
    /// which is not oldest-first and can be the newest sound in the pool. The reasoning above
    /// survives — it never depended on WHICH shot got stolen, only on water not being the one
    /// doing the stealing — but the premise it rested on is only true now that SfxLab.Rent
    /// genuinely steals the oldest. The word "round-robin" is struck rather than left standing;
    /// a stale mechanism named in a doc is how the next caller gets misled the same way.
    /// </summary>
    private bool TryTakeVoice(float clipSeconds)
    {
        ulong now = Time.GetTicksMsec();
        ulong busyFor = (ulong)(Mathf.Max(0.05f, clipSeconds) * 1000f);
        for (int i = 0; i < _voiceFreeAt.Length; i++)
        {
            if (_voiceFreeAt[i] <= now)
            {
                _voiceFreeAt[i] = now + busyFor;
                return true;
            }
        }
        return false;
    }

    /// <summary>How many of water's four voice slots are sounding right now. Instrumentation for
    /// the budget proof.</summary>
    public int WaterVoicesLive
    {
        get
        {
            ulong now = Time.GetTicksMsec();
            int live = 0;
            foreach (ulong freeAt in _voiceFreeAt)
                if (freeAt > now)
                    live++;
            return live;
        }
    }

    /// <summary>Test hook: override the night weight the next dispatch reads. The live path
    /// recomputes it from the clock every frame, so this only holds for as long as no frame
    /// passes — which is exactly the window a self-test dispatching synchronously occupies.</summary>
    public void TestSetNightWeight(float weight) =>
        _nightWeight = Mathf.Clamp(weight, 0f, 1f);
}
