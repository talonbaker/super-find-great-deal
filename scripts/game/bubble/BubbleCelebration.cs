using Godot;
using MpFoundation;
using MpFoundation.Game.Sandbox;

namespace Sail.Game.Bubble;

/// <summary>
/// <b>The level noticing that the last bubble is gone.</b> CELEBRATE-1, 2026-09-04, closing
/// Talon's ask at the end of the playtest — <i>"when the player collects all the bubbles, when
/// they get 100 out of 100, I would like a small sound to play like a triumph sound and ... maybe
/// do something also special to let them know they're special."</i>
///
/// <para><b>Nothing here decides anything.</b> Completion is adjudicated by
/// <see cref="BubbleCounter"/> on the server and arrives as one payload-free broadcast; this
/// class is the receiving end, and every line of it is presentation. It writes no state, touches
/// no bit of <see cref="BubbleCounterState"/>, persists nothing, and unlocks nothing — see the
/// achievement note at the bottom of this doc.</para>
///
/// <para><b>THE STANDING CONSTRAINT, which is the actual specification.</b> Talon reverts "more
/// juice"; the durable preference is restraint. So the beat is bounded before it is designed: it
/// takes no control from the player, blocks and swallows no input, moves and locks no camera,
/// freezes nothing, covers no part of the screen, and is over in
/// <see cref="TotalSeconds"/>. It is the game NOTICING, not the game interrupting. Everything it
/// is made of already shipped — a synthesised one-shot (<see cref="Sfx.Triumph"/>), the puff
/// particles the pop already uses (<c>JuiceFx</c>), and the phase-toast line
/// (<c>PhaseToastLayer</c>). <b>No new art or audio asset.</b></para>
///
/// <para><b>The three channels, and why three.</b> INTERACTION-BIBLE §8.1 scope for this
/// consequence is <c>all_players</c>, declared rather than arrived at: the tally is one shared,
/// server-adjudicated number, so its completion is a fact about the whole session and not about
/// whoever's collider happened to touch the last sphere. §8.2's redundant-channel rule then
/// applies with force — an <c>all_players</c> consequence carried only by audio is invisible to a
/// player whose audio this game deliberately severs — so the sound is backed by a world-visible
/// cue (the puffs, which every peer draws on every body it can see) and a UI line. Any one of the
/// three carries the event on its own.</para>
///
/// <list type="number">
/// <item><b>The sound</b> — <see cref="Sfx.Triumph"/>, played NON-POSITIONALLY
/// (<c>SfxLab.PlayUi</c>). Deliberately not a 3D one-shot: there is no place in the world where
/// completing the level happened. A positional triumph would be quieter for the player standing
/// furthest from the last bubble, which is precisely backwards for a shared achievement — and it
/// would make "who popped it" audible, which is not a distinction the tally makes.</item>
/// <item><b>The puffs</b> — one small burst of soap-film sparkles at every avatar this peer can
/// see, its own included. Positions come from this peer's OWN replicated avatar nodes, never from
/// anything the wire asserted — <c>HonkManager.HeadOf</c>'s shape, and for the same reason: a
/// position that is resolved locally is not merely un-forgeable, it is unsayable. Everyone
/// sparkles, not just the last popper, because everyone's tally it was.</item>
/// <item><b>The line</b> — one phase toast, if this peer has a toast layer (a headless one does
/// not). It is the <c>all_players</c> channel that survives a severed audio path.</item>
/// </list>
///
/// <para><b>THE BOT GATE.</b> <c>.claude/rules/test-suite.md</c>: <c>user://</c> resolves by
/// project NAME, so every checkout on this machine shares ONE profile, and a suite bot has
/// already spent a real achievement into Talon's real one. Bubble suites pop bubbles, and a suite
/// that pops them ALL would fire this on every automated run. So the celebration asks the same
/// question the achievement gate asks — <b>is a person driving a body in this process</b>
/// (<c>IIntentSource.IsHumanInput</c>, via <see cref="SandboxAvatar.IsHumanDriven"/>) — and a
/// headless server, a scripted bot, a capture run and a self-test all answer no. The question is
/// asked of the process rather than of one avatar because the celebration is not addressed to a
/// particular body: there is at most one local player per process in this game, and "is anybody
/// home" is the honest form of the question a global sound is asking.</para>
///
/// <para><b>Witnessing an absence.</b> <see cref="Received"/> and <see cref="Celebrated"/> are the
/// pair <c>HonkManager</c>'s <c>_received</c>/<c>_heard</c> are, for the identical reason: a bot
/// with <c>received &gt; 0</c> and <c>celebrated == 0</c> proves the GATE culled it, where
/// <c>received == 0</c> alone would equally be a broken wire. Without the pair, "the suite ran and
/// nothing celebrated" is not evidence about the gate at all.</para>
///
/// <para><b>No achievement is unlocked here, and that is deliberate.</b> An all-bubbles
/// achievement already exists (<c>AchievementId.Bubblholic</c>, via
/// <c>BubblholicRule.AllPopped</c>) and is unlocked on its own path from
/// <c>AchievementRuntime.CheckBubblholic</c> — which is already gated the same way, because the
/// runtime that would do the writing is never built for a scripted body. This class adds no
/// second writer to that story.</para>
/// </summary>
public static class BubbleCelebration
{
    /// <summary>How long the whole beat lasts, seconds — the sound's tail is the longest part of
    /// it. Named so the restraint constraint is a number a reader can check rather than a claim,
    /// and read by the suite's "the player kept playing through it" window.</summary>
    public const double TotalSeconds = 1.6;

