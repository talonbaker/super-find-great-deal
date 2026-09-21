using Godot;

namespace MpFoundation.Game.Sandbox;

/// <summary>
/// <b>The manoeuvre Talon's complaint came from</b>, scripted (FEEL-1, 2026-09-20): once the bot
/// is holding something, walk forward, walk backward, strafe both ways and turn 180 degrees
/// twice — on a loop, deterministically, for as long as the run lasts.
///
/// <para><b>Why those five and not a patrol.</b> <c>Run-CarryDriftTest</c>'s patrol already walks
/// a long straight lane, and a straight lane is the one motion the old carry survived: the hold
/// point ran ahead of the prop and the lag pulled it toward the body but never past it. The clip
/// Talon rode into needs the body to move TOWARD where the prop is — walking backwards, or
/// turning so the object swings across you — and a brain that only ever walks forward cannot
/// stage it. The 180s are the sharpest version: the hold point travels a full half-circle around
/// the holder in a fraction of a second while the prop is still behind it.</para>
///
/// <para><b>A decorator, so the grab it stresses is the shipped one.</b> It wraps whatever brain
/// walked to the prop and pressed (<see cref="ScriptedCarryIntentSource"/>) and only takes over
/// the MOVEMENT, and only while that brain is actually holding something — every request the
/// suite exercises still goes through the real verbs. Before the grab lands it forwards the inner
/// brain's intent untouched, through the same travel-facing wrapper every scripted brain gets, so
/// the walk to the prop is byte-for-byte what it was without this node.</para>
///
/// <para><b>It supplies its own look</b> (<see cref="IIntentSource.SuppliesLook"/>), which is the
/// whole reason that seam exists: the travel-facing wrapper fills <c>AimYaw</c> from the movement
/// direction, so a bot that strafes while turning would have its turn silently overwritten and the
/// suite would be testing a manoeuvre that never happened.</para>
/// </summary>
public sealed class HoldStressIntentSource : IIntentSource
{
    /// <summary>One leg of the loop, seconds. Long enough at the browse pace to build full speed
    /// and cover a couple of metres (so a lag has somewhere to accumulate), short enough that a
    /// 20 s run repeats the whole cycle twice.</summary>
    public const double LegSec = 1.4;

    /// <summary>How long a 180 takes. A turn is the sharpest part of the test, so it is the
    /// fastest thing here — but not instant: a teleported yaw would move the hold point without
    /// ever passing through the positions in between, which is precisely the interval where a
    /// prop can end up inside its holder.</summary>
    public const double TurnSec = 0.5;

    private readonly IIntentSource _inner;
    private readonly System.Func<bool> _isHolding;
    private readonly float _spawnYaw;
    private double _clock;
    private float _yaw;
    private bool _armed;

    public HoldStressIntentSource(IIntentSource inner, System.Func<bool> isHolding, float spawnYaw)
    {
        // The inner brain keeps the travel facing it has always had for the walk-and-grab phase.
        _inner = new TravelFacingIntentSource(inner);
        _isHolding = isHolding;
        _spawnYaw = spawnYaw;
        _yaw = spawnYaw;
    }

    /// <summary>Forwarded, never defaulted — see <see cref="IIntentSource.IsHumanInput"/>'s
    /// decorator note. In practice always false, because only a bot is ever given this brain;
    /// forwarding is what makes that a fact about the stream rather than an assumption.</summary>
    public bool IsHumanInput => _inner.IsHumanInput;

    /// <summary>Always — see the class doc. Even before the stress begins this source is the one
    /// that decides the look, because it owns the inner brain's travel-facing wrapper itself.</summary>
    public bool SuppliesLook => true;

    /// <summary>Where in the loop this brain is, for a log line the suite can read back.</summary>
    public string Phase { get; private set; } = "approach";

    public MoveIntent NextIntent(double delta)
    {
        MoveIntent inner = _inner.NextIntent(delta);
        if (!_armed)
        {
            if (!_isHolding())
                return inner;
            // The grab has landed: start the loop from here, facing wherever the walk left us.
            _armed = true;
            _clock = 0;
            _yaw = inner.AimYaw;
        }

        _clock += delta;
        double cycle = 4 * LegSec + 2 * TurnSec;
        double t = _clock % cycle;

        Vector3 local;
        if (t < LegSec) { local = Vector3.Forward; Phase = "forward"; }
        else if (t < 2 * LegSec) { local = Vector3.Back; Phase = "backward"; }
        else if (t < 3 * LegSec) { local = Vector3.Left; Phase = "strafe-left"; }
        else if (t < 4 * LegSec) { local = Vector3.Right; Phase = "strafe-right"; }
        else
        {
            // The two 180s, back to back: half a turn each, so the pair returns the body to the
            // heading it started the cycle on and the loop does not walk the bot out of the room.
            local = Vector3.Zero;
            Phase = t < 4 * LegSec + TurnSec ? "turn-a" : "turn-b";
            _yaw += (float)(Mathf.Pi * delta / TurnSec);
        }

        // Movement is expressed in the BODY's frame and rotated by the look, so "strafe left"
        // stays a strafe through a turn instead of becoming a world-axis walk.
        Vector3 dir = new Basis(Vector3.Up, _yaw) * local;
        return inner with
        {
            MoveDir = dir.LengthSquared() > 1e-6f ? dir.Normalized() : Vector3.Zero,
            AimYaw = _yaw,
            // Level with the floor: the hold's PITCH is a separate question and SICK-1 owns the
            // camera. A stress test that also pitched would not be able to say which of the two
            // produced a clearance reading.
            AimPitch = 0f,
            // Never voluntarily let go — the inner brain's own drop schedule is what a suite
            // would use for that, and this one is launched with holdSec < 0.
            Interact = false,
            Throw = false,
        };
    }

    /// <summary>The heading this brain was built at, for a suite that wants to assert the body
    /// came back to it. Exposed rather than recomputed because the two 180s are what make it
    /// true.</summary>
    public float SpawnYaw => _spawnYaw;
}
