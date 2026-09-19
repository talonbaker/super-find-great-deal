using Godot;
using MpFoundation.Net;
using MpFoundation.Game.Aim;
using MpFoundation.Game.Props;
using MpFoundation.Game.Presentation;
using MpFoundation.Ui;

namespace MpFoundation.Game.Sandbox;

/// <summary>
/// The game avatar: light, bouncy third-person locomotion with squash-and-stretch and
/// single-slot carrying. Input arrives through an IIntentSource so the same body serves
/// the offline sandbox (local keyboard / wander brain) and the networked game.
///
/// Networked movement is SERVER-AUTHORITATIVE: the owning client samples input, stamps
/// it with a sequence number, predicts the result locally through the shared
/// <see cref="AvatarMotor.Step"/> (zero-latency feel), and streams the inputs to the
/// server; the server — the only authority — simulates every avatar from those inputs
/// and broadcasts state snapshots. The owner reconciles its prediction against each
/// acked snapshot (rewind + replay of unacknowledged inputs, correction smoothed on the
/// visual only); every other peer renders the avatar ~100 ms in the past by
/// interpolating buffered snapshots. Movement cheats are structurally impossible:
/// clients can only ever say "here is my stick input", never "here is my position".
///
/// Design rule (Talon): the player NEVER loses control. There is no reaction state —
/// running into things just stops you like normal movement (plus a cosmetic squish),
/// landings are visual-only, and nothing ever locks input or interrupts an action.
/// Reconciliation obeys the same rule: corrections never touch input or the simulation
/// the player is steering, only the rendered offset, which decays over a few frames.
///
/// <b>NAMED exceptions — two of them, and no others.</b> The first was added 2026-08-08 (W2,
/// the lake water contract); the second 2026-08-09 (phase 1c, the failure states). This comment
/// is edited rather than left standing, on `STATE-CASCADE-TABLE.md`'s own instruction: a stale
/// invariant left in the source is read as law by the next agent, who then quietly removes the
/// state — which is documented there as having already happened once to the previous death path.
///
/// <b>1. The sputter-out</b> (lake spec §6, approved by Talon) takes control for the go-under
/// and the shore recovery, and returns it at exactly one moment.
///
/// <b>2. Incapacitation and the impulse ragdoll</b> (beta plan §10, ratified by Talon, Issue
/// #179). Knocked Out and Frozen take control until a teammate's rescue or dawn; the Breaker's
/// impulse ragdoll takes it for about a second. These are the states the whole game's failure
/// layer is built on, so this is a real narrowing of the rule rather than an edge case.
///
/// The rule is narrowed, not abandoned, and <b>every exception meets all four conditions</b>:
/// <list type="bullet">
/// <item>Control is taken <b>only</b> through <c>AvatarMotor.ControlLockedBy</c> — the single
/// union of <c>MoveState.ControlLocked</c> (only <c>WaterService</c> writes it),
/// <c>MoveState.Incapacity</c> and <c>MoveState.ImpulseRagdoll</c> (only
/// <c>IncapacitationService</c> writes those, and only through the atomic cascade).</item>
/// <item>It is taken <b>wholly</b> — no partial steering, no drift, no ragdoll you can still
/// nudge (MECHANICS-BIBLE §2, and the bug this repo shipped once). There is deliberately no
/// physics ragdoll at all: the failure states are poses, and the body stays a
/// <c>CharacterBody3D</c> with exactly one transform writer.</item>
/// <item>It is <b>replicated</b>, not inferred, so the owning client stops predicting
/// steering on the same tick the server stops accepting it.</item>
/// <item>It <b>always</b> ends in bounded time. For the lake, proved by
/// <c>WaterChillTests.AntiUnwinnable_NoPositionInTheWaterCanLeaveAPlayerStuck</c>. For the
/// failure states, by <b>the dawn floor</b> — every state, from every cause, unconditionally,
/// with no guard that can refuse it (MECHANICS-BIBLE §10.5, beta plan §4.1, pinned by
/// <c>IncapacitationMachineTests.Dawn_RecoversEveryStateFromEveryCause</c>).</item>
/// </list>
/// Everything else in the paragraph above still holds: bumps, landings and reconciliation
/// remain cosmetic-only and never touch input.
/// </summary>
public partial class SandboxAvatar : CharacterBody3D, IServerConfirmedBody
{
    // Movement feel constants live in AvatarMotor (the deterministic step that client
    // prediction, server authority, and reconciliation replay all share). Everything
    // below is cosmetic-only tuning.

    // Bumps are cosmetic only: a squish + bonk, never a control change.
    private const float BumpSoundThreshold = 2.2f;
    private const float BumpSoundCooldownSec = 0.35f;

    // Soft footstep pad: one quiet step per stride of grounded travel.
    private const float FootstepMinSpeed = 0.6f;

    private const float ThrowForwardSpeed = 7.5f;
    private const float ThrowUpSpeed = 3.2f;
    public const float PickupRadius = 1.5f;

    /// <summary>How near a taken item has to be for E to mean "I was reaching for THAT" rather
    /// than "put down what I'm holding" (two-slot carry, 2026-08-07 — see
    /// <see cref="FindNearestUnavailableCarryable"/> for the incident and the rule).
    ///
    /// <b>The same reach the SERVER already grants, not a new number.</b>
    /// <c>PropManager.GrabRange</c> is <see cref="PickupRadius"/> plus
    /// <c>PropManager.GrabRangeTolerance</c> (0.75 m), and that tolerance exists for exactly this
    /// situation: the client tests reach against its PREDICTED positions and the server against
    /// the AUTHORITATIVE ones, and those differ by up to a round trip of movement. An item that
    /// was in reach when the player pressed E and has since swung into a teammate's hands sits
    /// inside precisely that band — measured at 1.52 m on the run that exposed the bug.
    ///
    /// It was briefly 2x PickupRadius (3.0 m). That was too wide and the review caught it: in a
    /// co-op game players cluster, so any teammate holding anything within 3 m would have
    /// suppressed the drop entirely, and "I can never put this down near my friends" is its own
    /// defect. Reusing the server's own tolerance keeps the guard covering the case it was built
    /// for without inventing a second, larger notion of reach.</summary>
    // Fully qualified: this class has its own `Props` PROPERTY, which shadows the
    // MpFoundation.Game.Props namespace in any unqualified reference from inside it.
    public const float GrabIntentRadius =
        PickupRadius + MpFoundation.Game.Props.PropManager.GrabRangeTolerance;

    // --- Body dimensions ------------------------------------------------------------------
    // Eight numbers here used to be typed in against the original squat body: a 0.9 m capsule, a 0.78 m
    // aim anchor, a +1.15 m nameplate, a 0.46 m shadow, two carry anchors, and (in the camera
    // and the voice layer) a 0.7 m focus and a 1.6 m emitter. Every one of them is now derived
    // from the character that actually got built — see AvatarProportions, which reproduces the
    // shipped values from that body's own measurements and gives a 1.16–1.27 m
    // figure its own. Nothing below hardcodes a dimension.

    /// <summary>This character's measured dimensions. Recomputed on every appearance rebuild
    /// (see <see cref="ApplyProportions"/>), so an avatar-key synchronizer catching up to the
    /// authority's real pick a beat after spawn resizes the body along with the mesh rather than
    /// leaving the previous character's collider behind.</summary>
    public AvatarProportions Proportions { get; private set; } = AvatarProportions.Fallback;

    /// <summary>Rest offset of the carry anchor in the avatar's local space — where a held item
    /// rides, in front of the chest. The held-item waddle bob
    /// (<see cref="AvatarVisual.CarryBobOffset"/>) is added to this each frame so a carried prop
    /// rides the gait. Cosmetic only — never part of the simulated/replicated transform.</summary>
    private Vector3 _carryAnchorRest = AvatarProportions.Fallback.CarryAnchorRestLocal;

    // --- Netcode tuning -------------------------------------------------------------
    /// <summary>Server broadcasts authoritative state every N sim ticks (30 Hz at 60 Hz sim).</summary>
    private const int SnapshotIntervalTicks = MpFoundation.Net.NetProfile.SnapshotIntervalTicks;

    /// <summary>Prediction-vs-authority position slack below which no replay happens.
    /// The Godot movement path is not bit-perfectly deterministic across machines
    /// (float/MoveAndSlide), so tiny divergence is expected and self-heals here.</summary>
    private const float PredictionEpsilonM = 0.03f;
    private const float VelocityEpsilon = 0.5f;
    private const float YawEpsilonRad = 0.05f;

    /// <summary>Aim-substrate (WP-L3) reconciliation tolerance on SteadyElapsedSec — a float
    /// accumulator, so exact equality would false-positive-mismatch on ordinary float noise
    /// the way position/velocity already need their own epsilons for.</summary>
    private const float AimSteadyEpsilonSec = 0.02f;

    /// <summary>Reconciliation corrections are hidden by offsetting the visual and
    /// decaying that offset exponentially (~halved every 58 ms). Beyond the cap the
    /// situation is a genuine teleport and the visual snaps with the body.</summary>
    private const float CorrectionSmoothRate = 12f;
    private const float MaxVisualErrorM = 2f;

    /// <summary>--cheat-move test hook: how hard the doctored packets lie.</summary>
    private const float CheatDirScale = 25f;
    private const float CheatSpeedFactorClaim = 5f;

    private const ulong ResetCooldownMsec = 1000;

    private enum NetRole { Offline, ServerSim, PredictedOwner, RemoteProxy }

    [Export] public Color BodyColor { get; set; } = new(0.62f, 0.83f, 0.72f); // soft mint

    /// <summary>This creature's event→cosmetics mapping (spec: presentation profiles).
    /// Inspector-assignable per scene; defaults to the shipped avatar profile in _Ready so
    /// code-constructed avatars (offline sandbox, tests, bots) stay behavior-identical.
    /// When creature #2 gets its own controller/scene, its scene assigns its own.</summary>
    [Export] public PresentationProfile? Profile { get; set; }

    private const string DefaultProfilePath = "res://assets/creatures/boxkid/boxkid_presentation.tres";

    /// <summary>The pastel dealt to the Nth joining player (server calls this; the color
    /// travels in the spawn data so every peer reconstructs it identically).</summary>
    public static Color PaletteColorFor(int spawnIndex) =>
        AvatarVisual.PastelPalette[((spawnIndex % AvatarVisual.PastelPalette.Length)
            + AvatarVisual.PastelPalette.Length) % AvatarVisual.PastelPalette.Length];

    /// <summary>
    /// <b>How many palette slots the random deal draws from</b> — the DEALT SIX
    /// (<c>AvatarVisual.PastelPalette</c>'s first six), and this is the value fork BT-7 scope
    /// item 4 leaves open, decided here so nobody re-derives it.
    ///
    /// <para>The alternatives were the full sixteen-entry array and a fresh six-entry saturated
    /// set. <b>The full array loses the separation guarantee</b>: only the first six are chosen
    /// for the widest value ladder and the best worst-case separation under deuteranope and
    /// protanope simulation (see <c>AvatarVisual.PastelPalette</c>'s doc, point 3), and drawing
    /// randomly from all sixteen can hand two players bone (luma 0.823) and chalk (0.770) —
    /// near-identical on a body whose only colour IS this. <b>A fresh saturated set invents six
    /// colours</b> to solve a problem nobody has measured: the dealt six span 0.823 to 0.160 and
    /// three of them (ochre, terracotta, fern) are already saturated, so "the pastels read too
    /// close on grey" is not true of this subset. Random over six with no dedup does mean two
    /// players can share a colour — Talon: <i>"no picking, no dedup logic needed"</i> — which is
    /// the accepted cost, and the reason it is acceptable is that a collision is obvious and
    /// harmless where a near-collision is neither.</para>
    /// </summary>
    public const int RandomPaletteSlots = 6;

    /// <summary>
    /// <b>The randomly dealt palette slot for the Nth joining player (BT-7).</b> Server-side, at
    /// spawn; the colour itself travels in the existing spawn args, so no client and no protocol
    /// bit changes.
    ///
    /// <para><b>A pure function of (seed, spawnIndex), not a stateful generator</b>, and that is
    /// what makes the "same seed, same colours" acceptance criterion checkable at all: a running
    /// <c>Random</c> would make the answer depend on how many times anything else happened to
    /// draw from it, so a reconnect, a bot, or a second world in the same process would silently
    /// change the sequence. Here the third player's colour is the third player's colour.</para>
    ///
    /// <para>The mixer is SplitMix64's finalizer — the standard avalanche step, chosen because it
    /// is four lines with no state and decorrelates adjacent <paramref name="spawnIndex"/> values
    /// completely, which a cheaper hash does not: with <c>seed ^ index</c> the first six players
    /// of one session walk a recognisable pattern.</para>
    ///
    /// <para><b>No dedup, deliberately.</b> Two players CAN be dealt the same colour. See
    /// <see cref="RandomPaletteSlots"/> for why that is the accepted cost and not an oversight,
    /// and note that nothing anywhere rejects a repeat — there is no uniqueness check to find.</para>
    /// </summary>
    public static int RandomPaletteIndexFor(ulong seed, int spawnIndex)
    {
        ulong z = seed + 0x9E3779B97F4A7C15UL * (ulong)(uint)spawnIndex;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        z ^= z >> 31;
        return (int)(z % RandomPaletteSlots);
    }

    /// <summary>The randomly dealt colour itself — <see cref="RandomPaletteIndexFor"/> through
    /// <see cref="PaletteColorFor"/>, so the random deal and the round-robin deal read the same
    /// array and <see cref="PaletteIndexFor"/> still reverses either of them.</summary>
    public static Color RandomPaletteColorFor(ulong seed, int spawnIndex) =>
        PaletteColorFor(RandomPaletteIndexFor(seed, spawnIndex));

    /// <summary>Reverse of <see cref="PaletteColorFor"/>: the palette slot exactly matching a
    /// given color, or -1 if none matches. Observability only (BotHarness's JSONL colorIdx
    /// field — see Task A3/P11); never used for gameplay logic. Exact-equality search is safe
    /// here because BodyColor is set exactly once, straight from a PaletteColorFor(...) call
    /// (see Gameplay.SpawnPlayer), and nothing mutates it afterward.</summary>
    public static int PaletteIndexFor(Color color)
    {
        for (int i = 0; i < AvatarVisual.PastelPalette.Length; i++)
        {
            if (AvatarVisual.PastelPalette[i] == color)
                return i;
        }
        return -1;
    }

    private string _displayName = "";

    /// <summary>Longest replicated display name. Enforced on EVERY write — local or
    /// network-received — exactly like <see cref="AvatarKey"/> below, and for the same reason:
    /// this property carries CLIENT authority, so its value is an untrusted peer's choice.</summary>
    public const int MaxDisplayNameLength = 24;

    /// <summary>Name shown on the floating billboard; replicated on the networked instance.</summary>
    [Export]
    public string DisplayName
    {
        get => _displayName;
        set
        {
            _displayName = SanitizeDisplayName(value);
            if (_nameLabel != null)
                _nameLabel.Text = _displayName;
        }
    }

    /// <summary>Caps length and strips control characters. The cap bounds what a modified client
    /// can spawn-replicate to every peer and draw across their screen. The control-character strip
    /// is the security half: the server writes names unescaped into its status log (Gameplay's
    /// "names=[…]" line), so an embedded newline let any client forge whole log lines — poisoning
    /// the very record a host would rely on after an incident. A LineEdit cannot produce one; a
    /// modified client writing the synchronized property directly can.</summary>
    private static string SanitizeDisplayName(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        Span<char> buf = stackalloc char[MaxDisplayNameLength];
        int n = 0;
        foreach (char c in value)
        {
            if (char.IsControl(c))
                continue;
            buf[n++] = c;
            if (n == MaxDisplayNameLength)
                break;
        }
        return new string(buf[..n]);
    }

    // Defaults to SAIL_AVATAR (unset -> the roster default), exactly the old process-wide default,
    // so any avatar nobody ever explicitly assigns a key to (offline sandbox, tests,
    // tools) behaves precisely as before this feature existed.
    private string _avatarKey = AvatarVisual.ResolveEnvAvatarKey();

    /// <summary>The roster creature this avatar renders as
    /// (docs/creatures/avatar-roster-description.md). Client-authority and
    /// spawn-replicated on the "Sync" child exactly like <see cref="DisplayName"/> (see
    /// NetworkedAvatar.tscn's SceneReplicationConfig): the owning peer sets its own pick
    /// (<see cref="ConfigureNetworkedInstance"/>), every other peer — including a late
    /// joiner, via the synchronizer's spawn=true property — renders whatever value lands
    /// here. Every write, local or network-received, is clamped through
    /// <see cref="AvatarVisual.NormalizeAvatarKey"/>, so an out-of-range or unknown value
    /// (a malicious client, a stale build missing a roster entry) can never make this or
    /// any other peer resolve an invalid model — it silently falls back to the default
    /// roster body instead.</summary>
    [Export]
    public string AvatarKey
    {
        get => _avatarKey;
        set
        {
            string normalized = AvatarVisual.NormalizeAvatarKey(value);
            if (normalized == _avatarKey)
                return;
            _avatarKey = normalized;
            // _visual doesn't exist yet on the very first assignment — spawn-time property
            // application runs before _Ready, which does its own initial BuildAppearance
            // with whatever AvatarKey holds by then. Once built, a later correction (the
            // synchronizer catching the authority's real pick up a beat after spawn — the
            // same eventual-consistency window DisplayName already has) rebuilds the model
            // in place instead of leaving the wrong creature on screen.
            if (_visual == null)
                return;
            _visual.BuildAppearance(BodyColor, _avatarKey);
            // The two families differ by ~30 cm of height and ~2x in width, so a rebuild that
            // swapped the mesh and left the capsule, anchors and shadow at the previous
            // character's size would be exactly the defect this class was just fixed for.
            ApplyProportions();
        }
    }

    /// <summary>On-floor flag for the animation layer. Owner/offline write it from their
    /// own simulation; remote proxies from interpolated snapshots (a non-simulated
    /// CharacterBody3D can't compute IsOnFloor itself).</summary>
    [Export] public bool Grounded { get; set; }

