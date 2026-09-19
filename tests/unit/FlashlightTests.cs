using System;
using System.IO;
using MpFoundation.Game.Light;
using MpFoundation.Ui;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// NIGHT-2's arithmetic and its guard rails (Talon's 2026-08-29 notes 10 and 11). Three groups,
/// and the third is the one that matters most:
/// <list type="number">
/// <item>the numbers sit under canon fact 3's personal-light backstops;</item>
/// <item>the toast the player reads and the key that actually works are the same fact;</item>
/// <item><b>the flashlight never becomes a gameplay light</b> — the parity scanner, with a
/// positive control, so a scanner that has stopped reading files fails instead of reporting
/// clean.</item>
/// </list>
/// </summary>
public class FlashlightTests
{
    // =======================================================================================
    // 1. Canon fact 3's backstops — the campfire keeps its weight
    // =======================================================================================

    /// <summary>The two numbers <see cref="FlashlightProfile"/> calls "canon fact 3's absolute
    /// backstop … asserted, not merely documented". A personal light that broke either would be
    /// competing with the campfire for the one thing the campfire is for.</summary>
    [Fact]
    public void Profile_StaysUnderTheCanonFact3PersonalLightBackstops()
    {
        Assert.True(FlashlightProfile.LitRadiusM <= FlashlightProfile.MaxPersonalLitRadiusM,
            $"the flashlight lights {FlashlightProfile.LitRadiusM} m, over the " +
            $"{FlashlightProfile.MaxPersonalLitRadiusM} m ceiling canon fact 3 puts on any light " +
            "that is not the campfire.");
        Assert.True(FlashlightProfile.Intensity01 < FlashlightProfile.MaxPersonalIntensity01,
            $"the flashlight declares intensity {FlashlightProfile.Intensity01}, at or over the " +
            $"{FlashlightProfile.MaxPersonalIntensity01} backstop. 1.0 is reserved for the fire.");
    }

    /// <summary>The rendered energy is DERIVED from the one intensity→energy scale this repo has,
    /// never restated. A second copy of the conversion drifts the day either number moves.</summary>
    [Fact]
    public void Profile_EnergyIsDerivedFromTheSharedPersonalLightScale()
    {
        Assert.Equal(
            FlashlightProfile.Intensity01 * FlashlightProfile.LightEnergyPerIntensity,
            FlashlightProfile.LightEnergy,
            0.0001f);
    }

    /// <summary><b>Shadows off, and this is a performance contract rather than a look.</b> The
    /// floor is a GTX 970 on Forward+ and this is one light per player across the whole group; a
    /// shadow-casting omni is a cube render each. Flipping it must be an edit somebody argues for,
    /// not a property poke, so the constant is asserted.</summary>
    [Fact]
    public void Profile_CastsNoShadows()
    {
        Assert.False(FlashlightProfile.ShadowsEnabled);
    }

    /// <summary>Cool, not warm. THRILL-BIBLE §6.6 makes colour temperature a channel and the
    /// campfire owns warm (canon fact 3) — a battery light that read warm would be competing with
    /// the fire's whole reason to exist. Asserted as "blue channel beats red", which is the claim,
    /// rather than as three floats, which would only pin today's swatch.</summary>
    [Fact]
    public void Profile_ReadsCoolRatherThanAsFirelight()
    {
        Assert.True(FlashlightProfile.LightColor.B > FlashlightProfile.LightColor.R,
            "the flashlight is warmer than it is cool — it is competing with the campfire.");
    }

    // =======================================================================================
    // 2. The hint and the binding are the same fact
    // =======================================================================================

    /// <summary>Talon's note 10, verbatim and unedited. Pinned as a literal on purpose: he quoted
    /// the exact sentence he wanted, so a later copy pass that "tidies" it is a regression against
    /// a direct instruction rather than a wording preference.</summary>
    [Fact]
    public void Toast_ReadsExactlyWhatTalonAskedFor()
    {
        Assert.Equal("Night has fallen. Press F to toggle flashlight.",
            PhaseToastText.NightfallToast);
        Assert.Equal(PhaseToastText.NightfallToast,
            PhaseToastText.TextFor(MpFoundation.Game.World.PhaseEventKind.DuskToNight));
    }

