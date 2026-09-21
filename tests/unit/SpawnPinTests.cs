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

    // --- The pin ORDER (INT-2 part B, 2026-09-21, ruling 5) -----------------------------------
    // PHYS-2 section 2.1 measured a pin teleporting SfxProduceBot onto marker 3 while the
    // still-unnamed witness was standing on it by join order: the produce bot spent 0.6 s at
    // y = 2.30 on the witness's head, fell into the +Z walkway and stopped 5.7 m from its prop.
    // These assert the geometry half; the ordering half is four lines in Gameplay.ApplySpawnPins.

    /// <summary><b>A marker with an unnamed peer standing on it is blocked.</b> That peer is one
    /// the same poll is about to move, so the pin waits a frame rather than landing on it.</summary>
    [Fact]
    public void AMarkerAnUnnamedPeerIsStandingOnIsBlocked()
    {
        var destination = new Godot.Vector3(41.5f, 1.10f, -2.1f);
        var unsettled = new[] { new Godot.Vector3(41.46f, 0.82f, -2.03f) };  // PHYS-2's own trace
        Assert.True(SpawnPin.BlockedByAnUnsettledPeer(destination, unsettled, SpawnPin.PinClearM));
    }

    /// <summary><b>A peer on the NEXT marker does not block.</b> The nearest two SearchSpawn
    /// markers are 2.1 m apart and the clearance is 1 m, so this can only ever mean "standing on
    /// it" -- a radius that swallowed the neighbouring marker would deadlock every pinned suite,
    /// which is the failure mode worth a test of its own.</summary>
    [Fact]
    public void APeerOnTheNextMarkerDoesNotBlock()
    {
        var destination = new Godot.Vector3(41.5f, 1.10f, -2.1f);
        var elsewhere = new[] { new Godot.Vector3(44.5f, 1.10f, 0f) };
        Assert.False(SpawnPin.BlockedByAnUnsettledPeer(destination, elsewhere, SpawnPin.PinClearM));
    }

    /// <summary>Nobody waiting, nothing blocked -- including the null the adapter passes when it
    /// never allocated the list, which is every ordinary frame.</summary>
    [Fact]
    public void NothingIsBlockedWhenEveryPeerIsNamed()
    {
        var destination = new Godot.Vector3(41.5f, 1.10f, -2.1f);
        Assert.False(SpawnPin.BlockedByAnUnsettledPeer(destination, new Godot.Vector3[0], SpawnPin.PinClearM));
        Assert.False(SpawnPin.BlockedByAnUnsettledPeer(destination, null!, SpawnPin.PinClearM));
    }

    /// <summary><b>The clearance is under the marker spacing, and that is the property that makes
    /// the wait terminate.</b> Asserted as a relation rather than as the number, so a later ride
    /// that moves the markers gets a red here instead of a deadlocked suite.</summary>
    [Fact]
    public void TheClearanceIsWellInsideTheMarkerSpacing()
    {
        Assert.True(SpawnPin.PinClearM > 0f);
        Assert.True(SpawnPin.PinClearM < 2.1f);   // closest two SearchSpawn markers
    }
}