    /// <summary>Where the local player's input comes from; dummies get a wander brain.
    ///
    /// <para><b>A non-human source is wrapped on the way in</b> (FP-1, 2026-09-19). With
    /// <c>MotorTuning.BodyYawFollowsAim</c> on, a body faces the look yaw its intent carries — and
    /// a scripted brain carries none, so every bot and every dummy in the repo would face world
    /// zero while walking sideways past the camera. <see cref="TravelFacingIntentSource"/> fills
    /// the look from the brain's own movement direction, which is the facing those fixtures had
    /// before this knob existed. Done HERE, at the one place any source is adopted, rather than in
    /// each of the five construction sites — the next scripted brain someone writes inherits it
    /// without having to know the rule exists. Human sources are passed through untouched: they
    /// already report a real look.</para></summary>
    public IIntentSource? IntentSource
    {
        get => _intentSource;
        set => _intentSource = value is { IsHumanInput: false } scripted
            ? new TravelFacingIntentSource(scripted)
            : value;
    }

    private IIntentSource? _intentSource;

    public CarryController Carry { get; private set; } = null!;
    public VoiceRangePublisher VoicePublisher { get; } = new();

    // --- Aim substrate (WP-L3, Issue #106) --------------------------------------------------
    // Exactly one AimController per avatar instance, driven the same three-way (owner-predict /
    // server-authority / reconcile-replay) shape as movement itself — see OwnerTick/ServerTick/
    // Reconcile below. Verb-agnostic: no equipment/held-item gate lives here (see MoveIntent.
    // AimRaise's doc comment) — L4/L6 own that layer.
    private readonly AimController _aim = new();

    /// <summary>Raise/lower stance, replicated to every peer (SnapshotBuffer.RenderSample.
    /// AimStance) so a proxy avatar's pose reflects it too, not just the owner's own camera FOV.</summary>
    public AimStance AimStance { get; private set; }

    /// <summary>0..1, replicated via the same AimSteadyElapsedSec accumulator a verb's
    /// server-side validity check reads directly off the authoritative AimController instance —
    /// see AimController.Steadiness01's doc comment for the determinism argument.</summary>
    public float AimSteadiness01 { get; private set; }

    /// <summary>This tick's look yaw (world radians, SandboxCamera.Yaw convention). Populated
    /// from the owner's own live camera every OwnerTick and from the server's latest-received
    /// input every ServerTick; meaningless (stays 0) on a remote proxy or an avatar with no
    /// human input source — nothing needs it there in the lean build (see MoveIntent.AimYaw).</summary>
    public float AimYaw { get; private set; }

    /// <summary>This tick's look pitch (world radians, clamped to SandboxCamera.PitchMin/Max).
    /// Same population/scope rule as <see cref="AimYaw"/>.</summary>
    public float AimPitch { get; private set; }

    private Node3D _aimAnchor = null!;

    /// <summary>World-space aim-ray origin (roughly eye height) — what an aimed verb anchors its
    /// ray at. Falls back to the avatar's
    /// own transform if the anchor node was somehow freed, same defensive pattern as
    /// <see cref="CarryAnchorGlobalTransform"/>.</summary>
    public Vector3 AimOriginGlobalPosition =>
        IsInstanceValid(_aimAnchor) ? _aimAnchor.GlobalPosition : GlobalPosition;

    /// <summary>Where this character's voice leaves their body — their own face, which is the
    /// same measured eyeline the aim ray uses. Deliberately its own named property rather than
    /// callers reaching for <see cref="AimOriginGlobalPosition"/>: a voice and an aim ray happen
    /// to start at the same place today, and a future first-person or over-the-shoulder aim
    /// origin must not silently move everybody's mouth with it.</summary>
    public Vector3 VoiceOriginGlobalPosition => AimOriginGlobalPosition;

    /// <summary>World-space carry anchor transform, for a networked prop bound to this avatar as
    /// its holder (local = predicted, remote = interpolated — the prop just reads wherever this
    /// avatar's anchor currently renders). Falls back to the avatar's own transform if the anchor
    /// node was somehow freed, so a stale binding never dereferences a dead node.</summary>
    public Transform3D CarryAnchorGlobalTransform =>
        IsInstanceValid(_carryAnchor) ? _carryAnchor.GlobalTransform : GlobalTransform;

    /// <summary>Networked-carry funnel: set by Gameplay when it spawns this avatar's networked
    /// instance, so the local Interact/drop intent can route grab/drop requests through the
    /// server instead of mutating <see cref="Carry"/> directly (see <see cref="HandleCarryIntent"/>).
    /// Null in the offline sandbox, where Carry is still mutated locally.</summary>
    public PropManager? Props
    {
        get => _props;
        set
        {
            if (_props != null)
                _props.GrabDenied -= OnGrabDenied;
            _props = value;
            if (_props != null)
                _props.GrabDenied += OnGrabDenied;
        }
    }

    private PropManager? _props;

    /// <summary>A refused grab has to be perceivable (INTERACTION-BIBLE 2) — pressing
    /// interact and getting silence reads as a broken game, not a refused action.
    ///
    /// This reuses the existing <see cref="ActorEvent.Bump"/> cue rather than minting a new
    /// ordinal: ActorEvent ordinals are a .tres serialization contract, so a dedicated
    /// "Denied" event means authoring every creature's profile, and a refused grab is
    /// tonally a small bonk anyway. If it doesn't read clearly in playtest, a dedicated
    /// event + its own sound is the natural upgrade — this is the cheap correct default,
    /// not a claim that it's the final feel.</summary>
    private void OnGrabDenied(PropManager.GrabDenial reason)
    {
        if (_role != NetRole.PredictedOwner && _role != NetRole.Offline)
            return; // only the player who pressed the key gets told
        _visual?.TriggerBumpSquish();
        ActorFx.Fire(GetParent(), Profile, ActorEvent.Bump, GlobalPosition);
    }

    /// <summary>Networked world-interact hook (the escape world's tiny door): set by
    /// Gameplay on the locally-owned avatar. Consulted FIRST on an Interact edge —
    /// returning true means the world consumed it (e.g. a door-open request went to the
    /// server) and no grab/drop should happen. Null offline (hosts relay their own
    /// interact seam) and on remote/serverside instances (which never read input).</summary>
    public System.Func<bool>? WorldInteract { get; set; }

    /// <summary>The camera whose aim disambiguates this avatar's interact target (see
    /// <see cref="InteractTargeting"/>). Set ONLY for a locally-controlled human player;
    /// null for bots, scripted test brains, remote proxies, and server sims — they keep
    /// the plain nearest-in-reach rule.</summary>
    public Camera3D? AimCamera { get; set; }

    /// <summary>World-interactive hook, checked BEFORE the carry logic on an Interact
    /// edge: hosts install a delegate that tries doors / coin slots / the keyhole and
    /// returns true when it consumed the press. This is what lets "insert the held coin
    /// into the slot" win over "drop the held coin" on the same key — one E, one
    /// affordance grammar, interactive-first priority. Null (the default) keeps the
    /// original behaviour exactly.</summary>
    public System.Func<bool>? InteractOverride { get; set; }

    /// <summary>Visual layer accessor — used by the floor-clamp test invariants.</summary>
    public AvatarVisual Visual => _visual;

    /// <summary>Owner-side: magnitude of the last authoritative-vs-predicted delta at the
    /// acked input (0 when prediction matched). Logged by BotHarness; the reconciliation
    /// suite asserts it stays bounded and settles to ~0.</summary>
    public float LastCorrectionM { get; private set; }

    /// <summary>Owner-side: the position carried by the newest authoritative snapshot applied,
    /// before this tick's prediction ran. Null until the first snapshot lands. See
    /// <see cref="ServerConfirmedPosition"/>, which is the only thing that reads it.</summary>
    private Vector3? _serverConfirmedPos;

    /// <inheritdoc/>
    ///
    /// <remarks>
    /// The role split is the whole content of this property, and each arm is a different claim:
    ///
    /// <list type="bullet">
    /// <item><b>Offline / ServerSim</b> — this process IS the authority for this body, so its
    /// live position is already confirmed by definition and there is nothing to wait for. This
    /// arm is what keeps every offline suite and every server-side fixture behaving exactly as it
    /// did before this property existed.</item>
    /// <item><b>PredictedOwner</b> — the interesting one. <see cref="Position"/> here is a guess
    /// that <see cref="Reconcile"/> may revoke; the last authoritative snapshot is the newest
    /// thing the server has actually agreed to. Null before the first snapshot arrives, because
    /// at that point this client genuinely does not know.</item>
    /// <item><b>RemoteProxy</b> — null, and deliberately not the interpolated render position:
    /// that is presentation smoothing between two snapshots, so it is neither a prediction nor a
    /// confirmed fact, and handing it out under this name would be a lie a caller cannot detect.
    /// </item>
    /// </list>
    ///
    /// Read in the same space as <see cref="Node3D.Position"/> (which is what
    /// <c>ApplyStateToNode</c> writes the snapshot's own state into), so a caller may compare the
    /// two directly.
    /// </remarks>
    public Vector3? ServerConfirmedPosition => _role switch
    {
        NetRole.Offline or NetRole.ServerSim => Position,
        NetRole.PredictedOwner => _serverConfirmedPos,
        _ => null,
    };

    /// <summary>Owner-side: current smoothed render-offset magnitude (drains to 0).</summary>
    public float VisualErrorM => _visualError.Length();

    /// <summary>Where the avatar APPEARS on screen this frame: the simulated body position
    /// plus the undrained render-space correction offset (zero except on a predicted owner
    /// in the frames after a reconciliation pop). Everything cosmetic that tracks the avatar
    /// per frame — camera focus, carry anchor, blob shadow — must read this, not
    /// <see cref="Node3D.GlobalPosition"/>, or it visibly detaches from the rendered mesh
    /// under real network conditions. Simulation reads (reach checks, server validation)
    /// stay on the raw body on purpose.</summary>
    public Vector3 RenderGlobalPosition => GlobalPosition + _visualError;

    /// <summary>Test hook (render-tracking invariants): plant a correction offset exactly as
    /// <see cref="Reconcile"/> folds a server pop into the render layer. Offline roles never
    /// drain it, so the offset holds still for the assertion window.</summary>
    public void TestInjectVisualError(Vector3 worldError)
    {
        _visualError = worldError;
        _visual.SetRenderOffset(GlobalTransform.Basis.Inverse() * worldError);
    }

    private AvatarVisual _visual = null!;
    private CanvasLayer _nameLayer = null!;
    private Label _nameLabel = null!;

    /// <summary>Test hook: whether the nameplate is currently drawn. Exists so the
    /// world-UI suppression cascade (MECHANICS-BIBLE 2) is checkable headlessly rather
    /// than by eye.</summary>
    internal bool NameplateVisible => _nameLabel != null && _nameLabel.Visible;
    private Node3D _carryAnchor = null!;
    private Node3D _stowAnchor = null!;
    private CollisionShape3D _collider = null!;
    private BlobShadow? _blobShadow;

    // Presentation-only state (never replayed; derived from consumed sim ticks).
    private float _bumpSoundCooldown;
    private bool _wasGrounded;
    private float _maxFallSpeed;

    // Offline-only sim extras (networked roles carry these inside MoveState).
    private float _coyoteRemaining;
    private float _jumpBufferRemaining;
    /// <summary>SKID-1. Same reason as the two above, and the same failure without it: the offline
    /// path rebuilds its <c>MoveState</c> from the NODE every tick, so any simulation state that is
    /// not on the node has to be cached here or it resets to zero every frame — which would let the
    /// skid re-enter forever and never advance its own clock.</summary>
    private float _skidRemaining;

    /// <summary>MOVE-5. Same reason as the three above, and the same failure without them: the
    /// offline path rebuilds its <c>MoveState</c> from the NODE every tick, so any simulation state
    /// that is not on the node has to be cached here or it resets to zero every frame — which would
    /// mean the ground hold window never reaches twelve, the slide never advances its own clock,
    /// the chain never accrues, and a spent air jump is re-granted every single tick.</summary>
    private MoveVerb _verb;
    private byte _verbClockTicks;
    private byte _chainDepth;
    private byte _chainTimerTicks;
    private byte _airJumpsUsed;

    // --- Networking (offline sandbox leaves these at their defaults) ---
    private NetRole _role = NetRole.Offline;
    private int _ownerPeerId;

    /// <summary><b>Achievements were pruned at the fork</b> (BASE-1, 2026-09-19): the whole
    /// <c>scripts/game/achievements/</c> package and its UI toast went with the old game's
    /// objectives, so there is no per-body runtime here any more and nothing writes to
    /// <c>user://settings.cfg</c> from an avatar.
    ///
    /// <para><b>The gate this replaced is worth reading before anything persistent is added back.</b>
    /// A bot owns its own avatar, so an <c>isOwner</c>-only gate let a suite bot earn achievements
    /// into <c>user://settings.cfg</c> — a path that resolves by project NAME and is therefore one
    /// profile shared by every checkout on this machine. Run-AimTest's scripted 2s->6s aim hold
    /// cleared a 3.0 s threshold and spent Talon's real first-time unlock. Any future per-player
    /// persisted fact gates on <see cref="IsHumanDriven"/>, never on ownership alone.</para></summary>
    internal bool TracksAchievements => false;

    /// <summary><b>Is a PERSON driving this body?</b> The question the pruned achievement runtime
    /// was gated on, hoisted to a public read so a second consumer does not have to re-derive it —
    /// and so two consumers can never answer differently, which is exactly how a gate rots.
    ///
    /// <para><b>Added by CELEBRATE-1 (2026-09-04) for the all-bubbles celebration</b>, which has
    /// the same hazard the achievement gate was built for and a slightly different shape: nothing
    /// is persisted, so it cannot spend a real unlock, but a suite that pops every bubble would
    /// otherwise fire a triumph on every automated run — and the counter is SHARED, so a bot
    /// completing the level would celebrate at a real player standing next to it.
    /// <c>BubbleCelebration</c> reads this across <see cref="Live"/>; see its own class doc.</para>
    ///
    /// <para>False for a remote proxy (someone else's body, rendered here), for a headless
    /// server's simulation of every avatar, for every scripted suite bot, and for the offline
    /// labs — <see cref="IIntentSource.IsHumanInput"/> defaults to false, so a source that has not
    /// thought about the question is excluded rather than admitted.</para></summary>
    public bool IsHumanDriven => IntentSource?.IsHumanInput == true;

    /// <summary>The peer this avatar belongs to. Exposed (Issue #152) so a caller outside this
    /// class can ask the prop layer what this avatar is holding — <c>FindHeldBy</c> is keyed by
    /// peer id, and every in-class caller already passes <c>_ownerPeerId</c> for exactly that.
    /// Read-only: ownership is assigned once, in <c>ConfigureAsNetworked</c>.</summary>
    public int OwnerPeerId => _ownerPeerId;
    private Vector3 _spawnPosition;
    private MoveState _state;              // owner prediction / server authority
    private byte _epoch;                   // bumped by server-side teleports (reset-to-spawn)

    // --- Water contract seam (W2) ---------------------------------------------------------------
    // Three members, and deliberately no more. WaterService owns the cold clock, the sputter-out
    // and Soaked; the avatar owns the simulation state those two facts have to live inside. This
    // is the whole surface between them.

    /// <summary>This avatar's current water state (W2, lake-water contract §4). Derived inside
    /// <c>AvatarMotor.Step</c> from submersion depth and the previous state's hysteresis, so it is
    /// already correct on the server, in owner prediction and after a reconciliation replay —
    /// <c>WaterService</c> reads it and never writes it. On a remote proxy this reflects the last
    /// authoritative snapshot, which is exactly the right answer for a cosmetic reader.</summary>
    public Sail.Game.Water.WaterState WaterStateNow => _state.Water;

    /// <summary>Whether this avatar is Soaked (§7). Server-authoritative; on a client this is
    /// whatever the last snapshot said.</summary>
    public bool SoakedNow => _state.Soaked;

    /// <summary>Whether this body is mid-turnaround-skid (SKID-1). Read off the same replicated
    /// state the pose is driven from, so a lab or a HUD cannot disagree with what the motor
    /// simulated. Cosmetic readers only — nothing here decides anything.</summary>
    public bool SkiddingNow => AvatarMotor.IsSkidding(_state);

    /// <summary>Seconds of skid left, for a readout. Zero when not skidding.</summary>
    public float SkidRemainingSec => _state.SkidRemaining;

    /// <summary>Seconds of coyote time left, read off the same <c>MoveState</c> the motor actually
    /// simulated (MOVE-3, added for a movement-playground readout). Cosmetic readers only — nothing
    /// here decides anything, and a readout that recomputed its own copy of a forgiveness timer is
    /// a readout that can quietly disagree with the motor it is reporting on.</summary>
    public float CoyoteRemainingSec => _state.CoyoteRemaining;

    /// <summary>Seconds of jump buffer left, same contract as
    /// <see cref="CoyoteRemainingSec"/>.</summary>
    public float JumpBufferRemainingSec => _state.JumpBufferRemaining;

    // --- MOVE-5: the verb seam (spec §12's animation contract and the playground readout) --------
    // Five read-only accessors over the same replicated MoveState the motor simulated, exactly as
    // SkiddingNow / SkidRemainingSec are. Cosmetic readers only — nothing here decides anything,
    // and a pose or a readout that recomputed its own copy of a verb is one that can quietly
    // disagree with the motor it is reporting on.
    //
    // THEY SHIP WITH THIS PACKET EVEN THOUGH NOTHING READS THEM YET. MOVE-5c (the animation tells)
    // and MOVE-5d (the readout and the panel rows) fan out behind this one and both consume these;
    // a shared seam whose members arrive late is how parallel agents produce merge conflicts.

    /// <summary>Which crouch verb this body is in (MOVE-5 §3). The duck walk's latch IS this
    /// value — there is no second flag to disagree with it.</summary>
    public MoveVerb VerbNow => _state.Verb;

    /// <summary>The shared verb clock in whole ticks — the hold window in
    /// <see cref="MoveVerb.Normal"/>, the slide's duration in <see cref="MoveVerb.Slide"/>. Equal
    /// to <see cref="AvatarMotor.VerbClockAirborne"/> when the previous tick was airborne, which is
    /// a distinguished not-counting value rather than a count; a readout should show it as "air",
    /// not as 255.</summary>
    public byte VerbClockTicks => _state.VerbClockTicks;

    /// <summary>
    /// Consecutive jumps beyond the first (MOVE-5 §6.2). <b>§12's procedural animation tell is the
    /// ONLY thing that may read this for the player's benefit.</b>
    ///
    /// <para><b>Spec §6.6 is a prohibition, not an omission, and it names the lab explicitly:</b>
    /// "the chain has no numeric readout, no bar, no icon, no text, no HUD element of any kind, in
    /// any build, <i>including the lab</i>. §12 is the entire communication channel. This is stated
    /// as a prohibition rather than an omission so that a debug readout does not arrive later as a
    /// convenience and stay." <b>So this accessor exists for the pose layer and for tests, and the
    /// playground readout must not print it</b> — a constraint on MOVE-5d, recorded here because
    /// this is the member it would reach for.</para>
    /// </summary>
    public byte ChainDepthNow => _state.ChainDepth;