    /// <summary><b>The hint may not out-live the key.</b> The toast is the flashlight's entire
    /// discoverability — nothing else in the game mentions F — so a rebind that leaves the sentence
    /// behind produces a player pressing a dead key at the exact moment they need the tool. This is
    /// the cheapest possible link between the two: the advertised letter must be the bound one.
    /// </summary>
    [Fact]
    public void Toast_AdvertisesTheKeyThatIsActuallyBound()
    {
        Assert.Contains($"Press {FlashlightController.FallbackKey} ",
            PhaseToastText.NightfallToast, StringComparison.Ordinal);
    }

    /// <summary>The three other crossings are untouched by this packet. Stated so the string change
    /// above cannot quietly become a rewrite of the phase toasts.</summary>
    [Fact]
    public void Toast_TheOtherCrossingsAreUnchanged()
    {
        // W7-2, 2026-08-30 (Talon note 5): the dusk line no longer names a campfire — the world
        // has not had one for two premises. Still pinned as a literal rather than to
        // PhaseToastText.DuskToast, because a test that asserts a constant equals itself is not a
        // test; the point of this fixture is that the wording cannot drift unnoticed.
        Assert.Equal("Sunset. Head for the light.",
            PhaseToastText.TextFor(MpFoundation.Game.World.PhaseEventKind.DayToDusk));
        Assert.Equal("Dawn is breaking.",
            PhaseToastText.TextFor(MpFoundation.Game.World.PhaseEventKind.NightToDawn));
        Assert.Null(PhaseToastText.TextFor(MpFoundation.Game.World.PhaseEventKind.DawnToDay));
    }

    // =======================================================================================
    // 3. The parity guard — the flashlight is presentation and must stay presentation
    // =======================================================================================

    /// <summary>
    /// <b>The guard against the NEXT edit, not this one.</b> Canon's parity law says gameplay
    /// visibility is server data identical on every client and that rendered light is presentation
    /// only. The flashlight is presentation: it illuminates geometry and contributes nothing to
    /// <c>PlayerSightCurve</c>, which stays the sole producer of how far anyone can see.
    ///
    /// <para>The structural half of that guarantee is that <c>FlashlightManager</c> has no
    /// <c>AppendLitLightSamples</c> and <c>PlayerSightService</c> has no <c>Flashlights</c>
    /// property — wiring this into the sight union is a method somebody would have to write, not a
    /// line they add by forgetting. This test is the other half: it scans the flashlight source for
    /// the sight symbols, so the day somebody writes that method the test goes red before the
    /// feature ships.</para>
    ///
    /// <para><b>The positive control is the load-bearing part</b> (the same shape
    /// <c>SightPresentationTests.ContractScan_NothingInTheSightPathReadsTheGraphicsTier</c> uses):
    /// the same scanner is pointed at a file that genuinely does append light samples, so a scanner
    /// that has stopped reading files — a moved path, a renamed directory, a broken repo-root walk
    /// — fails instead of quietly reporting every flashlight file clean.</para>
    /// </summary>
    [Fact]
    public void ContractScan_TheFlashlightNeverReachesTheSightSystem()
    {
        string[] flashlightFiles =
        {
            "scripts/game/light/FlashlightProfile.cs",
            "scripts/game/light/FlashlightManager.cs",
            "scripts/game/light/FlashlightController.cs",
        };

        string[] forbidden = { "AppendLitLightSamples", "LightSample", "PlayerSightService", "PlayerSightCurve" };

        foreach (string path in flashlightFiles)
        {
            string source = StripCommentsAndDocs(ReadRepoFile(path));
            foreach (string symbol in forbidden)
            {
                Assert.False(source.Contains(symbol, StringComparison.Ordinal),
                    $"{path} reaches {symbol} in executable code. The flashlight is PRESENTATION " +
                    "ONLY — it may light geometry and may not change how far anybody can see. If " +
                    "that decision is being reversed it is a canon call (does a personal light " +
                    "grant sight?), not an edit that passes under a comment.");
            }
        }

        // POSITIVE CONTROL. PlayerSightCurve genuinely defines LightSample in executable code
        // (the torch manager that used to append them into the sight union left with the MVP
        // extraction, so the struct's own definition is the control now); if the scanner cannot
        // find that there, it is not scanning and every assertion above is vacuous.
        string control = StripCommentsAndDocs(ReadRepoFile("scripts/game/sight/PlayerSightCurve.cs"));
        Assert.True(control.Contains("LightSample", StringComparison.Ordinal),
            "the scanner did not find LightSample in PlayerSightCurve.cs, which defines it " +
            "— the scan is broken and every 'clean' result above is meaningless.");
    }

