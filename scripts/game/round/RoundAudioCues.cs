using System;
using System.Collections.Generic;
using MpFoundation.Game.Sandbox;

namespace MpFoundation.Game.Round;

/// <summary>
/// One sound the round asks for, and where it comes from.
///
/// <para><b><see cref="Flat"/> and <see cref="Positional"/> are not exclusive</b>, and the two
/// buzzers use both: the buzzer is a fact about the building (it comes out of the clocks, so a
/// hider can tell which way the room is) AND a fact about the round (it happened to you, wherever
/// you are standing). A cue that was only positional would be inaudible to a player who had
/// walked round a shelf; one that was only flat would be a sound with no source, which in a game
/// whose whole hook is "where is the other person" is a lie.</para>
///
/// <para><b><see cref="PitchBias"/> is a transposition, not jitter.</b> It is chosen — the last
/// seconds of a countdown rise — and it is added to the pitch scale before
/// <c>SfxLab</c>'s per-shot randomness, which exists to be unnoticed.</para>
/// </summary>
/// <param name="Sound">The recipe, from the one palette.</param>
/// <param name="Flat">Play it non-positionally, once, on this peer (<c>SfxLab.PlayUi</c>).</param>
/// <param name="Positional">Play it from every <c>RoundClock</c> in the world
/// (<c>SfxLab.PlayStream3D</c> at each clock's position).</param>
/// <param name="PitchBias">Added to the pitch scale. 0 for everything but the last ticks.</param>
public readonly record struct RoundCue(Sfx Sound, bool Flat, bool Positional, float PitchBias);

/// <summary>
/// The dials on the round's audio. Mutable static with a <see cref="Default"/>, the same shape
/// <c>HideSeekTuning.Current</c> uses, so a lab scene or a suite can move one without a wire.
/// </summary>
/// <param name="HumOn">The low steady hum on the clocks during <c>Seeking</c> (proposal §3.2).
/// <b>Off, and not implemented</b> — see <c>RoundAudio</c>'s class doc for why the flag landed
/// without the loop behind it.</param>
public readonly record struct RoundAudioTuning(bool HumOn)
{
    /// <summary>The shipped values.</summary>
    public static RoundAudioTuning Default => new(HumOn: false);

    /// <summary>What this process is running with.</summary>
    public static RoundAudioTuning Current { get; set; } = Default;
}

/// <summary>
/// <b>What the round sounds like, as a pure function of two replicated views</b> (CLOCK-1,
/// 2026-09-19; design <c>PROPOSAL-2026-09-19-SOUND-STATES.md</c> §3.2).
///
/// <para><b>Engine-free and derived from the CLIENT's own view, on purpose.</b> There is no new
/// wire and no RPC in this feature. Every peer already receives one absolute message whenever the
/// round moves (<see cref="HideSeekWire"/>), so every peer can compute the same cue list from the
/// same two messages — and a cue that was pushed would be a second source of truth for a thing
/// the wire already carries, with its own ordering and its own way of arriving late.</para>
///
/// <para><b>On a 10 Hz wire a tick fires within 100 ms of the true second, and that is fine.</b>
/// The packet says so explicitly and it is worth restating here, because the obvious "fix" —
/// extrapolating the clock locally between messages — trades a bounded, silent 100 ms for an
/// unbounded, audible disagreement between two players' clocks the moment one of them stalls.
/// <b>Do not extrapolate.</b></para>
///
/// <para><b>A peer with no previous view gets nothing.</b> That is the late-join rule the driver's
/// own <c>PhaseChanged</c> event already follows: a client that arrives mid-seek has WITNESSED no
/// transition, and chiming the round's start at it would announce something it missed. Its clock
/// is correct from the first message and silent on it.</para>
/// </summary>
public static class RoundAudioCues
{
    /// <summary>Nothing happened. One allocation for the whole process — this is called ten times
    /// a second on every peer and returns empty for almost all of them.</summary>
    private static readonly IReadOnlyList<RoundCue> Silence = Array.Empty<RoundCue>();

    /// <summary>The countdown starts being audible when the clock reads this. Ten ticks: 10, 9,
    /// … 1. The clock reading 0 is not a tick — whatever happens at 0 has its own cue.</summary>
    public const int TickFromSec = 10;

    /// <summary>
    /// How near zero the previous view's clock has to be for a <c>Hiding → Seeking</c> edge to be
    /// read as the BUZZER rather than as Confirm.
    ///
    /// <para><b>This is the one place the two are genuinely indistinguishable from a client's
    /// view, and the ambiguity is real rather than an implementation gap.</b> The server knows: it
    /// took the <c>EnterSeeking</c> branch either from <c>input.HiderPressedConfirm</c> or from
    /// <c>left &lt;= 0</c>. The client sees only "the phase changed and the old clock read
    /// <i>x</i>". At 10 Hz the last Hiding message before the buzzer reads 0.0 s, so anything at
    /// or under one wire tick is the buzzer; a Confirm pressed inside that same tenth of a second
    /// is misread as a buzzer, and BOTH cues mean "the hide is over" to the player who hears
    /// them. Putting a flag on the wire to close a 100 ms window would be a wire field bought
    /// with a merge conflict for an audible difference nobody can produce on purpose.</para>
    /// </summary>
    public const float BuzzerWindowSec = 0.15f;

    /// <summary>How far the clock has to jump UP inside <c>Hiding</c> before it is the grace
    /// extension rather than wire noise. The clock only ever counts down within a phase, so any
    /// increase at all is the extension; the epsilon is against the tenths quantisation, not
    /// against a real rise.</summary>
    public const float GraceJumpSec = 0.05f;

