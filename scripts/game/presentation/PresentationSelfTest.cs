using Godot;

namespace MpFoundation.Game.Presentation;

/// <summary>Headless self-test for the presentation-profile layer. Run via
/// --presentation-selftest; exits 0/1. Each case is its own method, OR'd into the exit
/// code so one failure doesn't hide the rest. Sound
/// output itself is unhearable headless — these cases pin the DATA and the resolution
/// logic (profile contents, event→responses lookup, Fire's safety guards, and — via
/// FireCore directly, since Fire itself no-ops headless — the MinIntensity gate, the
/// CustomSound/Sound pick, the intensity volume formula, and puff spawning), which is
/// exactly the part a typo'd .tres or a broken index would silently corrupt.</summary>
public static class PresentationSelfTest
{
    public static int Run(SceneTree tree)
    {
        int code = 0;
        code |= RunResolution();
        code |= RunFireSafety(tree);
        code |= RunFireCore(tree);
        code |= RunProfileAssets();

        if (code == 0)
            GD.Print("[presentation-selftest] PASS");
        return code;
    }

    /// <summary>An in-code profile resolves per event: multiple responses per event
    /// come back together, unmapped events return an EMPTY array (never null), the
    /// index survives repeated lookups (built once, then cached), and reassigning
    /// Responses INVALIDATES that cache (a .tres live-reload re-runs the setter on the
    /// same cached instance — a stale index would serve the old data forever).</summary>
    private static int RunResolution()
    {
        int code = 0;
        var profile = new PresentationProfile
        {
            Responses = new[]
            {
                new EventResponse { Event = ActorEvent.Step, VolumeDb = -19f },
                new EventResponse { Event = ActorEvent.Step, VolumeDb = -22f },
                new EventResponse { Event = ActorEvent.Jump, VolumeDb = -12f },
            },
        };

        EventResponse[] step = profile.ResponsesFor(ActorEvent.Step);
        if (step.Length != 2)
            code |= Fail($"expected 2 Step responses, got {step.Length}");
        else if (step[0].VolumeDb != -19f || step[1].VolumeDb != -22f)
            code |= Fail($"Step responses out of order or wrong values ({step[0].VolumeDb}, {step[1].VolumeDb})");

        if (profile.ResponsesFor(ActorEvent.Jump).Length != 1)
            code |= Fail("expected 1 Jump response");

        EventResponse[] unmapped = profile.ResponsesFor(ActorEvent.Bark);
        if (unmapped == null)
            code |= Fail("unmapped event must return an empty array, not null");
        else if (unmapped.Length != 0)
            code |= Fail($"unmapped event must resolve to 0 responses, got {unmapped.Length}");

        if (!ReferenceEquals(profile.ResponsesFor(ActorEvent.Step), step))
            code |= Fail("ResponsesFor must return the same cached array on repeat lookups (zero-alloc contract)");

        // The index is now warm (every lookup above built and hit it). Re-populating
        // Responses — exactly what ResourceLoader's CacheMode.Replace does to an
        // already-queried instance on an editor .tres live-reload — must rebuild it.
        profile.Responses = new[]
        {
            new EventResponse { Event = ActorEvent.Bump, VolumeDb = -3f },
        };

        EventResponse[] rebuiltBump = profile.ResponsesFor(ActorEvent.Bump);
        if (rebuiltBump.Length != 1)
            code |= Fail($"after reassigning Responses, expected 1 Bump response, got {rebuiltBump.Length} (stale index)");
        else if (rebuiltBump[0].VolumeDb != -3f)
            code |= Fail($"after reassigning Responses, Bump resolved to the wrong value ({rebuiltBump[0].VolumeDb})");

        int rebuiltStep = profile.ResponsesFor(ActorEvent.Step).Length;
        if (rebuiltStep != 0)
            code |= Fail($"after reassigning Responses, the dropped Step entries must be gone, got {rebuiltStep} (stale index)");

        return code;
    }

