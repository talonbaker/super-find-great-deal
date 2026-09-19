using System;
using System.IO;
using MpFoundation.Ui.Hud;
using Sail.Game.Bubble;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// <b>BT-8: what the tally READS, and the rule that decides whether the lever's press counts.</b>
///
/// <para>Two engine-free surfaces, tested here because a green scene suite cannot tell you WHY it
/// was green. <see cref="HudBubbleCount.Format"/> is the exact string the packet's first
/// acceptance criterion is stated on, and <see cref="BubbleResetGate"/> is the whole of criterion
/// 4 — "two presses within 1.2 s produce one reset broadcast" — which is arithmetic on a clock and
/// deserves to be proved exhaustively rather than sampled once in a three-process run.</para>
///
/// <para><b>What is deliberately NOT here.</b> Anything needing a scene tree: the widget appearing
/// only when a <see cref="BubbleCounter"/> exists, and the wobble firing on a pop, are both
/// properties of a live <c>GameHud</c> and are covered by the source-level assertions below plus
/// <c>tests/Run-BubbleSyncTest.ps1</c>. Constructing a Godot <c>Control</c> in xUnit would need an
/// engine this project does not boot for unit tests, and a mock of one would prove only that the
/// mock behaves.</para>
/// </summary>
public class HudBubbleCountTests
{
    // --- 1: the string on the screen --------------------------------------------------------

    [Theory]
    [InlineData(0, "x 0")]
    [InlineData(1, "x 1")]
    [InlineData(42, "x 42")]
    [InlineData(100, "x 100")]
    public void Format_IsTheBriefsString(int count, string expected) =>
        Assert.Equal(expected, HudBubbleCount.Format(count));

    [Fact]
    public void Format_CarriesNoTotal()
    {
        // The total lives on the pedestal, deliberately (packet: "no total — the total is on the
        // pedestal"). A slash creeping into this string is the readout quietly becoming a second
        // copy of the scoreboard, so it is asserted rather than left to review.
        Assert.DoesNotContain("/", HudBubbleCount.Format(42));
        Assert.Equal("x 42", HudBubbleCount.Format(42));
    }

    [Fact]
    public void UnsyncedText_IsNotAZero()
    {
        // A peer that has not been told the tally must not render a confident 0 — a late joiner
        // walking into a level at 38/100 would flash "x 0" for a frame. BubbleCounter.Synced
        // exists for exactly this, and this asserts the widget spends it.
        Assert.NotEqual(HudBubbleCount.UnsyncedText, HudBubbleCount.Format(0));
        Assert.DoesNotContain("0", HudBubbleCount.UnsyncedText);
    }

    // --- 2: the pedestal's string, which DOES carry the total --------------------------------

    [Fact]
    public void DisplayFormat_CarriesBothNumbers()
    {
        Assert.Equal("42 / 100", BubbleCounterDisplay.Format(42, 100));
        Assert.Equal("0 / 6", BubbleCounterDisplay.Format(0, 6));
    }

    [Fact]
    public void DisplayUnsyncedText_IsNotAZero() =>
        Assert.NotEqual(BubbleCounterDisplay.UnsyncedText, BubbleCounterDisplay.Format(0, 0));

    // --- 3: the lever's rule (packet acceptance criterion 4) ---------------------------------

    [Fact]
    public void FirstPress_IsAlwaysHonoured()
    {
        var gate = new BubbleResetGate();
        Assert.True(gate.TryPress(0));
        Assert.Equal(1, gate.Accepted);
        Assert.Equal(0, gate.Refused);
    }

    [Fact]
    public void TwoPressesInsideTheCooldown_ProduceOneReset()
    {
        var gate = new BubbleResetGate();
        Assert.True(gate.TryPress(10.0));
        Assert.False(gate.TryPress(10.0));                              // same frame
        Assert.False(gate.TryPress(10.0 + BubbleResetGate.CooldownSec - 0.001)); // one tick short
        Assert.Equal(1, gate.Accepted);
        Assert.Equal(2, gate.Refused);
    }

