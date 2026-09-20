using MpFoundation.Game.World;

namespace MpFoundation.Ui;

/// <summary>
/// Pure kind -> toast-line mapping for L11's phase toasts (spec §1 step 4/5: "Phase toasts on
/// L1's events (sunset/night/dawn — one line each, no art)"). Pulled out of
/// <see cref="PhaseToastLayer"/> so the "which of the four events get a toast, and what do they
/// say" decision is directly testable without a scene tree, same seam as
/// <see cref="LoadingOverlayGate"/>.
///
/// Deliberately returns null for <see cref="PhaseEventKind.DawnToDay"/>: the spec names exactly
/// three toasts (sunset/night/dawn). A "new day" toast was never asked for, and on the run's
/// FINAL cycle that crossing is also the one that fires <c>RunEndedSignal</c> — showing a toast
/// a frame before the summary panel takes over would be a flash of UI nobody has time to read.
/// Wording is a value pick (not a direction) — short, diegetic, no art per scope.
///
/// <para><b>It also owns the one line that is not a crossing</b> — the on-entry goal reminder
/// (<see cref="BubbleGoalToast"/>). Same layer, same queue, same testable-without-an-engine
/// seam; a parallel copy class holding a single string would split "what does a toast say"
/// across two files and buy nothing.</para>
/// </summary>
public static class PhaseToastText
{
    /// <summary><b>The nightfall line, and it carries the flashlight's only tutorial.</b> Talon's
    /// 2026-08-29 note 10, verbatim: *"there is text that reads 'night has fallen' can you change
    /// this to say 'Night has fallen. Press F to toggle flashlight.'"* — his wording, his
    /// punctuation, unedited.
    ///
    /// <para><b>Why the hint rides THIS line rather than a HOW TO PLAY entry.</b> It is the one
    /// moment the player both needs the tool and is looking at the words: the key is named at the
    /// instant it becomes useful, which is the only teaching moment a toast can win. It is also the
    /// whole of the flashlight's discoverability today — nothing else in the game mentions
    /// <c>F</c>. A named constant rather than an inline string so
    /// <c>FlashlightController.ActionName</c>'s binding and the sentence that advertises it can be
    /// asserted against each other by a test instead of by a reader.</para></summary>
    public const string NightfallToast = "Night has fallen. Press F to toggle flashlight.";

    /// <summary><b>The sunset line, and why it no longer names a campfire.</b> Talon,
    /// 2026-08-30 playtest note 5, verbatim: <i>"The night says 'head to the campfire' this needs
    /// to remove the 'campfire' reference, it's not a campfire anymore."</i>
    ///
    /// <para><b>The replacement is a LAW, not the furniture that currently implements it.</b>
    /// <c>docs/CANON.md</c>'s one-place rule is that a string references the premise rather than
    /// restating it, and the test it gives is: if the premise changed tomorrow, would this still
    /// be correct? The old line named a prop, failed that test, and has now failed it in play.
    /// "Head for the light" passes it: light being the thing worth moving toward in the dark is what
    /// the sentence is actually telling
    /// the player to do, and it stays true whether the light is a campfire, a pedestal, a strung
    /// line or whatever comes next. The world this shipped into no longer has a campfire in it;
    /// it has never not had light.</para>
    ///
    /// <para><b>FLAGGED FOR TALON, not closed</b> (W7-2, 2026-08-30). A value pick by the rule
    /// above, not a direction — the wording is the kind of thing he may have his own view on, and
    /// he is the one who noticed the old one.</para></summary>
    public const string DuskToast = "Sunset. Head for the light.";

    /// <summary><b>The on-entry goal line: what the player is here to do, said once.</b> Raised
    /// from Gameplay's client-UI block the instant the loading ground clears. It is not a
    /// crossing, so it has no <see cref="PhaseEventKind"/> and <see cref="TextFor"/> can never
    /// return it.
    ///
    /// <para><b>Shown every session, and that is the point.</b> A goal reminder is not
    /// onboarding: there is no "don't show this again" and nothing persisted about it. It obeys
    /// exactly one gate, the run-level capture/bot suppression every panel obeys — see
    /// <c>OnboardingSettings.GoalLineSuppressed</c>.</para>
    ///
    /// <para><b>It names bubbles, so it only fires where bubbles are.</b> The caller gates it on
    /// the world id it actually built the level from, not on the autoload's options (see
    /// <c>LaunchOptions.DefaultWorld</c>'s trap note). Copy asserting a mechanic the running
    /// world does not have is the failure <c>docs/CANON.md</c>'s one-place rule and Talon's note
    /// 4 are both about; <see cref="NightfallLinesFor"/> is the same rule pointed at the
    /// nightfall plate.</para></summary>
    public const string BubbleGoalToast = "Collect all the bubbles.";

    /// <summary><b>The other end of <see cref="BubbleGoalToast"/>: the goal, met.</b>
    /// CELEBRATE-1, 2026-09-04. Talon asked for the completion to <i>"do something also special to
    /// let them know they're special"</i>; this is the non-audio half of that (INTERACTION-BIBLE
    /// §8.2 — an <c>all_players</c> consequence carried only by audio is invisible to a player
    /// whose audio this game deliberately severs).
    ///
    /// <para><b>It names no number, on purpose.</b> "100 / 100" would be a fourth copy of a bubble
    /// count that already lives in the level's authoring, in <c>BubbleTestLayout</c> and on the
    /// HUD — and the HUD is already showing the player the number at the moment this fires, so the
    /// line has nothing to add by repeating it. Naming it here would also mean a packet that
    /// changes the bubble count has to remember to change a string in the UI layer, which is
    /// exactly the coupling <c>docs/CANON.md</c>'s one-place rule exists to prevent.</para>
    ///
    /// <para><b>Wording is a value pick, not a direction</b> — recorded in <c>DECISION-LOG.md</c>
    /// so Talon can retune or delete it by hand without reading any code. Warm, short, and no
    /// exclamation mark: the packet's standing constraint is restraint, and a line that shouts is
    /// the text equivalent of the juice he reverts. Like <see cref="BubbleGoalToast"/> it asserts
    /// no premise — it says what the player did and nothing about what the place is.</para></summary>
    public const string AllBubblesToast = "That's every bubble in the world.";

