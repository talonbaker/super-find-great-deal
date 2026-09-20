using Godot;

namespace MpFoundation.Ui.Design;

/// <summary>Which lighting condition the interface is under. <b>Not two skins</b> — the same
/// paper objects, lit differently (PAPER &amp; FIRELIGHT kill rule 2: "any night idea that is
/// not expressible as a token change on the day object is wrong by definition"). Every
/// component in the game is built once and rendered at whichever temperature is current.</summary>
public enum UiTemperature : byte
{
    /// <summary>Paper at noon. Flat-lit, bright construction colours, full pages.</summary>
    Day = 0,

    /// <summary>The same scrapbook by firelight. Near-black ground, most of the page
    /// surrendered to shadow, and the accent on the few things that matter. Sinister is
    /// subtraction — nothing is added to make it frightening.</summary>
    Night = 1,
}

/// <summary>
/// <b>The stock.</b> Construction paper in a fixed palette, and nothing else. This is the
/// closed material kit's colour half: a colour that is not on this list may not appear in the
/// interface, and the kit grows only deliberately (kill rule 1 — one PR, one review).
///
/// <para>These are <i>raw</i> values with no meaning attached. Screens never read them. They
/// exist to be assembled into the two <see cref="UiTokens"/> sets below, which is where a
/// colour acquires a job. Editing a value here restains every surface that token set feeds —
/// that is the point.</para>
/// </summary>
public static class UiPaper
{
    // --- day stock: construction paper under an open sky ------------------------------------

    /// <summary>The picnic table the scrapbook lies on — the deepest day ground.</summary>
    public static readonly Color TableTan = new("e3d5ba");

    /// <summary>The cream sheet. The main page of every day screen.</summary>
    public static readonly Color Cream = new("f2e8d5");

    /// <summary>A lighter cutout laid on the sheet — cards, panels.</summary>
    public static readonly Color CreamLight = new("fbf4e6");

    /// <summary>A cutout lifted above a cutout — dropdowns, popups.</summary>
    public static readonly Color CreamLift = new("fdf9f0");

    /// <summary>A well pressed into the page — inputs, sunken rows.</summary>
    public static readonly Color CreamSunken = new("e6d9bf");

    /// <summary>Small tags and chips.</summary>
    public static readonly Color CreamChip = new("e9ddc5");

    /// <summary>Graphite. The darkest mark a child's pencil makes on the page.</summary>
    public static readonly Color Graphite = new("241c14");

    /// <summary>Softer pencil — body weight.</summary>
    public static readonly Color GraphiteSoft = new("3d3226");

    /// <summary>Faded pencil — captions, annotations.</summary>
    public static readonly Color GraphiteFaint = new("5c4e3c");

    /// <summary>Pencil-grey: the untaped, inert mark. Still legible — see
    /// <see cref="UiTokens.InkDisabled"/> for why disabled is never illegible here.</summary>
    public static readonly Color Pencil = new("675943");

    /// <summary>The inert fill by day: paper with the colour drained out of it. Deliberately
    /// near-neutral — a merely <i>dimmer</i> accent still reads as clickable, so a disabled
    /// control has to leave the accent's hue family, not just its brightness.</summary>
    public static readonly Color PencilFill = new("ded7c9");

    // Camp primaries. Decoration ranks only — content is always real text, never a mark.
    public static readonly Color Tomato = new("c23a24");
    public static readonly Color Marigold = new("e0a03a");
    public static readonly Color Grass = new("5f8038");
    public static readonly Color Lake = new("2f6f88");

    // --- night stock: the same paper, by firelight -------------------------------------------

    /// <summary>Near-black. The dark the page is surrendered to.</summary>
    public static readonly Color NightGround = new("0a0910");

    /// <summary>The sheet, barely lit.</summary>
    public static readonly Color NightSheet = new("12111a");

    /// <summary>A card catching a little of the fire.</summary>
    public static readonly Color NightCard = new("1a1824");

    /// <summary>A card lifted toward the fire — dropdowns, popups.</summary>
    public static readonly Color NightLift = new("232030");

    /// <summary>A well, deeper in shadow than the card around it.</summary>
    public static readonly Color NightSunken = new("0e0d15");

