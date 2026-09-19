using System.Collections.Generic;
using Godot;

namespace Sail.Game.Water.Fx;

/// <summary>
/// The lake's splash particles: a fixed pool of <see cref="GpuParticles3D"/> emitters, allocated
/// once and retargeted forever. Spec §9.1 is explicit — "allocate the pool once, never
/// instantiate per splash" — and this repo has the anti-pattern shipped in
/// <c>JuiceFx.Puff</c> (a fresh <c>CpuParticles3D</c> per event, self-freeing on a timer), which
/// is fine for one bonk and is not fine for six players thrashing at 0.45 s intervals.
///
/// Mirrors <c>FlashFx</c>'s pooled-lights pattern exactly, including the stale-timer guard: prefer
/// a slot whose burst has already finished, else steal the oldest round-robin, and never let a
/// late timer from a stolen slot cut a newer burst short.
///
/// <b><see cref="WaterFxTuning.MaxConcurrentBursts"/> is structural here, not a check.</b> The
/// pool physically cannot exceed six emitters, so the spec's concurrency ceiling holds even if a
/// future call site forgets it exists. A seventh simultaneous splash steals the oldest slot,
/// which at a 0.45 s throttle means cutting a burst that was already most of the way through its
/// lifetime.
///
/// <b>The <c>Amount</c> trap.</b> Writing <c>GpuParticles3D.Amount</c> reallocates the particle
/// buffer and RESTARTS the system — <c>NightDome</c> learned this and documents it. So
/// <see cref="WaterFxTuning.MaxParticlesPerBurst"/> is written once at allocation and per-burst
/// scaling goes through <c>AmountRatio</c>, which is exactly the dial that exists for it. A pool
/// that scaled by writing <c>Amount</c> would restart every burst it retargeted and would look
/// like a pool while behaving like a per-event allocation.
///
/// <b>Draw cost, stated honestly.</b> Up to six emitters, and therefore up to six draw calls,
/// but only while splashes are live: an idle slot is <c>Visible = false</c> and
/// <c>Emitting = false</c> and costs nothing. At rest — which is the state the lake is in for
/// almost every frame of a session, including every frame nobody is in the water — this system
/// adds zero. All six share one <see cref="QuadMesh"/> and one draw material, so the emission cap
/// direction §10.3 demands is a single write rather than six.
/// </summary>
public static class SplashParticlePool
{
    /// <summary>Spec §9.1's ceiling, as the allocation.</summary>
    public const int PoolSize = WaterFxTuning.MaxConcurrentBursts;

    /// <summary>Half-extent of every emitter's visibility box, metres. Set explicitly rather than
    /// left to the default for the reason <c>GrassField</c> and <c>NightDome</c> both set theirs:
    /// a particle system whose AABB does not cover where its particles actually go pops out of
    /// existence when the camera turns. Four metres comfortably contains the biggest burst (an
    /// entry at full speed, thrown ~2.2 m/s under gravity for under a second).</summary>
    private const float VisibilityHalfExtentM = 4f;

    private static readonly List<GpuParticles3D> Pool = new();

    /// <summary>Parallel to <see cref="Pool"/> by index: the tick at which that slot's burst is
    /// over and the slot is rentable again. Same standin <c>FlashFx</c> uses, and for the same
    /// reason — <see cref="GpuParticles3D.Emitting"/> goes false the instant a one-shot has
    /// emitted, long before the particles it emitted have finished living.</summary>
    private static readonly List<ulong> FreeAtMsec = new();

    private static int _next; // round-robin cursor; a full sweep = steal the oldest

    /// <summary>The one draw material every slot shares. Held statically so the night emission
    /// cap is a single write per frame rather than six, and so the six emitters can batch.</summary>
    private static StandardMaterial3D? _droplet;

    /// <summary>The night weight the listener last asked for. Always current, even before the
    /// material exists.</summary>
    private static float _requestedNight;

