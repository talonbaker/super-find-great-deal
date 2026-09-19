using Godot;

namespace MpFoundation.Ui.Menu;

/// <summary>
/// <b>Every number the title screen is built from, in one file.</b>
///
/// <para><b>MENU-2, 2026-08-29 — this screen is now NIGHT.</b> MENU-1 built it to the light previz
/// Talon approved on the morning of 2026-08-28 and reversed the same afternoon ("clinical and
/// sterile"; target "dark, moody, campfire-at-night"), and the build has since gone <i>darker in
/// general</i> by standing ruling
/// (<c>docs/agents/handoffs/2026-08-28-TALON-RULING-darker-in-general.md</c>). MENU-1's structure is
/// direction-independent and was kept whole: this was a <b>re-skin</b>, not a rebuild. The frame it
/// now reproduces is <c>docs/agents/handoffs/2026-08-28-menu-previz/menu-night-1.jpg</c>
/// (<c>menu_night.py</c> at <c>MOOD=night</c>).</para>
///
/// <para><b>THE ORANGE IS GONE, AND THAT IS AN INSTRUCTION, NOT A TASTE CALL.</b> Talon, 2026-08-29
/// (<c>docs/playtest/2026-08-29-talon-notes-bubble-test.md</c> note 5): <i>"I don't like the orange
/// bar that is the button for the host game simply please remove this orange it clashes with the
/// game aesthetic overall."</i> The ember accent — <c>Ember</c> / <c>EmberHover</c> /
/// <c>EmberPressed</c> / <c>OnEmber</c> — was deleted, not desaturated, and replaced by
/// <see cref="Accent"/>: moonlight, drawn from this screen's own sky family. <b>The night previz's
/// accent was a BRIGHTER orange</b> (<c>#E28D4A</c>) than the one he objected to, so that one part
/// of it is deliberately not reproduced; everything else in it is. The warm campfire glow stays,
/// because it is off-frame light inside the picture rather than a UI element — see
/// <see cref="GlowColour"/>.</para>
///
/// <para><b>The accent story, stated once.</b> Iridescence carries identity (the wordmark rule,
/// BT-9's authorised placement); moonlight carries action (the primary button). Warm light exists
/// only in the world, never on the interface. That split is why nothing on the UI layer now has a
/// hue that can clash with the picture behind it.</para>
///
/// <para><b>This file used to hold four campfire "looks" to choose between.</b> That job is done —
/// the variant sheet is gone, the look is chosen, and a preset array with one entry in it is a
/// worse thing than a constant. What replaced it is the same idea at the next stage: one named home
/// for the agreed values, so tuning is an edit here rather than a hunt through four files.</para>
///
/// <para><b>The palette IS in <c>UiTokens</c> now — that question closed on 2026-08-29.</b> This
/// paragraph used to say the opposite: that pushing this screen's accent through the shared token
/// set "would restyle every dark surface in the game to match a menu", and that the HUD half of
/// Talon's request was a separate, unanswered question deliberately not touched by MENU-2. It was
/// a correctly-parked decision, and it came unparked the same day. Talon, second-pass notes 1 and
/// 5: <i>"I would prefer this main UI color, the cooler color, to be places around and replace all
/// the orange in the game UIs"</i> and <i>"Please make the rest of the menus match this main menu
/// UI... with the same accents and buttons and things."</i> Restyling every dark surface to match
/// the menu is precisely what he asked for.</para>
///
/// <para><b>What that means for this file, which is the part that matters to anyone editing it.</b>
/// UI-3 copied <see cref="Accent"/>, <see cref="AccentHover"/>, <see cref="AccentPressed"/> and
/// <see cref="OnAccent"/> into <c>UiPaper.Moonlight</c> / <c>MoonlightHi</c> / <c>MoonlightDim</c> /
/// <c>OnMoonlight</c> by value. They are duplicated, not shared, and that is on purpose: this screen
/// is a rendered frame with its own design space and its own measured contrast pairs, and making it
/// read a token set that a HUD packet can restain would put the approved frame one edit away from
/// changing without anyone looking at it. <b>The duty runs one way.</b> If Talon retunes this
/// screen's accent, carry the new values across to <c>UiPaper</c>. Never the reverse — a token edit
/// must not reach back into the approved frame. Note 1 also says, in the same breath, "please keep
/// them exactly as they are".</para>
/// </summary>
public static class MenuLook
{
    // =============================================================================================
    // The design space
    // =============================================================================================