    /// <summary>Ticks until the chain loses its next level (MOVE-5 §6.4). Non-zero and falling
    /// during ground dwell; frozen while airborne. <b>Same §6.6 prohibition as
    /// <see cref="ChainDepthNow"/></b> — it is chain state, and the chain has no readout.</summary>
    public byte ChainTimerTicks => _state.ChainTimerTicks;

    /// <summary>Air jumps spent this flight (MOVE-5 §7). Always zero at the shipped
    /// <c>AirJumpMode</c> of 0.</summary>
    public byte AirJumpsUsed => _state.AirJumpsUsed;

    // --- LD-2: the cadence seam (2026-09-02) ----------------------------------------------------
    // Two read-only accessors for Sail.Game.World.CadenceTracker, in the same spirit as the verb
    // seam above: cosmetic readers only, nothing here decides anything, and a clock that recomputed
    // its own copy of the motion state is a clock that can disagree with the motor it reports on.

    /// <summary><b>Is this the body THIS process drives?</b> The predicted owner on a networked
    /// peer, or the offline body in a lab — the one whose <see cref="MotionNow"/> is a local fact
    /// rather than a replicated one. A remote proxy and the server's simulation of another peer
    /// both answer false. Per-avatar, like <c>IIntentSource.IsHumanInput</c>, and deliberately
    /// not the same question: a scripted capture bot drives its own body and answers true.</summary>
    public bool IsLocallyDriven => _role is NetRole.PredictedOwner or NetRole.Offline;

    /// <summary>
    /// The motor's most recent <see cref="MoveState"/> for this body, complete. On the predicted
    /// owner that is <c>_state</c> as the last step (or reconciliation) left it. On the offline
    /// role the node itself carries position, velocity and the floor flag — <c>OfflineTick</c>
    /// rebuilds its state from the node every tick and mirrors only the sim-extra fields into
    /// <c>_state</c> — so those three are read off the node here to hand back one coherent state.
    /// On any other role this is whatever the last snapshot adopted; readers should gate on
    /// <see cref="IsLocallyDriven"/> first.
    /// </summary>
    public MoveState MotionNow
    {
        get
        {
            if (_role != NetRole.Offline)
                return _state;
            MoveState s = _state;
            s.Position = Position;
            s.Velocity = Velocity;
            s.Grounded = Grounded;
            return s;
        }
    }

    /// <summary>
    /// Server-only: write the two water facts the avatar cannot derive for itself into the
    /// replicated <c>MoveState</c>, from where they reach every client on the next snapshot and
    /// are consumed by <c>AvatarMotor.Step</c> on the very next tick.
    ///
    /// Called unconditionally every server tick by <c>WaterService</c> rather than only on a
    /// change: the values are two bits inside a struct that is rewritten every tick anyway, and an
    /// "only on change" version would need its own edge bookkeeping that could drift out of step
    /// with the service's. A no-op when the values already match.
    /// </summary>
    public void ServerSetWaterFlags(bool soaked, bool controlLocked)
    {
        if (_role != NetRole.ServerSim)
            return;
        _state.Soaked = soaked;
        _state.ControlLocked = controlLocked;
    }

    // --- Failure-state seam (phase 1c, beta plan §10) --------------------------------------------
    // Same posture as the water seam above it, and deliberately about the same size:
    // IncapacitationService owns the machine, the avatar owns the simulation state that machine's
    // verdict has to live inside. This is the whole surface between them.

    /// <summary>What this body currently is. Server-authoritative; on a client this is whatever
    /// the last snapshot said, which is the right answer for every cosmetic and gating reader.</summary>
    public Sail.Game.Failure.IncapacityState IncapacityNow => _state.Incapacity;

    /// <summary>Down — Knocked Out or Frozen. The predicate every gate in this class reads, and
    /// the one the run-end condition counts (beta plan §4.1). An impulse ragdoll is deliberately
    /// NOT included; see <see cref="ImpulseRagdolledNow"/>.</summary>
    public bool IncapacitatedNow => _state.Incapacity != Sail.Game.Failure.IncapacityState.Active;

    /// <summary>Momentarily bowled over. Denies steering and nothing else.</summary>
    public bool ImpulseRagdolledNow => _state.ImpulseRagdoll;

    /// <summary>Steering is not reaching the motor, for any of the three reasons that can stop it
    /// (a sputter-out, incapacitation, an impulse ragdoll). The union, exposed once, so no caller
    /// re-derives it and gets two of the three.</summary>
    public bool ControlDeniedNow => _state.ControlLocked || IncapacitatedNow || ImpulseRagdolledNow;

    /// <summary>
    /// Server-only: <b>the commit row</b> of the incapacitation cascade
    /// (<c>IncapacityCascade</c> / <c>CascadeRow.ReplicatedState</c>). Writing these two values is
    /// simultaneously what disables input, what keeps <c>CharacterBody3D</c> the single transform
    /// writer, what suspends the owner's prediction, and what replicates — four cascade rows on
    /// one carrier, because <c>MoveState</c> is the only place any of them could have lived (see
    /// <c>STATE-CASCADE-TABLE.md</c> hard constraint 1).
    ///
    /// <para>Called only from <c>IncapacitationService</c>, and only through the cascade, so there
    /// is exactly one path by which a player's liveness can change.</para>
    /// </summary>
    public void ServerCommitIncapacity(Sail.Game.Failure.IncapacityState state, bool impulseRagdoll)
    {
        if (_role != NetRole.ServerSim)
            return;
        _state.Incapacity = state;
        _state.ImpulseRagdoll = impulseRagdoll;
    }

    /// <summary>
    /// Server-only: the drag verb's motion, applied as an authoritative velocity on the frozen
    /// body's own <c>MoveState</c> so that <c>AvatarMotor.Step</c> — <b>still the single transform
    /// writer</b> — moves it. The dragging teammate never writes another player's transform;
    /// nothing does. That is what keeps cascade row 2 answerable at all while one player is
    /// hauling another around.
    ///
    /// <para><paramref name="velocity"/> is a target ground speed. It is pre-compensated for the
    /// deceleration <c>Step</c> applies in the same tick (a locked body has no movement intent, so
    /// its horizontal velocity decays toward zero at <c>AvatarMotor.Deceleration</c>), so the
    /// block actually slides at the requested speed rather than at a slightly lower one nobody
    /// could have predicted from the constant.</para>
    /// </summary>
    public void ServerSetDragVelocity(Vector3 velocity)
    {
        if (_role != NetRole.ServerSim)
            return;
        Vector3 compensated = velocity;
        if (velocity.LengthSquared() > 1e-6f)
            compensated += velocity.Normalized() * (AvatarMotor.Deceleration * AvatarMotor.TickDelta);
        _state.Velocity = new Vector3(compensated.X, _state.Velocity.Y, compensated.Z);
    }

    /// <summary>
    /// Server-only: a genuine rising edge of the Interact button, latched from the authoritative
    /// input stream and consumed exactly once by whoever asks first.
    ///
    /// <para><b>Why this exists at all.</b> The assist verbs (shake, drag) act on ANOTHER PLAYER,
    /// and <c>STATE-CASCADE-TABLE</c> row 22 says a player is not addressable as an interaction
    /// target today — <c>InteractTargeting.Pick</c> only ever considers the <c>Carryable</c>
    /// group. So the resolution has to be server-side and proximity-based, and the server needs
    /// the Interact edge, which until now it consumed nowhere (grabs ride their own RPC). The bit
    /// was already on the wire; this reads it. No new input bit — the buttons byte is full.</para>
    ///
    /// <para>Edge, not level, and re-derived here rather than trusted: <see cref="HandleCarryIntent"/>'s
    /// own comment records the incident where a bot held Interact for two seconds and a level
    /// signal became a hundred requests. Same discipline, applied on the authority's side.</para>
    /// </summary>
    public bool TakeServerInteractEdge()
    {
        bool edge = _serverInteractEdge;
        _serverInteractEdge = false;
        return edge;
    }

    private bool _serverInteractEdge;
    private bool _serverInteractHeldPrev;

    /// <summary>Clearance above a collapsed body's own half-width for its nameplate. Derived from
    /// the character rather than typed as an absolute, for the reason the whole dimensions cascade
    /// exists: a body lying down is about as tall as it is wide, and an absolute would be wrong
    /// again the next time the roster changes.</summary>
    private const float KnockedOutNameplateGapM = 0.25f;

    /// <summary>The follow camera this avatar owns, kept as a typed field so the failure-state
    /// cascade has something to call <c>SetInputDetached</c> / <c>Detach</c> on. Null for bots
    /// without <c>--spectate-cam</c>, for remote proxies, and on a headless server — every caller
    /// null-checks, and "there is no camera here" is a perfectly ordinary answer.
    ///
    /// <para><b>Written only by <see cref="AdoptFollowCamera"/> and
    /// <see cref="ReleaseFollowCamera"/> since MOVE-4f</b>, both of which only
    /// <c>SandboxCamera.Attach</c> / <c>Reattach</c> / <c>Detach</c> call. Until then it was
    /// assigned on two lines inside <see cref="ConfigureNetworkedInstance"/>'s owned-client branch
    /// and nowhere else, so every dev harness in the repo drove a real follow camera this field
    /// never learned about — which is how the landing dip shipped as a permanent no-op in the one
    /// scene built to tune it (MOVE-4e, AC3).</para></summary>
    private SandboxCamera? _followCamera;

    /// <summary>
    /// <b>A camera has attached to this avatar; it is now the follow camera.</b> Called by
    /// <c>SandboxCamera.Attach</c> and <c>Reattach</c> — not a setter, and deliberately not
    /// reachable as one.
    ///
    /// <para><b>It refuses a camera that is not actually following this body.</b> That check is
    /// the whole reason this is a method: the alternative shape MOVE-4e offered was a public
    /// <c>FollowCamera</c> property, which would let any caller anywhere point a live session's
    /// camera field at a camera framing somebody else. Here the only way to become an avatar's
    /// follow camera is to be attached to it, which is also the only way to be one in fact.</para>
    /// </summary>
    internal void AdoptFollowCamera(SandboxCamera camera)
    {
        if (!ReferenceEquals(camera.FollowTarget, this))
            return;
        _followCamera = camera;
    }

    /// <summary>The counterpart: a camera that detached, or retargeted onto another body, stops
    /// being this avatar's. Ignores a camera that was never this one's, so a detach cannot clear a
    /// field it does not own.</summary>
    internal void ReleaseFollowCamera(SandboxCamera camera)
    {
        if (ReferenceEquals(_followCamera, camera))
            _followCamera = null;
    }

    /// <summary><b>Is a camera registered against this body?</b> A read-only probe, so a test can
    /// prove the registration happened without the field becoming writable — which is the whole
    /// point of the shape above. MOVE-4e could only establish the absence of a dip; this is what
    /// lets the presence of the wiring be checked directly rather than through its effect.</summary>
    public bool HasFollowCamera => _followCamera is not null;

    /// <summary><b>This avatar is the one behind the local player's eyes</b> (FP-1, 2026-09-19).
    /// Set by <see cref="FirstPersonCamera.Attach"/>, and only there, so it cannot be true of a
    /// body no first-person camera is mounted on. Two things read it: the nameplate, which must
    /// not be drawn to its own wearer, and the self-test probe.</summary>
    public bool IsLocalFirstPersonBody { get; private set; }

    /// <summary>
    /// <b>Take this body out of its own eyes.</b> Called by <see cref="FirstPersonCamera.Attach"/>
    /// once it has dropped <see cref="AvatarVisual.FirstPersonHiddenLayer"/> from its cull mask;
    /// the two halves belong together, which is why this is a method on the avatar rather than a
    /// caller pushing a flag into the visual from outside.
    ///
    /// <para>The BLOB SHADOW is deliberately NOT hidden. It is not part of the visual rig — it is a
    /// top-level quad on the floor under the body, and it is the one ground-contact cue a
    /// first-person player has left once their legs are gone. Every first-person game draws the
    /// player's own shadow; none draws the inside of their own skull.</para>
    /// </summary>
    internal void HideOwnBodyFromFirstPerson()
    {
        IsLocalFirstPersonBody = true;
        _visual.HideFromFirstPerson();
    }

    /// <summary>
    /// <b>The camera cascade row</b> (STATE-CASCADE-TABLE row 5, BEHAVIOR-BIBLE §10.2's third
    /// missing API). Idempotent, and safe to call on any role and with no camera.
    ///
    /// <para>Input-detach rather than full <c>Detach</c>, deliberately. The player must stop
    /// driving the camera — that half is not negotiable, and it is the half the shipped
    /// controllable-ragdoll defect got wrong — but they should keep <i>seeing</i>: watching
    /// yourself slide across camp as an ice block is the comedy the register law is asking for,
    /// and a black screen would throw it away. So the rig keeps framing the body and stops
    /// answering the mouse and the stick. <c>SandboxCamera.Detach</c> exists too, is tested, and
    /// is what a future spectate mode will use.</para>
    /// </summary>
    public void ApplyCameraDetach(bool detached) => _followCamera?.SetInputDetached(detached);

    /// <summary>
    /// <b>The legibility cascade row</b> (table rows 6/11/12/20). Applied on the server through
    /// the cascade and re-derived on every other peer from the replicated state each frame (see
    /// <see cref="SyncIncapacitySkin"/>) — so a remote proxy, a late joiner and the server all
    /// reach the same skin from the same fact, rather than from an event any one of them could
    /// have missed.
    /// </summary>
    public void ApplyIncapacitySkin(Sail.Game.Failure.IncapacityState state,
        Sail.Game.Failure.IncapacityCause cause)
    {
        _visual?.SetIncapacity(state, Proportions.CrownM, Proportions.HalfWidthM);
        _skinApplied = state;
    }

    private Sail.Game.Failure.IncapacityState _skinApplied = Sail.Game.Failure.IncapacityState.Active;

    /// <summary>Keeps the visual skin honest against the replicated state, every frame, on every
    /// role. Cheap (an enum compare), and it is what makes the skin a function of the state rather
    /// than of a delivered event — the difference between a late joiner seeing an ice block and a
    /// late joiner seeing a player standing perfectly still in the dark.</summary>
    private void SyncIncapacitySkin()
    {
        if (_skinApplied != _state.Incapacity)
            ApplyIncapacitySkin(_state.Incapacity, Sail.Game.Failure.IncapacityCause.None);
    }

    // Owner (prediction + reconciliation).
    private uint _seq;
    private readonly InputRing _ring = new();
    private NetCodec.Snapshot? _pendingAuth; // newest authoritative snapshot, applied at tick start
    private Vector3 _visualError;            // world-space render offset hiding corrections
    private bool _cheatMove;

    // Server (authority).
    private ServerInputQueue? _serverQueue;
    private uint _serverTick;
    private int _sinceSnapshot;
    private ulong _lastResetMsec;

    // Remote proxy (interpolation).
    private SnapshotBuffer? _snapshots;
    private bool _hasRendered;
    private float _maxRenderStep;
    // INT-0: commanded-teleport (epoch bump) accounting on the OWNER — see TakeCommandedTeleports.
    private int _commandedTeleports;
    private float _lastTeleportJumpM;

    /// <summary>
    /// Networked wiring, kept for tests and internal use: the owner predicts + reconciles,
    /// a remote runs no physics and interpolates snapshots. The offline sandbox never
    /// calls this, so everything behaves exactly as before.
    /// </summary>
    public void ConfigureAsNetworked(bool isOwner, IIntentSource? source)
    {
        IntentSource = source;
        _spawnPosition = Position;
        _state = MoveState.AtSpawn(Position);
        if (isOwner)
        {
            _role = NetRole.PredictedOwner;
            _cheatMove = NetworkManager.Instance.Options.CheatMove;
            // A SCRIPTED BODY EARNS NOTHING (W7-8, 2026-08-30). The achievement runtime this
            // branch used to build was pruned at the fork (BASE-1), but the rule it enforced
            // outlives it and the next per-player persisted fact belongs here: isOwner alone is
            // not "a player" — every suite bot owns its own avatar, and user://settings.cfg
            // resolves by project NAME, so one shared profile is a bot press away. Gate on the
            // INTENT SOURCE (IsHumanDriven / IIntentSource.IsHumanInput), never on --bot or a
            // build flag, because "is a person driving this body" is a per-avatar question.
            SetPhysicsProcess(true);
        }
        else
        {
            _role = NetRole.RemoteProxy;
            _snapshots = new SnapshotBuffer();
            SetPhysicsProcess(false);
        }
    }

    /// <summary>
    /// Teleports this avatar back to its spawn point. Offline: immediate. Networked
    /// owner: a request to the server — the authority performs the teleport, bumps the
    /// snapshot epoch, and every peer (including the owner) snaps to it.
    /// </summary>
    public void ResetToSpawn()
    {
        switch (_role)
        {
            case NetRole.PredictedOwner:
                RpcId(1, MethodName.RequestResetToSpawn);
                break;
            case NetRole.ServerSim:
                DoServerReset();
                break;
            default:
                Velocity = Vector3.Zero;
                Position = _spawnPosition;
                break;
        }
    }

    /// <summary>Every avatar currently in a tree, and a version stamp bumped on each
    /// change — the camera rig uses this to exclude ALL player bodies from its spring
    /// arm (two players face-to-face must not collapse each other's cameras).</summary>
    public static readonly System.Collections.Generic.List<SandboxAvatar> Live = new();
    public static int LiveVersion { get; private set; }

    public override void _EnterTree()
    {
        Live.Add(this);
        LiveVersion++;

        // Node name is the owning peer id (assigned by the server at spawn). Only the
        // synchronizer (DisplayName) gets client authority; the avatar node itself stays
        // server-authority so the spawner owns the spawn lifecycle and only the server
        // may broadcast state. Offline instances have no Sync child.
        if (long.TryParse(Name.ToString(), out long peerId))
        {
            _ownerPeerId = (int)peerId;
            GetNodeOrNull<MultiplayerSynchronizer>("Sync")?.SetMultiplayerAuthority((int)peerId);
        }
    }