    /// <summary>The night weight actually written to the material. NaN until the first write, so
    /// the first write always happens — see <see cref="ApplyNightLevel"/> for the bug that made
    /// these two separate fields.</summary>
    private static float _appliedNight = float.NaN;

    /// <summary>How many emitters have actually been allocated. Never exceeds
    /// <see cref="PoolSize"/> — that claim is what <c>WaterFxSelfTest</c> proves by firing far
    /// more bursts than there are slots and counting the nodes afterwards.</summary>
    public static int AllocatedCount
    {
        get
        {
            Prune();
            return Pool.Count;
        }
    }

    /// <summary>How many slots are mid-burst right now.</summary>
    public static int LiveCount
    {
        get
        {
            Prune();
            ulong now = Time.GetTicksMsec();
            int live = 0;
            foreach (ulong freeAt in FreeAtMsec)
                if (freeAt > now)
                    live++;
            return live;
        }
    }

    /// <summary>
    /// Fire one burst at a world position. Every parameter comes from
    /// <see cref="WaterFxTuning"/>'s pure functions, so the values are decided (and tested)
    /// somewhere a headless run can reach and only spent here.
    /// </summary>
    public static void Burst(Node context, Vector3 globalPos, WaterEventKind kind,
        int count, float lifetimeSec, float velocityMps, float sizeM)
    {
        if (!GodotObject.IsInstanceValid(context) || !context.IsInsideTree())
            return;
        if (!globalPos.IsFinite())
            return;
        Node anchor = context.GetTree().CurrentScene ?? context.GetTree().Root;

        int idx = Rent(anchor);
        if (idx < 0)
            return;
        GpuParticles3D emitter = Pool[idx];

        // Everything is placed and configured BEFORE Emitting goes true. JuiceFx.Puff documents
        // the bug this ordering avoids from having shipped it: a system switched on before it is
        // positioned emits its first frame at the world origin.
        emitter.GlobalPosition = globalPos;
        emitter.Lifetime = Mathf.Max(0.05f, lifetimeSec);

        // AmountRatio, never Amount — see the class doc. Amount stays pinned at the ceiling for
        // the whole life of the node, so the buffer is allocated exactly once per slot, ever.
        emitter.AmountRatio =
            Mathf.Clamp(count / (float)WaterFxTuning.MaxParticlesPerBurst, 0.02f, 1f);

        if (emitter.ProcessMaterial is ParticleProcessMaterial proc)
            ShapeBurst(proc, kind, velocityMps, sizeM);

        emitter.Visible = true;
        emitter.Restart(); // a one-shot that has already fired will not re-fire on Emitting alone
        emitter.Emitting = true;

        // The slot is busy until the last particle it emitted has died, not until it has finished
        // emitting — plus a small margin so a slot is never re-rented on the exact frame its tail
        // is still on screen.
        ulong freeAt = Time.GetTicksMsec() + (ulong)((emitter.Lifetime + 0.15f) * 1000f);
        FreeAtMsec[idx] = freeAt;

        context.GetTree().CreateTimer(emitter.Lifetime + 0.15f).Timeout += () =>
        {
            if (!GodotObject.IsInstanceValid(emitter))
                return;
            int i = Pool.IndexOf(emitter);
            // Stale guard, copied from FlashFx and load-bearing for the same reason: if this slot
            // was stolen and re-rented for a NEWER burst after this timer was scheduled, the
            // recorded free-at no longer matches and the old timer must not hide the new burst.
            if (i < 0 || FreeAtMsec[i] != freeAt)
                return;
            emitter.Emitting = false;
            emitter.Visible = false;
        };
    }