    /// <summary>Fire's two guards, checked in order: a null profile is a safe no-op —
    /// warn once, never a crash — and (since the null check now runs BEFORE the headless
    /// check) that's reachable even though this self-test's own process is headless.
    /// Then, given a non-null profile, a headless instance early-outs before touching the
    /// audio pool at all: the dedicated server must not pay for sounds it can't output.
    ///
    /// This proves ONLY those two guards. It does NOT exercise MinIntensity gating, the
    /// CustomSound/Sound pick, the intensity volume formula, or puff spawning — Fire
    /// never reaches FireCore while headless, and this test IS headless. That response
    /// logic is proven by <see cref="RunFireCore"/>, which drives FireCore directly.</summary>
    private static int RunFireSafety(SceneTree tree)
    {
        int code = 0;
        var anchor = new Node3D { Name = "FireSafetyAnchor" };
        tree.Root.AddChild(anchor);

        int childrenBefore = tree.Root.GetChildCount();
        ActorFx.Fire(anchor, null, ActorEvent.Step, Vector3.Zero); // must not throw

        var profile = new PresentationProfile
        {
            Responses = new[]
            {
                new EventResponse { Event = ActorEvent.Step, Sound = Sandbox.Sfx.Step, PuffCount = 3 },
            },
        };
        ActorFx.Fire(anchor, profile, ActorEvent.Step, Vector3.Zero, 1f);
        ActorFx.Fire(anchor, profile, ActorEvent.Bark, Vector3.Zero); // unmapped: silent

        int childrenAfter = tree.Root.GetChildCount();
        if (childrenAfter != childrenBefore)
            code |= Fail($"headless Fire must create no nodes (root children {childrenBefore} -> {childrenAfter})");

        anchor.QueueFree();
        return code;
    }

    /// <summary>Drives <see cref="ActorFx.FireCore"/> directly — bypassing Fire's
    /// headless early-out, which this self-test's own headless process would otherwise
    /// trigger every time — to exercise the actual response logic: MinIntensity gating,
    /// the CustomSound-over-Sound pick, the intensity volume formula, and puff spawning.
    /// Pool players are a shared pool by design (SfxLab) and persist/get reused across
    /// cases, so assertions here are DELTAS and searches, never assumed zero-state.</summary>
    private static int RunFireCore(SceneTree tree)
    {
        int code = 0;
        var anchor = new Node3D { Name = "FireCoreAnchor" };
        tree.Root.AddChild(anchor);

        var profile = new PresentationProfile
        {
            Responses = new[]
            {
                // Response A: plain sound, no gate — volume scales with intensity.
                new EventResponse
                {
                    Event = ActorEvent.Step,
                    Sound = Sandbox.Sfx.Step,
                    VolumeDb = -19f,
                    IntensityVolumeBoostDb = 3f,
                },
                // Response B: gated by MinIntensity, particle-bearing.
                new EventResponse
                {
                    Event = ActorEvent.Step,
                    Sound = Sandbox.Sfx.Squeak,
                    MinIntensity = 0.9f,
                    PuffCount = 3,
                    PuffSize = 0.05f,
                },
            },
        };

        // --- Case a: intensity 0 — response A fires at its base volume; response B is
        // gated out by MinIntensity, so no particles yet. ---
        int poolBefore = CountPoolPlayers(tree.Root);
        ActorFx.FireCore(anchor, profile, ActorEvent.Step, Vector3.Zero, intensity: 0f);

        int poolAfterA = CountPoolPlayers(tree.Root);
        if (poolAfterA != poolBefore + 1)
            code |= Fail($"case a: expected exactly one new pool player, {poolBefore} -> {poolAfterA}");

        if (FindPlayingPoolPlayer(tree.Root, -19f) == null)
            code |= Fail("case a: no playing pool player at VolumeDb -19 (intensity-0 formula: -19 + 3x0)");

        int particlesAfterA = CountParticleChildren(anchor);
        if (particlesAfterA != 0)
            code |= Fail($"case a: MinIntensity-0.9 response must not fire at intensity 0, got {particlesAfterA} puffs");

        // --- Case b: intensity 1 — response A's volume shifts by the full boost;
        // response B now clears MinIntensity and its puff spawns. ---
        ActorFx.FireCore(anchor, profile, ActorEvent.Step, Vector3.Zero, intensity: 1f);

        if (FindPlayingPoolPlayer(tree.Root, -16f) == null)
            code |= Fail("case b: no playing pool player at VolumeDb -16 (intensity-1 formula: -19 + 3x1)");

        int particlesAfterB = CountParticleChildren(anchor);
        if (particlesAfterB != particlesAfterA + 1)
            code |= Fail($"case b: expected puff count to grow by 1, {particlesAfterA} -> {particlesAfterB}");

        // --- Case c: unmapped event — profile has no Jump responses, so nothing fires. ---
        int poolBeforeC = CountPoolPlayers(tree.Root);
        ActorFx.FireCore(anchor, profile, ActorEvent.Jump, Vector3.Zero, intensity: 1f);

        int poolAfterC = CountPoolPlayers(tree.Root);
        if (poolAfterC != poolBeforeC)
            code |= Fail($"case c: unmapped event must not touch the pool, {poolBeforeC} -> {poolAfterC}");

        int particlesAfterC = CountParticleChildren(anchor);
        if (particlesAfterC != particlesAfterB)
            code |= Fail($"case c: unmapped event must not spawn particles, {particlesAfterB} -> {particlesAfterC}");

        // --- Case d: CustomSound-over-Sound — a response with BOTH set must play the
        // CustomSound stream, never the Sound-derived one. Reference equality against
        // the exact stream instance is the only proof; Sfx.Step's cached stream is a
        // different object, so this would fail if the pick fell through to Sound. ---
        AudioStream customStream = Sandbox.SfxLab.Get(Sandbox.Sfx.Chirp);
        var customProfile = new PresentationProfile
        {
            Responses = new[]
            {
                new EventResponse
                {
                    Event = ActorEvent.Jump,
                    Sound = Sandbox.Sfx.Step,
                    CustomSound = customStream,
                    VolumeDb = -25f,
                },
            },
        };
        ActorFx.FireCore(anchor, customProfile, ActorEvent.Jump, Vector3.Zero, intensity: 0f);

        AudioStreamPlayer3D? customPlayer = FindPlayingPoolPlayer(tree.Root, -25f);
        if (customPlayer == null)
            code |= Fail("case d: no playing pool player at VolumeDb -25 (CustomSound response)");
        else if (!ReferenceEquals(customPlayer.Stream, customStream))
            code |= Fail("case d: CustomSound must win over Sound — pool player is not playing the CustomSound stream instance");

        anchor.QueueFree();
        return code;
    }

