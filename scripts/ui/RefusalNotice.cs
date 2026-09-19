using Godot;

namespace MpFoundation.Ui;

/// <summary>
/// <b>Says WHY the game just refused something the player pressed</b>, in words, on the screen of
/// the one player who pressed it.
///
/// <para><b>The gap this closes.</b> The server has always answered a refused grab with a reason
/// ordinal — <c>PropManager.GrabDenial</c> exists precisely because "silently ignored" is a defect
/// class (INTERACTION-BIBLE §2) — and the avatar has always turned it into a bump and a bonk. But
/// the bump does not say which of five things went wrong, so from the player's side a refusal for
/// "someone else got it first" and a refusal for "you are one step too far away" are the same
/// event, and neither tells them what to do differently. This repo has already paid for that
/// exact shape once: the one in-world control the foundation ever shipped failed a playtest
/// because its warning <i>"never said which key"</i> (INTERACTION-BIBLE §3, §7). CARRY-1's place
/// verb adds five more reasons to get wrong, so the reason became text.</para>
///
/// <para><b>Transient by construction.</b> It is a notice, not a state: no input, no focus,
/// nothing to dismiss, and it takes itself down after <see cref="HoldSec"/>. A second refusal
/// while one is up replaces it and restarts the clock, because the newest refusal is the one the
/// player is currently confused by.</para>
///
/// <para><b>It reuses <see cref="ConsequenceWarning"/>'s plate rather than minting a second one.</b>
/// That class already owns a non-occludable, non-blocking, lower-band plate that was built after a
/// playtest proved a world-space sign cannot do this job — and it is deliberately empty of copy,
/// so the words live with the mechanism that can be wrong about them. Here, that mechanism is the
/// denial ordinal.</para>
/// </summary>
public partial class RefusalNotice : Node
{
    /// <summary>How long a refusal stays up. Long enough to read a short sentence, short enough
    /// that it is gone before the player has finished doing the thing it told them to do.</summary>
    public const double HoldSec = 2.2;

    private static RefusalNotice? _instance;

    private ConsequenceWarning? _plate;
    private double _remaining;

    /// <summary>Adds one to a scene if this peer renders. Call once per scene, beside the other
    /// world-furniture attachments.</summary>
    public static void Attach(Node sceneRoot)
    {
        if (NetworkManager.Instance is { IsHeadless: true })
            return;
        if (_instance != null && GodotObject.IsInstanceValid(_instance))
            return;
        sceneRoot.AddChild(new RefusalNotice { Name = "RefusalNotice" });
    }

    /// <summary>
    /// Put a refusal on screen. <paramref name="heading"/> names the verb that was refused
    /// ("CAN'T PICK THAT UP"), <paramref name="body"/> says why in the player's terms ("It doesn't
    /// fit there").
    ///
    /// <para>A no-op on a headless peer and on any peer where nothing was attached, so a caller
    /// never has to ask whether there is a screen — the same contract
    /// <c>InteractPrompt.SetTarget</c> has.</para>
    /// </summary>
    public static void Say(string heading, string body)
    {
        if (_instance == null || !GodotObject.IsInstanceValid(_instance))
            return;
        _instance.Show(heading, body);
    }

    private void Show(string heading, string body)
    {
        _plate ??= ConsequenceWarning.Attach(this);
        if (_plate == null)
            return;
        // urgent: true — a refusal is the one thing on this plate that the player is actively
        // waiting on an answer for, and the urgent treatment is what separates it from ambient
        // chrome at a glance.
        _plate.Set(heading, body, "", urgent: true);
        _remaining = HoldSec;
    }

    public override void _Ready() => _instance = this;

    public override void _ExitTree()
    {
        if (ReferenceEquals(_instance, this))
            _instance = null;
    }

    public override void _Process(double delta)
    {
        if (_remaining <= 0)
            return;
        _remaining -= delta;
        if (_remaining <= 0)
            _plate?.Clear();
    }
}