    public override void _ExitTree()
    {
        Live.Remove(this);
        LiveVersion++;
        // Unsubscribe from the long-lived PropManager (the setter detaches OnGrabDenied):
        // a freed avatar left subscribed made the next denied grab throw ObjectDisposed
        // inside the event multicast — eating the live avatar's denial cue after reconnect.
        Props = null;
    }

    public override void _Ready()
    {
        // THE VISUAL IS BUILT FIRST, DELIBERATELY. The collider, the anchors, the shadow and
        // the nameplate are all measured off the character that got built (ApplyProportions at
        // the bottom of this method), so the model has to exist before any of them can be
        // sized. It used to be the other way round — a hand-typed capsule first, the mesh
        // after — which is precisely how a 1.24 m figure ended up inside a 0.9 m collider.
        _visual = new AvatarVisual { Name = "Visual" };
        AddChild(_visual);
        _visual.BuildAppearance(BodyColor, AvatarKey);

        _collider = new CollisionShape3D { Name = "Collision", Shape = new CapsuleShape3D() };
        AddChild(_collider);

        Profile ??= GD.Load<PresentationProfile>(DefaultProfilePath);

        // The nameplate is SCREEN-SPACE UI, not a world object. A Label3D at head height
        // gets caught by the camera's depth-of-field (SandboxCamera far-blur) and goes soft
        // at range — UI must never be touched by a 3D post-process. Instead we project the
        // head position onto a CanvasLayer each frame (see UpdateNameplate), so the name
        // stays crisp regardless of DOF/fog/glow. Owned by the avatar, so it dies with it.
        _nameLayer = new CanvasLayer { Name = "NameLayer" };
        AddChild(_nameLayer);
        _nameLabel = new Label
        {
            Name = "NameLabel",
            Text = _displayName,
            HorizontalAlignment = HorizontalAlignment.Center,
            Visible = false, // shown once we have a valid on-screen projection
        };
        _nameLabel.AddThemeColorOverride("font_color", new Color(0.96f, 0.96f, 0.98f));
        _nameLabel.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.75f));
        _nameLabel.AddThemeConstantOverride("outline_size", 5);
        _nameLabel.AddThemeFontSizeOverride("font_size", 15);
        _nameLayer.AddChild(_nameLabel);

        _spawnPosition = Position;

        // PARENTED INTO THE VISUAL'S POSE, not onto this node (ANIM-1, Gap 0). Held items now inherit
        // the body's lean, waddle roll, hop, fidget yaw, reconciliation offset and knocked-out root
        // pose by parentage, instead of having a position — and only a position — copied onto them
        // once a frame. AvatarVisual._pose carries the full argument for why this is parentage rather
        // than a better copy, and why the pose lives on its own node rather than on the body.
        //
        // CarryAnchorGlobalTransform and every caller of it are untouched: a global transform does
        // not care what its parent chain is.
        _carryAnchor = new Node3D { Name = "CarryAnchor" };
        _visual.Mounts.AddChild(_carryAnchor);

        // Eye-height anchor for the aim ray (WP-L3). It sits on the character's OWN eyes, measured
        // off the model's eye geometry — the previous 0.78 m was a value fork picked for "a sensible
        // eyeline" on a blob. An aim ray that leaves from the wrong height is not a cosmetic error:
        // it is where every aimed verb originates.
        //
        // DELIBERATELY NOT MOUNTED IN THE POSE, unlike the carry anchor above (ANIM-1 checked this and
        // decided against it). This is a GAMEPLAY origin: an aimed verb is placed from it, and the
        // server reads it on its own authoritative avatar. The body pose is presentation and is not
        // identical across peers — the idle fidget yaw is seeded from GD.RandRange per instance and
        // the squash spring is frame-rate dependent — so hanging the aim origin inside it would make
        // a rendered animation an input to a server-side decision, which canon fact 4's parity law
        // makes a correctness bug rather than a polish opportunity. It stays bolt upright, and that
        // is the right answer for this node even though it was the wrong one for the hands.
        _aimAnchor = new Node3D { Name = "AimAnchor" };
        AddChild(_aimAnchor);

        // Grounding without a shadow map (Bible §3/§4: zero real-time shadow casters).
        // Null on a headless peer, which renders nothing and is given no shadow.
        _blobShadow = BlobShadow.Attach(this);

        ApplyProportions();

        // The carry POSE (arms forward) is driven per-frame in AnimateVisual from the authoritative
        // "am I holding" signal, so it works for the offline sandbox, the local networked holder, AND
        // remote holders alike. These events only keep the voice-range contract (and the throw recoil).
        Carry = new CarryController { AnchorProvider = () => _carryAnchor.GlobalTransform };
        Carry.PickedUp += item => VoicePublisher.UpdateFromHeld(item);
        Carry.Dropped += _ => VoicePublisher.UpdateFromHeld(null);
        Carry.Thrown += (_, _) =>
        {
            VoicePublisher.UpdateFromHeld(null);
            _visual.TriggerBumpSquish(); // little recoil
            // Silent hook today (no Thrown response in the profile) — a future throw
            // whoosh becomes a data-only .tres edit, zero code.
            ActorFx.Fire(GetParent(), Profile, ActorEvent.Thrown, GlobalPosition);
        };

        ConfigureNetworkedInstance();
    }

    /// <summary>Re-measures the character now on screen and resizes everything shaped by its
    /// body: the collision capsule, the carry anchor, the aim origin and the blob shadow. (The nameplate and the follow camera read <see cref="Proportions"/> live each
    /// frame instead of being written here, because they are recomputed per frame anyway.)
    ///
    /// <para>Runs at <c>_Ready</c> and again after every appearance rebuild. Every peer runs it
    /// against the same imported model with the same arithmetic, so the capsule the server
    /// simulates is bit-identical to the one the owner predicts with — see
    /// <see cref="AvatarProportions"/>'s determinism note.</para></summary>
    private void ApplyProportions()
    {
        Proportions = AvatarProportions.For(_visual.BodyBounds, _visual.EyeCentre?.Y);

        // Mutated in place rather than replaced: a live CharacterBody3D re-resolves its shape
        // from the same CollisionShape3D node, and swapping the node mid-session would drop the
        // body out of the physics world for a tick.
        var capsule = (CapsuleShape3D)_collider.Shape;
        capsule.Radius = Proportions.CapsuleRadiusM;
        capsule.Height = Proportions.CapsuleHeightM;
        _collider.Position = Proportions.CapsuleCentreLocal;

        _carryAnchorRest = Proportions.CarryAnchorRestLocal;
        _carryAnchor.Position = _carryAnchorRest;
        _aimAnchor.Position = Proportions.AimAnchorLocal;

        if (_blobShadow != null)
            _blobShadow.Radius = Proportions.ShadowRadiusM;
    }

    /// <summary>
    /// If this avatar was spawned from NetworkedAvatar.tscn (has a Sync child), wire it
    /// for multiplayer. On the dedicated server EVERY avatar becomes a server-side
    /// simulation (the authority); on a client, the owned avatar gets camera + input (or
    /// the deterministic walk when it's a headless bot) and predicts, while every other
    /// avatar is a snapshot-interpolated remote. Offline sandbox instances have no Sync
    /// child and configure themselves in SandboxWorld/LabHost.
    /// </summary>
    private void ConfigureNetworkedInstance()
    {
        var sync = GetNodeOrNull<MultiplayerSynchronizer>("Sync");
        if (sync == null)
        {
            // On a server this leaves _role = Offline and every input from the owner
            // is silently dropped — a permanently frozen avatar. Fail loud.
            if (Multiplayer.IsServer())
                GD.PushError($"[avatar {Name}] networked spawn without a Sync child — input will be rejected");
            return;
        }

        if (Multiplayer.IsServer())
        {
            _role = NetRole.ServerSim;
            _state = MoveState.AtSpawn(Position);
            _serverQueue = new ServerInputQueue();
            SetPhysicsProcess(true);
            return;
        }

        bool isMine = sync.GetMultiplayerAuthority() == Multiplayer.GetUniqueId();
        if (!isMine)
        {
            ConfigureAsNetworked(false, null);
            return;
        }

        var net = NetworkManager.Instance;
        DisplayName = net.LocalDisplayName;
        // A picker choice wins; unset (headless bots, CI, tools that never touch a
        // picker) falls back to SAIL_AVATAR exactly like before this feature existed, and an
        // unset SAIL_AVATAR falls back to whatever body THIS WORLD prefers (BT-7 — the bubble
        // test is the box kid, everywhere else is the greybox). Three tiers, narrowest first.
        AvatarKey = net.LocalAvatarKey.Length > 0
            ? net.LocalAvatarKey
            : AvatarVisual.ResolveEnvAvatarKey(net.Options.World);
        IIntentSource source;
        if (net.IsBot)
        {
            IIntentSource baseSource = net.Options.CarryScript
                ? BuildScriptedCarry(net)
                : net.Options.GotoScript
                    // --goto-script: walk to a point (sprinting if asked) and stand there.
                    ? new ScriptedGotoIntentSource(this, net.Options.GotoTarget, net.Options.GotoSprint)
                    : new DeterministicWalkIntentSource(this, net.Options.DurationSec);
            // Aim substrate (WP-L3): --aim-script layers a raise/lower schedule on top of
            // whatever movement brain the bot already has (Run-AimTest.ps1's convergence proof).
            source = net.Options.AimScript
                ? new ScriptedAimIntentSource(baseSource, net.Options.AimRaiseAtSec, net.Options.AimLowerAtSec)
                : baseSource;
            // Marketing/screenshot capture: a windowed bot with --spectate-cam gets a follow
            // camera so the roaming avatar renders a real gameplay view. View-only; the bot's
            // intent source above is unchanged. Headless CI never sets this.
            if (net.Options.SpectateCam)
            {
                var specCam = new SandboxCamera { Name = "Camera", HandlesPauseToggle = false };
                GetParent().AddChild(specCam);
                // Attach registers itself as _followCamera (MOVE-4f) — one writer, and the same
                // path every dev harness takes, so the two can no longer disagree.
                specCam.Attach(this);
                AimCamera = specCam.CameraNode;
            }
            // --first-person-cam / --first-person-selftest: a WINDOWED bot renders through the
            // real first-person rig, so the smoke suite photographs and measures the same camera
            // a player looks through rather than a second one built to resemble it. View-only —
            // the bot's intent source above is untouched, and the cursor is left alone.
            if (net.Options.FirstPersonCam || net.Options.FirstPersonSelfTest)
            {
                FirstPersonCamera probeCam = AttachFirstPersonCamera(captureMouse: false);
                // --fp-look: aim the lens. A bot brain decides where it WALKS; in first person
                // that no longer decides where it LOOKS, so a capture harness has to be able to
                // point the camera itself. The intent source is untouched.
                if (net.Options.HasFirstPersonLook)
                {
                    probeCam.SetLook(net.Options.FirstPersonLookYaw, net.Options.FirstPersonLookPitch);
                    GD.Print($"[fp] look set by --fp-look to yaw " +
                             $"{Mathf.RadToDeg(probeCam.Yaw):F1} deg, pitch " +
                             $"{Mathf.RadToDeg(probeCam.Pitch):F1} deg");
                }
                if (net.Options.FirstPersonSelfTest)
                {
                    var probe = new FirstPersonSelfTest { Name = "FirstPersonSelfTest" };
                    probe.Setup(probeCam, this);
                    AddChild(probe);
                }
            }
        }
        else
        {
            // FIRST PERSON IS THE ONLY HUMAN CAMERA IN THIS GAME (FP-1, 2026-09-19). The
            // third-person attach that stood here is gone, and there is no toggle back — see
            // FirstPersonCamera's class doc. SandboxCamera is still built for --spectate-cam
            // captures above and for the offline dev harnesses (SandboxWorld, FeelSandboxWorld).
            source = new FirstPersonIntentSource(AttachFirstPersonCamera(captureMouse: true));
        }
        ConfigureAsNetworked(true, source);
    }

    /// <summary>
    /// <b>Mount the first-person rig on this body and hand back the camera.</b> One method, taken
    /// by both the human path and the capture/probe path, so the smoke suite cannot be measuring a
    /// camera built differently from the one a player looks through.
    ///
    /// <para>A CHILD of the avatar, unlike <see cref="SandboxCamera"/>, which is a sibling rig
    /// parented next to the body: the eyeline is a point on the body, so parentage is what makes
    /// the lens unable to lag it. <see cref="AimCamera"/> is set here too — it is what
    /// <c>InteractTargeting.Pick</c> resolves "the thing I am looking at" against, so a camera
    /// swap that forgot it would leave E acting on whatever the third-person lens used to see.</para>
    /// </summary>
    private FirstPersonCamera AttachFirstPersonCamera(bool captureMouse)
    {
        var camera = new FirstPersonCamera { Name = "FirstPersonCamera", HandlesPauseToggle = false };
        AddChild(camera);
        camera.Attach(this, captureMouse);
        AimCamera = camera.CameraNode;
        GD.Print($"[fp] first-person camera active on '{DisplayName}' — eye height " +
                 $"{Proportions.EyeHeightM:F3} m (measured={Proportions.EyesMeasured}), fov " +
                 $"{FirstPersonCamera.DefaultFovDeg:F0} deg, near {FirstPersonCamera.NearPlaneM:F2} m, " +
                 $"own body hidden on render layer {AvatarVisual.FirstPersonHiddenLayer}");
        return camera;
    }

    /// <summary>The scripted carry bot brain, with the optional continuous-patrol walk
    /// (Run-CarryDriftTest.ps1) and the optional regrab phase wired to live replicated prop
    /// state (Run-RegrabTest.ps1's drift regression proof). The regrab lambdas read
    /// <see cref="Props"/> lazily — it is bound by SpawnPlayer before this avatar enters the tree.</summary>
    private ScriptedCarryIntentSource BuildScriptedCarry(NetworkManager net)
    {
        System.Func<Vector3?>? regrabTarget = null;
        System.Func<bool>? isHolding = null;
        if (net.Options.CarryRegrab)
        {
            int propId = net.Options.CarryRegrabPropId;
            regrabTarget = () => Props?.NodeFor(propId) is { HolderPeerId: 0 } loose
                ? loose.WorldPosition
                : null;
            isHolding = () => Props?.FindHeldBy(Multiplayer.GetUniqueId()) != null;
        }
        // --carry-grab-retry needs the same "am I actually holding?" probe to know when to stop
        // pressing, and it is independent of --carry-regrab. Asked of the replicated holder
        // state, the same live server-derived state every other holder check in this file reads.
        if (net.Options.CarryGrabRetrySec >= 0 && isHolding == null)
            isHolding = () => Props?.FindHeldBy(Multiplayer.GetUniqueId()) != null;
        return new ScriptedCarryIntentSource(this, net.Options.CarryTarget, net.Options.CarryGrabAtSec,
            net.Options.CarryHoldSec, net.Options.CarryThrowSec,
            net.Options.CarryWalk ? net.Options.CarryWalkTo : (Vector3?)null,
            net.Options.CarryPatrol ? (net.Options.CarryPatrolA, net.Options.CarryPatrolB) : ((Vector3, Vector3)?)null,
            regrabTarget, isHolding,
            // --goto-sprint doubles as "sprint the patrol" here. See ScriptedCarryIntentSource._sprint
            // for why an existing flag is reused rather than a new one added.
            net.Options.GotoSprint,
            net.Options.CarryGrabRetrySec,
            net.Options.CarryTargetPropId >= 0
                ? () => Props?.NodeFor(net.Options.CarryTargetPropId) is { HolderPeerId: 0 } free
                    ? free.WorldPosition
                    : (Vector3?)null
                : null);
    }

    public override void _PhysicsProcess(double delta)
    {
        switch (_role)
        {
            case NetRole.Offline:
                OfflineTick(delta);
                break;
            case NetRole.PredictedOwner:
                OwnerTick(delta);
                break;
            case NetRole.ServerSim:
                ServerTick();
                break;
        }
    }

    public override void _Process(double delta)
    {
        if (_role == NetRole.RemoteProxy)
            RemoteFrame(delta);
        // Every role, every frame: the skin and the camera follow the replicated state rather
        // than a delivered event. Both are idempotent and both early-out on no change.
        SyncIncapacitySkin();
        if (_role != NetRole.RemoteProxy)
            ApplyCameraDetach(IncapacitatedNow);
        UpdateNameplate();
    }

    // Screen-space nameplate: project the head onto the local camera each frame so the name
    // renders as crisp UI, never caught by the camera's depth-of-field (see the NameLayer
    // creation note). No camera (headless server) or behind the camera → hidden; fades out
    // beyond conversational range so distant names don't clutter, but never blurs.
    private void UpdateNameplate()
    {
        // World-anchored UI hides behind menus (MECHANICS-BIBLE 2). The pause overlay used
        // to suppress only the interact chip, so names kept drawing over the pause menu —
        // one dependent system updated, a sibling missed. Both now read the same flag.
        // NOBODY IS SHOWN THEIR OWN NAME (FP-1, 2026-09-19). In third person the plate floated
        // over a head the player could see, so projecting it cost nothing and was never
        // suppressed. In first person the anchor point sits 0.4-0.5 m directly above the lens:
        // level and looking down it falls behind the near plane and hides itself, which reads as
        // "it already isn't drawn" — but the moment the player looks UP past about 5 degrees the
        // anchor crosses in front of the lens and their own name appears across the top of their
        // own screen. Checked in engine, not reasoned about; the plate is suppressed at the
        // source rather than left to a projection accident.
        if (IsLocalFirstPersonBody || WorldUi.Suppressed || _displayName.Length == 0)
        {
            _nameLabel.Visible = false;
            return;
        }
        Camera3D? cam = GetViewport().GetCamera3D();
        // Anchored a fixed small gap above THIS character's own crown, so the name sits just
        // over the head rather than floating a body-length above it — or, on a taller figure,
        // being drawn across its face, which is where the old absolute 1.15 m (tuned to the
        // original body's 0.93 m crown) put it on a 1.16–1.27 m figure.
        // RenderGlobalPosition, not GlobalPosition: the nameplate is per-frame cosmetic
        // tracking (see that property's contract) — the raw body detaches from the rendered
        // mesh by up to MaxVisualErrorM during reconciliation corrections.
        // THE NAMEPLATE CASCADE ROW (table row 6), and the one that names this repo's own
        // recurring failure in as many words: "the nameplate floating over a headless body".
        // NameplateHeightM is a fixed gap above a STANDING player's crown; over a body lying flat
        // on its back it is a metre of empty air with a name in it. A Knocked Out player's plate
        // drops to just above the collapsed silhouette. An ice block keeps the standing height —
        // it IS standing, it is simply frozen — which is also what makes the two states tell
        // themselves apart at a distance (BEHAVIOR-BIBLE §10.3).
        // THE CROUCH IS THE THIRD ENTRY IN THIS SAME ROW (MOVE-5c, cascade table row 6). Everything
        // the paragraph above says about a body lying flat is true in miniature of a body squatting:
        // NameplateHeightM is a gap above a STANDING crown, and a crouch-slide sinks the rig 20 cm
        // on its own knees (AvatarVisual.CrouchDropM), so an unadjusted plate floats a sixth of a
        // body above a head that is no longer there. It is not a new mechanism — it is the one term
        // that already exists for exactly this, applied to the one state that newly moves the crown.
        //
        // CrouchDropM is >= 0 by construction and it is the WHOLE drop, so the landing absorb's own
        // 4.4 cm is corrected with it. That is a correction rather than a change of feel: during an
        // absorb the crown genuinely is that much lower, and the plate following it is what this
        // paragraph has always asked for.
        float plateHeight = IncapacityNow == Sail.Game.Failure.IncapacityState.KnockedOut
            ? Proportions.HalfWidthM + KnockedOutNameplateGapM
            : Proportions.NameplateHeightM - _visual.CrouchDropM;
        Vector3 head = RenderGlobalPosition + new Vector3(0, plateHeight, 0);
        if (cam == null || cam.IsPositionBehind(head))
        {
            _nameLabel.Visible = false;
            return;
        }
        float dist = cam.GlobalPosition.DistanceTo(head);
        float alpha = Mathf.Clamp(Mathf.Remap(dist, 24f, 34f, 1f, 0f), 0f, 1f);
        if (alpha <= 0.01f)
        {
            _nameLabel.Visible = false;
            return;
        }
        Vector2 screen = cam.UnprojectPosition(head);
        _nameLabel.Position = screen - new Vector2(_nameLabel.Size.X * 0.5f, _nameLabel.Size.Y);
        _nameLabel.Modulate = new Color(1, 1, 1, alpha);
        _nameLabel.Visible = true;
    }

    // --- Offline: simulate straight off the node ---------------------------------------
    // State is read from the node itself each tick (not a private cache) so external
    // writes — tests and tools teleport the avatar and zero Velocity — keep working.
    private void OfflineTick(double delta)
    {
        // The offline clock (ANIM-M3). No server exists here, so this counter IS the replicated tick
        // as far as the clip-time anchor is concerned.
        _offlineTick++;
        float dt = (float)delta;
        MoveIntent intent = IntentSource?.NextIntent(delta) ?? MoveIntent.None;
        var prev = new MoveState
        {
            Position = Position,
            Velocity = Velocity,
            Yaw = Rotation.Y,
            CoyoteRemaining = _coyoteRemaining,
            JumpBufferRemaining = _jumpBufferRemaining,
            SkidRemaining = _skidRemaining,
            Grounded = IsOnFloor(),
            // MOVE-5: the five verb-machine fields, cached on the node for the reason their own
            // declarations give. Without them the offline path — which IS the movement playground —
            // would rebuild a virgin state every tick and no verb could ever start.
            Verb = _verb,
            VerbClockTicks = _verbClockTicks,
            ChainDepth = _chainDepth,
            ChainTimerTicks = _chainTimerTicks,
            AirJumpsUsed = _airJumpsUsed,
        };
        AimYaw = intent.AimYaw;
        AimPitch = intent.AimPitch;
        MoveState next = AvatarMotor.Step(this, prev, intent, Carry.SpeedFactor, dt, out StepEvents ev);
        _coyoteRemaining = next.CoyoteRemaining;
        _jumpBufferRemaining = next.JumpBufferRemaining;
        _skidRemaining = next.SkidRemaining;
        _state.SkidRemaining = next.SkidRemaining; // the pose reads _state on every role — see AnimateVisual
        // MOVE-5: the same mirror, and the same two reasons. The node fields carry the simulation
        // forward; the _state copy is what VerbNow / ChainDepthNow and (behind this packet) the
        // pose layer read on every role.
        _verb = next.Verb;
        _verbClockTicks = next.VerbClockTicks;
        _chainDepth = next.ChainDepth;
        _chainTimerTicks = next.ChainTimerTicks;
        _airJumpsUsed = next.AirJumpsUsed;
        _state.Verb = next.Verb;
        _state.VerbClockTicks = next.VerbClockTicks;
        _state.ChainDepth = next.ChainDepth;
        _state.ChainTimerTicks = next.ChainTimerTicks;
        _state.AirJumpsUsed = next.AirJumpsUsed;
        // Same mirror, same reason (MOVE-3): the readout accessors read _state on every role, and
        // offline is the one role whose forgiveness timers live in node fields instead.
        _state.CoyoteRemaining = next.CoyoteRemaining;
        _state.JumpBufferRemaining = next.JumpBufferRemaining;

        PlayStepCosmetics(ev, next.Grounded, HorizontalSpeed(next.Velocity), dt, intent.Sprint);
        HandleCarryIntent(intent);
        Grounded = next.Grounded;
        AnimateVisual(delta, GlobalTransform.Basis.Inverse() * next.Velocity, next.Grounded, intent.Sprint);
    }

    // --- Owner: predict, send, reconcile ------------------------------------------------
    private void OwnerTick(double delta)
    {
        // Apply the newest authoritative state first (deferred from the RPC so every
        // Step — including replays — runs inside a fixed physics tick).
        if (_pendingAuth is NetCodec.Snapshot auth)
        {
            _pendingAuth = null;
            Reconcile(auth);
        }

        // The owner's estimate of the server tick it is about to render (ANIM-M3). Reconcile above
        // has just snapped it to the newest authoritative tick if one arrived; this is the tick this
        // predicted step produces.
        _ownerRenderTick++;
        MoveIntent raw = IntentSource?.NextIntent(delta) ?? MoveIntent.None;
        MoveIntent intent = raw with { Seq = ++_seq };
        AimYaw = intent.AimYaw;
        AimPitch = intent.AimPitch;
        // Aim substrate (WP-L3): stepped BEFORE the movement step, reading the PREVIOUS tick's
        // resulting velocity (_state.Velocity, not yet overwritten this tick) — this is what
        // lets _aim.SpeedFactor feed into THIS tick's movement speedFactor below without a
        // circular same-tick dependency, and ServerTick/Reconcile mirror this exact ordering so
        // owner prediction and server authority derive identical Steadiness01 from identical
        // (Stance, wantRaised, horizontalSpeed) history (see AimController's class doc).
        _aim.Step(AvatarMotor.TickDelta, intent.AimRaise, HorizontalSpeed(_state.Velocity));
        AimStance = _aim.Stance;
        AimSteadiness01 = _aim.Steadiness01;

        // Predict encumbrance the same way the server derives it (ServerTick): on the
        // networked path the local CarryController is bypassed (grabs bind via PropManager),
        // so Carry.SpeedFactor stays 1.0 while the server simulates the held mass — which
        // made every carried step mispredict and re-correct forever. Offline, Props is null
        // and the local controller's factor still applies.
        // CarriedMassKgFor reads the same replicated holder state the server reads, so
        // prediction and authority compute the identical number from identical state, which is
        // the only thing that keeps a carried step from mispredicting and re-correcting forever.
        float speedFactor = (Props != null
            ? CarryController.ComputeSpeedFactor(Props.CarriedMassKgFor(_ownerPeerId))
            : Carry.SpeedFactor) * _aim.SpeedFactor;
        _ring.Add(intent, speedFactor);
        SendInputs();

        // Predict locally, immediately: this is what makes owned movement 0-latency — EXCEPT
        // while incapacitated, where prediction is suspended and the body simply holds wherever
        // the last authoritative snapshot put it (see the matching block in Reconcile for the
        // full argument). Inputs keep being sampled and sent throughout: the server needs the
        // stream to keep acking and to keep its input queue fed, and it is the server — not this
        // client — that decides those inputs do nothing. That ordering is the whole point.
        StepEvents ev = default;
        if (IncapacitatedNow)
            ApplyStateToNode(_state);
        else
            _state = AvatarMotor.Step(this, _state, intent, speedFactor, AvatarMotor.TickDelta, out ev);
        _ring.RecordPrediction(_seq, _state, _aim.Stance, _aim.SteadyElapsedSec);

        float dt = (float)delta;
        // (The achievement tracker that used to be stepped here went with the achievements
        // package at the fork — BASE-1, 2026-09-19. It was fed the SAME verb/aim-stance this
        // tick had just resolved, never raw input; anything scored off the motor later should
        // read the resolved state the same way.)
        PlayStepCosmetics(ev, _state.Grounded, HorizontalSpeed(_state.Velocity), dt, intent.Sprint);
        // THE INTERACTION-GATES CASCADE ROW, owner half (table rows 7/9/14/22). A body that is
        // down cannot grab, drop, throw, fire a verb or act on the world. Gated in ONE place —
        // the intent handlers' shared call site — rather than inside each of them, because "two
        // of the three systems were updated" is the precise failure shape the cascade table
        // exists to catch, and adjacent lines with one guard cannot drift apart the way separate
        // guards can.
        //
        // Client-side prediction of a rule the SERVER also enforces (PropManager's own gates, and
        // AvatarMotor discarding the intent regardless). This half exists so the local player's
        // interact key feels dead the same instant their steering does, rather than firing
        // requests that come back refused. The authority is still the authority.
        if (!ControlDeniedNow)
        {
            HandleCarryIntent(intent);
            HandleFireIntent(intent);
        }
        else
        {
            // Keep the edge tracker honest across the gate. Without this, a player who was
            // holding Interact when they went down would get a phantom rising edge on the frame
            // they got back up, having never released the key — one free grab out of a state
            // that is supposed to have cost them everything they were carrying.
            _interactHeldPrev = intent.Interact;
        }
        Grounded = _state.Grounded;

        // Drain the correction offset on the render layer only; simulation is untouched.
        _visualError *= Mathf.Exp(-CorrectionSmoothRate * dt);
        if (_visualError.LengthSquared() < 1e-6f)
            _visualError = Vector3.Zero;
        // W7-4: DECLARED, not written. This node has exactly one writer (AvatarVisual.ApplyRootPose)
        // and three contributors; before the split this line silently zeroed the death beat's pose
        // on every physics tick while the beat wrote it back on every render frame.
        _visual.SetRenderOffset(GlobalTransform.Basis.Inverse() * _visualError);

        AnimateVisual(delta, GlobalTransform.Basis.Inverse() * _state.Velocity, _state.Grounded, intent.Sprint);
    }

    private void SendInputs()
    {
        var window = _ring.Window(NetCodec.InputRedundancy);
        if (_cheatMove)
        {
            // Test hook: lie on the wire (inflated direction, absurd speed factor, the
            // occasional NaN) while predicting honestly. The anti-cheat suite asserts the
            // server's authoritative view never exceeds legitimate movement.
            for (int i = 0; i < window.Count; i++)
            {
                NetCodec.InputEntry e = window[i];
                Vector3 dir = _seq % 97 == 0
                    ? new Vector3(float.NaN, 0, float.NaN)
                    : e.Intent.MoveDir * CheatDirScale;
                window[i] = new NetCodec.InputEntry(
                    e.Intent with { MoveDir = dir }, CheatSpeedFactorClaim);
            }
        }
        byte[] packet = NetCodec.PackInputs(window);
        if (NetSim.Instance is NetSim sim)
            sim.Queue(() =>
            {
                if (IsInstanceValid(this) && IsInsideTree())
                    RpcId(1, MethodName.SubmitInput, packet);
            });
        else
            RpcId(1, MethodName.SubmitInput, packet);
    }

    /// <summary>
    /// Rewinds and replays movement AND, independently, the aim-substrate mirror — either can
    /// diverge without the other (same shape as CamcorderController's Task-4 fix on
    /// feat/tier0-camcorder): a lost aim-raise edge outside the input redundancy window leaves
    /// Stance sticky-diverged from the server's view even while position prediction matched
    /// perfectly, so each system gets its OWN match check and its OWN rewind+replay, gated
    /// independently. Replaying a system that already matched would double-apply it (every
    /// entry in <c>_ring.Entries</c> was already stepped through exactly once, live, as it was
    /// created) — see InputRing.SetPredictionAtAim's doc comment for the same reasoning from
    /// the ring's side.
    /// </summary>
    private void Reconcile(in NetCodec.Snapshot auth)
    {
        // Recorded before anything below can move the body: this is the newest position the
        // SERVER has actually simulated, as distinct from the prediction this tick is about to
        // reconcile. Every arm of this method either adopts it, replays onto it, or agrees with
        // it, so one assignment here covers all three — see ServerConfirmedPosition, and
        // ScriptedGotoIntentSource for the irreversible decision that depends on it.
        _serverConfirmedPos = auth.State.Position;

        InputRing.AckSnapshot? ack = _ring.DropThrough(auth.LastProcessedSeq);
        // The owner's animation clock re-based onto the authority (ANIM-M3). The replay below
        // re-steps every unacknowledged input, so the ticks it will advance past are exactly the ones
        // still in the ring — which is what makes this an estimate of the SERVER's tick rather than a
        // count of local frames.
        _ownerRenderTick = auth.Tick;
        bool teleported = auth.Epoch != _epoch;
        _epoch = auth.Epoch;

        // --- THE CLIENT-PREDICTION CASCADE ROW (row 3, BEHAVIOR-BIBLE §10.2) -------------------
        // While this body is incapacitated — on either side of the comparison — the owner does
        // not predict at all. It adopts the authority outright and drops every unacknowledged
        // input on the floor.
        //
        // The bible's stated reason is "ragdoll physics cannot be replayed". This package ships
        // no ragdoll physics (see AvatarMotor.Step's failure-state comment), but the row is
        // load-bearing anyway for a reason the bible could not have known: the DRAG VERB. A
        // teammate hauling this ice block injects an authoritative velocity the owning client has
        // no way to derive — it depends on where the *other* player is standing this tick — so
        // every replayed step would resolve a position the server did not, and the two would
        // re-diverge for as long as the drag lasted. Suspension is the honest answer: this state
        // is server-authoritative at full round-trip latency, exactly as §10.2 says.
        //
        // Both directions of the comparison, not just the authority's: on the way OUT, a replay
        // of inputs that were sampled while the body was down would apply a burst of real
        // movement the player never actually steered.
        //
        // An impulse ragdoll is deliberately NOT suspended. It injects no external velocity, its
        // bit is replicated, and AvatarMotor.Step is deterministic — so the ordinary rewind-and-
        // replay resolves it correctly, and suspending a 1.2-second effect would cost it its
        // responsiveness for no correctness gain.
        if (auth.State.Incapacity != Sail.Game.Failure.IncapacityState.Active
            || _state.Incapacity != Sail.Game.Failure.IncapacityState.Active)
        {
            Vector3 beforeAdopt = Position + _visualError;
            _state = auth.State;
            ApplyStateToNode(_state);
            _aim.AdoptAuthoritative(auth.AimStance, auth.AimSteadyElapsedSec);
            AimStance = _aim.Stance;
            AimSteadiness01 = _aim.Steadiness01;
            // Everything still in flight was steering this body could not do. Replaying it is the
            // one thing that must not happen here.
            _ring.Clear();
            LastCorrectionM = beforeAdopt.DistanceTo(Position);
            // Deliberately no ObserveDesync: adoption at full RTT is the DESIGNED behaviour of
            // this state, and every tick of a drag would otherwise be logged as a suspected
            // desync until the monitor's sustained counter tripped on a working feature.
            //
            // The render offset is still folded (bounded, presentation-only), because the body is
            // sliding slowly and smoothing 30 Hz adoption is the difference between a comic slide
            // and a stutter. A teleport still cuts.
            Vector3 adoptError = beforeAdopt - Position;
            _visualError = !teleported && adoptError.LengthSquared() <= MaxVisualErrorM * MaxVisualErrorM
                ? adoptError
                : Vector3.Zero;
            return;
        }

        bool posMatches = !teleported && ack is InputRing.AckSnapshot posAck
            && PredictionMatches(posAck.State, auth.State);
        bool aimMatches = !teleported && ack is InputRing.AckSnapshot aimAck && aimAck.HasAimPrediction
            && aimAck.AimStance == auth.AimStance
            && Mathf.Abs(aimAck.AimSteadyElapsedSec - auth.AimSteadyElapsedSec) <= AimSteadyEpsilonSec;

        if (posMatches && aimMatches)
        {
            // The common case on a clean connection: the server agreed with everything we
            // already predicted. No replay, correction invisible by definition.
            LastCorrectionM = ack!.Value.State.Position.DistanceTo(auth.State.Position);
            ObserveDesync();
            return;
        }

        Vector3 oldRenderPos = Position + _visualError;

        // Movement: rewind to the authoritative state and replay every unacknowledged input
        // through the same deterministic step to return to the present tick. Skipped entirely
        // when position already matched — see the class doc above for why replaying an
        // already-correct system would corrupt it.
        if (!posMatches)
        {
            _state = auth.State;
            ApplyStateToNode(_state);
            for (int i = 0; i < _ring.Count; i++)
            {
                InputRing.Entry e = _ring.Entries[i];
                _state = AvatarMotor.Step(this, _state, e.Intent, e.SpeedFactor, AvatarMotor.TickDelta, out _);
                _ring.SetPredictionAt(i, _state);
            }
            ApplyStateToNode(_state);
        }

        // Aim substrate: same rewind+replay shape, its own independent gate. Adopting a
        // mid-transition authoritative stance collapses to the nearest committed endpoint (see
        // AimController.AdoptAuthoritative's own doc comment). prevVelocity seeds from the
        // rewind point (auth.State.Velocity) and walks forward one entry at a time via each
        // entry's own (possibly just-refreshed, above) Predicted.Velocity — reproducing the
        // exact "previous tick's velocity" ordering OwnerTick/ServerTick use, so a replay lands
        // on the identical Steadiness01 a live tick would have (see AimController's class doc).
        if (!aimMatches)
        {
            _aim.AdoptAuthoritative(auth.AimStance, auth.AimSteadyElapsedSec);
            Vector3 prevVelocity = auth.State.Velocity;
            for (int i = 0; i < _ring.Count; i++)
            {
                InputRing.Entry e = _ring.Entries[i];
                _aim.Step(AvatarMotor.TickDelta, e.Intent.AimRaise, HorizontalSpeed(prevVelocity));
                _ring.SetPredictionAtAim(i, _aim.Stance, _aim.SteadyElapsedSec);
                prevVelocity = _ring.Entries[i].Predicted.Velocity;
            }
            AimStance = _aim.Stance;
            AimSteadiness01 = _aim.Steadiness01;
        }

        LastCorrectionM = ack is InputRing.AckSnapshot ackVal
            ? ackVal.State.Position.DistanceTo(auth.State.Position)
            : oldRenderPos.DistanceTo(Position);

        // A commanded teleport is an expected large correction, not a desync — don't feed it in.
        if (!teleported)
            ObserveDesync();

        if (teleported)
        {
            // Commanded teleport (reset-to-spawn / door): snapping IS the expected result.
            // Same rule for the aim rig (see DoServerReset/DoServerTeleport's own comment): a
            // teleport lands Lowered on the server BEFORE this snapshot was ever built, so
            // auth.AimStance already reads Lowered here — nothing extra to force.
            _visualError = Vector3.Zero;
            // INT-0 instrumentation: count the bump where it is actually CONSUMED, not where the
            // position happens to move a long way. See TakeCommandedTeleports.
            _commandedTeleports++;
            _lastTeleportJumpM = LastCorrectionM;
            return;
        }
        // Fold the pop into the render offset; OwnerTick drains it over a few frames.
        // Input and simulation were never interrupted — the player keeps steering. A no-op when
        // position already matched (Position is unchanged, so this recomputes the same offset).
        Vector3 error = oldRenderPos - Position;
        _visualError = error.LengthSquared() <= MaxVisualErrorM * MaxVisualErrorM
            ? error
            : Vector3.Zero;
    }

    // Read-only desync watch: a single large correction is normal (loss, a hitch); a large
    // correction sustained across many snapshots means prediction and authority genuinely
    // diverged — a real bug that otherwise hides as "occasional rubber-banding". Logged once
    // per episode, on the server-visible log so it surfaces in playtest captures. Touches no
    // simulation state.
    private readonly DesyncMonitor _desync = new();

    private void ObserveDesync()
    {
        if (_desync.Observe(LastCorrectionM))
            Net.ServerLog.Warn("client desync suspected",
                $"avatar={Name} correction={LastCorrectionM:F2}m sustained={_desync.ConsecutiveCount}");
    }

    /// <summary>
    /// Does the owner's prediction at the acked tick agree with the authority's state at that same
    /// tick? A <c>false</c> here is what makes <see cref="Reconcile"/> rewind and replay; a
    /// <c>true</c> takes the early return and adopts NOTHING, so anything missing from this
    /// comparison can stay diverged between client and server indefinitely, silently, with no
    /// correction and no desync log.
    ///
    /// <para><b>Public because it is a pure static function of two states with no instance
    /// dependency, and because the boundary it draws is a design decision that has to be pinned in
    /// a test</b> — see <c>PredictionMatchTests</c>. Nothing outside those tests calls it.</para>
    /// </summary>
    public static bool PredictionMatches(in MoveState predicted, in MoveState auth)
    {
        return predicted.Grounded == auth.Grounded
            // Water (W2). Compared EXACTLY, with no epsilon, and load-bearing in both directions.
            //   - Water is derived from position on both sides, so a disagreement means the two
            //     landed on opposite sides of the hysteresis band and will keep diverging until
            //     one is rewound to the other.
            //   - Soaked and ControlLocked are server-authored and reach the client ONLY through
            //     auth.State. Without them here, a prediction that matched positionally would
            //     take the early-return above and the client would never adopt them at all — the
            //     player would keep steering through a sputter-out the server had already locked.
            //     That is precisely the "dead and player-controlled" defect MECHANICS-BIBLE §2
            //     names, arriving through the reconciliation door instead of the obvious one.
            && predicted.Water == auth.Water
            && predicted.Soaked == auth.Soaked
            && predicted.ControlLocked == auth.ControlLocked
            // Incapacity and ImpulseRagdoll (phase 1c). Same clause, same exact comparison, same
            // reasoning as Soaked/ControlLocked directly above — and this is the pair the
            // reasoning was originally written about. Both are server-authored and reach the
            // client ONLY through auth.State. Without them here, a prediction that matched
            // positionally (which it always does the tick a stationary player is knocked out)
            // would take the early-return and the client would never adopt the state at all: the
            // player would keep steering a body the server had already flattened. That is the
            // controllable-ragdoll defect this repo has shipped once, arriving through the
            // reconciliation door rather than the obvious one.
            && predicted.Incapacity == auth.Incapacity
            && predicted.ImpulseRagdoll == auth.ImpulseRagdoll
            // The skid (SKID-1), compared as the BOOLEAN and deliberately not as the float. Both
            // sides derive the timer from the same inputs through the same pure StepSkid, so the
            // interesting disagreement is "one of us is sliding and the other is steering" — which
            // diverges the trajectory and must rewind. A raw float compare would instead fire a
            // correction on the sub-tick offset between the client's and the server's clocks on
            // every single skidding tick, which is the mispredict-forever shape the carry-factor
            // comment in OwnerTick describes.
            && AvatarMotor.IsSkidding(predicted) == AvatarMotor.IsSkidding(auth)
            // --- THE VERB GATE (MOVE-5e) -------------------------------------------------------
            // All five compared EXACTLY, and the choice between exact and coarse is the whole of
            // this clause's design. THE SKID'S COARSE COMPARE IS NOT THE PRECEDENT HERE, because
            // its stated reason does not transfer: SkidRemaining is a float decremented by dt and
            // tested against a continuous speed threshold, so an exact compare would fire on the
            // sub-tick offset between the client's clock and the server's on EVERY skidding tick.
            // The verb fields have no such offset. Verb is a four-value enum; the four counters are
            // bytes that MoveState's own §10.3 argument calls "integer by construction" — set from
            // a knob at round(sec x 60), decremented by exactly 1 per fixed tick, compared against
            // integers. There is no representable near-miss for any of them, so "exact" and "coarse
            // enough to ignore clock noise" are the same comparison, and the coarse version would
            // only be discarding real information.
            //
            // The precedent that DOES transfer is Water, six lines up: derived on both sides by the
            // same pure function from the same replicated inputs, compared exactly, because a
            // disagreement means the two simulations resolved different branches and will keep
            // diverging until one is rewound to the other. StepVerb and StepChain are pure in
            // exactly that way — (tuning, prev verb/clock/depth/timer, grounded, locked, ballistic,
            // jumped, jumpHeld, water, velocity, stick) and nothing else.
            //
            // What each one buys, because "all five" should not be a shrug:
            //   Verb          — changes the wish speed, the deceleration authority and the steering
            //                   rule. A Tuck and a Normal at the same standing position are
            //                   positionally identical, so nothing else in this method can see it.
            //   ChainDepth    — a term on the wish speed (§6.1), and §12's animation tell.
            //   AirJumpsUsed  — the one with teeth. MoveState says it is replicated "so a replay
            //                   can never re-grant a spent air jump"; that holds for the replay and
            //                   NOT for the early return, where without this line the counter was
            //                   never compared at all. Inert only while AirJumpMode ships at 0.
            //   VerbClockTicks / ChainTimerTicks — the clocks that decide WHEN the three above
            //                   change. Leaving them out would let a divergence sit unseen until it
            //                   surfaced as a verb one tick later, which is a correction deferred,
            //                   not avoided. They also carry VerbClockAirborne's 255 sentinel, on
            //                   which the whole touchdown tick depends.
            //
            // The cost, stated honestly: at a genuine settle-tick disagreement (the client's speed
            // and the server's straddling SlideExitSpeedMps within the 0.5 m/s velocity epsilon
            // this method already tolerates) one extra rewind fires. That rewind adopts the
            // authority and the disagreement is over — it is self-healing, it costs a correction of
            // under PredictionEpsilonM by construction, and it is folded into the render offset and
            // therefore invisible. An UNCORRECTED verb divergence is not self-healing and is the
            // failure this gate exists for.
            && predicted.Verb == auth.Verb
            && predicted.VerbClockTicks == auth.VerbClockTicks
            && predicted.ChainDepth == auth.ChainDepth
            && predicted.ChainTimerTicks == auth.ChainTimerTicks
            && predicted.AirJumpsUsed == auth.AirJumpsUsed
            && predicted.Position.DistanceSquaredTo(auth.Position) <= PredictionEpsilonM * PredictionEpsilonM
            && (predicted.Velocity - auth.Velocity).LengthSquared() <= VelocityEpsilon * VelocityEpsilon
            && Mathf.Abs(WrapAngle(predicted.Yaw - auth.Yaw)) <= YawEpsilonRad;
    }

    private static float WrapAngle(float radians) =>
        Mathf.PosMod(radians + Mathf.Pi, Mathf.Tau) - Mathf.Pi;

    private void ApplyStateToNode(in MoveState s)
    {
        Position = s.Position;
        Rotation = new Vector3(0, s.Yaw, 0);
        Velocity = s.Velocity;
    }

    // --- Server: the authority ----------------------------------------------------------
    private void ServerTick()
    {
        _serverTick++;
        // Authoritative encumbrance is derived from server-owned holder state (PropManager),
        // never from the client-reported e.SpeedFactor in the input packet - a doctored client
        // can claim any value there, but this is the only place the server actually simulates
        // movement (see RISK-AUDIT-2026-07-12.md 4.1). The client keeps computing/sending its own
        // e.SpeedFactor for LOCAL PREDICTION ONLY (InputRing / Reconcile's replay loop, both
        // untouched by this change) - the server's value below always wins at reconciliation,
        // exactly like every other movement input in this codebase.
        // See the matching comment in OwnerTick, which must compute the identical number from
        // the identical replicated holder state.
        float carrySpeedFactor = Props != null
            ? CarryController.ComputeSpeedFactor(Props.CarriedMassKgFor(_ownerPeerId))
            : 1f;
        foreach (NetCodec.InputEntry e in _serverQueue!.TakeForTick())
        {
            // Aim substrate (WP-L3): same "step aim first, using the velocity carried in from
            // the previous tick/entry, THEN fold its SpeedFactor into this entry's movement
            // step" ordering OwnerTick uses — see that method's comment for the determinism
            // argument (no client-reported aim speed factor is ever trusted; this is the only
            // place the server actually derives it, exactly like carrySpeedFactor above).
            _aim.Step(AvatarMotor.TickDelta, e.Intent.AimRaise, HorizontalSpeed(_state.Velocity));
            AimYaw = e.Intent.AimYaw;
            AimPitch = e.Intent.AimPitch;
            float authoritativeSpeedFactor = carrySpeedFactor * _aim.SpeedFactor;
            _state = AvatarMotor.Step(this, _state, e.Intent, authoritativeSpeedFactor,
                AvatarMotor.TickDelta, out _);

            // Latch a genuine Interact rising edge for the assist verbs (see
            // TakeServerInteractEdge). Derived here, inside the loop over consumed entries, so it
            // sees every input the authority actually simulated rather than only the last one of
            // a burst — a press-and-release inside a single redundant packet would otherwise be
            // invisible to the server. Sticky until consumed: the service ticks after this and
            // takes it exactly once.
            if (e.Intent.Interact && !_serverInteractHeldPrev)
                _serverInteractEdge = true;
            _serverInteractHeldPrev = e.Intent.Interact;
        }
        AimStance = _aim.Stance;
        AimSteadiness01 = _aim.Steadiness01;
        Grounded = _state.Grounded;

        // Out-of-bounds recovery: fell through a gap, off a ledge past the world's edge, or any
        // other way off the play space — reuse the exact same kill-plane every networked prop's
        // loose loop honors (Carryable.KillPlaneY), so "out of bounds" means one thing everywhere.
        // Same mechanism as the commanded pause-menu reset (epoch bump -> every peer snaps); also
        // forces the aim rig down (ForceAimLoweredOnTeleport, inside DoServerReset).
        if (_state.Position.Y < Carryable.KillPlaneY)
            DoServerReset();

        if (++_sinceSnapshot < SnapshotIntervalTicks)
            return;
        _sinceSnapshot = 0;
        byte[] packet = NetCodec.PackSnapshot(
            new NetCodec.Snapshot(_serverTick, _epoch, _serverQueue.LastConsumedSeq, _state,
                _aim.Stance, _aim.SteadyElapsedSec));
        Rpc(MethodName.ReceiveSnapshot, packet);
    }

    private void DoServerReset() => DoServerTeleport(_spawnPosition);

    private void DoServerTeleport(Vector3 target)
    {
        _state = SpawnStatePreservingWaterFlags(target);
        _epoch++;
        ApplyStateToNode(_state);
        ForceAimLoweredOnTeleport();
    }

    /// <summary>
    /// A fresh spawn state that keeps the two server-owned water flags (W2, lake-water contract
    /// §6/§7). Every other field is deliberately wiped — a teleport is a clean slate for
    /// velocity, forgiveness timers and water state (which recomputes from the new position on
    /// the very next step anyway).
    ///
    /// <b>Soaked and ControlLocked are the exceptions, and the lock is the load-bearing one.</b>
    /// The sputter-out's hard cut IS a teleport: wiping <c>ControlLocked</c> here would hand the
    /// player one tick of steering in the middle of a cut to black before <c>WaterService</c>
    /// re-asserted it on the next physics frame. One tick is enough to be a bug of exactly the
    /// shape MECHANICS-BIBLE §2 names, and "the service will fix it next frame" is how that class
    /// of defect ships. The service still owns both values; this only stops a teleport from
    /// silently un-owning them for a frame.
    /// </summary>
    private MoveState SpawnStatePreservingWaterFlags(Vector3 target)
    {
        MoveState fresh = MoveState.AtSpawn(target);
        fresh.Soaked = _state.Soaked;
        fresh.ControlLocked = _state.ControlLocked;
        // Incapacity and ImpulseRagdoll join the exception list for the identical reason, one
        // notch stronger: wiping them here would hand a knocked-out or frozen player a tick of
        // steering across any teleport that reaches them (the kill-plane recovery, a pause-menu
        // reset, the sputter-out's own hard cut — which is precisely the teleport that lands a
        // player on the shore about to be frozen). One tick of a controllable ice block is the
        // whole defect in miniature, and "the service re-asserts it next frame" is exactly how
        // that class of bug ships.
        fresh.Incapacity = _state.Incapacity;
        fresh.ImpulseRagdoll = _state.ImpulseRagdoll;
        return fresh;
    }

    /// <summary>Defined behaviour for "teleported while raised" (WP-L3 degenerate-transition
    /// requirement): every authoritative teleport (reset-to-spawn, a door, a TV portal) forces
    /// the aim rig down. A raised pose is meaningless across an instantaneous relocation — there
    /// is no "still aiming, just somewhere else" to preserve — and forcing Lowered here means
    /// the very Snapshot that carries the epoch bump ALSO already carries AimStance=Lowered, so
    /// Reconcile's teleported branch (auth.Epoch != _epoch) never needs its own special case:
    /// it just adopts whatever auth.AimStance says, which is already correct by construction.</summary>
    private void ForceAimLoweredOnTeleport()
    {
        _aim.AdoptAuthoritative(AimStance.Lowered, 0f);
        AimStance = _aim.Stance;
        AimSteadiness01 = _aim.Steadiness01;
    }

    /// <summary>Server-only: launch this avatar by adding velocity to its authoritative movement
    /// state. AvatarMotor.Step carries and decays it via MoveToward (Deceleration), so the player is
    /// shoved but keeps full input control the whole time — the "player never loses control" rule
    /// (see the class header). No epoch bump: the shove propagates through the normal snapshot +
    /// owner reconciliation path, same as any other authoritative velocity change. Offline: the
    /// sandbox demo has no MoveState loop, so the shove goes straight onto the node's own Velocity
    /// (OfflineTick reads it into prev next tick).</summary>
    public void ServerApplyKnockback(Vector3 velocity)
    {
        switch (_role)
        {
            case NetRole.ServerSim: _state.Velocity += velocity; break;
            case NetRole.Offline: Velocity += velocity; break;
        }
    }

    /// <summary>Server-only commanded teleport to an arbitrary point (the TV portals).
    /// Identical mechanism to reset-to-spawn: the authority moves the state and bumps
    /// the snapshot epoch, so the owner's prediction snaps to it cleanly (a commanded
    /// teleport is the expected result, not an error to smooth over) and every remote
    /// proxy jumps with it. Offline instances teleport directly instead.</summary>
    public void ServerTeleportTo(Vector3 position)
    {
        switch (_role)
        {
            case NetRole.ServerSim:
                _state = SpawnStatePreservingWaterFlags(position);
                _epoch++;
                ApplyStateToNode(_state);
                ForceAimLoweredOnTeleport();
                break;
            case NetRole.Offline:
                Velocity = Vector3.Zero;
                Position = position;
                _skidRemaining = 0f;
                _state.SkidRemaining = 0f;
                // MOVE-5: a teleport must not carry a verb, a chain or a spent air jump across it.
                // The networked roles get this for free — ServerTeleportTo rebuilds the whole state
                // from SpawnStatePreservingWaterFlags — so this clause is the offline path catching
                // up, exactly as the skid line above it is.
                _verb = MoveVerb.Normal;
                _verbClockTicks = 0;
                _chainDepth = 0;
                _chainTimerTicks = 0;
                _airJumpsUsed = 0;
                _state.Verb = MoveVerb.Normal;
                _state.VerbClockTicks = 0;
                _state.ChainDepth = 0;
                _state.ChainTimerTicks = 0;
                _state.AirJumpsUsed = 0;
                break;
        }
    }

    // --- Remote proxy: render the buffered authoritative trajectory ---------------------
    private void RemoteFrame(double delta)
    {
        if (_snapshots!.Sample(delta) is not SnapshotBuffer.RenderSample s)
            return; // nothing authoritative yet — hold at spawn pose

        // THE SHARED REPLICATED CLOCK (ANIM-M3). Only a remote proxy publishes, and it publishes the
        // INTERPOLATED render tick — the tick the body on screen is at. A predicted owner
        // deliberately does not: its clock runs ahead by half a round trip and by a different amount
        // on every machine, which is exactly what would make a "shared" clock unshared. See NetClock.
        Net.NetClock.Observe(_snapshots.RenderTick);

        if (_hasRendered && !s.Teleported)
        {
            float step = Position.DistanceTo(s.Position);
            if (step > _maxRenderStep)
                _maxRenderStep = step;
        }
        _hasRendered = true;

        Position = s.Position;
        Rotation = new Vector3(0, s.Yaw, 0);
        Grounded = s.Grounded;
        // Aim substrate (WP-L3): the discrete "nearest bracket" stance SnapshotBuffer already
        // resolved (see RenderSample.AimStance's doc comment) — this is what makes a proxy
        // avatar visibly raise/lower, not just the owner's own camera FOV.
        AimStance = s.AimStance;
        AnimateVisual(delta, GlobalTransform.Basis.Inverse() * s.Velocity, s.Grounded);
        // AFTER AnimateVisual, and it has to be: the gait is what plants the foot, so the plant
        // flag for this frame does not exist until the visual has been stepped. This is the call
        // that makes other players audible at all — see PlayFootstepCosmetic.
        //
        // No sprint flag on this path (it is not replicated), which is fine: the speed fallback
        // inside PlayFootstepCosmetic is the same one AvatarVisual's own run gait already uses for
        // remote proxies, so a sprinting teammate's steps read as running on every peer.
        PlayFootstepCosmetic(s.Grounded, HorizontalSpeed(s.Velocity), sprinting: false);
    }

    /// <summary>Test hook: hands a proxy an authoritative snapshot without a network, exactly as
    /// <c>ReceiveSnapshot</c> would after unpacking one. Same role as
    /// <see cref="TestInjectVisualError"/> — a headless self-test cannot stand up two peers to
    /// prove that a remote proxy makes a sound, and the alternative is proving it about a code
    /// path nothing exercises.</summary>
    public void TestFeedSnapshot(in NetCodec.Snapshot snap) => ApplySnapshot(snap);

    /// <summary>Peak frame-to-frame rendered movement since the last call (teleport
    /// epochs excluded). BotHarness logs it; the smoothness suite asserts remote
    /// avatars never visibly jump under latency/loss/jitter.</summary>
    public float TakeMaxRenderStep()
    {
        float peak = _maxRenderStep;
        _maxRenderStep = 0;
        return peak;
    }

    /// <summary><b>Commanded teleports this OWNER has applied since the last call, and how far the
    /// last one moved it</b> (INT-0, 2026-09-19). Take-and-reset, exactly like
    /// <see cref="TakeMaxRenderStep"/> above and for the same reason: a bot samples at about 5 Hz
    /// and <c>Reconcile</c> runs at 60, so a "last correction" read at sample time has already
    /// been overwritten by the ordinary sub-centimetre ones and a teleport leaves no trace in it.
    ///
    /// <para>Counted inside <c>Reconcile</c>'s <c>teleported</c> branch — where the epoch bump is
    /// actually CONSUMED — rather than inferred from a large position delta, which is the
    /// distinction the count exists to make. A room change that arrived as an ordinary big
    /// correction rather than as an epoch bump moves the body just as far and reads identically in
    /// every position log; it is also a body the owner interpolated into rather than snapped to,
    /// which is the stale-view failure one layer down. Server-sim and offline roles never
    /// reconcile, so this stays zero on them.</para></summary>
    public (int Count, float LastJumpM) TakeCommandedTeleports()
    {
        var taken = (_commandedTeleports, _lastTeleportJumpM);
        _commandedTeleports = 0;
        _lastTeleportJumpM = 0f;
        return taken;
    }

    // --- RPCs (movement rides its own unreliable ENet channel; see NetCodec) ------------

    /// <summary>Client → server: a redundancy window of sequenced inputs. AnyPeer so
    /// clients can call it, but the server only accepts it from this avatar's owner.</summary>
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable, TransferChannel = NetCodec.MoveChannel)]
    private void SubmitInput(byte[] packet)
    {
        if (_role != NetRole.ServerSim || Multiplayer.GetRemoteSenderId() != _ownerPeerId)
        {
            // A structurally dropped input stream (misrouted node, unconfigured server
            // instance) is a permanently frozen avatar — make it loud, once.
            if (!_inputRejectLogged)
            {
                _inputRejectLogged = true;
                Net.ServerLog.Warn("input stream rejected",
                    $"avatar={Name} role={_role} owner={_ownerPeerId} sender={Multiplayer.GetRemoteSenderId()}");
            }
            return;
        }
        if (NetCodec.UnpackInputs(packet) is { } entries)
            _serverQueue!.Enqueue(entries);
    }

    private bool _inputRejectLogged;

    /// <summary>Server → everyone: authoritative state. Authority mode — only the node's
    /// multiplayer authority (the server) can invoke it on us.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable, TransferChannel = NetCodec.MoveChannel)]
    private void ReceiveSnapshot(byte[] packet)
    {
        if (NetCodec.UnpackSnapshot(packet) is not NetCodec.Snapshot snap)
            return;
        if (NetSim.Instance is NetSim sim)
            sim.Queue(() =>
            {
                if (IsInstanceValid(this) && IsInsideTree())
                    ApplySnapshot(snap);
            });
        else
            ApplySnapshot(snap);
    }

    private void ApplySnapshot(in NetCodec.Snapshot snap)
    {
        switch (_role)
        {
            case NetRole.PredictedOwner:
                // Latest-wins: unreliable delivery may reorder; only the newest matters.
                if (_pendingAuth is not NetCodec.Snapshot held || snap.Tick > held.Tick)
                    _pendingAuth = snap;
                break;
            case NetRole.RemoteProxy:
                _snapshots!.Add(snap);
                // THE PROXY ADOPTION (MOVE-5e). A proxy never runs AvatarMotor, so its _state is
                // otherwise frozen at spawn forever — and _state is what the presentation layer
                // reads on EVERY role: AnimateVisual, SyncIncapacitySkin, UpdateNameplate, and the
                // VerbNow / ChainDepthNow seam the animation tells pose from.
                //
                // THIS USED TO BE A HAND-WRITTEN LIST AND THAT IS EXACTLY WHY IT WAS WRONG. Water,
                // Soaked and ControlLocked were adopted (W2); SkidRemaining was added one wave late
                // (SKID-1) after a teammate slid 2.8 m with no brake pose; Incapacity,
                // ImpulseRagdoll and all five MOVE-5 verb fields were never added at all. Every one
                // of those is the same defect — a field on the wire that never reaches the state
                // the pose reads — and a list only ever catches the instance somebody remembered.
                //
                // MoveState.AdoptForProxy inverts the default: it adopts the whole authoritative
                // state and hands back only the four render-interpolated fields (Position,
                // Velocity, Yaw, Grounded), which SnapshotBuffer already resolves at the render
                // tick and RemoteFrame is the single writer of. A field added to MoveState tomorrow
                // reaches proxies with no edit here. See that function for the full argument and
                // for why the adopted facts deliberately lead the interpolated body.
                _state = MoveState.AdoptForProxy(_state, snap.State);
                break;
        }
    }

    /// <summary>Owner → server: please teleport me home (pause-menu Reset Position).
    /// Server-validated and rate-limited; lands on everyone via the epoch bump.</summary>
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    private void RequestResetToSpawn()
    {
        if (_role != NetRole.ServerSim || Multiplayer.GetRemoteSenderId() != _ownerPeerId)
            return;
        ulong now = Time.GetTicksMsec();
        if (now - _lastResetMsec < ResetCooldownMsec)
            return;
        _lastResetMsec = now;
        DoServerReset();
    }

    private static float HorizontalSpeed(Vector3 v) => Mathf.Sqrt(v.X * v.X + v.Z * v.Z);

    /// <summary>Drives the cosmetic layer for one rendered frame: sets the carry pose from the
    /// authoritative "am I holding" signal, animates the body, then rides the carry anchor on the
    /// waddle bob. Every rendering role (offline, predicted owner, remote proxy) funnels through
    /// here, so the pose and the held-item bob look identical for the local player and every remote
    /// holder. Visual-only — never touches simulation, input, or the network.</summary>
    private void AnimateVisual(double delta, Vector3 localVelocity, bool grounded, bool sprinting = false)
    {
        _visual.SetCarry(CarryPoseForVisual());
        // Keyed on DIRECTION (Raising/Raised -> aiming pose), not "!= Lowered" — matches the
        // camcorder branch's FOV-keying fix (feat/tier0-camcorder, Task 4 review Finding 4):
        // recovery begins the instant Lowering starts, symmetric with engagement beginning the
        // instant Raising starts, instead of holding the raised pose through the whole Lowering
        // transition. Every rendering role funnels through here (see class doc above), so a
        // remote proxy's pose keys off the SAME replicated AimStance (SnapshotBuffer.
        // RenderSample.AimStance) the owner's own pose keys off locally.
        _visual.SetAiming(AimStance is AimStance.Raising or AimStance.Raised);
        // Soaked (W2, lake-water contract §7). Driven from the replicated MoveState here rather
        // than from a WaterService event for the same reason the carry pose is: every rendering
        // role funnels through this one method, so the owner, every remote proxy and the offline
        // sandbox all get the wet look from the identical signal. SetSoaked is a no-op when the
        // value is unchanged, so this costs a bool compare per frame.
        _visual.SetSoaked(_state.Soaked);
        // The turnaround skid (SKID-1). Driven from the replicated MoveState for the identical
        // reason the two lines above are: every rendering role funnels through this method, so the
        // owner, every remote proxy and the offline sandbox all take the brake pose from the one
        // signal the motor actually simulated — never from a second, local guess about whether a
        // body looks like it is sliding.
        _visual.SetSkidding(AvatarMotor.IsSkidding(_state), _state.SkidRemaining);
        // THE CROUCH VERBS AND THE CHAIN JUMP (MOVE-5c, spec §12). Driven from the replicated
        // MoveState for the identical reason the four lines above are — and it matters more here
        // than anywhere else in this method. Spec §6.6 forbids any UI for the chain, in any build,
        // including the lab; §12's pose is the ONLY way a co-op partner learns that a teammate's
        // chain is deepening, and a partner's body is always a RemoteProxy. A pose driven from
        // anything but the replicated state would leave that partner reading a locally invented one.
        _visual.SetVerb(_state.Verb, _state.ChainDepth);
        // THE REPLICATED CLOCK (ANIM-M3). The clip layer anchors a one-shot's time to
        // (now − entryTick) in the SERVER's tick clock, so it needs the tick this role is actually
        // rendering — see RenderTickNow. Presentation only, like every other line in this method.
        _visual.SetNetworkTick(RenderTickNow());
        // Animation LOD (ANIM-M3). Distance to whichever camera this client is looking through, and
        // never applied to the body this client is controlling — see AvatarAnimationLod.
        if (GetViewport()?.GetCamera3D() is { } lodCamera)
        {
            _visual.SetAnimationLod(
                lodCamera.GlobalPosition.DistanceTo(GlobalPosition),
                isLocalPlayer: _role != NetRole.RemoteProxy);
        }
        _visual.Animate(delta, localVelocity, grounded, sprinting);
        if (IsInstanceValid(_carryAnchor))
            // NO _visual.Position TERM ANY MORE (ANIM-1). The anchor is a child of the visual's pose
            // node, so the local-space reconciliation offset — and the lean, roll, hop and fidget it
            // used to miss entirely — arrive by parentage. Adding it here as well would apply it
            // twice. What remains is the small extra HAND motion the body itself does not provide:
            // the carry bob (halved, see AvatarVisual.CarryBobHeight).
            _carryAnchor.Position = _carryAnchorRest + _visual.CarryBobOffset;
    }

    /// <summary>
    /// <b>The tick this role is RENDERING, in the server's own clock.</b> One number, resolved four
    /// ways, because "now" is genuinely a different quantity per role and pretending otherwise is
    /// how a clip-time anchor ends up off by the interpolation delay on every remote body:
    /// <list type="bullet">
    /// <item><b>Server</b> — its own <c>_serverTick</c>, which is the clock everyone else is
    /// reading.</item>
    /// <item><b>Predicted owner</b> — the last authoritative tick plus the ticks predicted since,
    /// so the owner's animation runs on the same clock as the position it is predicting.</item>
    /// <item><b>Remote proxy</b> — <c>SnapshotBuffer.RenderTick</c>, the interpolated clock, which is
    /// the tick the BODY ON SCREEN is at. Using the newest received tick instead would put the pose
    /// ahead of the position by <c>SnapshotBuffer.InterpDelayTicks</c>.</item>
    /// <item><b>Offline</b> — a local counter. There is no server, so this is the server.</item>
    /// </list>
    ///
    /// <para><b>Nothing here is on the wire and nothing here decides anything.</b> The tick already
    /// rides every snapshot; this method only says which of the ticks a client already has is the one
    /// its own renderer is looking at.</para>
    /// </summary>
    private double RenderTickNow() => _role switch
    {
        NetRole.RemoteProxy => _snapshots?.RenderTick ?? -1.0,
        NetRole.ServerSim => _serverTick,
        NetRole.PredictedOwner => _ownerRenderTick,
        _ => _offlineTick,
    };

    /// <summary>The predicted owner's estimate of the server tick it is rendering: reset to each
    /// authoritative snapshot's tick on reconcile, then incremented once per simulated tick.</summary>
    private double _ownerRenderTick;

    /// <summary>The offline sandbox's own tick counter. No server exists, so this is the clock.</summary>
    private double _offlineTick;

    /// <summary>
    /// <b>Which of the two carries this avatar is in</b>, for the pose only — read from whichever
    /// source is authoritative for its role. Networked: the replicated <see cref="NetworkedProp"/>
    /// holder id via <see cref="PropManager.FindHeldBy"/>, set on EVERY peer, so a remote holder
    /// poses too and poses the SAME way. Offline sandbox: the local CarryController. Never feeds
    /// gameplay — a purely cosmetic read, and it adds nothing to the wire (CARRY-1: the holder
    /// state already replicates).
    /// </summary>
    private CarryPose CarryPoseForVisual()
    {
        // WHICH carry, decided by WHAT is in the hand (W7-8, Talon's note 4). The hand can hold
        // either a handle or a bulky handle-less thing (a golden cube), and posing the second as
        // the first is what put the wrist 0.22 m inside the cube. PropManager.IsArmfulPose is the
        // pose predicate — see its doc.
        //
        // Kind comes off the REPLICATED prop node, which every peer has (PropManager.FindHeldBy
        // reads the replicated held-by registers), so a remote holder poses the same way the owner does.
        if (Props?.FindHeldBy(_ownerPeerId) is { } heldProp)
            return PropManager.IsArmfulPose(heldProp.Kind) ? CarryPose.Armful : CarryPose.Handle;
        // Offline sandbox: no PropManager, so the local CarryController is the authority. Carryable
        // .Shape mirrors PropKind ordinal-for-ordinal (Carryable.Shape's own doc), so one cast maps
        // it onto the same predicate rather than duplicating the kind list.
        if (Carry.Held is Carryable localHeld)
            return PropManager.IsArmfulPose((PropKind)(int)localHeld.Kind)
                ? CarryPose.Armful : CarryPose.Handle;
        if (Carry.Held != null)
            return CarryPose.Handle;
        return CarryPose.None;
    }

    /// <summary><b>The carry pose this peer resolves for this avatar right now</b>, for
    /// instrumentation — <see cref="CarryPoseForVisual"/> made readable without rendering.
    ///
    /// <para><b>Why it exists</b> (W7-8): "the pose is the same on every peer" is only a claim
    /// until a peer other than the owner can be asked. The computation is a pure read of
    /// replicated state (the held-by registers and the prop's kind), so it answers correctly on a
    /// headless observer too — which is what lets <c>Run-ArmfulCarryTest.ps1</c> prove parity from
    /// a witness bot's own log rather than from the holder's local view. Modelled on
    /// <c>AimStance</c>'s instrumentation, for the same reason and in the same JSONL.</para></summary>
    public CarryPose CarryPoseNow => CarryPoseForVisual();

    // --- Presentation (consumed once per first-time-simulated tick; never on replay) -----
    private void PlayStepCosmetics(in StepEvents ev, bool groundedNow, float horizontalSpeed, float dt, bool sprinting)
    {
        if (ev.Jumped)
        {
            // NO VISUAL TRIGGER ANY MORE (JUMP-1). The take-off pose is derived inside
            // AvatarVisual.Animate from the Grounded edge and Velocity.Y, which every rendering
            // role has — this method is owner/offline only, so a trigger fired here was a jump a
            // teammate never saw the body react to. The sound stays here: it is an EVENT, it fires
            // once, and it has no remote path of its own.
            ActorFx.Fire(GetParent(), Profile, ActorEvent.Jump, GlobalPosition);
        }

        PlayFootstepCosmetic(groundedNow, horizontalSpeed, sprinting);

        if (ev.BumpImpact >= BumpSoundThreshold && _bumpSoundCooldown <= 0)
        {
            // Cosmetic only: squish + bonk. The cooldown is event-GENERATION debounce
            // ("was this a real bump"), so it stays here, not in the profile.
            _bumpSoundCooldown = BumpSoundCooldownSec;
            _visual.TriggerBumpSquish();
            ActorFx.Fire(GetParent(), Profile, ActorEvent.Bump, ev.BumpPosition);
        }

        _maxFallSpeed = Mathf.Max(_maxFallSpeed, ev.FallSpeed);
        if (groundedNow && !_wasGrounded)
        {
            float fall = _maxFallSpeed;
            _maxFallSpeed = 0;
            // The gate and the scale are AvatarVisual's constants now (JUMP-1) rather than two
            // literals here — the landing absorb is derived from the same fall speed, and the pose
            // and the sound disagreeing about what counts as a landing is the "some system was
            // never told" shape. The absorb itself is NOT triggered from here: see the Jumped
            // branch above for why an owner-only path cannot own a pose.
            if (fall >= AvatarVisual.LandMinFallMps)
            {
                // ONE CURVE, not two (MOVE-4c, spec §5.2). This used to clamp to [0, 1] while
                // AvatarVisual's absorb clamped the same quantity off the same gate to
                // [LandMinIntensity, 1] — one gate, one fall speed, two intensities. See
                // AvatarVisual.LandIntensityFor for which floor is right and for what unifying
                // actually moves: at most 0.286 dB on land_pad below 3.5 m/s, and no response
                // changes whether it fires.
                float intensity = AvatarVisual.LandIntensityFor(fall);
                // One event; the profile fans it out (pad + squeak always, dust gated
                // by MinIntensity = the old fall > 6 threshold as 6/14).
                ActorFx.Fire(GetParent(), Profile, ActorEvent.Land,
                    GlobalPosition + new Vector3(0, 0.05f, 0), intensity);
            }

            // THE CAMERA IS THE THIRD CHANNEL — and since MOVE-4f (Talon's choice C, 2026-08-27)
            // it is OUTSIDE the gate above, on purpose. MOVE-4e measured the gate's premise and
            // falsified it: LandMinFallMps of 2.5 m/s is a 0.105 m fall, the jog tap lands at
            // 5.27 and a held jump at ~9.5, so a gate meant to spare a hop chain from a 2-4 Hz
            // camera oscillation was in fact letting every hop through. The dip's amplitude ramps
            // continuously from zero in the fall speed instead (SandboxCamera.DipIntensityFor), so
            // a tap's dip is imperceptible by construction rather than by a threshold, and the
            // knee absorb and the landing sound keep their own gate exactly where it was.
            //
            // The RAW fall speed is what crosses, not AvatarVisual's gated intensity: the dip's
            // curve has no legibility floor and must not inherit one. Client-local presentation —
            // _followCamera is the camera attached to the body this machine drives, so no remote
            // peer's view is touched and nothing here crosses the wire. At the shipped
            // CameraDipStrengthM of 0.00 the call is an exact no-op at every fall speed.
            _followCamera?.NotifyLanding(fall);
        }

        _bumpSoundCooldown -= dt;
        _wasGrounded = groundedNow;
    }

    /// <summary>The footfall half of the cosmetics, split out because it is the ONE piece every
    /// rendering role shares — including <see cref="NetRole.RemoteProxy"/>, which has no
    /// <see cref="StepEvents"/> to speak of (it runs no motor) but does animate a real gait off
    /// the replicated velocity and therefore does plant real feet.
    ///
    /// <b>Until 2026-08-13 a remote proxy planted feet in total silence.</b> Every other peer's
    /// footsteps simply did not exist: <c>ActorEvent.Step</c> was fired from
    /// <see cref="OfflineTick"/> and <see cref="OwnerTick"/> only, so in a six-player session each
    /// player heard exactly one set of footsteps — their own. Canon item 4 says that away from
    /// light "sound becomes the information channel", and this was that channel missing its most
    /// common signal. Calling this from <see cref="RemoteFrame"/> is the fix.
    ///
    /// Fired on the ACTUAL animated footfall (see <c>AvatarVisual.ConsumeFootPlant</c>) rather
    /// than on distance walked, so the sound lands with the visible foot on every role.
    /// Intensity 1 = running (the profile boosts pad/squeak volume), 0 = walking; a proxy gets no
    /// sprint flag, so it takes the speed fallback <c>AvatarVisual</c>'s gait already uses for the
    /// same reason.
    ///
    /// Every footfall is routed past <see cref="FootstepAudioDirector"/> — including the local
    /// player's, which wins the nearest rank by construction rather than by an exemption. Read
    /// that class for why the cap is not optional: six players' worth of uncapped footsteps is 12
    /// of the 14 one-shot slots, and the slots they would take are the ones every other one-shot
    /// lives in.</summary>
    private void PlayFootstepCosmetic(bool groundedNow, float horizontalSpeed, bool sprinting)
    {
        bool stepping = groundedNow && horizontalSpeed > FootstepMinSpeed;
        // Published every frame, before any early-out: the director ranks the whole lobby off
        // this flag, and an avatar that only updated it on the frames it happened to plant would
        // hold a slot through every silent frame in between.
        StepAudioCandidate = stepping;

        // Consume unconditionally. The flag latches until read, so an early-out that skipped the
        // read would bank a plant and fire it on some later, unrelated frame.
        bool planted = _visual.ConsumeFootPlant();
        if (!planted || !stepping)
            return;

        FootstepAudioDirector.EnsureTicked(this);
        if (!FootstepAudioDirector.TryTakeStep(this, out float trimDb))
            return;

        if (Profile != null)
            FootstepAudioDirector.NoteShotsPerFootfall(Profile.ResponsesFor(ActorEvent.Step).Length);
        bool running = sprinting || horizontalSpeed > AvatarMotor.MoveSpeed * 1.05f;
        ActorFx.Fire(GetParent(), Profile, ActorEvent.Step, GlobalPosition, running ? 1f : 0f, trimDb);
    }

    // --- Footstep audibility, written by FootstepAudioDirector once per frame ------------------
    //
    // The same shape LoopingSfxEmitter.SetGranted/ShouldSound uses for fires: the director owns
    // the ranking, the emitter owns the sound, and the grant is a plain field rather than a query
    // so a footfall costs a bool read instead of a walk over the lobby.

    /// <summary>Whether this avatar is currently producing footfalls at all — grounded and above
    /// the walk threshold. A still avatar is not a candidate for a slot (see the director).</summary>
    internal bool StepAudioCandidate { get; private set; }

    /// <summary>Whether this avatar's next footfall may sound. Defaults TRUE: an avatar that has
    /// never been ranked (no director tick yet, an offline lab with no camera) is audible, because
    /// the failure mode of a wrong default here is a silent world, and the failure mode of the
    /// other default is a slot spent.</summary>
    internal bool StepAudioGranted { get; private set; } = true;

    /// <summary>dB to subtract from the profile's own step volume for distance — 0 near, exactly
    /// <c>SfxLab.SilentDb</c> at the cull horizon.</summary>
    internal float StepAudioTrimDb { get; private set; }

    internal void SetStepAudioGrant(bool granted, float trimDb)
    {
        StepAudioGranted = granted;
        StepAudioTrimDb = trimDb;
    }

    /// <summary>
    /// Picks up the nearest free carryable within reach, or drops the held item.
    /// Public so tests (and later the net layer) can invoke the exact same path as
    /// the local player's Interact key.
    /// </summary>
    public bool InteractPickDrop()
    {
        if (Carry.Held != null)
            return Carry.Drop();

        Carryable? nearest = FindNearestCarryable();
        return nearest != null && Carry.TryPickUp(nearest);
    }

    /// <summary>Throws the held item along current facing. Same path as the Throw key.</summary>
    public bool ThrowHeld()
    {
        Vector3 forward = -GlobalTransform.Basis.Z;
        return Carry.Throw(forward * ThrowForwardSpeed + Vector3.Up * ThrowUpSpeed);
    }

    /// <summary>Previous tick's raw <see cref="MoveIntent.Interact"/> value — lets
    /// <see cref="HandleCarryIntent"/> defensively re-derive a genuine rising edge instead of
    /// trusting every possible <see cref="IIntentSource"/> to honor MoveIntent.Interact's own
    /// documented "edge, not level" contract. Real keyboard input already honors it
    /// (LocalInputIntentSource samples Input.IsActionJustPressed), but nothing enforced it for
    /// every source — see the edge-detection note on <see cref="HandleCarryIntent"/> itself for
    /// the incident that proved it needed enforcing.</summary>
    private bool _interactHeldPrev;

    /// <summary>The general interaction seam: first refusal to <see cref="InteractOverride"/>
    /// (a pressable world control), then <see cref="WorldInteract"/> (anything that asks the
    /// server), then carry grab/drop. Every future interactable plugs into
    /// WorldInteract/InteractOverride the same way, so this method — not any one interactable's
    /// own manager — is where "one key press means one request" has to be guaranteed.
    ///
    /// <b>Edge-detection, not just trust.</b> <c>intent.Interact</c> is documented "edge, not
    /// level" (see <see cref="MoveIntent.Interact"/>), and real human input already honors that.
    /// But this method is fed by ANY <see cref="IIntentSource"/> — including scripted test bots
    /// — and nothing upstream enforced the contract. A spam-idempotency bot once held Interact
    /// continuously for a multi-second burst (a level signal), and because this method re-invoked
    /// <see cref="WorldInteract"/> on every physics tick that read true, a 2s held press queued
    /// 100+ reliable, ordered RPCs — a backlog that took longer to drain than the window it was
    /// generated in, so that test's own convergence check read stale (PR #128 review).
    ///
    /// <c>interactEdge</c> below re-derives a true rising edge from <c>_interactHeldPrev</c>
    /// regardless of what the source produced, so WorldInteract/InteractOverride/grab/drop each
    /// fire AT MOST ONCE per genuine press-and-release, no matter how long Interact reads true.
    /// Fixed HERE so every WorldInteract consumer inherits it without rediscovering this
    /// incident.</summary>
    private void HandleCarryIntent(MoveIntent intent)
    {
        bool interactEdge = intent.Interact && !_interactHeldPrev;
        _interactHeldPrev = intent.Interact;

        // Interactives get first refusal on the interact key (see InteractOverride).
        if (interactEdge && InteractOverride != null && InteractOverride())
            return;

        // (THE DOWNED-TEAMMATE ASSIST that used to outrank every other use of this key went with
        // the incapacitation service at the fork - BASE-1, 2026-09-19. The shape it had is the
        // one to copy for any verb that competes with carry for this key, and BTN-1's button
        // press is exactly that: the server resolved the assist itself from the Interact edge it
        // latched out of the authoritative input stream, and THIS branch was only the owning
        // client agreeing not to ALSO fire a grab for the same press. Both sides ran the
        // identical pure query over identical replicated facts, so they could not reach different
        // answers - which is the only thing that makes a client-side prediction of a server rule
        // safe. Two answers to "what did that press do" is the defect.)

        if (_role == NetRole.PredictedOwner && Props != null)
        {
            // Networked: never mutate local state — request the server, and only attach/detach
            // once ApplyPropState confirms it (no grab prediction; matches "never lose control").
            if (interactEdge)
            {
                // The world outranks carrying: standing at a control and pressing Interact means
                // "use it", even with full hands.
                if (WorldInteract?.Invoke() == true)
                    return;

                // THE INTERACT RULE, in the order the player experiences it:
                //
                //   something in reach  -> ASK FOR IT. The server fills the hand, or swaps it for
                //                          whatever is in it. One request either way, because a
                //                          swap the client split into drop-then-grab would have a
                //                          window where the player holds nothing and someone else
                //                          can take the thing they just put down.
                //   nothing in reach    -> DROP what is in your hand.
                //
                // E still puts things down, while E-near-a-thing always means "take that thing".
                Carryable? nearest = FindNearestCarryable();
                NetworkedProp? np = nearest?.GetParentOrNull<NetworkedProp>();
                if (np != null)
                {
                    Props.ClientRequestGrab(np.PropId);
                    return;
                }

                // NOTHING FREE IN REACH — but is something in reach that simply isn't available?
                //
                // This branch exists because of a bug the two-slot suite caught on its first
                // green-enough run, and it is worth stating plainly because the shape recurs:
                // two players walked to the same prop and pressed E in the same tick. One won. The
                // OTHER fell straight through to the drop branch below — because a prop held by
                // someone else is not a "free carryable" and FindNearestCarryable stops seeing it
                // — and put its own held item on the ground. One press, the item lost, for a
                // reason no player could ever reconstruct.
                //
                // So: if the thing you pressed E at is in reach but taken, ASK FOR IT ANYWAY and
                // let the server refuse. That costs one reliable RPC, routes through the existing
                // first-grab-wins arbitration, and produces the existing GrabDenied cue
                // (INTERACTION-BIBLE 2 — a refusal the player can perceive) instead of a silent
                // no-op OR a catastrophic drop. Deliberately checked only AFTER the free-candidate
                // pick, so a teammate walking past with a crate can never steal the press from
                // the loose item you were actually reaching for.
                NetworkedProp? taken = FindNearestUnavailableCarryable();
                if (taken != null)
                {
                    Props.ClientRequestGrab(taken.PropId);
                    return;
                }

                // Genuinely nothing to act on: E means "put down what is in my hand".
                // "Am I holding?" reads live server-derived state rather than duplicating it in a
                // local field.
                if (Props.FindHeldBy(_ownerPeerId) != null)
                    Props.ClientRequestDrop();
            }
            else if (intent.Throw && Props.FindHeldBy(_ownerPeerId) != null)
            {
                // Never mutate local state — request the server, same "no prediction" rule as
                // grab/drop; the throw only actually happens once ApplyPropState confirms it.
                Props.ClientRequestThrow();
            }
            return;
        }

        // Offline/sandbox path unchanged: mutate Carry directly, no server involved.
        if (interactEdge)
            InteractPickDrop();
        else if (intent.Throw && Carry.Held != null)
            ThrowHeld();
    }

    /// <summary>What the fire button does when a <c>PropKind</c> key is the item in this avatar's
    /// hand — populated by <see cref="RegisterFireHandler"/>, read by
    /// <see cref="HandleFireIntent"/>. Neither method touches this dictionary's contents directly
    /// beyond registering/reading it: that indirection is the whole point (see
    /// <see cref="RegisterFireHandler"/>'s own doc).</summary>
    private readonly System.Collections.Generic.Dictionary<PropKind, System.Action> _fireHandlers = new();

    /// <summary>
    /// Declares what the fire button does while <paramref name="kind"/> is the item in the hand.
    /// P3a, 2026-08-08: replaces a hardcoded "if fire, ask the one verb" branch that fired
    /// regardless of what was in hand, so two tool verbs fought over one mouse button the first
    /// time a player carried both. Gameplay calls this once per tool verb, at the same wiring site
    /// it already sets <see cref="Props"/> — a new verb registers its own entry and never has to
    /// edit <see cref="HandleFireIntent"/> again. Nothing in the MVP build registers one; the
    /// router stays so the next verb has somewhere to plug in.
    /// </summary>
    public void RegisterFireHandler(PropKind kind, System.Action handler) => _fireHandlers[kind] = handler;

    /// <summary>Owner-only funnel for the fire button — every tool verb registered via
    /// <see cref="RegisterFireHandler"/>. Routes by the held item's <c>PropKind</c>, resolved
    /// fresh on every edge — never a hardcoded "if this, else if that" chain (INTERACTION-BIBLE
    /// 9.1: effect is a function of (verb, target_type), resolved at use time, one dispatch
    /// servicing many verbs).
    ///
    /// Same "request the server, never resolve locally" rule <see cref="HandleCarryIntent"/>
    /// already follows for grab/drop/throw — every registered handler is itself a client-request
    /// funnel, never a local resolution, for the identical reason: what is in the hand, and what
    /// firing it does, are both server-authoritative facts a client could otherwise lie about.
    ///
    /// No-op when this avatar has no networked Props funnel wired (offline sandbox, remote
    /// proxies, server-side instances that never read local input) or when the hand holds nothing
    /// this dispatch has a handler for. Holding nothing, or an item with no registered verb (a
    /// log), does nothing mechanically and says so only at the log tier (the debug print below) —
    /// not a new player-facing UI channel, since no verb was ever engaged for one to describe
    /// (INTERACTION-BIBLE 2 governs a triggered interaction going silent, not a press with no
    /// verb behind it to trigger).</summary>
    private void HandleFireIntent(MoveIntent intent)
    {
        if (!intent.Fire || Props == null)
            return;
        PropKind? kind = Props.FindHeldBy(_ownerPeerId)?.Kind;
        if (kind is PropKind k && _fireHandlers.TryGetValue(k, out System.Action? handler))
            handler();
        else
            GD.Print($"[fire] no verb registered for the held item (kind={(kind.HasValue ? kind.Value.ToString() : "empty")})");
    }

    /// <summary>The free carryable in reach that Interact would grab (also what the
    /// highlight poll shimmers). Aim-aware when <see cref="AimCamera"/> is set, so the
    /// grabbed thing is the looked-at thing, not merely the nearest one.</summary>
    public Carryable? FindNearestCarryable()
    {
        var candidates = new System.Collections.Generic.List<InteractTargeting.Candidate>();
        foreach (Node node in GetTree().GetNodesInGroup(Carryable.Group))
        {
            if (node is Carryable c && !c.IsHeld)
                candidates.Add(new InteractTargeting.Candidate(c, c.GlobalPosition, PickupRadius));
        }
        return InteractTargeting.Pick(candidates, GlobalPosition, AimCamera) as Carryable;
    }

    /// <summary>
    /// The nearest carryable within the INTENT radius that is held by somebody else — the thing
    /// the player was plainly reaching for but cannot have. Null when there is nothing of the
    /// kind nearby. Never returns this avatar's own carried items (pressing E at your own held
    /// item is not a grab attempt).
    ///
    /// <b>Why a WIDER radius than <see cref="PickupRadius"/>, and why this method exists at all.</b>
    /// Two players walked to the same item and pressed E within a tick of each other. The winner
    /// took it; on the loser's screen the item stopped being a free carryable AND swung into the
    /// winner's hands, so by the time the loser's press resolved there was nothing in reach at
    /// all — and E fell through to its drop fallback and put the loser's held item on the ground.
    /// One press, the night verb lost, for a reason no player could reconstruct. Measured on a
    /// real run: the item ended up 1.52 m away, just past the 1.5 m grab reach, so a same-radius
    /// guard did not fire either.
    ///
    /// The general rule this enforces is worth more than the case that produced it: <b>a single
    /// key must not have a destructive meaning that fires whenever its constructive meaning loses
    /// a race.</b> Within the intent radius E is ALWAYS an acquisition attempt, answered by the
    /// server with a perceivable refusal (INTERACTION-BIBLE 2) — never by silently discarding
    /// what you were carrying. Only genuinely open ground restores E's put-down meaning.
    ///
    /// Same <see cref="InteractTargeting"/> rule as the free-candidate pick so the two agree
    /// about direction, and checked strictly AFTER it so a teammate carrying a crate past you can
    /// never steal the press from the loose item you were actually reaching for.
    /// </summary>
    public NetworkedProp? FindNearestUnavailableCarryable()
    {
        var candidates = new System.Collections.Generic.List<InteractTargeting.Candidate>();
        foreach (Node node in GetTree().GetNodesInGroup(Carryable.Group))
        {
            if (node is not Carryable c || !c.IsHeld)
                continue;
            if (c.GetParentOrNull<NetworkedProp>() is not { } prop || prop.HolderPeerId == _ownerPeerId)
                continue;
            candidates.Add(new InteractTargeting.Candidate(prop, c.GlobalPosition, GrabIntentRadius));
        }
        return InteractTargeting.Pick(candidates, GlobalPosition, AimCamera) as NetworkedProp;
    }

    /// <summary>The fixed world control in reach that Interact would press (BT-8's reset lever
    /// and anything else that joins <see cref="IPressable.Group"/>) — same aim-aware pick as its
    /// siblings above, with the range read from the candidate rather than from a constant here,
    /// because this one is not a single named type.</summary>
    public IPressable? FindNearestPressable()
    {
        var candidates = new System.Collections.Generic.List<InteractTargeting.Candidate>();
        foreach (Node node in GetTree().GetNodesInGroup(IPressable.Group))
        {
            if (node is IPressable p)
                candidates.Add(new InteractTargeting.Candidate(p, p.GlobalPosition, p.PressRadiusM));
        }
        return InteractTargeting.Pick(candidates, GlobalPosition, AimCamera) as IPressable;
    }
}
