using Godot;
using MpFoundation.Game.Round;
using MpFoundation.Ui.Design;

namespace MpFoundation.Ui.Hud;

/// <summary>
/// Top-centre: what part of the round this is, how long is left, which side you are on, and which
/// round it is.
///
/// <code>
///                          SEEKING · 2:41 · YOU HIDE · ROUND 2
///                            PUT THE OBJECT DOWN FIRST
/// </code>
///
/// <para><b>Why the centre.</b> Same reason the readout it replaces took that slot: this is the
/// one line on screen that changes the meaning of everything else. The same walk down the same
/// aisle is a hiding place in one phase and a search in the next, and a value that reframes the
/// rest of the frame belongs where the eye returns to rather than in a corner it has to go
/// looking for.</para>
///
/// <para><b>It holds until the round is synced.</b> <c>HideSeekDriver</c>'s zero-initialised
/// default is "Holding, round 1, no roles", which is a perfectly plausible sentence and, on a
/// client that has joined mid-seek, a wrong one. Nothing is painted until a real message has
/// landed — the same discipline <see cref="DayPhaseWidget"/> applies to the cycle clock and the
/// same reason.</para>
///
/// <para><b>The refusal line is latched here, not on the wire.</b> The server's refusal is true
/// for exactly one tick (it is an answer to a press, and a sticky one would still be on screen two
/// minutes later describing a question nobody remembers asking), so it arrives in one message and
/// is gone from the next. This widget holds the sentence for
/// <see cref="RefusalHoldSec"/> and then drops it. BTN-1's button shake and click are the other
/// half of the same answer; this is the half that says the words, because
/// <c>INTERACTION-BIBLE.md</c> §5 is about a control that refused and never said which key.</para>
///
/// <para><b>Nothing here holds its own appearance.</b> Every colour, size, weight and gap comes
/// from <see cref="HudTheme"/>, which forwards to <c>UiTokens</c>/<c>UiScale</c>/<c>UiRecipes</c>
/// — the unit tests enforce it by scanning this file's text for a typed literal.</para>
/// </summary>
public partial class RoundStripWidget : PanelContainer
{
    /// <summary>How long a refusal stays on screen. The packet's two seconds: long enough to read
    /// eight words under pressure, short enough that the next press's answer is unambiguous.</summary>
    public const double RefusalHoldSec = 2.0;

    /// <summary>How long the intercom lamp stays lit after the last PA frame from that speaker.
    /// The voice envelope already has a ~120 ms release, so this is not de-bounce — it is the gap
    /// between two sentences of the same taunt, which a lamp that went out in between would read
    /// as two different people talking.</summary>
    public const double IntercomHoldSec = 0.8;

    private VBoxContainer _rows = null!;
    private Label _line = null!;
    private Label _refusal = null!;
    private Label _intercom = null!;

    private string _intercomText = string.Empty;
    private double _intercomLeft;

    private string _lastLine = string.Empty;
    private HideSeekPhase _lastPhase = HideSeekPhase.Holding;
    private bool _painted;

    private string _refusalText = string.Empty;
    private double _refusalLeft;

    public RoundStripWidget() => MouseFilter = MouseFilterEnum.Ignore;

    public override void _Ready()
    {
        // Bound rather than set once: a HUD scrap that took its stylebox in _Ready would still be
        // lit at noon after nightfall. Accented while a refusal is up — paired with the extra line
        // of text, so the state reads in VALUE and in CONTENT, not in hue alone (ART-BIBLE §3).
        HudTheme.BindPanel(this, () => _refusalLeft > 0.0);

        _rows = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        _rows.AddThemeConstantOverride("separation", HudTheme.Space1);
        AddChild(_rows);

        _line = HudTheme.MakeLabel(string.Empty, HudTheme.RoleBody, HorizontalAlignment.Center);
        _rows.AddChild(_line);

        _refusal = HudTheme.MakeLabel(string.Empty, HudTheme.RoleCaption, HorizontalAlignment.Center);
        _refusal.Visible = false;
        _rows.AddChild(_refusal);

        // The intercom lamp (VOICE-1), under the refusal row rather than beside the phase line:
        // it appears and disappears mid-round, and a row that grows and shrinks inside the line
        // the eye returns to would make the phase readout jump. Its own row simply pushes the
        // strip a caption taller while somebody is talking, and GameHud re-publishes the column
        // off the measured height, so nothing below it has to know.
        _intercom = HudTheme.MakeLabel(string.Empty, HudTheme.RoleCaption, HorizontalAlignment.Center);
        _intercom.Visible = false;
        _rows.AddChild(_intercom);

        // Nothing is painted until the round is synced, so the strip starts out of the frame's way
        // entirely rather than showing a plausible default.
        Visible = false;
    }