    /// <summary>The frame the approved previz was rendered in, and the space every layout number
    /// below is expressed in. The UI layer is scaled by <c>viewport height / 900</c>, so the screen
    /// is proportionally identical to the approved image at any resolution instead of being correct
    /// at one and cramped at the rest.</summary>
    public const float DesignWidth = 1600f;
    public const float DesignHeight = 900f;

    /// <summary>The 8 px grid. Everything vertical is a multiple of this; the four steps below are
    /// the only ones used, which is the same closed-scale discipline
    /// <see cref="Design.UiScale"/> enforces on the rest of the interface (its own steps are
    /// 4/8/12/16/24 on a 4 px grid — this screen's display-scale layout needs the larger half).</summary>
    public const int Grid = 8;

    public const int SpaceS = Grid * 2;      // 16
    public const int SpaceM = Grid * 3;      // 24
    public const int SpaceL = Grid * 4;      // 32
    public const int SpaceXl = Grid * 6;     // 48

    // =============================================================================================
    // Blur — the agreed 18 px world / 9 px bubbles, and how they are actually paid for
    // =============================================================================================

    /// <summary>World blur, as a fraction of the frame width. The agreed number is <b>18 px at
    /// 1920 x 1080</b>; expressing it as a fraction is what keeps the frame the same picture at
    /// other resolutions rather than the same pixel count.
    ///
    /// <para><b>18, down from the light frame's 30, and it is the night previz's own number.</b> A
    /// dark frame has far less contrast for a blur to eat: at 30 px the blocks stop reading as
    /// objects at all and the night becomes a flat gradient with nothing standing in it.</para></summary>
    public const float WorldBlurFraction = 18f / 1920f;

    /// <summary>Bubble blur, same convention. The agreed number is <b>9 px at 1920 x 1080</b>.</summary>
    public const float BubbleBlurFraction = 9f / 1920f;

    /// <summary>What fraction of the output resolution each layer actually renders at.
    ///
    /// <para><b>This is the whole performance story of the screen and it is not a compromise.</b>
    /// An 18 px gaussian at 1920 x 1080 needs a kernel reaching 54 px; rendering the world at a
    /// quarter scale makes that a 14 px reach over a sixteenth of the pixels — about 200x less work
    /// — and loses nothing, because an 18 px blur has already destroyed every detail smaller than
    /// the quarter-res grid. The bubbles keep half scale: their film has real structure at the
    /// 9 px blur's scale and a quarter-res proxy would visibly coarsen it.</para></summary>
    public const float WorldProxyScale = 0.25f;
    public const float BubbleProxyScale = 0.5f;

    // =============================================================================================
    // The world — palette, camera and haze
    // =============================================================================================

    /// <summary>Sky, top of frame to bottom of frame. The gradient runs the WHOLE height and the
    /// ground is painted over its lower half, so the colour that actually shows at the skyline is
    /// the midpoint of these two, not the second one.</summary>
    public static readonly Color SkyTop = Rgb(9, 13, 24);
    public static readonly Color SkyHorizon = Rgb(34, 46, 70);

    /// <summary><b>The blue depth effect Talon asked to keep.</b> Everything reads bluer with
    /// distance. Same mechanism the campfire backdrop used; it has been through a light key and
    /// back into a night one, and the mechanism itself has never changed.</summary>
    public static readonly Color Haze = Rgb(28, 40, 62);

    /// <summary>Ground, near and far. The far end is its own blue before the haze reaches it, which
    /// is what gives the ground a gradient of its own rather than just fading into the air.</summary>
    public static readonly Color GroundNear = Rgb(30, 32, 38);
    public static readonly Color GroundFar = Rgb(26, 37, 57);

    /// <summary>How much of the residual haze the ground takes after its own far mix.</summary>
    public const float GroundHazeExtra = 0.25f;

    /// <summary>Beer-Lambert density and the curve over it.</summary>
    public const float HazeDensity = 0.030f;
    public const float HazeCurve = 0.78f;

    /// <summary>The light key, as one multiplier on every block. The ground does not take it.
    /// 0.62 at night: dark, but still readable as blocks rather than as one silhouette.</summary>
    public const float Key = 0.62f;

