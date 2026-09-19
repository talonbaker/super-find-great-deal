using System.Collections.Generic;
using Godot;
using MpFoundation.Game.Presentation;
using MpFoundation.Ui.Design;
using MpFoundation.Ui.Flow;

namespace MpFoundation.Ui.Hud;

/// <summary>
/// In-engine headless proof for PLAY-1 (<c>--hudlayout-selftest</c>; Run-HudLayoutTest.ps1 gates
/// on it): builds the real HUD CanvasLayers in a live tree and measures the RECTANGLES THEY
/// ACTUALLY OCCUPY, then asserts the layout defects Talon reported from play on 2026-08-14 are
/// gone.
///
/// <para><b>Why measured and not reviewed.</b> All three defects shipped past code review because
/// each widget was individually correct: two surfaces anchored <c>CenterTop</c> at the shared
/// margin look right in every diff and land on each other in every frame, and a full-rect
/// <c>ColorRect</c> at 55% alpha does not read as "covers the screen" until you are standing in
/// the dark behind it. Nothing short of measuring the drawn geometry catches that class of bug.</para>
///
/// <para><b>What it asserts</b></para>
/// <list type="number">
/// <item><b>The coverage law.</b> The nightfall treatment plays while the player keeps camera and
/// movement, so <see cref="UiCoverageLaw"/> must permit it: aim point clear, under the fraction
/// bound. <b>With a positive control</b> — the same collector, the same measurement and the same
/// predicate are run against a deliberately violating full-screen wash, which must come back
/// covered ~1.0, aim-point covered, and NOT permitted. An absence check whose measurement is
/// broken passes forever; this one has to show it can return the other answer.</item>
/// <item><b>The upper-centre column.</b> Day/phase readout, winter-cache strip and phase toast,
/// pairwise disjoint — asserted in the cache's NOT-MET state and again in its MET state, because
/// Talon's note pins the collision to that readout being up and its wording changes with the
/// stage.</item>
/// <item><b>The bottom-left column.</b> The room code, placed by the same
/// <see cref="UiColumns.PlaceRoomCode"/> the real Gameplay scene calls, lands inside the frame
/// with a real rectangle — the carry HUD it used to be measured against is not part of this
/// build, so the check is that the placement still resolves to a drawable box rather than to
/// the off-frame position the pre-fix offsets produced.</item>
/// <item><b>The bottom-right column.</b> The permanent "ESC · HOW TO PLAY" hint (2026-09-04):
/// on the frame, inside the coverage law, disjoint from the room code and from every top-centre
/// occupant, landing where <see cref="UiColumns.HowToPlayHintTop"/> says it does, and gone —
/// rung and all — while world UI is suppressed. The rung cross-check is there because the first
/// version of this widget placed itself from its own published height, came up as a
/// near-full-frame panel, and satisfied every other assertion in this list while doing it.</item>
/// </list>
/// </summary>
public partial class HudLayoutSelfTest : Node
{
    private readonly List<string> _failures = new();

    private GameHud _hud = null!;
    private QuotaStripWidget _strip = null!;
    private PhaseToastLayer _toasts = null!;
    private Label _roomCode = null!;
    private FakePlaythroughDriver _driver = null!;
    private SubViewport _frame = null!;

    public override void _Ready() => Run();

    private async void Run()
    {
        // Same discipline as ScreenFlowSelfTest: an unhandled throw in an async void would leave
        // the process alive with no verdict, so every path reaches Finish.
        try
        {
            await RunChecks();
        }
        catch (System.Exception e)
        {
            Fail($"unhandled: {e}");
        }
        Finish();
    }