    /// <summary>
    /// Drive the shared droplet material to a night weight — direction §10.3's hard cap, BOTH
    /// halves of it. On an unshaded material the albedo is the brightness (nothing in the scene
    /// lights it), so writing only the emission caps almost nothing; the first headed night
    /// capture proved that by coming back with a white flare on a green emission number.
    ///
    /// One write for the whole system rather than six, and suppressed when the weight has not
    /// moved, because this is called every frame from the listener's <c>_Process</c> against a
    /// smoothly-varying value and a per-frame material write for an unchanged number is the sort
    /// of cost that only looks free.
    /// </summary>
    public static void SetNightLevel(float nightWeight)
    {
        if (!float.IsFinite(nightWeight))
            return;
        _requestedNight = Mathf.Clamp(nightWeight, 0f, 1f);
        ApplyNightLevel();
    }

    /// <summary>
    /// Push the requested level onto the material if it is there and the value has moved.
    ///
    /// <b>The requested and the applied level are two fields, and that is the whole point.</b>
    /// Collapsing them into one shipped a real bug: the listener calls
    /// <see cref="SetNightLevel"/> every frame from <c>_Process</c>, which starts long before the
    /// first splash of a session, so by the time the first burst allocates the shared material
    /// the single field already equals the current night weight — the write short-circuits, and
    /// the material keeps the DAY values it was constructed with. During the flat night band the
    /// weight is exactly 1.0 and never moves again, so the first splash of the night would render
    /// at full day brightness for the rest of the night.
    ///
    /// The capture lab missed it by pure luck of ordering: it fires a warm-up burst inside
    /// <c>_Ready</c>'s synchronous prologue, so the material existed before the first
    /// <c>_Process</c> and got written correctly. A player gets the other order.
    /// </summary>
    private static void ApplyNightLevel()
    {
        if (_droplet == null)
            return;
        // Suppressed when nothing has moved: this runs every frame against a smoothly-varying
        // value, and a per-frame material write for an unchanged number is the sort of cost that
        // only looks free.
        if (Mathf.Abs(_requestedNight - _appliedNight) < 0.004f)
            return;
        _appliedNight = _requestedNight;
        _droplet.EmissionEnergyMultiplier = WaterFxTuning.EmissionEnergy(_requestedNight);
        _droplet.AlbedoColor = WaterFxTuning.DropletTint(_requestedNight);
    }

    /// <summary>Drop every pooled node and forget the pool. For scene teardown between tests
    /// only — the live game never calls it, because a pool that is torn down and rebuilt per
    /// scene is not a pool.</summary>
    public static void Reset()
    {
        foreach (GpuParticles3D p in Pool)
            if (GodotObject.IsInstanceValid(p))
                p.QueueFree();
        Pool.Clear();
        FreeAtMsec.Clear();
        _next = 0;
        // The shared draw material goes too. It is part of the pool, and a Reset that kept it
        // would make the "material created AFTER the night level was set" path — the one a real
        // session always takes and the one that shipped a bug — permanently unreachable from a
        // test. Proved by running the negative control: with the material retained, reintroducing
        // the bug left the suite green.
        _droplet = null;
        _appliedNight = float.NaN;
    }

    // --- Renting -------------------------------------------------------------------------------

    private static void Prune()
    {
        for (int i = Pool.Count - 1; i >= 0; i--)
        {
            if (!GodotObject.IsInstanceValid(Pool[i]))
            {
                Pool.RemoveAt(i);
                FreeAtMsec.RemoveAt(i);
            }
        }
    }

    private static int Rent(Node anchor)
    {
        Prune();

        ulong now = Time.GetTicksMsec();
        for (int i = 0; i < Pool.Count; i++)
        {
            if (FreeAtMsec[i] <= now)
            {
                Reanchor(Pool[i], anchor);
                return i;
            }
        }
        if (Pool.Count < PoolSize)
        {
            GpuParticles3D fresh = Allocate(Pool.Count);
            anchor.AddChild(fresh);
            Pool.Add(fresh);
            FreeAtMsec.Add(0);
            return Pool.Count - 1;
        }
        int victim = _next;
        _next = (_next + 1) % Pool.Count;
        Reanchor(Pool[victim], anchor);
        return victim;
    }