    /// <summary>Poster shading: the three face values a block is drawn with. Not a renderer's
    /// shading and not trying to be — see <c>resources/shaders/ui_menu_world.gdshader</c>.
    ///
    /// <para><b>The face separation WIDENS at night rather than flattening.</b> 1.00 / 0.52 / 0.30
    /// against the light frame's 1.00 / 0.84 / 0.68. Once the fill itself is dark the only thing
    /// left carrying a block's third dimension is a real key direction — the top catches the moon
    /// and the flanks fall away hard. Compressing the ratios instead, which is the reflex when a
    /// picture gets dark, produces 56 flat shapes.</para></summary>
    public const float ShadeTop = 1.00f;
    public const float ShadeFront = 0.52f;
    public const float ShadeSide = 0.30f;

    /// <summary>Camera. Height 1.75 m, seven metres back, no pitch — so the horizon sits exactly on
    /// the frame's centre line, which is what the approved frame's ground/sky split depends on.
    /// The FOV is the vertical angle equivalent to the previz's 58-degree HORIZONTAL field at 16:9:
    /// 2 * atan(0.5625 * tan(29 deg)).</summary>
    public static readonly Vector3 CameraPosition = new(0f, 1.75f, 7f);
    public const float CameraFovDegrees = 34.64f;

    /// <summary>How far the camera drifts, in metres.
    ///
    /// <para>Talon asked for a screen that is moving. This is deliberately at the very bottom of
    /// perceptible: a few centimetres over most of a minute, which parallaxes the near blocks
    /// against the far ones without ever reading as a camera move. It runs off the same phase as
    /// the bubbles, so reduced motion stops all of it at once.</para></summary>
    public const float CameraDriftX = 0.14f;
    public const float CameraDriftY = 0.06f;

    /// <summary><b>Block desaturation, at the source.</b> Talon: "It's overall too colourful."
    /// Heavy blur turns saturated colour into poster paint, so pulling the saturation out has to
    /// happen in the palette as well as in the blur.
    ///
    /// <para><b>10% at night, down from the light frame's 42%, and that is not a reversal of his
    /// note.</b> "Too colourful" was a complaint about a HIGH-KEY frame, where saturated fills at
    /// 84% of full value are the loudest thing on screen. Here <see cref="Key"/> is 0.62 and the
    /// flanks are at 0.30, so the same fills land dark on their own; pulling the saturation out as
    /// well would leave six identical greys and nothing would be "the green one" any more. The
    /// saturation that survives is spent almost entirely on <see cref="RimGain"/>, which is where a
    /// dark frame can actually afford it.</para></summary>
    public const float BlockDesaturation = 0.10f;

    /// <summary>The six block colours, BEFORE desaturation. <see cref="BlockColours"/> is what
    /// actually gets used; these are kept so the pull is legible as a transformation rather than
    /// arriving as six unexplained values.</summary>
    private static readonly Color[] RawBlockColours =
    {
        Rgb(228, 126, 96), Rgb(108, 186, 158), Rgb(240, 196, 96),
        Rgb(118, 156, 214), Rgb(214, 140, 182), Rgb(170, 206, 116),
    };

    public static readonly Color[] BlockColours = BuildBlockColours();

    /// <summary>Seed for the block scatter. Fixed, so the screen is the same picture every launch —
    /// a title screen that reshuffles itself is a title screen nobody can compare to a capture.</summary>
    public const ulong BlockSeed = 7;

    public const int ScatterBlockCount = 52;

    // =============================================================================================
    // The bubbles and the thread
    // =============================================================================================

    /// <summary>How fast the shared phase advances, in phase units per second.
    ///
    /// <para>The underline's colour window turns over on <c>sin(phase * 1.7)</c>, so a full cycle is
    /// 2*pi/1.7 = 3.696 phase units; at this rate that is <b>one 24-second cycle</b>. That is the
    /// same order as the iridescent thread's own 20-second drift and two orders slower than the
    /// 120-300 ms this interface uses for state transitions — which is what stops it competing for
    /// attention. It is never seen moving, only ever noticed to have moved.</para></summary>
    public const float PhaseRate = 0.154f;

    /// <summary>The phase the approved frame (<c>menu-deep-3.jpg</c>) was rendered at, and
    /// therefore the phase a reduced-motion screen and a reproducible capture both rest at.</summary>
    public const float RestPhase = 1.70f;

