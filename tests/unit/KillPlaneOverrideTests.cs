using MpFoundation.Game.Sandbox;
using MpFoundation.Net;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// The per-world out-of-bounds floor (DET-1 item 2, RISK-AUDIT-2026-07-12.md 3.2).
///
/// <para><b>What this exists to catch.</b> Before the override there were two out-of-bounds
/// systems and only one of them was configurable per world: <c>RespawnService.VoidKillY</c>
/// (set by the world) and <c>NetProfile.KillPlaneY</c> (a global <c>const</c> enforced every
/// server tick by <c>SandboxAvatar.ServerTick</c>). A level with real vertical extent — the
/// Bubble Test has a 41 m tower and a sub-grade room — got the flat-world number whether it
/// fitted or not, and BT-10 measured the consequence: a player teleported below the const was
/// snapped back to spawn on the next tick, invisibly, because that path only runs in ServerSim.
/// These tests pin the three properties the override has to keep.</para>
///
/// <para><b>The alias is the load-bearing one.</b> <c>Carryable.KillPlaneY</c> used to be a
/// <c>const</c> initialised from the NetProfile <c>const</c>, so it was a compile-time COPY.
/// Had the override shipped without changing it, avatars and props would have gone on using the
/// flat-world floor while the world around them used its own — a second, silent copy of exactly
/// the constant whose duplication caused the original defect.</para>
/// </summary>
public class KillPlaneOverrideTests
{
    [Fact]
    public void DefaultIsTheFlatWorldFloor()
    {
        Assert.Equal(-30f, NetProfile.KillPlaneYDefault);
        Assert.Equal(NetProfile.KillPlaneYDefault, NetProfile.KillPlaneY);
    }

    [Fact]
    public void CarryableAliasTracksTheOverrideRatherThanCopyingIt()
    {
        float original = NetProfile.KillPlaneY;
        try
        {
            Assert.Equal(NetProfile.KillPlaneY, Carryable.KillPlaneY);

            NetProfile.KillPlaneY = -140f;
            Assert.Equal(-140f, Carryable.KillPlaneY);

            NetProfile.ResetKillPlaneToDefault();
            Assert.Equal(NetProfile.KillPlaneYDefault, NetProfile.KillPlaneY);
            Assert.Equal(NetProfile.KillPlaneYDefault, Carryable.KillPlaneY);
        }
        finally
        {
            NetProfile.KillPlaneY = original;
        }
    }

    [Fact]
    public void ResetIsIdempotentAndReturnsToTheCompileTimeDefault()
    {
        float original = NetProfile.KillPlaneY;
        try
        {
            NetProfile.ResetKillPlaneToDefault();
            NetProfile.ResetKillPlaneToDefault();
            Assert.Equal(-30f, NetProfile.KillPlaneY);
        }
        finally
        {
            NetProfile.KillPlaneY = original;
        }
    }
}