    /// <summary>Particles per body, and their size in metres. Twice the pop's own burst
    /// (<c>BubbleCounter.PlayPopFeedback</c> spends 10 at 0.05 m) and no more: the completion has
    /// to read as a different event from the ninety-nine pops that led to it, and doubling the
    /// smallest thing in the game is the whole of the increase. At the six-player cap this is six
    /// of these at once, once per completion.
    ///
    /// <para><b>Measured, not guessed.</b> The first capture run spent 14 at 0.045 m and the
    /// sparkle was a smudge beside the avatar's head at 2 m — see
    /// <c>docs/qa/CELEBRATE-1/</c>.</para></summary>
    private const int PuffCount = 20;
    private const float PuffSizeM = 0.065f;

    /// <summary>Where the sparkles leave a body, metres above its origin — roughly chest height on
    /// this avatar, so they read as coming off the player rather than off the floor.</summary>
    private const float PuffHeightM = 0.9f;

    /// <summary>Soap-film white with a faint warm cast — the pop puff's colour
    /// (<c>BubbleCounter.PuffColor</c>) nudged toward gold so the completion reads as a different
    /// event from the ninety-nine pops that led to it, without introducing a colour the level does
    /// not already contain (the golden cubes are this hue).</summary>
    private static readonly Color PuffColor = new(0.98f, 0.93f, 0.74f, 0.85f);

    /// <summary>Volume for the triumph, dB. Quieter than the goose on purpose: a non-positional
    /// sound is already at full level wherever the listener stands, so it needs less than a 3D one
    /// to be equally present.</summary>
    private const float VolumeDb = -9f;

    /// <summary><b>Test-only (<c>--celebrate-force</c>): pretend a person is present.</b> The
    /// positive control for the gate below, and the reason the suite's absence proof means
    /// anything: a run in which nothing celebrates is indistinguishable from a broken wire unless
    /// a peer in the SAME shape can be made to celebrate on demand.
    ///
    /// <para><b>It cannot unlock anything.</b> It bypasses this class's presentation gate and
    /// nothing else — <c>AchievementRuntime</c> is built (or not) in
    /// <c>SandboxAvatar.ConfigureAsNetworked</c>, which never reads this, so a forced bot still
    /// writes nothing to the shared <c>user://</c> profile. Precedent: <c>--honk-forge</c> and
    /// <c>--voice-flood</c>, test-only probes that exist so a claimed property is measured rather
    /// than read off an attribute.</para></summary>
    public static bool ForceForTest { get; set; }