    /// <summary><b>The underline's agreed settings.</b> A 58 nm window — a zoomed-in slice of the
    /// spectrum, not a rainbow — at a base opacity of 0.30 rising to a peak of
    /// 0.30 + 0.34 = <b>0.64</b> where the travelling highlight is.</summary>
    public const float RuleSpanNm = 58f;
    public const float RuleBaseAlpha = 0.30f;
    public const float RuleHighlightGain = 0.34f;
    public const float RulePeakAlpha = RuleBaseAlpha + RuleHighlightGain;

    /// <summary>Optical path multiplier for a film seen at the angle a screen rule is seen at:
    /// 2 * 1.34 * cos(refracted). Sharing it with the menu's bubbles is what makes the rule the
    /// same material rather than merely another colourful thing.</summary>
    public const float RuleOpdScale = 2.5629f;

    // =============================================================================================
    // The scrim
    // =============================================================================================

    /// <summary><b>The scrim's colour, and at night it is a SHADOW rather than a glow.</b> The light
    /// frame washed the column toward white to lift the field the dark ink sat on; the night frame
    /// pushes it toward black for exactly the same reason with the ink inverted. Same shader, same
    /// curve, one uniform — which is the whole argument for having had the tint be a uniform.
    ///
    /// <para>Not pure black. A dead-black wash under a blue-black picture reads as a rectangle cut
    /// out of the frame; carrying the sky's own hue into the shadow keeps it part of the
    /// picture.</para></summary>
    public static readonly Color ScrimTint = Rgb(4, 7, 14);

    /// <summary>0.50 at night, against the light frame's 0.62 — the night previz's own number. A
    /// darkening wash needs less strength than a lifting one because the field it is working on is
    /// already most of the way down.</summary>
    public const float ScrimPeak = 0.50f;
    public const float ScrimXEnd = 0.52f;
    public const float ScrimYCentre = 0.46f;
    public const float ScrimYReach = 0.46f;

    /// <summary>How much of the scrim's vertical tail takes the C2 correction that removes the
    /// previz's one known defect (a faintly visible lower-left column boundary).
    ///
    /// <para><b>Narrow on purpose.</b> A first attempt replaced the whole falloff with smootherstep;
    /// it removed the edge and made the scrim about 2.8x too strong at the top-left — measured
    /// against the approved frame at +23 RGB on the sky and +20 on the ground. The approved frame's
    /// brightness IS the approval, so the curve is the previz's own and only the tail is
    /// corrected.</para></summary>
    public const float ScrimYToe = 0.22f;

    /// <summary>Film grain, kept only as a dither. The frame is one enormous smooth gradient and an
    /// 8-bit sky bands visibly without it — and a NIGHT sky bands worse, because the whole ramp now
    /// lives in the bottom two dozen code values where the quantisation steps are widest relative
    /// to the range. The vignette the same shader offers stays OFF: this frame is already dark at
    /// the edges and a second darkening would only crush what is left of the corners.</summary>
    public const float GrainStrength = 0.018f;
    public const float GrainSpeed = 12f;

    // =============================================================================================
    // The night layer — the three things a dark frame needs that a light one does not
    // =============================================================================================

    /// <summary><b>The campfire, off-frame low-left.</b> A single wide additive gaussian, centred
    /// outside the picture, in UV.
    ///
    /// <para><b>It is what makes the frame a place rather than a void.</b> Without it the night is
    /// an evenly graded backdrop that could be anywhere; with it there is something warm just past
    /// the edge of the shot that the ground is catching, which is the entire premise of the game
    /// stated in one soft shape. It is also the one piece of warm colour left in the design — see
    /// the type header on why the UI's own accent went cool instead.</para></summary>
    public static readonly Vector2 GlowCentre = new(0.10f, 0.86f);
    public static readonly Vector2 GlowReach = new(0.30f, 0.34f);

    /// <summary>Peak additive contribution of the glow, in sRGB 0..1. Firelight, warm and dim: it
    /// lifts the near ground by about 116/255 at its centre and is under a code value by the time
    /// it reaches the type column.</summary>
    public static readonly Color GlowColour = Rgb(116, 58, 20);

    /// <summary><b>The star field, and it is applied AFTER the world blur.</b> Painted before it,
    /// an 18 px gaussian erases it completely — this cost a render to find. Stars are the one
    /// backdrop element on this screen that stays sharp, and sharpness is precisely what makes them
    /// read as distance rather than as specks of dust on the lens.</summary>
    public const float StarFieldHeight = 0.55f;