    /// <summary>Small tags and chips at night.</summary>
    public static readonly Color NightChip = new("1f1d2a");

    /// <summary>Paper white, warmed by firelight — the brightest ink at night.</summary>
    public static readonly Color PaperWhite = new("f5efe3");

    /// <summary>Body ink at night. Deliberately bright: dark reading is measurably worse, so
    /// night body text targets ~6:1 rather than the 4.5:1 floor (direction brief, "the
    /// discipline underneath").</summary>
    public static readonly Color PaperWarm = new("ddd5c6");

    /// <summary>Caption ink at night.</summary>
    public static readonly Color PaperDim = new("b3a996");

    /// <summary>The inert mark at night — pencil-grey, still legible.</summary>
    public static readonly Color PaperInert = new("989080");

    /// <summary>
    /// The inert fill at night. Neutral for the same reason <see cref="PencilFill"/> is: the night
    /// surfaces carry a cool tint, and a tinted disabled fill sits close enough to a live one to
    /// read as available.
    ///
    /// <para><b>UI-3, 2026-08-29 — this went from NEARLY neutral to ACTUALLY neutral, and the
    /// reason is worth keeping.</b> It was <c>26252a</c>: (38, 37, 42), a faint violet lean, at
    /// saturation 0.119. That was comfortably inert while the accent was ember (saturation 0.750),
    /// and <c>UiKitStateTests.Disabled_ReadsDisabled_NotJustDimmer</c> — which requires the
    /// disabled fill to sit under HALF the saturation of every live state — passed with three
    /// times the margin it needed.
    ///
    /// <para>Moonlight is saturation 0.238. Half of that is 0.1189, and the old inert fill was
    /// 0.1190. The rule did not change and the test was not wrong; the disabled fill had simply
    /// been coasting on the accent being loud, and the moment the accent went quiet the gap closed
    /// to a ten-thousandth. <c>262626</c> is saturation <b>0</b> — it cannot be closed again by any
    /// future accent, which is the property that actually wanted fixing. Value is preserved to
    /// within a fifth of an L* unit (14.97 -> 15.16), so nothing about how dark a disabled control
    /// looks has moved; only its hue has, and moving off the hue is the entire point of the
    /// token.</para></para></summary>
    public static readonly Color PaperInertFill = new("262626");

    // Night primaries: the same hues, desaturated and darkened. Decoration only.
    public static readonly Color TomatoNight = new("8f4436");
    public static readonly Color MarigoldNight = new("9b7a3c");
    public static readonly Color GrassNight = new("4b6039");
    public static readonly Color LakeNight = new("2c5266");

    // --- the one accent, shared by both temperatures -----------------------------------------
    //
    // UI-3, 2026-08-29 — THE EMBER ACCENT IS GONE FROM THE INTERFACE, BY INSTRUCTION.
    // Talon, second-pass playtest notes, note 1: "The colors for the UI on the main menu is
    // perfect... after the initial 'start game' screen section, it's back to the orange accent
    // color. I would prefer this main UI color, the cooler color, to be places around and
    // replace all the orange in the game UIs."
    //
    // What was here was Ember #e8863a / EmberHi #f4a866 / EmberDim #cc7a30. Following MENU-2's
    // precedent on the same instruction one screen earlier, it was DELETED rather than
    // desaturated — a half-orange is the thing he objected to the first time. The replacement is
    // the main menu's own accent family, lifted value-for-value from MenuLook so the two are
    // literally the same colour rather than merely a close match.
    //
    // WARM LIGHT IN THE WORLD IS UNTOUCHED and must stay that way: MenuLook.GlowColour and
    // every fire VFX are off-frame light inside the picture, not interface.
    // The split MenuLook states is now the whole game's: firelight lives in the world, moonlight
    // lives on the interface. (Nothing in scripts/game/world/ ever consumed these colours.)

