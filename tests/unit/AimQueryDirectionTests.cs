using Godot;
using MpFoundation.Game.Aim;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// Pure-math coverage for AimQuery.DirectionFromYawPitch — the one piece of AimQuery that needs
/// no live World3D/physics, so it runs in the Godot-free xUnit tier (Godot.Vector3/Basis are
/// plain managed structs — see this project's own doc comment). AimQuery.QueryGroup itself
/// (the frustum+raycast half) needs real physics and lives in SandboxSelfTest instead — see
/// Run-SandboxTest.ps1's "aim_*" checks for the range/frustum/occlusion boundary cases.
/// </summary>
public class AimQueryDirectionTests
{
    private const float Epsilon = 1e-4f;

    [Fact]
    public void ZeroYawZeroPitch_PointsForwardMinusZ()
    {
        Vector3 dir = AimQuery.DirectionFromYawPitch(0f, 0f);
        AssertClose(new Vector3(0, 0, -1), dir);
    }

    [Fact]
    public void Direction_IsAlwaysUnitLength()
    {
        foreach (float yaw in new[] { 0f, 0.3f, 1.5f, -2.1f, Mathf.Pi })
        foreach (float pitch in new[] { -1.0f, -0.3f, 0f, 0.29f })
            Assert.True(Mathf.Abs(AimQuery.DirectionFromYawPitch(yaw, pitch).Length() - 1f) < Epsilon);
    }

    [Fact]
    public void YawQuarterTurn_RotatesTowardMinusX()
    {
        // Godot's Basis(Up, yaw) convention: a +90 degree yaw turns the forward direction from
        // -Z toward -X (right-handed rotation about +Y, matching SandboxCamera's own pivot
        // rig). Pinned explicitly so a future refactor can't silently flip the sign.
        Vector3 dir = AimQuery.DirectionFromYawPitch(Mathf.Pi / 2f, 0f);
        AssertClose(new Vector3(-1, 0, 0), dir);
    }

    [Fact]
    public void PositivePitch_TiltsUpward()
    {
        Vector3 dir = AimQuery.DirectionFromYawPitch(0f, 0.3f);
        Assert.True(dir.Y > 0f);
    }

    [Fact]
    public void NegativePitch_TiltsDownward()
    {
        Vector3 dir = AimQuery.DirectionFromYawPitch(0f, -0.3f);
        Assert.True(dir.Y < 0f);
    }

    [Fact]
    public void YawIsPeriodic_TwoPiEqualsZero()
    {
        Vector3 a = AimQuery.DirectionFromYawPitch(0f, 0.1f);
        Vector3 b = AimQuery.DirectionFromYawPitch(Mathf.Tau, 0.1f);
        AssertClose(a, b);
    }

    private static void AssertClose(Vector3 expected, Vector3 actual)
    {
        Assert.True(expected.DistanceTo(actual) < Epsilon,
            $"expected {expected}, got {actual}");
    }
}