    /// <summary>One star per cell of this many design-frame widths, so the field is the same
    /// picture at any resolution rather than the same star count. 1/41 of the width gives the night
    /// previz's ~520 stars over the top 55% at 16:9.</summary>
    public const float StarCellFraction = 1f / 41f;

    /// <summary>Star radius in previz pixels (the 1600-wide design frame), scaled with the frame.
    /// Sub-pixel on purpose at the small end: a star that is reliably a whole pixel wide is a
    /// pinprick, and a field of pinpricks all the same size reads as a texture.</summary>
    public const float StarRadiusMinPx = 0.5f;
    public const float StarRadiusMaxPx = 1.5f;

    /// <summary>Brightness range before the vertical fade, and the peak additive amplitude.</summary>
    public const float StarBrightnessMin = 0.30f;
    public const float StarBrightnessMax = 1.00f;
    public const float StarAmplitude = 235f / 255f;

    /// <summary>Cool white. Stars are not the campfire and must not borrow its temperature.</summary>
    public static readonly Color StarTint = new(0.88f, 0.92f, 1.00f);

    /// <summary>Fixed, like <see cref="BlockSeed"/>: a title screen that reshuffles its own sky
    /// every launch cannot be compared against a capture.</summary>
    public const float StarSeed = 19f;

    // =============================================================================================
    // The rim — how section colour survives a dark frame
    // =============================================================================================

    /// <summary><b>Section colour moves from the FILL to a 3 px top rim.</b>
    ///
    /// <para><b>The problem it solves.</b> On a dark base a coloured fill has two options and both
    /// are losses: stay dark, and stop being identifiable at all; or get lifted, and take the whole
    /// palette back toward the light frame that was rejected. A saturated line on the top edge is
    /// the third option — the body falls to silhouette while "the green one" stays nameable, and
    /// the total quantity of saturated pixels on screen goes DOWN rather than up.</para>
    ///
    /// <para>Same technique DARK-1 uses on the playtest scene's zone sections. Stated here because
    /// this is where it was measured first, not because the menu owns it.</para></summary>
    public const float RimWidthPx = 3f;

    /// <summary>The rim takes the block's own colour lifted 6%, at full saturation — it does NOT
    /// take <see cref="Key"/>, <see cref="ShadeTop"/> or the haze. That is the point of it: the rim
    /// is the one place on a night block where the section's colour is stated undimmed.</summary>
    public const float RimGain = 1.06f;

    /// <summary>Rim opacity at the camera, and its decay per metre. Distance has to take the rim
    /// away or the far field turns into a mesh of bright wires — which is the failure mode of every
    /// edge-highlight effect and the reason this one fades faster than the blocks do.</summary>
    public const float RimAlpha = 235f / 255f;
    public const float RimDistanceFalloff = 0.022f;

    // =============================================================================================
    // Contact smudges and the bubble film — the two things the dark base re-tunes
    // =============================================================================================

    /// <summary>The soft ellipse under each block, and at night it is both darker and stronger:
    /// 86/255 at the camera against the light frame's 52/255. A contact shadow's job is to say the
    /// block is standing on something, and it has to do that against a ground that is already
    /// nearly as dark as the shadow itself.</summary>
    public static readonly Color SmudgeColour = Rgb(6, 8, 14);
    public const float SmudgePeakAlpha = 86f / 255f;
    public const float SmudgeDistanceFalloff = 0.024f;

    /// <summary><b>The bubble becomes emissive.</b> Against a dark ground the film is the brightest
    /// thing on screen, and iridescence works far harder here than it ever did over white — the
    /// interference colours are being read against black instead of competing with a lit sky. Six
    /// numbers say it: the film's own contrast goes UP (0.94 from 0.72), the flat white lift it was
    /// floated on goes DOWN (0.12 from 0.28) because a dark frame no longer needs the bubble to
    /// carry the sky's brightness, the speculars strengthen, and a faint halo appears OUTSIDE the
    /// disc, which is the part that actually reads as "giving off light" rather than "being
    /// lit".</summary>
    public const float BubbleFilmGain = 0.94f;
    public const float BubbleBaseLift = 30f / 255f;
    public const float BubbleSpecGain = 235f / 255f;
    public const float BubbleAlphaGain = 1.25f;
    public const float BubbleSpecAlpha = 0.60f;
    public const float BubbleHaloGain = 0.16f;

    // =============================================================================================
    // Type and colour — bone ink on a night field, and no orange anywhere
    // =============================================================================================