    [Fact]
    public void SixPlayersPressingAtOnce_ProduceOneReset()
    {
        // The realistic case, and the reason the gate is on the SERVER rather than on each
        // client: six peers all see the counter fill and all pull. One reset.
        var gate = new BubbleResetGate();
        int accepted = 0;
        for (int i = 0; i < 6; i++)
        {
            if (gate.TryPress(5.0 + i * 0.03))
                accepted++;
        }
        Assert.Equal(1, accepted);
        Assert.Equal(5, gate.Refused);
    }

    [Fact]
    public void APressAfterTheCooldown_IsHonouredAgain()
    {
        var gate = new BubbleResetGate();
        Assert.True(gate.TryPress(0));
        Assert.True(gate.TryPress(BubbleResetGate.CooldownSec));
        Assert.Equal(2, gate.Accepted);
    }

    [Fact]
    public void RemainingSec_CountsDownAndReachesZero()
    {
        var gate = new BubbleResetGate();
        Assert.Equal(0, gate.RemainingSec(0));  // never pressed: ready now
        gate.TryPress(4.0);
        Assert.Equal(BubbleResetGate.CooldownSec, gate.RemainingSec(4.0), 6);
        Assert.Equal(BubbleResetGate.CooldownSec / 2, gate.RemainingSec(4.0 + BubbleResetGate.CooldownSec / 2), 6);
        Assert.Equal(0, gate.RemainingSec(4.0 + BubbleResetGate.CooldownSec));
        Assert.Equal(0, gate.RemainingSec(400.0)); // never goes negative
    }

    [Fact]
    public void Cooldown_OutlastsTheLeversOwnSwing()
    {
        // Stated in BubbleResetGate's doc and worth pinning: a cooldown shorter than the swing
        // would let a second player's press land while the handle was still returning, so the
        // world would show one animation for two accepted resets.
        string lever = ReadRepoFile("scripts/game/bubble/BubbleResetLever.cs");
        double swing = ReadDouble(lever, "SwingOutSec") + ReadDouble(lever, "SwingBackSec");
        Assert.True(BubbleResetGate.CooldownSec > swing,
            $"cooldown {BubbleResetGate.CooldownSec}s must outlast the {swing}s swing");
    }

    // --- 4: the absence assertions the packet states as ABSENCE criteria ---------------------

    [Fact]
    public void TheHudWidget_AddsNoCanvasLayer()
    {
        // Acceptance criterion 2. Source-level because the property is about what the diff
        // CONTAINS: a new CanvasLayer would be a second suppression surface and a second thing
        // for UiColumns to not know about (the exact defect UiColumns was written for).
        foreach (string path in new[]
                 {
                     "scripts/ui/hud/HudBubbleCount.cs",
                     "scripts/ui/hud/BubbleIcon.cs",
                 })
        {
            string src = ReadRepoFile(path);
            Assert.DoesNotContain("CanvasLayer", src);
        }
    }

    [Fact]
    public void TheFullHouseFlourish_AddsNoControl()
    {
        // Acceptance criterion 5, and the register reason underneath it: the level ends by the
        // group noticing, never by the game interrupting them with a panel.
        string src = ReadRepoFile("scripts/game/bubble/BubbleCounterDisplay.cs");
        Assert.DoesNotContain("new Control", src);
        Assert.DoesNotContain("CanvasLayer", src);
        Assert.DoesNotContain("AcceptDialog", src);
    }

    [Fact]
    public void TheAbsenceControls_WouldActuallyFail()
    {
        // The positive control the two tests above are worth nothing without: the matcher must be
        // able to SEE a CanvasLayer when one is there. GameHud is one, and says so on line one of
        // its declaration.
        string src = ReadRepoFile("scripts/ui/hud/GameHud.cs");
        Assert.Contains("CanvasLayer", src);
    }

    [Fact]
    public void TheHubDisplay_IsTheOnlyLightInTheSceneItShipsWith()
    {
        // Program D10: "Nothing else in the level gets a light — the darkness is real and the aids
        // are the point." Asserted on the two prop scenes BT-8 authors, which are the only two
        // this packet could have broken it with.
        string display = ReadRepoFile("scenes/game/props/BubbleCounterDisplay.tscn");
        Assert.Equal(1, CountOccurrences(display, "Light3D"));
        string lever = ReadRepoFile("scenes/game/props/BubbleResetLever.tscn");
        Assert.DoesNotContain("Light3D", lever);
    }