    /// <summary><b>Moonlight = the thing that matters.</b> One accent, shared across both
    /// temperatures, used <i>once per screen</i>. The moment a second thing on a screen is
    /// accent-coloured, the first one stops meaning anything.
    ///
    /// <para>This is <c>MenuLook.Accent</c> exactly — the sky family of the approved title screen
    /// taken to its light end. It cannot clash with the frame behind it because it <i>is</i> the
    /// frame's colour.</para></summary>
    public static readonly Color Moonlight = new("9db4ce");

    /// <summary>Moonlight brightening — hover, and the accent-on-accent case. One ramp step up;
    /// with a dark label the direction of the step and the direction of the contrast agree, so
    /// brighter is simply better (10.00:1 against <see cref="OnMoonlight"/>).</summary>
    public static readonly Color MoonlightHi = new("b2c6dc");

    /// <summary>Moonlight banked — pressed, and quiet accent fills. 6.47:1 against
    /// <see cref="OnMoonlight"/>, so press can be carried by colour as well as by the 2px depress
    /// and the collapsed shadow.</summary>
    public static readonly Color MoonlightDim = new("86a0bc");

    /// <summary>The label on the accent plate: deep night, from the night sky's own family. A
    /// dark label on a light plate — the inverse of what the ember ran, and the reason the accent
    /// no longer has a ceiling imposed on it by its own text.</summary>
    public static readonly Color OnMoonlight = new("101a28");

    /// <summary>Danger by day: something was lost, or is about to be. Never gore, never a
    /// warning colour used decoratively.</summary>
    public static readonly Color Scorch = new("9c2f18");

    /// <summary>Danger at night. Lifted well past "a red that survives the dark", because it also
    /// has to separate from the ember accent in <b>value</b> and not merely in hue (ART-BIBLE §3):
    /// the bible's own rule puts danger and accents both in the warm half, so on a near-black
    /// ground a mid red sat 1.13:1 from the ember — indistinguishable in greyscale, which is what
    /// a colourblind player is reading.
    ///
    /// <para>Going darker cannot work: clearing 4.5:1 against the night card and separating from
    /// the ember pull in opposite directions, and the window is empty. So it goes pale — scorched
    /// rather than alarming, which suits a register that bans gore and means "loss" by danger
    /// anyway.</para></summary>
    public static readonly Color ScorchNight = new("ffd0c6");
}

/// <summary>
/// <b>A complete set of semantic tokens: everything the interface is allowed to paint with, at
/// one temperature.</b> Two instances exist — <see cref="Day"/> and <see cref="Night"/> — and
/// they are the ONLY two. Every screen, widget, HUD scrap and flow card resolves its colour
/// through a token name here; none of them knows a hex value.
///
/// <para><b>This is the file Talon edits to restain the game.</b> Change
/// <see cref="SurfaceCard"/> in <see cref="Night"/> and every card, popup, panel and scrap in
/// the night half of the game moves with it — menus, HUD, tally, loss screen — because there
/// is no second place for a card colour to live. That property is the whole point of the
/// overhaul: three styling systems collapsed to one.</para>
///
/// <para><b>Counts are deliberate</b> and are the overhaul's acceptance bar (B2 target table):
/// six ink tokens (was 27 distinct text colours), twelve fill tokens (was 46), <b>one</b>
/// scrim (was five, spread 0.45–0.85).</para>
///
/// <para>Pure data — <see cref="Color"/> is a struct with no engine dependency — so the
/// Godot-free xUnit suite measures contrast on the real shipped values rather than on a copy
/// of them.</para>
/// </summary>
public sealed record UiTokens
{
    // --- ground: twelve fills, and no thirteenth ---------------------------------------------

    /// <summary>The deepest ground — behind everything, the table or the dark.</summary>
    public required Color PageGround { get; init; }

    /// <summary>The sheet a screen's content sits on.</summary>
    public required Color PageSheet { get; init; }

    /// <summary>A card or panel laid on the sheet.</summary>
    public required Color SurfaceCard { get; init; }

    /// <summary>A surface lifted above a card — dropdowns, popups, tooltips.</summary>
    public required Color SurfaceRaised { get; init; }

    /// <summary>A well pressed into a surface — text fields, sunken rows.</summary>
    public required Color SurfaceSunken { get; init; }

    /// <summary>Small tags, chips, pills of information.</summary>
    public required Color SurfaceChip { get; init; }