    private async System.Threading.Tasks.Task RunChecks()
    {
        // Measured inside a SubViewport at the PROJECT'S authored size, not in the root window.
        // The headless display server hands out a 64x64 root, and every anchored offset in the
        // HUD resolves against the frame it is in — so measuring the root would put half the
        // interface off-screen and report coverage of zero for a plate that covers a sixth of the
        // real frame. A SubViewport's size is explicit and identical on every machine.
        _frame = new SubViewport
        {
            Name = "AuthoredFrame",
            Size = new Vector2I(
                (int)ProjectSettings.GetSetting("display/window/size/viewport_width", 1280),
                (int)ProjectSettings.GetSetting("display/window/size/viewport_height", 720)),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
        };
        AddChild(_frame);

        Rect2 frame = _frame.GetVisibleRect();
        GD.Print($"HUDLAYOUT: frame={frame.Size} (project's authored viewport)");
        if (frame.Size.X < 2f || frame.Size.Y < 2f)
        {
            // A degenerate frame would let every coverage assertion below pass vacuously.
            Fail($"frame is {frame.Size} — nothing measured here would mean anything");
            return;
        }

        Build();
        await WaitFrames(4);

        AssertCoverageLaw(frame);
        await AssertUpperCentreColumn();
        AssertBottomLeftColumn();
        await AssertBottomRightColumn(frame);
    }

    private void Build()
    {
        _driver = new FakePlaythroughDriver();
        _driver.CommitSynced();

        // Constructed directly rather than through Attach: the HUD no-ops on a headless peer by
        // design (it is a client render surface), and this test is precisely a headless
        // measurement of what it lays out. Its data sources stay null, which every widget
        // already treats as "not spawned yet" — the geometry under test is offset-driven and does
        // not depend on a live world.
        _hud = new GameHud { Name = "GameHud" };
        _frame.AddChild(_hud);
        _strip = new QuotaStripWidget(_driver, _driver) { Name = "QuotaStripWidget" };
        _frame.AddChild(_strip);
        _toasts = new PhaseToastLayer { Name = "PhaseToastLayer" };
        _frame.AddChild(_toasts);

        var roomCodeLayer = new CanvasLayer { Name = "RoomCodeLayer" };
        _frame.AddChild(roomCodeLayer);
        _roomCode = new Label { Name = "RoomCodeLabel", Text = "Room: ABCDE" };
        roomCodeLayer.AddChild(_roomCode);
        UiColumns.PlaceRoomCode(_roomCode);
    }

    // --- 1: the coverage law, with its positive control -------------------------------------