    /// <summary><b>Bone. Warm, off the fire, and the wordmark's colour.</b> The light frame's ink
    /// was a deep navy-slate drawn from the sky; inverted onto a night field the same reasoning
    /// gives a bone that is slightly warm rather than a clinical white — it belongs to the picture
    /// instead of sitting on it, and it is the one place the campfire's temperature reaches the
    /// type. Measured <b>11.68:1</b> against the worst-case field under the column.</summary>
    public static readonly Color Ink = Rgb(234, 229, 218);

    /// <summary>The strongest ink on the screen, and the colour of every live secondary control and
    /// the build stamp. <b>Lighter</b> than <see cref="Ink"/>, which is the same relationship the
    /// light frame had with the sign flipped: the strongest ink is the one furthest from the field.
    ///
    /// <para><b>Small live text does not get a muted tone here, and that is a measured decision
    /// rather than a stylistic one.</b> The night previz put SETTINGS and QUIT on (150,156,170) and
    /// the build stamp on (138,145,158). Against the field those items ACTUALLY sit on — the row
    /// runs out to x 700, well past the scrim's reach at x_end 0.52, so it lands on unscrimmed
    /// blurred blocks whose 95th percentile is (71,81,91) — those measure <b>2.94:1</b> and
    /// <b>2.50:1</b>. Both fail. This is the light frame's own failure repeating with the palette
    /// inverted, and it gets the same answer MENU-1 gave: rank moves off colour and onto size and
    /// weight, where this screen can afford it. 19 px at weight 600 and 15 px at weight 400 against
    /// a 78 px wordmark is plenty of hierarchy without spending legibility on it.</para></summary>
    public static readonly Color InkStrong = Rgb(240, 237, 231);

    /// <summary>Disabled secondary text, and the ONLY place a muted tone survives — the one job
    /// WCAG exempts. Against <see cref="InkStrong"/> it is an unmistakable drop in weight, which is
    /// the whole signal: a disabled control that reads as clearly as a live one is a worse failure
    /// than a quiet one.</summary>
    public static readonly Color InkDisabled = Rgb(122, 132, 148);

    /// <summary><b>THE accent, and it is the button's FILL, not its ring. It is moonlight.</b>
    ///
    /// <para><b>The orange is gone by instruction.</b> Talon, 2026-08-29: <i>"I don't like the
    /// orange bar that is the button for the host game simply please remove this orange it clashes
    /// with the game aesthetic overall."</i> What was here was ember-700 (154,85,39) with a white
    /// label at a measured 5.67:1. It has not been desaturated, warmed down or re-ramped — it has
    /// been removed, because "remove this orange" does not have a version of itself that is still
    /// orange.</para>
    ///
    /// <para><b>Why moonlight specifically, and not simply a lighter grey.</b> This is the sky's own
    /// family taken to its light end: the same hue the whole frame recedes into, at the value a
    /// primary action needs on a dark screen. Three consequences, all of them the point. It cannot
    /// clash with the picture, because it IS the picture's colour. It states the screen's one
    /// remaining warm/cool split — firelight lives in the world (see <see cref="GlowColour"/>),
    /// moonlight lives on the interface — so the warmth Talon objected to on a control is still in
    /// the frame, where it never bothered him. And it leaves iridescence, which is the house accent
    /// by BT-9, as the only saturated mark on the UI layer, which is where BT-9 says it
    /// belongs.</para>
    ///
    /// <para><b>Measured, not eyeballed:</b> <see cref="OnAccent"/> on this is <b>8.21:1</b> — AAA,
    /// and comfortably above the 5.67:1 the orange it replaces held, so removing the orange did not
    /// cost the button any legibility. The plate itself holds <b>6.38:1</b> against the frame it
    /// sits on, well past the 3:1 a control's own boundary owes.</para></summary>
    public static readonly Color Accent = Rgb(157, 180, 206);

    /// <summary>Hover: one ramp step brighter. <b>The light frame could not afford this step and
    /// this one can</b> — its ember was already at the top of what a white label could sit on, so
    /// hover had to be shortened to a half step to stay above AA. With a dark label the direction
    /// of the step and the direction of the contrast agree, so brighter is simply better:
    /// <b>10.00:1</b>, and 6.6 CIE L* clear of rest where about 2 is the perceptible
    /// threshold.</summary>
    public static readonly Color AccentHover = Rgb(178, 198, 220);