    /// <summary><b>The one scrim.</b> Every modal, overlay and state screen dims with this and
    /// nothing else. The audit found five near-identical dim strengths across the project
    /// (0.45–0.85); a reader cannot tell them apart, so they were never a design, only drift.
    ///
    /// <para>The loss screen is the deliberate exception and it is NOT a scrim: it paints
    /// <see cref="PageGround"/> at full opacity, because the run is over and the frame should
    /// say so. A screen that is fully replaced is a ground, not a dim.</para></summary>
    public required Color Scrim { get; init; }

    /// <summary>The fill of something inert.</summary>
    public required Color DisabledFill { get; init; }

    /// <summary>The one accent at rest. Named by its <i>job</i> rather than by a material since
    /// UI-3 (2026-08-29): it used to be <c>Ember</c>, and a token whose name says "orange" while
    /// it holds moonlight is how the next reader reintroduces the orange.</summary>
    public required Color Accent { get; init; }

    /// <summary>The one accent, brightening.</summary>
    public required Color AccentHi { get; init; }

    /// <summary>The one accent, banked.</summary>
    public required Color AccentDim { get; init; }

    /// <summary>Danger fill — loss, destruction, the irreversible verb.</summary>
    public required Color Danger { get; init; }

    // --- ink: six, and no seventh -------------------------------------------------------------

    /// <summary>Rank 1. Headings and the one number that matters.</summary>
    public required Color InkRank1 { get; init; }

    /// <summary>Rank 2. Body — the text a player actually reads.</summary>
    public required Color InkRank2 { get; init; }

    /// <summary>Rank 3. Captions, field labels, annotations.</summary>
    public required Color InkRank3 { get; init; }

    /// <summary>Inert text. <b>Still passes 4.5:1</b> — the audit measured the shipped disabled
    /// colour at 3.36–3.79:1 on all three surfaces, which is not "de-emphasised", it is
    /// unreadable. Disabled reads as inert through the <i>material</i> (untaped, unfilled, no
    /// border), never through illegibility.</summary>
    public required Color InkDisabled { get; init; }

    /// <summary>Text drawn on the accent. Dark, and it stayed dark when the accent stopped being
    /// orange: white on the ember was ~2.6:1 and white on the moonlight is 2.13:1, so the defect
    /// this token exists to prevent — "just use white on the button" — is if anything worse now.
    /// At Night it is <see cref="UiPaper.OnMoonlight"/>, the main menu's own label colour, which
    /// measures 8.21:1 on the accent, 10.00:1 on <see cref="AccentHi"/> and 6.47:1 on
    /// <see cref="AccentDim"/>.</summary>
    public required Color InkOnAccent { get; init; }

    /// <summary>Text carrying loss or error.</summary>
    public required Color InkDanger { get; init; }

    /// <summary>Text drawn <i>directly on the scrim</i> rather than on a card above it — the
    /// word PAUSED, the loss line. Paper-white at both temperatures, because a scrim is a
    /// shadow falling across the page in both: even the day scrim is darkness, so ink on it
    /// behaves the way ink on the night page behaves. Seven ink tokens total, against a
    /// measured baseline of 27 distinct text colours.</summary>
    public required Color InkOnScrim { get; init; }

    // --- lines and light ----------------------------------------------------------------------

    /// <summary>The hairline that separates one paper edge from another. <b>Polarity flips with
    /// temperature</b> — dark on cream, light on near-black — which is exactly the kind of thing
    /// a single hardcoded value cannot do and is why hairlines belong in the token set.</summary>
    public required Color Hairline { get; init; }

    /// <summary>The hairline where an edge has to carry weight.</summary>
    public required Color HairlineStrong { get; init; }

    /// <summary><b>Focus.</b> By day the fastener — the dark metal of the paperclip that moves
    /// to the focused element. By night the torchlight beam: the controller cursor is literally a
    /// light, so focus = light = on-thesis, and the warm halo around it is the ember.
    ///
    /// <para>Focus is the loudest state in the system, always, on every interactive thing,
    /// controller-first. <b>It cannot be the accent.</b> The first version of this token was
    /// ember at both temperatures, which put an ember ring on an ember button at 1.00:1 — the
    /// focus state was, by day, literally invisible on the one control it matters most on. Focus
    /// has to separate from whatever it rings, which means it lives outside the accent.</para></summary>
    public required Color Focus { get; init; }