    private static void Reanchor(GpuParticles3D p, Node anchor)
    {
        if (p.GetParent() != anchor)
        {
            p.GetParent()?.RemoveChild(p);
            anchor.AddChild(p);
        }
    }

    // --- Allocation ------------------------------------------------------------------------------

    private static GpuParticles3D Allocate(int index) => new()
    {
        Name = $"SplashPool{index}",
        // Written once, here, and never again — see the class doc's Amount trap.
        Amount = WaterFxTuning.MaxParticlesPerBurst,
        OneShot = true,
        Explosiveness = 1f, // the whole burst leaves at once; a splash is not a stream
        Emitting = false,
        Visible = false,
        Lifetime = WaterFxTuning.EntryLifetimeSec,
        // Each slot gets its OWN process material so a burst's velocity and grain cannot be
        // rewritten out from under a concurrently-live burst on another slot. Six allocations at
        // startup buys per-burst independence for the whole session.
        ProcessMaterial = BuildProcessMaterial(),
        DrawPass1 = BuildDropletMesh(),
        VisibilityAabb = new Aabb(
            new Vector3(-VisibilityHalfExtentM, -VisibilityHalfExtentM, -VisibilityHalfExtentM),
            new Vector3(VisibilityHalfExtentM * 2f, VisibilityHalfExtentM * 2f,
                VisibilityHalfExtentM * 2f)),
    };

    private static ParticleProcessMaterial BuildProcessMaterial() => new()
    {
        Direction = Vector3.Up,
        Spread = 48f,
        Gravity = new Vector3(0f, -9.8f, 0f),
        InitialVelocityMin = 1.0f,
        InitialVelocityMax = 2.2f,
        ScaleMin = WaterFxTuning.ParticleScale,
        ScaleMax = WaterFxTuning.ParticleScale * 1.6f,
        // Air drag on a water droplet is real and it is what stops spray from reading as
        // buckshot: without it every particle traces a clean parabola and the burst looks like a
        // firework.
        DampingMin = 0.4f,
        DampingMax = 1.4f,
        EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
        EmissionSphereRadius = 0.25f,
    };

    /// <summary>
    /// Per-event shaping of one slot's process material. This is where the five events stop being
    /// the same puff at five sizes: an entry throws up and out, a thrash is lower and wider, the
    /// go-under is a ring that lifts and settles rather than anything thrown, the sputter sheds
    /// water off a body at the shore, and the exit drips.
    ///
    /// <b>The go-under is a ring and not a second system.</b> Direction's brief says "a collapsing
    /// ring and bubbles"; both are carried by one emitter — a ring emission shape at arm's radius
    /// with low velocity and heavy damping, which lifts a hand's height and settles back rather
    /// than being thrown. A second emitter for the bubbles would double the draw cost of the one
    /// event that already has the longest lifetime, and would buy a detail nobody sees: by the
    /// time the bubbles would read, ChillCueOverlay's hard cut has taken the screen.
    /// </summary>
    private static void ShapeBurst(ParticleProcessMaterial proc, WaterEventKind kind,
        float velocityMps, float sizeM)
    {
        proc.ScaleMin = sizeM;
        proc.ScaleMax = sizeM * 1.6f;
        proc.InitialVelocityMin = velocityMps * 0.45f;
        proc.InitialVelocityMax = velocityMps;

        switch (kind)
        {
            case WaterEventKind.WentUnder:
                proc.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Ring;
                proc.EmissionRingAxis = Vector3.Up;
                proc.EmissionRingRadius = 0.85f;
                proc.EmissionRingInnerRadius = 0.55f;
                proc.EmissionRingHeight = 0.05f;
                proc.Spread = 18f;      // straight up out of the ring, not thrown outward
                proc.DampingMin = 2.0f; // heavy: it settles rather than flying
                proc.DampingMax = 4.0f;
                break;

            case WaterEventKind.Exited:
                proc.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere;
                proc.EmissionSphereRadius = 0.35f;
                proc.Direction = Vector3.Down; // drips fall off a body, they are not thrown
                proc.Spread = 30f;
                proc.DampingMin = 0.2f;
                proc.DampingMax = 0.6f;
                break;

            case WaterEventKind.Sputtered:
                proc.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere;
                proc.EmissionSphereRadius = 0.45f;
                proc.Direction = Vector3.Up;
                proc.Spread = 70f; // a shake-off goes everywhere
                proc.DampingMin = 0.8f;
                proc.DampingMax = 2.0f;
                break;

            case WaterEventKind.Splash:
                proc.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere;
                proc.EmissionSphereRadius = 0.3f;
                proc.Direction = Vector3.Up;
                proc.Spread = 62f; // low and wide — thrashing, not leaping
                proc.DampingMin = 0.6f;
                proc.DampingMax = 1.6f;
                break;

            default: // Entered
                proc.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere;
                proc.EmissionSphereRadius = 0.25f;
                proc.Direction = Vector3.Up;
                proc.Spread = 42f;
                proc.DampingMin = 0.4f;
                proc.DampingMax = 1.4f;
                break;
        }
    }

