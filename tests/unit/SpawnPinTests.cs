using MpFoundation.Game.World;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// <b><c>--spawn-index</c>'s mapping</b> (INT-1, 2026-09-19, packet ruling 6). Engine-free, so
/// the half that decides WHICH MARKER A NAMED BOT GETS is asserted here and the half that moves
/// the body is four lines in <c>Gameplay</c>.
///
/// <para>What these are defending is a measured race, not a hypothetical: SHELF-1 §6.3 recorded
/// the same bot drawing marker 2 on one run and marker 1 on the next, in a room where those two
/// markers are in different corridors and a straight-line walk brain cannot get between them.
/// The mapping is the whole fix, so it is the thing that gets tested.</para>
/// </summary>
public class SpawnPinTests
{
    [Fact]
    public void ParsesTheOrdinaryTwoBotSpec()
    {
        SpawnPin pins = SpawnPin.Parse("BotA=2,BotB=0");
        Assert.Equal(2, pins.Count);
        Assert.Equal(2, pins.IndexFor("BotA"));
        Assert.Equal(0, pins.IndexFor("BotB"));
    }

    /// <summary><b>An unpinned name answers −1, not 0.</b> Zero is a real marker, and a bot that
    /// silently took marker 0 because its name was not in the map would be the same race this
    /// flag exists to end, wearing a flag's clothes.</summary>
    [Fact]
    public void AnUnlistedNameIsNotPinned()
    {
        SpawnPin pins = SpawnPin.Parse("BotA=2");
        Assert.Equal(-1, pins.IndexFor("BotB"));
        Assert.Equal(-1, pins.IndexFor("bota"));   // exact and case-sensitive: see the class doc
    }

    /// <summary><b>An empty or absent name is "not known yet", not "not pinned".</b> The server
    /// polls until <c>DisplayName</c> replicates, and an avatar with no name must never match a
    /// pin — otherwise the first frame of every session would pin whoever happened to be empty.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void AnUnknownNameIsNeverPinned(string? name)
    {
        Assert.Equal(-1, SpawnPin.Parse("BotA=2,=3").IndexFor(name));
    }

    /// <summary><b>No flag is the shipped path</b>, and it must cost a <c>Count == 0</c> test
    /// rather than a lookup per avatar per frame.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(",,,")]
    [InlineData("garbage")]
    [InlineData("=5")]
    [InlineData("BotA=")]
    [InlineData("BotA=x")]
    public void AnUnusableSpecIsEmptyRatherThanAThrow(string? spec)
    {
        SpawnPin pins = SpawnPin.Parse(spec);
        Assert.Equal(0, pins.Count);
        Assert.Equal(-1, pins.IndexFor("BotA"));
    }

    /// <summary><b>One bad entry drops itself and the rest still land.</b> All-or-nothing would
    /// take a whole suite down at launch for one typo, with the error a long way from the flag;
    /// this leaves the mistyped bot on the ordinary join-order deal, which is exactly where it
    /// was before the flag existed, and <c>Count</c> says how many actually applied.</summary>
    [Fact]
    public void OneMalformedEntryDoesNotTakeTheGoodOnesWithIt()
    {
        SpawnPin pins = SpawnPin.Parse("BotA=2, oops ,BotB=nope,BotC=1,=9,BotD=-1");
        Assert.Equal(2, pins.Count);
        Assert.Equal(2, pins.IndexFor("BotA"));
        Assert.Equal(1, pins.IndexFor("BotC"));
        Assert.Equal(-1, pins.IndexFor("BotB"));
        Assert.Equal(-1, pins.IndexFor("BotD"));   // negative: there is no marker there
    }

    /// <summary><b>A negative index is dropped, not wrapped.</b> The caller wraps with
    /// <c>index % markerCount</c> for late joiners, and C#'s <c>%</c> on a negative gives a
    /// negative — so a wrapped −1 would index out of the array. Refusing it at the parse leaves
    /// the bot on the ordinary deal instead of crashing the spawn path.</summary>
    [Theory]
    [InlineData("BotA=-1")]
    [InlineData("BotA=-7")]
    public void ANegativeIndexIsRefused(string spec)
    {
        Assert.Equal(0, SpawnPin.Parse(spec).Count);
    }

    /// <summary><b>An out-of-range index is NOT refused here</b>, and that is deliberate: how
    /// many markers a room has is a fact about the world this type cannot see. The caller wraps
    /// it the same way it wraps a late joiner's index, so the pinned path and the ordinary path
    /// cannot disagree about what marker 5 of 4 means.</summary>
    [Fact]
    public void ABigIndexIsPassedThroughForTheCallerToWrap()
    {
        Assert.Equal(97, SpawnPin.Parse("BotA=97").IndexFor("BotA"));
    }

    /// <summary>Whitespace around either half is a formatting accident, not a different name.
    /// A suite writes this flag on a PowerShell command line where a space after a comma is
    /// almost free.</summary>
    [Fact]
    public void WhitespaceAroundEitherHalfIsIgnored()
    {
        SpawnPin pins = SpawnPin.Parse("  BotA = 2 ,  BotB =0  ");
        Assert.Equal(2, pins.IndexFor("BotA"));
        Assert.Equal(0, pins.IndexFor("BotB"));
    }

    /// <summary>A name repeated in one spec takes its LAST assignment — the way a reader's eye
    /// takes the last line of a list. Not an error, and not a silent half-application.</summary>
    [Fact]
    public void ARepeatedNameTakesItsLastAssignment()
    {
        SpawnPin pins = SpawnPin.Parse("BotA=1,BotA=3");
        Assert.Equal(1, pins.Count);
        Assert.Equal(3, pins.IndexFor("BotA"));
    }

    /// <summary>Two different names may share a marker. Nothing stops a suite doing it, two bots
    /// on one marker is a legal (if silly) staging, and refusing it here would be this type
    /// inventing a rule about a world it cannot see.</summary>
    [Fact]
    public void TwoNamesMaySharePinnedMarker()
    {
        SpawnPin pins = SpawnPin.Parse("BotA=1,BotB=1");
        Assert.Equal(1, pins.IndexFor("BotA"));
        Assert.Equal(1, pins.IndexFor("BotB"));
    }

    /// <summary>The shared <see cref="SpawnPin.Empty"/> answers "not pinned" for everything,
    /// which is what makes a null check unnecessary at the call site.</summary>
    [Fact]
    public void EmptyPinsNobody()
    {
        Assert.Equal(0, SpawnPin.Empty.Count);
        Assert.Equal(-1, SpawnPin.Empty.IndexFor("anybody"));
    }
}