    /// <summary>
    /// The last seconds rise. <b>The packet's own list is <c>+0 / +0.05 / +0.1</c> for
    /// <c>3, 2, 1</c></b>, so three gets no bias and the rise is heard on two and one — the
    /// smallest gesture that reads as "it is about to happen" rather than as a siren.
    /// </summary>
    public static float TickPitchBias(int second) => second switch
    {
        1 => 0.10f,
        2 => 0.05f,
        _ => 0f,
    };

    /// <summary>
    /// <b>Every sound one replicated edge is worth.</b> Empty for the overwhelming majority of
    /// edges, including every edge where only the clock's tenths moved.
    /// </summary>
    /// <param name="previous">The view this peer held before the message. <c>null</c> = this is
    /// the peer's FIRST message and it has witnessed nothing — see the class doc.</param>
    /// <param name="next">The view the message folded to.</param>
    public static IReadOnlyList<RoundCue> ForEdge(HideSeekView? previous, HideSeekView next)
    {
        if (previous is not { } prev)
            return Silence;
        if (prev.Equals(next))
            return Silence;

        HideSeekPhase from = prev.Phase;
        HideSeekPhase to = next.Phase;

        if (from == to)
            return WithinPhase(prev, next, to);

        // A round that ended because somebody left is not a round that ended. The loop commits a
        // card marked EndedByDisconnect from Hiding, Seeking or Together alike, and every one of
        // those edges would otherwise fire the cue for the ending it did not have — a triumph
        // sting for a player staring at an empty room is the worst of the three.
        if (to == HideSeekPhase.Tally && next.LastTally is { EndedByDisconnect: true })
            return Silence;

        return (from, to) switch
        {
            // The round begins. Flat only: everyone is in the holding room together, so there is
            // nothing for a positional layer to tell anybody.
            (HideSeekPhase.Holding, HideSeekPhase.Hiding) =>
                One(Sfx.ChimeUp, flat: true, positional: false),

            // Confirm, or the buzzer — see BuzzerWindowSec for why the clock is what tells them
            // apart and why the ambiguous tenth of a second does not matter.
            (HideSeekPhase.Hiding, HideSeekPhase.Seeking) =>
                prev.RemainingSec <= BuzzerWindowSec
                    ? One(Sfx.RoundBuzz, flat: true, positional: true)
                    : One(Sfx.Note, flat: true, positional: false),

            // The hide was still broken after the grace, so the round is abandoned. NOT IN THE
            // PACKET'S TABLE and added deliberately: it is the same event as the seek timeout —
            // a clock ran out into a tally — and the alternative is the one ending in this game
            // that happens in silence. Same cue as the timeout for exactly that reason.
            (HideSeekPhase.Hiding, HideSeekPhase.Tally) =>
                One(Sfx.BuzzDouble, flat: true, positional: true),

            // The find. DOOR-1 owns this instant; a second sound on top of the bang would be two
            // systems announcing one event.
            (HideSeekPhase.Seeking, HideSeekPhase.Together) => Silence,

            // The seek timed out.
            (HideSeekPhase.Seeking, HideSeekPhase.Tally) =>
                One(Sfx.BuzzDouble, flat: true, positional: true),

            // The players ended it themselves. The existing Triumph, flat — it is about the card,
            // and the card is on every screen at once rather than in a room.
            (HideSeekPhase.Together, HideSeekPhase.Tally) =>
                One(Sfx.Triumph, flat: true, positional: false),

            // The world going home.
            (HideSeekPhase.Tally, HideSeekPhase.Holding) =>
                One(Sfx.ResetWhoosh, flat: true, positional: false),

            _ => Silence,
        };
    }

    /// <summary>
    /// The edges that do not change the phase: the countdown, and the one-time grace extension.
    /// </summary>
    private static IReadOnlyList<RoundCue> WithinPhase(in HideSeekView prev, in HideSeekView next,
        HideSeekPhase phase)
    {
        if (phase is not (HideSeekPhase.Hiding or HideSeekPhase.Seeking))
            return Silence;

        // THE GRACE, and it is read off the CLOCK rather than off the refusal that rides with it.
        // A refused Confirm sets the same refusal reason without extending anything, so keying on
        // the reason would buzz every time the hider pressed the button with the object still in
        // their hands. The clock going UP inside a phase happens exactly once per round and only
        // here.
        if (phase == HideSeekPhase.Hiding && next.RemainingSec > prev.RemainingSec + GraceJumpSec)
            return One(Sfx.BuzzShort, flat: true, positional: true);

        // ONE candidate per edge, not every second in the gap. A tick is "the clock now READS n,
        // and it did not before", so a peer that missed messages hears the second it is actually
        // looking at rather than a burst of the ones it slept through.
        int before = WholeSeconds(prev.RemainingSec);
        int after = WholeSeconds(next.RemainingSec);
        if (after == before || after < 1 || after > TickFromSec)
            return Silence;

        // Flat is deliberately FALSE on both phases. The tick is the building counting, and it is
        // the one cue whose whole job is to have a direction — a flat layer on top would put the
        // countdown inside the player's head, where it says nothing about which way the clock is.
        return One(Sfx.Tick, flat: false, positional: true, TickPitchBias(after));
    }

    /// <summary>The clock as the player reads it — floored, never negative, matching
    /// <see cref="HideSeekText.TimerText"/> exactly. A tick that fired on a rounded value would
    /// land on a second the wall is not showing yet.</summary>
    private static int WholeSeconds(float remainingSec) =>
        float.IsNaN(remainingSec) || remainingSec < 0f ? 0 : (int)MathF.Floor(remainingSec);

    private static IReadOnlyList<RoundCue> One(Sfx sound, bool flat, bool positional,
        float pitchBias = 0f) =>
        new[] { new RoundCue(sound, flat, positional, pitchBias) };
}