    private static QuadMesh BuildDropletMesh() => new()
    {
        // Unit quad: the actual grain comes from the process material's scale range, which is
        // per-slot and therefore per-burst. A mesh sized per burst would have to be per-slot too,
        // and then the six emitters could not share a draw material.
        Size = Vector2.One,
        Material = DropletMaterial(),
    };

    /// <summary>
    /// The droplet's look: an unshaded, particle-billboarded, additively-lifted near-white with a
    /// faint cool cast.
    ///
    /// Unshaded on purpose. A lit droplet at night would be black — the camp core's light does
    /// not reach the lake and W3's night grade is deliberately near-lightless — and a splash that
    /// is invisible exactly when the register needs it curt is not curt, it is missing. The
    /// emission energy is what carries it, and that is the number direction §10.3 caps.
    ///
    /// <b>ART-BIBLE §4.4 is still blank</b>, so nothing here claims to satisfy a material law;
    /// these are stated picks, not a fill. The cool cast (a hair of blue over white) is the one
    /// deliberate colour call, and it is there so spray reads as water rather than as the dust
    /// motes <c>vfx-particles</c> already puts in the air.
    /// </summary>
    private static StandardMaterial3D Droplet() => _droplet ??= new StandardMaterial3D
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
        // MANDATORY with any billboard mode, and it is not obvious. A billboard material replaces
        // MODELVIEW_MATRIX in the vertex shader, and with keep-scale off it throws the model's
        // scale away with it — which is where the per-particle scale from ScaleMin/ScaleMax
        // lives. The first headed capture came back with 7.5 cm droplets rendered as one-metre
        // white slabs, and every headless check passed, because the process material really did
        // hold the right numbers; they were being discarded a layer further down.
        BillboardKeepScale = true,
        AlbedoColor = WaterFxTuning.DropletTint(0f),
        EmissionEnabled = true,
        Emission = new Color(0.86f, 0.93f, 1.0f),
        EmissionEnergyMultiplier = WaterFxTuning.DayEmissionEnergy,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        DisableReceiveShadows = true,
    };

    /// <summary>Builds the shared material on first use and immediately pushes whatever night
    /// level the listener has been asking for onto it. The push is not optional: the material is
    /// constructed at day values, and without this a session whose first splash happens at night
    /// renders it as though it were noon (see <see cref="ApplyNightLevel"/>).</summary>
    private static StandardMaterial3D DropletMaterial()
    {
        if (_droplet != null)
            return _droplet;
        _droplet = Droplet();
        _appliedNight = float.NaN; // force the first write through the suppression check
        ApplyNightLevel();
        return _droplet;
    }
}