    /// <summary>
    /// Driven by <see cref="GameHud"/>'s 10 Hz poll. <paramref name="delta"/> is the time since
    /// the last poll, not since the last frame — the refusal countdown runs on the same clock it
    /// is ticked on, so changing the HUD's poll interval cannot silently change how long a
    /// sentence stays up.
    /// </summary>
    public void Tick(double delta)
    {
        if (HideSeekDriver.Instance is not { Synced: true } driver)
        {
            Visible = false;
            return;
        }

        HideSeekView view = driver.View;
        Visible = true;

        // A refusal arrives in ONE message and is gone from the next, so it is latched on arrival
        // rather than read as a level. Re-arming on the same reason restarts the hold, which is
        // what a player pressing twice expects.
        if (view.Refusal != HideSeekRefusal.None)
        {
            string sentence = HideSeekText.RefusalSentence(view.Refusal);
            if (sentence.Length > 0)
            {
                bool fresh = _refusalLeft <= 0.0 || sentence != _refusalText;
                _refusalText = sentence;
                _refusalLeft = RefusalHoldSec;
                if (fresh)
                {
                    _refusal.Text = sentence;
                    _refusal.Visible = true;
                    HudMotion.FlashText(_refusal, HudTheme.AccentPrimary);
                }
            }
        }
        else if (_refusalLeft > 0.0)
        {
            _refusalLeft -= delta;
            if (_refusalLeft <= 0.0)
            {
                _refusal.Visible = false;
                _refusal.Text = string.Empty;
                _refusalText = string.Empty;
            }
        }

        TickIntercom(delta);

        // MATCH-1: one call, and the CHOICE of sentence is the text function's. During Tally the
        // strip carries the card ("ROUND 1 OF 2 · ADA hid · 3 sorted · found at 1:12 · BEN +48")
        // and at a match end the result ("MATCH 1 · ADA WINS 7–5"); in the holding room after a
        // match it says what the Start button will now do. Everywhere else it is the phase line
        // this widget has always drawn. Nothing about the LAYOUT changed — same one label, same
        // rung, same width policy — which is why the packet's "the change is in the text
        // function, not the widget" holds here literally.
        int self = (int)Multiplayer.GetUniqueId();
        string text = HideSeekText.StripLine(view, self, driver.NameOf, driver.Tuning);

        if (_painted && text == _lastLine)
            return;

        // Flash on a PHASE crossing, not on every changed string: the clock changes the line ten
        // times a second, and a readout that flashed each time would be the HUD twitching rather
        // than the round telling you something. Captured before _lastPhase is overwritten.
        bool phaseChanged = _painted && view.Phase != _lastPhase;
        bool first = !_painted;

        _painted = true;
        _lastLine = text;
        _lastPhase = view.Phase;
        _line.Text = text;

        // Suppressed on first paint: a client that joins mid-seek must not be told the seek just
        // started. It polls, it does not witness — the same rule the driver's own PhaseChanged
        // event follows for a joiner's first message.
        if (phaseChanged && !first)
            HudMotion.FlashText(_line, HudTheme.AccentPrimary);
    }

    /// <summary>
    /// <b>The intercom lamp</b> (VOICE-1): "INTERCOM: &lt;name&gt;" while a voice from another
    /// room is coming through the PA.
    ///
    /// <para><b>The non-audio half of a consequence whose primary channel is audio</b> —
    /// <c>INTERACTION-BIBLE.md</c> §8.2. The PA route is a filtered, quiet, deliberately distant
    /// voice, and the seeker taunting the hider through a wall is the bluff the whole startle
    /// hangs on. A hider who cannot tell whether anything was said is missing the mechanic, not
    /// missing flavour. It carries the NAME rather than just lighting, because with more than two
    /// players in a later cut "somebody is lying to you" and "that one is lying to you" are
    /// different pieces of information.</para>
    ///
    /// <para><b>It is deliberately not a mute control and deliberately not a warning.</b> Voice
    /// is always on (Talon, 2026-09-19); this reports, it does not gate.</para>
    ///
    /// <para><b>Read from the route, not from the round.</b> It asks <c>VoiceManager</c> who is
    /// actually being played on the PA bus right now, so a lamp that lit while the resolver was
    /// unwired would be lying about the one thing it exists to confirm.</para>
    /// </summary>
    private void TickIntercom(double delta)
    {
        (int id, string name) = MpFoundation.Voice.VoiceManager.Instance.PaSpeakerNow();
        if (id != 0)
        {
            string line = HideSeekText.IntercomLine(name);
            if (line != _intercomText)
            {
                _intercomText = line;
                _intercom.Text = line;
                HudMotion.FlashText(_intercom, HudTheme.AccentPrimary);
            }
            _intercom.Visible = true;
            _intercomLeft = IntercomHoldSec;
            return;
        }

        if (_intercomLeft <= 0.0)
            return;
        _intercomLeft -= delta;
        if (_intercomLeft > 0.0)
            return;
        _intercom.Visible = false;
        _intercom.Text = string.Empty;
        _intercomText = string.Empty;
    }
}