    /// <summary>Recursive count of pool players anywhere in the tree — SfxLab anchors
    /// them under CurrentScene ?? Root, not under the caller's context node, so a
    /// name-prefix search from the tree root is the only reliable way to find them.</summary>
    private static int CountPoolPlayers(Node root)
    {
        int count = root.Name.ToString().StartsWith("SfxPool") ? 1 : 0;
        foreach (Node child in root.GetChildren())
            count += CountPoolPlayers(child);
        return count;
    }

    /// <summary>Finds a pool player that is currently playing at (approximately) the
    /// given VolumeDb — the closest thing to "the response we just fired" without SfxLab
    /// exposing rent history to test code.</summary>
    private static AudioStreamPlayer3D? FindPlayingPoolPlayer(Node root, float expectedVolumeDb)
    {
        if (root is AudioStreamPlayer3D player && root.Name.ToString().StartsWith("SfxPool")
            && player.Playing && Mathf.Abs(player.VolumeDb - expectedVolumeDb) < 0.01f)
            return player;
        foreach (Node child in root.GetChildren())
        {
            AudioStreamPlayer3D? found = FindPlayingPoolPlayer(child, expectedVolumeDb);
            if (found != null)
                return found;
        }
        return null;
    }

    /// <summary>Immediate CpuParticles3D children of the given anchor — JuiceFx.Puff
    /// parents bursts directly under the context node passed to it, one level deep.</summary>
    private static int CountParticleChildren(Node anchor)
    {
        int count = 0;
        foreach (Node child in anchor.GetChildren())
            if (child is CpuParticles3D)
                count++;
        return count;
    }

    private const string PropProfilePath = "res://assets/items/default/prop_presentation.tres";

    /// <summary>Pins the hand-authored .tres file against the constants table in the
    /// plan (docs/superpowers/plans/2026-07-14-presentation-profiles.md, Task 4): the
    /// file loads, every expected event resolves with the exact ported values, and a
    /// property-name typo (which Godot drops SILENTLY on load) cannot ship. Only the prop
    /// profile is pinned now — the creature profile it used to sit beside left with its
    /// creature.</summary>
    private static int RunProfileAssets()
    {
        int code = 0;

        var prop = GD.Load<PresentationProfile>(PropProfilePath);
        if (prop == null)
            return code | Fail($"could not load {PropProfilePath}");
        // 2 = pickup_pop + impact_thunk. (The ScrapSorted responses — sorted_chime,
        // sorted_burst — were removed with the sorting mechanic; ActorEvent.ScrapSorted
        // stays reserved as a .tres ordinal but no profile maps it.)
        if (prop.Responses.Length != 2)
            code |= Fail($"prop profile: expected 2 responses, got {prop.Responses.Length}");
        EventResponse[] picked = prop.ResponsesFor(ActorEvent.PickedUp);
        EventResponse[] impact = prop.ResponsesFor(ActorEvent.Impact);
        if (picked.Length != 1 || picked[0].Sound != Sandbox.Sfx.Pop || picked[0].VolumeDb != -6f)
            code |= Fail("prop pickup_pop values drifted from the plan table");
        if (impact.Length != 1 || impact[0].Sound != Sandbox.Sfx.Thunk || impact[0].VolumeDb != -8f)
            code |= Fail("prop impact_thunk values drifted from the plan table");
        if (prop.ResponsesFor(ActorEvent.ScrapSorted).Length != 0)
            code |= Fail("prop ScrapSorted must be unmapped (sorting mechanic removed)");

        return code;
    }

    internal static int Fail(string msg)
    {
        GD.PrintErr($"[presentation-selftest] FAIL: {msg}");
        return 1;
    }
}