    /// <summary>Pressed: darker and shoved 2 px down. <b>6.47:1</b>.</summary>
    public static readonly Color AccentPressed = Rgb(134, 160, 188);

    /// <summary>The primary label. Deep night, from <see cref="SkyTop"/>'s family — a dark label on
    /// a light plate, which is the inverse of what the light frame ran and the reason the accent no
    /// longer has a ceiling imposed on it by its own text.</summary>
    public static readonly Color OnAccent = Rgb(16, 26, 40);

    /// <summary>Disabled: a dim slate plate, no accent, and visibly not a target. Its label pair
    /// measures <b>2.09:1</b>, which is intentional — WCAG exempts disabled controls. Note this
    /// inverted with the frame: on the light screen the disabled plate was LIGHTER than the accent
    /// and here it is much darker, because "recedes" is the property being preserved, not "grey
    /// is".</summary>
    public static readonly Color DisabledFill = Rgb(58, 64, 76);
    public static readonly Color DisabledInk = Rgb(104, 112, 126);

    /// <summary>The focus ring's two tones, <b>outermost first</b>. The ring is reserved for focus
    /// alone — the old button wore an ember ring at rest, so its resting state already looked
    /// focused and there was nothing left to express real focus with. In a controller-first game the
    /// focus ring is the entire cursor.
    ///
    /// <para><b>LIGHT outside, dark inside — and this is the exact reverse of what the light frame
    /// shipped, which is the whole lesson.</b> The indicator owes 3:1 against the colours it
    /// actually touches, and those are two different things: the frame outward, the accent plate
    /// inward. MENU-1 measured the light frame and found dark-outside/light-inside at 7.9:1 and
    /// 5.45:1 where the reflex order failed at 1.67:1 and 2.42:1, and wrote down that the reflex
    /// ("a white halo outside a dark ring") is correct only on a DARK interface. This is now a dark
    /// interface, so the order flips back, and it is re-measured rather than assumed:
    /// <b>12.31:1</b> outward against the frame, <b>8.31:1</b> inward against the accent. Nothing
    /// about this pair survives a palette change unmeasured.</para></summary>
    public static readonly Color FocusRingOuter = new(240f / 255f, 244f / 255f, 250f / 255f, 0.96f);
    public static readonly Color FocusRingInner = Rgb(16, 24, 42);

    // =============================================================================================
    // Layout, in design-space pixels
    // =============================================================================================

    /// <summary>The one left axis. Everything on the screen hangs off it.</summary>
    public const float AxisX = DesignWidth * 0.075f;          // 120

    public const float WordmarkTop = DesignHeight * 0.300f;   // 270

    /// <summary>Wordmark size in design-space pixels, and its letter-spacing.
    ///
    /// <para>78 px in the 1600-wide design frame — 94 device px at 1920 x 1080. It came DOWN from
    /// 96: at 96 the wordmark was the heaviest object in a frame whose subject is the bubble, and
    /// tracking buys back the presence the size gave up. <see cref="WordmarkTargetWidth"/> is what
    /// actually governs, because the shipped typeface is not the previz's condensed stand-in and a
    /// display line that overruns its column is a worse error than one a point off its nominal
    /// size.</para></summary>
    public const int WordmarkSize = 78;
    public const int WordmarkTracking = 5;

    /// <summary>How wide the wordmark should end up, as a fraction of the design width. The
    /// approved frame's is 615/1600. The size is scaled down from <see cref="WordmarkSize"/> if the
    /// real font would overrun it, never up.</summary>
    public const float WordmarkTargetWidth = 0.384f;

    public const float RuleTop = WordmarkTop + 96f + SpaceM;      // 390
    public const float RuleWidth = DesignWidth * 0.255f;          // 408
    public const float RuleHeight = 3f;

    public const float CtaTop = RuleTop + SpaceXl;                // 438
    public const float ButtonWidth = 232f;
    public const float ButtonHeight = 56f;
    public const int ButtonRadius = 10;
    public const int ButtonLabelSize = 22;
    public const int ButtonLabelTracking = 2;

    /// <summary>How far outside the control the focus ring reaches, and where the dark tone hands
    /// over to the light one. 8 px total: dark from 8 out to 5, light from 5 in to the edge — so the
    /// light tone is the one touching the control's own fill.</summary>
    public const int FocusRingOffset = 8;
    public const int FocusRingInnerSplit = 5;