    /// <summary>The shadow a lifted cutout casts. Warm and shallow by day, near-black at
    /// night.</summary>
    public required Color Shadow { get; init; }

    /// <summary><b>The outline behind text that has no paper under it.</b> A toast over the
    /// woods, a pickup value over a lit field, a marker on the map: these composite against an
    /// arbitrary frame, so there is no contrast pair to check and the outline is what actually
    /// guarantees legibility.
    ///
    /// <para>It is a token rather than a black each caller types because the outline's weight is
    /// a real decision — too little and the text dissolves over a bright frame, too much and it
    /// grows inward across the glyph stems and chokes small text into grey mush, which is
    /// precisely what the previous HUD did.</para></summary>
    public required Color TextOutline { get; init; }

    /// <summary>Which temperature this set describes. Carried on the set so a consumer that is
    /// handed tokens can branch on lighting without asking a global.</summary>
    public required UiTemperature Temperature { get; init; }

    // --- the two sets --------------------------------------------------------------------------

    /// <summary>Paper at noon.</summary>
    public static readonly UiTokens Day = new()
    {
        Temperature = UiTemperature.Day,

        PageGround = UiPaper.TableTan,
        PageSheet = UiPaper.Cream,
        SurfaceCard = UiPaper.CreamLight,
        SurfaceRaised = UiPaper.CreamLift,
        SurfaceSunken = UiPaper.CreamSunken,
        SurfaceChip = UiPaper.CreamChip,
        Scrim = new Color(UiPaper.Graphite, 0.72f),
        DisabledFill = new Color(UiPaper.PencilFill, 0.55f),
        Accent = UiPaper.Moonlight,
        AccentHi = UiPaper.MoonlightHi,
        AccentDim = UiPaper.MoonlightDim,
        Danger = UiPaper.Scorch,

        InkRank1 = UiPaper.Graphite,
        InkRank2 = UiPaper.GraphiteSoft,
        InkRank3 = UiPaper.GraphiteFaint,
        InkDisabled = UiPaper.Pencil,
        InkOnAccent = UiPaper.Graphite,
        InkDanger = UiPaper.Scorch,
        InkOnScrim = UiPaper.PaperWhite,

        Hairline = new Color(UiPaper.Graphite, 0.18f),
        HairlineStrong = new Color(UiPaper.Graphite, 0.38f),
        Focus = UiPaper.Graphite,
        Shadow = new Color(UiPaper.Graphite, 0.22f),
        TextOutline = new Color(0f, 0f, 0f, 0.80f),
    };

    /// <summary>The same scrapbook by firelight.</summary>
    public static readonly UiTokens Night = new()
    {
        Temperature = UiTemperature.Night,

        PageGround = UiPaper.NightGround,
        PageSheet = UiPaper.NightSheet,
        SurfaceCard = UiPaper.NightCard,
        SurfaceRaised = UiPaper.NightLift,
        SurfaceSunken = UiPaper.NightSunken,
        SurfaceChip = UiPaper.NightChip,
        Scrim = new Color(UiPaper.NightGround, 0.86f),
        DisabledFill = new Color(UiPaper.PaperInertFill, 0.55f),
        Accent = UiPaper.Moonlight,
        AccentHi = UiPaper.MoonlightHi,
        AccentDim = UiPaper.MoonlightDim,
        Danger = UiPaper.ScorchNight,

        InkRank1 = UiPaper.PaperWhite,
        InkRank2 = UiPaper.PaperWarm,
        InkRank3 = UiPaper.PaperDim,
        InkDisabled = UiPaper.PaperInert,
        InkOnAccent = UiPaper.OnMoonlight,
        InkDanger = UiPaper.ScorchNight,
        InkOnScrim = UiPaper.PaperWhite,

        Hairline = new Color(1f, 1f, 1f, 0.12f),
        HairlineStrong = new Color(1f, 1f, 1f, 0.28f),
        Focus = UiPaper.PaperWhite,
        Shadow = new Color(0f, 0f, 0f, 0.55f),
        TextOutline = new Color(0f, 0f, 0f, 0.85f),
    };