    private void AssertCoverageLaw(Rect2 frame)
    {
        NightfallOverlay overlay = _toasts.Nightfall;
        overlay.ShowNightfall();
        if (!overlay.Playing)
        {
            Fail("the nightfall treatment did not start — nothing was measured");
            return;
        }

        // Geometry, not opacity: the law is about what the surface takes away from the frame, and
        // a plate is just as much in the way at the top of its fade as at the bottom. The rect is
        // read straight off the plate the overlay exposes.
        var painted = new List<Rect2> { overlay.Plate.GetGlobalRect() };
        if (painted[0].Size.X < 1f || painted[0].Size.Y < 1f)
        {
            Fail($"the nightfall plate measured {painted[0].Size} — the treatment laid out to nothing, "
                + "so a coverage pass here would be measuring an empty rectangle");
            return;
        }

        // Which BRANCH of the law applies is derived, not assumed. The treatment plays over live
        // play, and "live play" here means two measurable things: it does not raise the shared
        // world-UI suppression flag the pause overlay uses to stop the player acting, and nothing
        // it draws accepts a mouse event. If either changed, the surface would have taken the
        // player with it and would be allowed the whole frame — so the check has to look.
        bool controlsTaken = WorldUi.Suppressed;
        bool cameraTaken = AnyControlAcceptsInput(overlay);
        GD.Print($"HUDLAYOUT: nightfall cameraTaken={cameraTaken} controlsTaken={controlsTaken}");
        if (cameraTaken || controlsTaken)
        {
            Fail("the nightfall treatment now takes input — that is a different (legal) shape for "
                + "this beat, but it is a design change to the beat and this assertion no longer "
                + "measures what it was written for");
            return;
        }

        (float covered, bool coversAim) = UiCoverageLaw.Measure(frame.Size, painted);
        GD.Print($"HUDLAYOUT: nightfall plate={painted[0]} covered={covered:F4} aim={coversAim}");
        if (!UiCoverageLaw.Permitted(covered, coversAim, cameraTaken, controlsTaken))
            Fail($"the nightfall treatment covers {covered:P1} of the frame (aim point covered: {coversAim}) "
                + $"while the player still has camera AND movement — the standing law allows at most "
                + $"{UiCoverageLaw.MaxCoveredFraction:P0} and never the aim point");

        // --- the positive control -----------------------------------------------------------
        // Everything above is an ABSENCE claim. Re-run the identical measurement against the
        // shape this overlay used to be — one full-rect wash — and require the check to report
        // the violation. If this passes, the assertion above proved nothing.
        var control = new CanvasLayer { Name = "CoverageLawPositiveControl" };
        _frame.AddChild(control);
        var wash = new ColorRect
        {
            Name = "ViolatingWash",
            Color = new Color(UiThemeService.Tokens.PageGround, 0.55f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        wash.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        control.AddChild(wash);
        wash.Size = frame.Size; // the layout pass has not run for a node added this frame.

        (float ctlCovered, bool ctlAim) = UiCoverageLaw.Measure(frame.Size, new[] { wash.GetGlobalRect() });
        GD.Print($"HUDLAYOUT: positive control covered={ctlCovered:F4} aim={ctlAim}");
        if (ctlCovered < 0.99f || !ctlAim)
            Fail($"positive control: a full-screen wash measured {ctlCovered:P1} coverage, aim={ctlAim} — "
                + "the MEASUREMENT is broken, so the law assertion above is worthless");
        else if (UiCoverageLaw.Permitted(ctlCovered, ctlAim, cameraTaken: false, controlsTaken: false))
            Fail("positive control: the coverage law PERMITTED a full-screen wash over a player who "
                + "still has camera and controls — the predicate cannot detect the violating state");
        control.QueueFree();
    }

    // --- 2: the upper-centre column ---------------------------------------------------------

    private async System.Threading.Tasks.Task AssertUpperCentreColumn()
    {
        // Not met: demand announced, nothing banked. This is the state the strip spends most of a
        // round in, and the one whose wording is shortest.
        _driver.CommitRoundIntro(1, ScriptedPlaythrough.DemandRound1);
        _driver.CommitRoundLive();
        _toasts.ShowLine(PhaseToastText.TextFor(Game.World.PhaseEventKind.DayToDusk)!);
        await WaitFrames(4);
        AssertTopCentreDisjoint("cache NOT met");

        // Met: banked past the demand. Talon's note pins the collision to the met/not-met readout,
        // and the two stages are different strings of different widths — so both get measured.
        _driver.CommitBank(ScriptedPlaythrough.DemandRound1);
        await WaitFrames(4);
        if (QuotaStripWidget.StageOf(_driver.CumulativeBanked, _driver.CumulativeDemand) != 3)
            Fail("the fake never reached the MET stage — the met-state assertion measured the wrong thing");
        AssertTopCentreDisjoint("cache MET");
    }

    private void AssertTopCentreDisjoint(string context)
    {
        var blocks = new List<(string Name, Rect2 Rect)>();
        if (FirstDescendant<DayPhaseWidget>(_hud) is { } dayPhase)
            blocks.Add(("day/phase readout", dayPhase.GetGlobalRect()));
        if (!_strip.Visible)
            Fail($"{context}: the winter-cache strip is hidden — the collision it is half of cannot be measured");
        else if (FirstChild<PanelContainer>(_strip) is { } strip)
            blocks.Add(("winter-cache strip", strip.GetGlobalRect()));
        // The toast label is a DIRECT child; the nightfall treatment nested under the same layer
        // has Labels of its own, and a depth-first search would find those instead.
        if (FirstChild<Label>(_toasts) is { } toast && toast.Text.Trim().Length > 0)
            blocks.Add(("phase toast", toast.GetGlobalRect()));

        if (blocks.Count < 3)
        {
            Fail($"{context}: found {blocks.Count} of the 3 upper-centre blocks — a disjointness check "
                + "over blocks that are not there passes for the wrong reason");
            return;
        }

        foreach ((string name, Rect2 rect) in blocks)
            GD.Print($"HUDLAYOUT: [{context}] {name} = {rect}");
        AssertPairwiseDisjoint(blocks, context);
    }

    // --- 3: the bottom-left column ------------------------------------------------------------

    private void AssertBottomLeftColumn()
    {
        Rect2 code = _roomCode.GetGlobalRect();
        Rect2 frame = _frame.GetVisibleRect();
        GD.Print($"HUDLAYOUT: room code = {code}");

        if (code.Size.X < 1f || code.Size.Y < 1f)
        {
            Fail($"the room code measured {code.Size} — an empty rectangle is not a readable room code");
            return;
        }
        if (!frame.Encloses(code))
            Fail($"the room code {code} is not inside the frame {frame} — a co-op session nobody can join");
    }

    // --- 4: the bottom-right column -----------------------------------------------------------

    /// <summary>The permanent "ESC · HOW TO PLAY" hint, measured where it actually lands (2026-09-04,
    /// the packet that removed the level's first-run How-to-Play door). Three claims, and each one
    /// is a way this widget could be shipped broken while reading fine in a diff:
    /// <list type="number">
    /// <item>It is <b>on the frame</b>. Its offsets are derived from its own measured size against
    /// a bottom-right anchor, so a size that has not resolved yet — the exact trap the room code
    /// fell into — puts it off the edge with a perfectly sensible-looking constant.</item>
    /// <item>It <b>obeys the coverage law</b>, over a player who still has camera and controls.</item>
    /// <item>It <b>misses everything else on the frame</b> — the room code beside it and every
    /// occupant of the top-centre column. Reserving a rung in <c>UiColumns</c> is a promise about
    /// arithmetic; this is the check that the arithmetic reached the rectangle.</item>
    /// </list></summary>
    private async System.Threading.Tasks.Task AssertBottomRightColumn(Rect2 frame)
    {
        if (FirstDescendant<HudHowToPlayHint>(_hud) is not { } hint)
        {
            // Not a soft skip: this suite runs with --world fullhud, whose profile turns the hint
            // ON. Absent means the widget was cut or the profile flag was flipped, and either way
            // every assertion below would pass by measuring nothing.
            Fail("no HudHowToPlayHint under the HUD — the --world fullhud profile is supposed to "
                + "build it, so there is nothing here to measure");
            return;
        }

        Rect2 rect = hint.GetGlobalRect();
        GD.Print($"HUDLAYOUT: how-to-play hint = {rect}");

        if (rect.Size.X < 1f || rect.Size.Y < 1f)
        {
            Fail($"the how-to-play hint measured {rect.Size} — an empty rectangle is not a readable hint");
            return;
        }
        if (!frame.Encloses(rect))
            Fail($"the how-to-play hint {rect} is not inside the frame {frame} — the one surface "
                + "telling the player where the controls are is off the screen");

        // The rung reached the rectangle. This is the check that would have caught the placement
        // feedback loop (UiColumns.PlaceHowToPlayHint's trap) before it went out in a capture: the
        // widget came up 1251x340 and every other assertion here was happy with it — on the frame,
        // under the coverage bound, overlapping nothing. What it was NOT was where the column said
        // it was. A published height nothing cross-checks is a number, not a measurement.
        float expectedTop = frame.Size.Y + UiColumns.HowToPlayHintTop;
        float expectedRight = frame.Size.X - UiColumns.Edge;
        if (Mathf.Abs(rect.Position.Y - expectedTop) > 1f)
            Fail($"the how-to-play hint's top is {rect.Position.Y} but the column publishes "
                + $"{expectedTop} (height {UiColumns.HowToPlayHintHeight}) — the drawn box and the "
                + "rung disagree, so anything stacking off this rung would land on it");
        if (Mathf.Abs(rect.End.X - expectedRight) > 1f)
            Fail($"the how-to-play hint's right edge is {rect.End.X}, not the frame margin's "
                + $"{expectedRight} — it is not anchored to the corner it claims");

        // The law, on the same terms the nightfall check above uses: this rides GameHud, which
        // never takes input, so the permitted branch is the size-and-aim-point one.
        (float covered, bool coversAim) = UiCoverageLaw.Measure(frame.Size, new[] { rect });
        GD.Print($"HUDLAYOUT: hint covered={covered:F4} aim={coversAim}");
        if (!UiCoverageLaw.Permitted(covered, coversAim, cameraTaken: false, controlsTaken: false))
            Fail($"the how-to-play hint covers {covered:P1} of the frame (aim point covered: "
                + $"{coversAim}) while the player still has camera AND movement");

        var neighbours = new List<(string Name, Rect2 Rect)> { ("room code", _roomCode.GetGlobalRect()) };
        if (FirstDescendant<DayPhaseWidget>(_hud) is { } dayPhase)
            neighbours.Add(("day/phase readout", dayPhase.GetGlobalRect()));
        if (_strip.Visible && FirstChild<PanelContainer>(_strip) is { } strip)
            neighbours.Add(("winter-cache strip", strip.GetGlobalRect()));
        if (FirstChild<Label>(_toasts) is { } toast && toast.Text.Trim().Length > 0)
            neighbours.Add(("phase toast", toast.GetGlobalRect()));

        foreach ((string name, Rect2 other) in neighbours)
            if (rect.Intersects(other))
                Fail($"the how-to-play hint {rect} overlaps the {name} {other}");

        // It goes away with the rest of the HUD, and it gives its rung back when it does. A
        // permanent hint is exactly the kind of widget that gets left floating over a pause menu,
        // and "it rides GameHud so it must hide" is an argument, not a measurement.
        bool wasSuppressed = WorldUi.Suppressed;
        WorldUi.Suppressed = true;
        await WaitFrames(3);
        if (hint.IsVisibleInTree())
            Fail("the how-to-play hint is still drawn while world UI is suppressed — it would sit "
                + "over the pause overlay");
        if (UiColumns.HowToPlayHintHeight != 0f)
            Fail($"the how-to-play hint still reserves {UiColumns.HowToPlayHintHeight} px of the "
                + "bottom-right column while hidden — the column has to close up behind it");

        WorldUi.Suppressed = wasSuppressed;
        await WaitFrames(3);
        if (!hint.IsVisibleInTree())
            Fail("the how-to-play hint did not come back when world UI was un-suppressed — closing "
                + "the pause menu would leave the player with no route to the controls");
    }

    // --- helpers ------------------------------------------------------------------------------

    private void AssertPairwiseDisjoint(List<(string Name, Rect2 Rect)> blocks, string context)
    {
        for (int i = 0; i < blocks.Count; i++)
            for (int j = i + 1; j < blocks.Count; j++)
                if (blocks[i].Rect.Intersects(blocks[j].Rect))
                    Fail($"{context}: {blocks[i].Name} {blocks[i].Rect} overlaps "
                        + $"{blocks[j].Name} {blocks[j].Rect}");
    }

    /// <summary>Whether anything a layer draws would swallow a mouse event — the codebase's own
    /// mark of a surface that has taken the pointer (and with it the look) away from the world.</summary>
    private static bool AnyControlAcceptsInput(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            if (child is Control { MouseFilter: not Control.MouseFilterEnum.Ignore })
                return true;
            if (AnyControlAcceptsInput(child))
                return true;
        }
        return false;
    }

    private static T? FirstChild<T>(Node parent) where T : Node
    {
        foreach (Node child in parent.GetChildren())
            if (child is T hit)
                return hit;
        return null;
    }

    private static T? FirstDescendant<T>(Node parent) where T : Node
    {
        foreach (Node child in parent.GetChildren())
        {
            if (child is T hit)
                return hit;
            if (FirstDescendant<T>(child) is { } deeper)
                return deeper;
        }
        return null;
    }

    private async System.Threading.Tasks.Task WaitFrames(int frames)
    {
        for (int i = 0; i < frames; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private void Fail(string message)
    {
        _failures.Add(message);
        GD.PrintErr($"[hudlayout-selftest] FAIL: {message}");
    }

    private void Finish()
    {
        if (_failures.Count == 0)
            GD.Print("[hudlayout-selftest] PASS");
        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }
}