    [Fact]
    public void TheDisplayGlyphs_AreEmissiveSoTheyReadAtNight()
    {
        // The one always-lit thing (program D10), and the assertion is on EMISSION specifically:
        // a headed midnight capture showed that an unshaded Label3D goes dark with everything else
        // under this project's night tonemap, so "bright colour" is not a substitute. Two faces,
        // one shared TextMesh, one emissive material.
        string display = ReadRepoFile("scenes/game/props/BubbleCounterDisplay.tscn");
        Assert.Equal(1, CountOccurrences(display, "type=\"TextMesh\""));
        Assert.Equal(2, CountOccurrences(display, "SubResource(\"TextMesh_glyphs\")"));
        Assert.Contains("emission_enabled = true", display);
        // A BAND, NOT A LITERAL AND NOT A ONE-SIDED FLOOR (COUNTER-1, 2026-08-29).
        //
        // History, because it is the whole point of the shape of this assertion. It first read
        // `Assert.Contains("emission_energy_multiplier = 40.0")` — an exact value never checked
        // against a render, because until FIX-1 fixed the font the board rendered nothing. FIX-1
        // measured 40 as too dim against the night fog of the day and went to 200, and replaced
        // the literal with a one-sided FLOOR of 40 so a measured improvement could not read as a
        // regression.
        //
        // THE FLOOR WAS THE WRONG HALF TO ASSERT, and this test is the reason nothing caught it.
        // DARK-1 then removed that fog and turned a glow pass on; 200 became a floodlight that
        // blew a white slab over the hub day and night, and the assertion that could have caught
        // it — a CEILING — did not exist. Talon caught it instead, by playing it.
        //
        // So: a band, both ends measured off COUNTER-1's capture ladder (the rig and the numbers
        // are in docs/qa/bubble-test/COUNTER-1/ and quoted in the scene's own comment).
        //   below 1.0  — the 20 m night read is being given away for nothing;
        //   above 4.0  — the bloom starts lifting the board's own housing in daylight, which is
        //                the exact defect Talon named as "the camera makes it overblown".
        // Moving either end is allowed and expected — but re-shoot the ladder first and move the
        // number deliberately, which is precisely what did not happen to the 200.
        System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(
            display, @"emission_energy_multiplier = (\d+(?:\.\d+)?)");
        Assert.True(m.Success, "no emission_energy_multiplier on the glyph material");
        float energy = float.Parse(m.Groups[1].Value,
            System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(energy >= 1.0f, $"glyph emission energy {energy} is below the 1.0 floor — "
                                    + "the glyphs stop reading at 20 m at night (BT-8 AC3)");
        Assert.True(energy <= 4.0f, $"glyph emission energy {energy} is above the 4.0 ceiling — "
                                    + "the glow pass starts washing the board's own housing "
                                    + "(COUNTER-1's ladder; re-shoot it before raising this)");
        // Shading must stay ON: an unshaded StandardMaterial3D ignores EMISSION entirely and only
        // ever draws ALBEDO. shading_mode = 0 is that mode, and it must not appear here.
        Assert.DoesNotContain("shading_mode = 0", display);
    }

    [Fact]
    public void TheDisplayGlyphs_NameAnExplicitFont_OrTextMeshRendersNothingAtAll()
    {
        // FIX-1 item 4, and this is the assertion whose absence let the board ship blank.
        //
        // A TextMesh with no `font` falls back to the engine's built-in default face, which this
        // Godot build CANNOT TRIANGULATE — "Triangulation failed" fires on every SetText and the
        // mesh comes out EMPTY. It is not a glyph-coverage problem: it fired just as readily on
        // the synced ASCII string "0 / 100" as on an em dash, which is why the original diagnosis
        // ("this font has no em dash") sent the fix in the wrong direction for a whole packet. The
        // failure is silent in the editor and invisible to every other test here, because the
        // scene loads perfectly and only the RENDER is empty.
        //
        // Assert the font line exists AND that it points at a real outline TTF: a .woff2, or a
        // face imported as MSDF, would satisfy a naive "has a font" check and still fail to mesh.
        string display = ReadRepoFile("scenes/game/props/BubbleCounterDisplay.tscn");
        Assert.Contains("font = ExtResource(", display);
        Assert.Matches(@"\[ext_resource type=""FontFile""[^\]]*\.ttf""", display);

        string import = ReadRepoFile("assets/fonts/WorkSans.ttf.import");
        Assert.Contains("multichannel_signed_distance_field=false", import);
    }

    [Fact]
    public void BluePrecision_WearsTheShippedNightEdgeMaterial_NotAStandIn()
    {
        // FIX-1 item 2. BluePrecision.tscn carried ONE inline StandardMaterial3D stand-in named
        // "night_edge_blue -- BT-3 stand-in for BT-9 .tres" worn by 80 rim meshes. A stand-in is a
        // StandardMaterial3D, so it cannot read the `bt_darkness` global at all — the blue tower
        // had no night aid whatever while looking, in the editor, exactly as though it did.
        //
        // Asserted on the FILE rather than on a loaded scene deliberately: this is a wiring
        // property, and the whole lesson of FIX-1 is that a scene which loads and runs is not
        // evidence that its materials are the intended ones.
        string blue = ReadRepoFile("scenes/game/world/bubbletest/sections/BluePrecision.tscn");
        Assert.Contains("resources/materials/bubbletest/night_edge_blue.tres", blue);
        Assert.DoesNotContain("stand-in", blue);
        // The rims must all point at the .tres, and none at a local material.
        Assert.Equal(0, CountOccurrences(blue, "SubResource(\"StandardMaterial3D_nightedge\")"));
    }

    [Fact]
    public void TvPortal_DoesNotOverwriteAnAuthoredScreenMaterial()
    {
        // FIX-1 item 1. TvPortal.cs adopted an authored Screen NODE and then assigned its own
        // generated NoiseTexture2D material over that node's material_override, so BT-9's
        // tv_static.tres had never once been on screen. BT-10's acceptance criterion measured
        // screen BRIGHTNESS and passed anyway, because both materials are bright.
        //
        // A source assertion, because the property is "this code path does not run when a material
        // is authored" and the observable difference is a render, not a value a headless test can
        // read. It is deliberately narrow: it pins the GUARD, not the material.
        string src = ReadRepoFile("scripts/game/world/TvPortal.cs");
        Assert.Contains("authoredScreen?.MaterialOverride is not null", src);
        // And TvRoom must actually author one, or the guard protects nothing.
        string room = ReadRepoFile("scenes/game/world/bubbletest/sections/TvRoom.tscn");
        Assert.Contains("resources/materials/bubbletest/tv_static.tres", room);
    }

    // --- helpers ----------------------------------------------------------------------------

    /// <summary>Repo root, walked up from the test assembly — the same idiom the other
    /// source-asserting suites in this project use.</summary>
    private static string ReadRepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "project.godot")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not find the repo root (no project.godot above the test assembly)");
        string path = Path.Combine(dir!.FullName, relative.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"expected {relative} to exist at {path}");
        return File.ReadAllText(path);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int n = 0;
        for (int i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            n++;
        return n;
    }

    /// <summary>Reads <c>= &lt;number&gt;;</c> off a named constant in a source file, so a test can
    /// pin a relationship between two constants that live in different classes without either one
    /// having to expose the other.</summary>
    private static double ReadDouble(string source, string constName)
    {
        int at = source.IndexOf(constName + " = ", StringComparison.Ordinal);
        Assert.True(at >= 0, $"constant {constName} not found");
        int start = at + constName.Length + 3;
        int end = source.IndexOf(';', start);
        Assert.True(end > start, $"constant {constName} has no terminator");
        return double.Parse(source.Substring(start, end - start),
            System.Globalization.CultureInfo.InvariantCulture);
    }
}