    /// <summary>The set for a temperature. The only lookup a consumer needs.
    ///
    /// <para><b>Light mode is retired (Talon, 2026-08-15): "theme light mode stinks."</b>
    /// This always resolves to <see cref="Night"/> regardless of <paramref name="temperature"/>
    /// — every screen, HUD and flow card ships the one dark look, always, including during the
    /// in-round day phase. <see cref="Day"/> is left defined rather than deleted: reverting this
    /// one line (<c>temperature == UiTemperature.Day ? Day : Night</c>) is the whole cost of
    /// bringing a light temperature back, which is deliberately the point — "easily changeable,"
    /// not merely reusable.</para></summary>
    public static UiTokens For(UiTemperature temperature) => Night;

    /// <summary>
    /// <b>Dusk, as a value.</b> The page losing its light is a single interpolation between the
    /// two sets — which is the whole reason day and night are token sets over one component set
    /// rather than two skins. There is nothing to cross-fade <i>between</i>: the objects never
    /// change, only the light on them.
    ///
    /// <para><paramref name="k"/> runs 0 (fully <paramref name="from"/>) to 1 (fully
    /// <paramref name="to"/>). The blended set reports the temperature it is nearest, so anything
    /// branching on temperature mid-transition commits once rather than flickering.</para>
    /// </summary>
    public static UiTokens Lerp(UiTokens from, UiTokens to, float k)
    {
        k = Mathf.Clamp(k, 0f, 1f);
        Color L(Color a, Color b) => a.Lerp(b, k);

        return new UiTokens
        {
            Temperature = k >= 0.5f ? to.Temperature : from.Temperature,

            PageGround = L(from.PageGround, to.PageGround),
            PageSheet = L(from.PageSheet, to.PageSheet),
            SurfaceCard = L(from.SurfaceCard, to.SurfaceCard),
            SurfaceRaised = L(from.SurfaceRaised, to.SurfaceRaised),
            SurfaceSunken = L(from.SurfaceSunken, to.SurfaceSunken),
            SurfaceChip = L(from.SurfaceChip, to.SurfaceChip),
            Scrim = L(from.Scrim, to.Scrim),
            DisabledFill = L(from.DisabledFill, to.DisabledFill),
            Accent = L(from.Accent, to.Accent),
            AccentHi = L(from.AccentHi, to.AccentHi),
            AccentDim = L(from.AccentDim, to.AccentDim),
            Danger = L(from.Danger, to.Danger),

            InkRank1 = L(from.InkRank1, to.InkRank1),
            InkRank2 = L(from.InkRank2, to.InkRank2),
            InkRank3 = L(from.InkRank3, to.InkRank3),
            InkDisabled = L(from.InkDisabled, to.InkDisabled),
            InkOnAccent = L(from.InkOnAccent, to.InkOnAccent),
            InkDanger = L(from.InkDanger, to.InkDanger),
            InkOnScrim = L(from.InkOnScrim, to.InkOnScrim),

            Hairline = L(from.Hairline, to.Hairline),
            TextOutline = L(from.TextOutline, to.TextOutline),
            HairlineStrong = L(from.HairlineStrong, to.HairlineStrong),
            Focus = L(from.Focus, to.Focus),
            Shadow = L(from.Shadow, to.Shadow),
        };
    }

    /// <summary>Decoration ranks — the camp primaries, at this temperature. Marks and stickers
    /// only: <b>content is always real text</b>, never a crayon shape, so nothing here may be
    /// the sole carrier of information (ART-BIBLE §3 — separate in value, not just hue).</summary>
    public Color[] Primaries => Temperature == UiTemperature.Day
        ? new[] { UiPaper.Tomato, UiPaper.Marigold, UiPaper.Grass, UiPaper.Lake }
        : new[] { UiPaper.TomatoNight, UiPaper.MarigoldNight, UiPaper.GrassNight, UiPaper.LakeNight };
}