    public static string? TextFor(PhaseEventKind kind) => kind switch
    {
        PhaseEventKind.DayToDusk => DuskToast,
        PhaseEventKind.DuskToNight => NightfallToast,
        PhaseEventKind.NightToDawn => "Dawn is breaking.",
        _ => null, // DawnToDay — see class doc.
    };

    // --- CORE-PROG-B1: the DuskToNight treatment's content ---------------------------------
    // Same seam as TextFor — pure statics, one source for both the live PhaseToastLayer
    // trigger and the scripted demo. Kept HERE rather than in a new decision class because
    // the packet's rule is extend-never-parallel: this file already owns "what does a
    // crossing say to the player".

    /// <summary>Whether a crossing gets the nightfall treatment on top of its toast. Exactly one
    /// does. (It was a full-screen wash until PLAY-1, 2026-08-16, made it a bounded plate — see
    /// <see cref="Flow.NightfallOverlay"/> and the coverage law it broke.)</summary>
    public static bool FullTreatmentFor(PhaseEventKind kind) => kind == PhaseEventKind.DuskToNight;

    public const string NightfallHeading = "NIGHT";

    /// <summary>What actually changes mechanically at night, SPELLED OUT (the brief: told,
    /// not discovered blind). PLACEHOLDER copy — the feel of this beat needs /direct; the
    /// mechanics named are canon facts 2, 5 and 13.
    ///
    /// <para><b>They are only true in a world that runs the scored playthrough.</b> Read them
    /// through <see cref="NightfallLinesFor"/>, never directly, unless you specifically want the
    /// playthrough set.</para></summary>
    public static readonly string[] NightfallLines =
    {
        "The creatures are out. Lit ground is safe ground - dark ground is theirs.",
        "Firelight holds them back. Keep the fires fed.",
        "Standing in darkness will freeze you. Carry light or keep moving.",
        "Bank the winter cache at the camp drop-off before dawn.",
    };

    /// <summary><b>The nightfall plate's lines for a world that may not have the mechanics they
    /// name.</b> Returns <see cref="NightfallLines"/> where a scored playthrough runs, and
    /// <b>nothing</b> where it does not.
    ///
    /// <para><b>Why this exists (WAVE-5 integration, 2026-08-29).</b> Talon played the bubble test
    /// and objected to the loss screen for asserting a fiction this world does not have: <i>"there
    /// is no 'cache' there is no 'winter' and there's no 'goal' for this playtest, it's about
    /// movement and exploration."</i> LOSS-1 removed that screen. <b>This plate was still telling
    /// him all four of the same things at every nightfall</b>, on a larger surface, directly above
    /// NIGHT-2's new flashlight toast. The bubble test has no creatures, no fires to feed, no cold
    /// that freezes and no cache to bank.</para>
    ///
    /// <para><b>Why nothing, rather than a rewrite.</b> Saying nothing beats saying something
    /// untrue, and a true replacement would have to describe what the bubble test <i>is</i> —
    /// a tone call belonging to Talon and <c>/direct</c>, not to a pass whose job is removing a
    /// lie. LOSS-1 reached the same conclusion independently for <c>LoadingHintOverlay</c>'s
    /// "Wood by day. Light by night." The heading and the toast still carry the crossing, so the
    /// beat survives; only the false mechanics leave.</para>
    ///
    /// <para><b>The quoted note has since been partly overtaken; this decision has not.</b> The
    /// bubble test now HAS a goal — "collect all the bubbles", said once on entry
    /// (<see cref="BubbleGoalToast"/>) — so read the <i>"there's no 'goal'"</i> clause above as
    /// where the level stood on 2026-08-29, not as a standing claim about it. Nothing else in the
    /// quote moved, and nothing this plate says is any truer: there are still no creatures, no
    /// fires to feed, no cold that freezes and no cache to bank, which are the only four things
    /// these lines name. The plate stays empty here for exactly the reason it always was.</para>
    ///
    /// <para><b>Why this is a method while <see cref="NightfallLines"/> stays a bare array.</b>
    /// Making the field itself world-aware turns <c>FlowCopyTests</c> red:
    /// <c>Nightfall_SpellsOutTheMechanics</c> reads the array directly and asserts it contains
    /// "creature", "light", "freeze" and "dawn" — and because <c>NetworkManager</c> is an autoload
    /// the world id is <b>never null and silently <c>bubbletest</c></b> (see
    /// <c>LaunchOptions.DefaultWorld</c>), so a world-aware property would hand that test the
    /// empty set. The caller passes the answer in; the field keeps meaning "the playthrough
    /// copy".</para></summary>
    public static System.Collections.Generic.IReadOnlyList<string> NightfallLinesFor(bool runsPlaythrough) =>
        runsPlaythrough ? NightfallLines : System.Array.Empty<string>();
}