    /// <summary><b>The other direction of the same guard.</b> Above proves the flashlight does not
    /// reach into the sight system; this proves the sight system does not reach out to it. Both are
    /// needed: a <c>Flashlights</c> property added to <c>PlayerSightService</c> would satisfy the
    /// scan above (the flashlight file would still be clean) while breaking the rule.</summary>
    [Fact]
    public void ContractScan_TheSightServiceDoesNotKnowTheFlashlightExists()
    {
        string sight = StripCommentsAndDocs(ReadRepoFile("scripts/game/sight/PlayerSightService.cs"));
        Assert.False(sight.Contains("Flashlight", StringComparison.Ordinal),
            "PlayerSightService references the flashlight. It is presentation only — adding a " +
            "light source to the sight union here is the canon call this packet deliberately " +
            "did not make.");

        // POSITIVE CONTROL, same reasoning as the scan above: PlayerSightService genuinely
        // handles LightSample in executable code (its gather scratch list). If the scanner cannot
        // find that, it is not reading the file. (SightLights, the former control, left with the
        // glow stick in the MVP extraction.)
        Assert.True(sight.Contains("LightSample", StringComparison.Ordinal),
            "the scanner did not find LightSample in PlayerSightService.cs, which gathers them — " +
            "the scan is broken and the 'clean' result above is meaningless.");
    }

    // =======================================================================================
    // Helpers
    // =======================================================================================

    /// <summary>Strips line comments and doc comments so a symbol NAMED in prose (this file's own
    /// class docs name every forbidden symbol at length) is not mistaken for a symbol USED in
    /// code. Block comments are not stripped — this repo's convention is <c>///</c> docs and
    /// <c>//</c> notes, and a stripper that tried to track <c>/* */</c> state would be a parser.
    /// </summary>
    private static string StripCommentsAndDocs(string source)
    {
        var kept = new System.Collections.Generic.List<string>();
        foreach (string line in source.Split('\n'))
        {
            string trimmed = line.TrimStart();
            if (trimmed.StartsWith("//", StringComparison.Ordinal))
                continue;
            int comment = line.IndexOf("//", StringComparison.Ordinal);
            kept.Add(comment >= 0 ? line[..comment] : line);
        }
        return string.Join("\n", kept);
    }

    /// <summary>Repo-relative file read, walking up from the test binary to the directory holding
    /// <c>project.godot</c> — <c>SightPresentationTests.ReadRepoFile</c>'s helper verbatim, and for
    /// the same reason: the runner's working directory is not something to depend on.</summary>
    private static string ReadRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "project.godot")))
            dir = dir.Parent;

        Assert.True(dir is not null, "could not locate the repo root (no project.godot above the test binary)");
        string full = Path.Combine(dir!.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(full), $"expected repo file is missing: {relativePath}");
        return File.ReadAllText(full);
    }
}