    /// <summary>Completion broadcasts that ARRIVED on this peer, in any shape. Bumped before the
    /// gate, so it counts the wire and not the decision.</summary>
    public static long Received { get; private set; }

    /// <summary>Completions this peer actually CELEBRATED — i.e. the gate said yes. Bumped after
    /// the gate and before the mixer, so a headless peer takes every decision and stops one line
    /// short of the audio device, which is the only reason a bot can witness any of this.</summary>
    public static long Celebrated { get; private set; }

    /// <summary>Session reset. Called from <see cref="BubbleCounter.Setup"/>, so a second session
    /// in one process does not inherit the first one's counters.</summary>
    public static void ResetCounters()
    {
        Received = 0;
        Celebrated = 0;
    }

    /// <summary>
    /// <b>The gate, as a pure function.</b> Split out of <see cref="Play"/> so the decision can be
    /// hammered in <c>tests/unit</c> without an engine — the same reason <c>HonkGate</c> is split
    /// out of <c>HonkManager</c>.
    ///
    /// <para><paramref name="headless"/> is deliberately NOT part of the decision. A headless peer
    /// is excluded by <paramref name="humanPresent"/> already (nobody is driving a body there),
    /// and folding "no audio device" into "may celebrate" would collapse two different facts:
    /// <see cref="Play"/> stops short of the mixer on a headless peer AFTER counting, which is
    /// what lets a forced bot prove the whole decision path ran.</para>
    /// </summary>
    public static bool ShouldCelebrate(bool humanPresent, bool forced) => forced || humanPresent;

    /// <summary>Is a person driving a body in this process right now? Reads
    /// <see cref="SandboxAvatar.Live"/> — every avatar node currently in this peer's tree — and
    /// asks each the same question the achievement gate asks. False on a headless server (no
    /// avatar there has a human source), on a bot, in a capture run and in a self-test.</summary>
    public static bool HumanPresent
    {
        get
        {
            foreach (SandboxAvatar avatar in SandboxAvatar.Live)
            {
                if (GodotObject.IsInstanceValid(avatar) && avatar.IsHumanDriven)
                    return true;
            }
            return false;
        }
    }

    /// <summary>
    /// <b>Every peer: the last bubble is gone.</b> Called from
    /// <c>BubbleCounter.CelebrateBroadcast</c> and from nowhere else, so there is exactly one
    /// funnel and the idempotency that protects it (the server-side latch) protects all of it.
    ///
    /// <para><paramref name="host"/> is any live node in the tree — the counter passes itself —
    /// used to reach the scene root for the particles and the toast layer.</para>
    /// </summary>
    public static void Play(Node host)
    {
        Received++;
        bool human = HumanPresent;
        if (!ShouldCelebrate(human, ForceForTest))
        {
            GD.Print($"[bubbletest] celebration suppressed: no human input on this peer " +
                     $"(received={Received} celebrated={Celebrated})");
            return;
        }
        Celebrated++;
        GD.Print($"[bubbletest] celebrate: every bubble popped " +
                 $"(received={Received} celebrated={Celebrated} human={human} forced={ForceForTest})");

        if (NetworkManager.Instance is { IsHeadless: true })
            return; // decision taken and counted; no audio device and no viewport to feed

        if (!GodotObject.IsInstanceValid(host) || !host.IsInsideTree())
            return;

        SfxLab.PlayUi(Sfx.Triumph, volumeDb: VolumeDb);

        foreach (SandboxAvatar avatar in SandboxAvatar.Live)
        {
            if (!GodotObject.IsInstanceValid(avatar) || !avatar.IsInsideTree())
                continue;
            JuiceFx.Puff(host, avatar.GlobalPosition + new Vector3(0f, PuffHeightM, 0f),
                count: PuffCount, color: PuffColor, size: PuffSizeM, speed: 2.1f, lifetime: 0.75f);
        }

        MpFoundation.Ui.PhaseToastLayer.Instance?.ShowLine(MpFoundation.Ui.PhaseToastText.AllBubblesToast);
    }
}