    public const int SecondarySize = 19;
    public const int SecondaryTracking = 1;

    public const float CaptionTop = CtaTop + ButtonHeight + SpaceL + SpaceS;
    public const int CaptionSize = 15;

    // =============================================================================================
    // The two tiers, and why they are two layouts rather than one layout with more buttons
    // =============================================================================================
    //
    // Talon, 2026-08-30 playtest note 17, verbatim: "right when you hit start you go to what looks
    // like just the same menu with an additional two options which is really stupid… take away the
    // settings and quit and replace this with 'press start'. The studio mark is different than the
    // splash screen and the splash screen should be different than the main menu."
    //
    // The complaint is not that the second screen is ugly. It is that pressing Start LOOKED LIKE
    // NOTHING HAPPENED — same wordmark, same place on the axis, same row, two more words in it. A
    // transition a player cannot see is a transition that did not happen, so the fix has to be a
    // change of layout, not a change of contents. Above: the splash — the wordmark is the subject,
    // and one prompt. Below: the main menu — the wordmark steps back to a header and the
    // destinations become the subject, in a column rather than a row.

    /// <summary>The wordmark's header position on the MAIN MENU. It moves to the top of the frame
    /// and stops being the subject — the single clearest signal in a still that this is a different
    /// screen and not the same one with more on it.</summary>
    public const float MenuWordmarkTop = DesignHeight * 0.100f;   // 90

    /// <summary>How much of its splash size the wordmark keeps as a header. Applied to the FITTED
    /// size (see <see cref="MenuType.Wordmark"/>), never to the nominal one, so the column-fit rule
    /// still governs on a face that runs wide.</summary>
    public const float MenuWordmarkScale = 0.52f;

    public const float MenuRuleTop = MenuWordmarkTop + 60f + SpaceM;   // 174
    public const float MenuRuleWidth = RuleWidth * 0.62f;              // ~253

    /// <summary>Where the destination column starts. The list is vertical on the main menu and the
    /// splash has no list at all, so the two screens do not share a single line of layout.</summary>
    public const float MenuListTop = MenuRuleTop + SpaceXl;            // 222
    public const int MenuListSeparation = SpaceM;                      // 24

    // --- the splash's one affordance ------------------------------------------------------------

    /// <summary>"PRESS START", set as a prompt rather than a button: Talon wrote it as words, and a
    /// bordered control would read as one of the three the note just removed.</summary>
    /// <summary>The build stamp's position on the MAIN MENU. It sits under the CTA on the splash
    /// (the approved previz frame) and at the foot of the frame here, because the destination
    /// column now occupies the band the stamp used to have to itself — measured, not guessed: the
    /// first main-menu capture had QUIT and the stamp overlapping at 720p.</summary>
    public const float MenuCaptionTop = DesignHeight * 0.88f;   // 792

    public const int PressStartSize = 24;
    public const int PressStartTracking = 8;

    /// <summary>The prompt breathes between this alpha and 1 so it reads as an invitation rather
    /// than a caption. It runs off the screen's ONE phase number like everything else, so reduced
    /// motion stops it with the rest of the picture — at full alpha, never faded out, because the
    /// resting frame has to say what to do.</summary>
    public const float PressStartRestAlpha = 0.45f;

    /// <summary>Multiplier on the shared phase for the prompt's breath. With
    /// <see cref="PhaseRate"/> at 0.154/s this is a period of about five seconds — slow enough to
    /// read as breathing rather than blinking.</summary>
    public const float PressStartBreathRate = 8f;

    // =============================================================================================

    private static Color Rgb(int r, int g, int b) => new(r / 255f, g / 255f, b / 255f);

    /// <summary>Pull each colour 42% toward its own mean. Toward its OWN mean rather than toward a
    /// shared grey, so the six stay distinguishable from each other while every one of them gets
    /// quieter.</summary>
    private static Color[] BuildBlockColours()
    {
        var built = new Color[RawBlockColours.Length];
        for (int i = 0; i < RawBlockColours.Length; i++)
        {
            Color c = RawBlockColours[i];
            float mean = (c.R + c.G + c.B) / 3f;
            const float k = BlockDesaturation;
            built[i] = new Color(
                c.R * (1f - k) + mean * k,
                c.G * (1f - k) + mean * k,
                c.B * (1f - k) + mean * k);
        }
        return built;
    }
}
