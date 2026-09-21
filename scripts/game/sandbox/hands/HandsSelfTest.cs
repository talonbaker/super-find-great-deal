using System.Collections.Generic;
using Godot;
using MpFoundation.Game.Props;
using MpFoundation.Game.Round;

namespace MpFoundation.Game.Sandbox.Hands;

/// <summary>
/// <b>HANDS-1's instrument</b> (<c>--hands-selftest</c>, driven by
/// <c>tests/Run-HandsSmoke.ps1</c>). One windowed bot beside a headless server: it grabs a can
/// one-handed, scrolls it near and far, turns it, lets go, grabs a crate two-handed, lets go, and
/// pokes a button — measuring on EVERY frame that the hand is where the grab point is, and
/// photographing each beat.
///
/// <para><b>It drives the shipped verbs, never a private path.</b> The grab is
/// <c>PropManager.ClientRequestGrab</c>, the release is <c>ClientRequestDrop</c>, the wheel is
/// <c>NetworkedProp.ScrollHold</c> (what <c>HoldDistanceController</c> calls), the turn is
/// <c>SandboxAvatar.HeldPropLocalRotation</c> (what <c>HeldPropRotator</c> writes) and the press
/// is <c>RoundControls.ClientRequestPress</c> (what a human's click on a <c>RoundButton</c>
/// reaches). A bot has no mouse — INT-0 measured that a headless process cannot even take the
/// cursor — so a probe that synthesised mouse events would be measuring the synthesiser. These
/// are the seams the human path writes, one call in.</para>
///
/// <para><b>The measurement is independent of the node it measures</b>, which is what makes
/// <c>--hands-plant-offset</c> able to turn it red. It reads the hands' world positions and
/// re-derives what they should be from the PROP's live transform, FEEL-1's own recorded grab
/// point and the eye — never from <see cref="FirstPersonHands"/>'s internal offset.</para>
///
/// <para><b>Why ACROSS the line of sight.</b> The hand is deliberately pushed out along the view
/// ray to the prop's surface, because FEEL-1's grab point is the foot of a perpendicular and
/// therefore sits INSIDE the prop for any well-aimed grab (see
/// <see cref="HandReach.SurfacePoint"/>). So a raw 3-D hand-to-grab-point distance would report
/// the prop's radius and measure nothing at all. Sideways is where an attach bug shows up.</para>
/// </summary>
public partial class HandsSelfTest : Node
{
    /// <summary>The bar: how far a hand (or the midpoint of a pair) may sit from the grab point
    /// ACROSS the line of sight, metres. The packet's 1 cm.</summary>
    public const float LateralBarM = 0.01f;

    /// <summary>...and how far outside the prop's own bounding sphere a hand may be, metres — a
    /// hand floating off the object is the other half of "the hand is on the thing".</summary>
    public const float OffSurfaceBarM = 0.05f;

    private FirstPersonHands _hands = null!;
    private SandboxAvatar _avatar = null!;
    private string _captureDir = string.Empty;
    private IReadOnlyList<Vector3> _fixture = System.Array.Empty<Vector3>();

    private double _t;
    private int _phase;
    private double _retry;
    private bool _capturing;
    private readonly List<string> _failures = new();

    // What was measured, so the summary is a reading rather than a verdict.
    private int _samples;
    private float _worstLateral;
    private float _worstOffSurface;
    private int _framesWithNoHand;
    private int _framesReaching;
    private readonly List<string> _holds = new();
    private int _lastLoggedProp = -1;

    public void Setup(FirstPersonHands hands, SandboxAvatar avatar, string captureDir,
        IReadOnlyList<Vector3> fixturePoints)
    {
        _hands = hands;
        _avatar = avatar;
        _captureDir = captureDir;
        _fixture = fixturePoints;
        // AFTER the hands, which are themselves after the camera. Measured in the wrong order
        // this probe compares LAST frame's hand position against THIS frame's prop transform,
        // and at a browse pace that is centimetres of pure instrument error on a 1 cm bar -- an
        // instrument reading a stale field looks exactly like the feature being broken, which is
        // the mistake FEEL-1's own lag table made and recorded.
        ProcessPriority = 2000;
    }

    // THE SCRIPT, in seconds SINCE THE PROBE SETTLED at its standing spot -- never since this
    // node was built.
    //
    // Measured, on a loaded machine: an absolute 4.5 s mark caught the bot still walking at
    // 1.56 m/s with nothing in reach, and the suite reported it as a staging failure about the
    // fixture's props. The walk is 3.4 m at the 2.4 m/s browse pace, but the probe's clock starts
    // at _Ready and the connect, the spawn and the first physics ticks all land in front of it,
    // and under another lane's load they land later. Every beat below is therefore an OFFSET from
    // the frame the probe actually arrived, which is the same rule ROUND-1's suite follows for
    // its teleports: gate on the event, never on a guess about when it happens.
    private const double SettleDeadline = 12.0;   // absolute; past this the walk genuinely failed
    private const double CanAim = 0.5;
    private const double CanGrab = 0.9;
    private const double ReachShot = 1.05;
    private const double CanShot = 2.5;
    private const double ScrollOut = 3.0;
    private const double ScrollIn = 4.1;
    private const double RotateFrom = 4.5;
    private const double RotateTo = 6.0;
    private const double CanDrop = 6.5;
    private const double ReturnShot = 6.58;
    private const double CrateAim = 7.5;
    private const double CrateGrab = 7.9;
    private const double CrateCarry = 8.6;
    private const double CrateShot = 9.6;
    private const double CrateDrop = 11.0;
    private const double PressAim = 12.0;
    private const double PressAt = 12.5;
    private const double PokeShot = 12.58;
    private const double Done = 14.0;

    /// <summary>Wall-clock (probe-clock) time the walk settled, or -1 while it has not.</summary>
    private double _settledAt = -1.0;

    /// <summary>Seconds since the probe settled. Negative before it has.</summary>
    private double E => _settledAt < 0.0 ? -1.0 : _t - _settledAt;

    public override void _Ready() => AimAtPoint(new Vector3(40f, 1.0f, -3f));

    public override void _Process(double delta)
    {
        _t += delta;
        Measure();
        Drive();
    }

    // ------------------------------------------------------------------------- the measurement

    private void Measure()
    {
        if (!_hands.IsHolding || _hands.HeldProp is not { } prop || !IsInstanceValid(prop.Body))
            return;

        Vector3[] hands = _hands.AttachedHandPositions();
        if (hands.Length == 0)
        {
            _framesWithNoHand++;
            return;
        }

        // THE REACH IS NOT A DEFECT. For the first ~120 ms of a hold the hand is deliberately
        // somewhere else -- travelling from its idle rest to the thing -- so a per-frame "the
        // hand is at the grab point" bar judged there is reading the lerp and calling it a bug.
        // Measured on this suite's first real run: 26.0, 22.4, 18.8, 15.2, 11.6, 8.0, 4.3 cm, a
        // clean linear decay ending on the bar. The skipped frames are COUNTED, so a build that
        // never settled cannot read as green -- it fails the sample floor instead.
        if (!_hands.Settled)
        {
            _framesReaching++;
            return;
        }

        Carryable body = prop.Body;
        Vector3 centre = body.GlobalPosition;
        Vector3 eye = _avatar.AimOriginGlobalPosition;
        Vector3 toward = eye - centre;
        Vector3 grab = prop.GrabPointWorld;

        // The pair's MIDPOINT, because two hands straddle the grab point by design: one hand sits
        // a half-span either side, and their middle is where the object is being held.
        Vector3 centroid = Vector3.Zero;
        foreach (Vector3 h in hands)
            centroid += h;
        centroid /= hands.Length;

        // ACROSS the view, decomposed into the two axes the design treats differently.
        //
        //   UP    -- the pair's height must be the GRAB POINT's height, one hand or two. Grab a
        //            crate low and you carry it low.
        //   SIDE  -- one hand sits ON the grab point sideways. TWO hands sit on the object's two
        //            side faces and their midpoint is therefore the OBJECT's centre line, not the
        //            grab point's: grip a box slightly off-centre and your hands are still on its
        //            faces. Measuring a two-handed midpoint against the grab point sideways would
        //            be asserting a pose nobody wants.
        //
        // Both still catch the plant, which pushes every attached hand along the SIDE axis.
        Vector3 across = HandReach.AcrossView(toward, Vector3.Up);
        Vector3 viewDir = toward.LengthSquared() > 1e-10f ? toward.Normalized() : Vector3.Back;
        Vector3 up = viewDir.Cross(across).Normalized();
        Vector3 sideRef = hands.Length >= 2 ? centre : grab;
        float sideErr = Mathf.Abs((centroid - sideRef).Dot(across));
        float upErr = Mathf.Abs((centroid - grab).Dot(up));
        float lateral = Mathf.Sqrt(sideErr * sideErr + upErr * upErr);
        _worstLateral = Mathf.Max(_worstLateral, lateral);
        if (lateral > LateralBarM)
        {
            Fail($"hand-to-grab-point {lateral * 100f:F2} cm across the view at t={_t:F2}s "
                 + $"(side {sideErr * 100f:F2} cm, up {upErr * 100f:F2} cm, bar "
                 + $"{LateralBarM * 100f:F0} cm), prop {prop.PropId}, {hands.Length} hand(s)");
        }

        foreach (Vector3 h in hands)
        {
            float off = h.DistanceTo(centre) - body.BoundingRadiusM;
            _worstOffSurface = Mathf.Max(_worstOffSurface, off);
            if (off > OffSurfaceBarM)
            {
                Fail($"a hand sat {off * 100f:F2} cm outside prop {prop.PropId}'s bounding "
                     + $"sphere at t={_t:F2}s (bar {OffSurfaceBarM * 100f:F0} cm)");
            }
        }

        _samples++;
        if (prop.PropId != _lastLoggedProp)
        {
            _lastLoggedProp = prop.PropId;
            // The two-hand rule's evidence, one line per hold, asserted by the runner: a can is
            // one hand and a crate is two, and the line carries the numbers that decided it.
            string line = $"prop={prop.PropId} r={body.BoundingRadiusM:F3} mass={body.MassKg:F2} "
                          + $"hands={hands.Length}";
            _holds.Add(line);
            GD.Print($"[hands-selftest] HOLD {line}");
        }
    }

    // ------------------------------------------------------------------------------- the script

    private void Drive()
    {
        switch (_phase)
        {
            case 0:
            {
                // ARRIVED? Both halves: standing still AND able to reach what the fixture staged.
                // Either one alone lies -- a bot wedged against a shelf is also standing still.
                bool still = _avatar.Velocity.Length() < 0.4f;
                NetworkedProp? small = FirstFixtureProp();
                NetworkedProp? large = SecondFixtureProp();
                if (!still || small == null || large == null)
                {
                    if (_t > SettleDeadline)
                    {
                        Fail($"the probe never settled within reach of its fixture by "
                             + $"t={_t:F2}s -- speed {_avatar.Velocity.Length():F2} m/s, "
                             + $"fixture[0]={Describe(small)} fixture[1]={Describe(large)}");
                        _settledAt = _t;     // run the rest anyway, so the log says what happened
                        _phase = 1;
                    }
                    return;
                }
                _settledAt = _t;
                IPressable? btn = _avatar.FindNearestPressable();
                Vector3 me = _avatar.GlobalPosition;
                // The staging line, printed before anything is asserted about the subject: what
                // this probe can actually reach from where it is standing. A fixture that cannot
                // reach its props fails on its SUBJECT unless it says this out loud first -- the
                // trap INT-1 measured when a seeded prop landed behind four aisles and the suite
                // reported it as "PLACE IS NOT ON THE WIRE".
                GD.Print($"[hands-selftest] STAGING settled at t={_t:F2}s at "
                         + $"({me.X:F2},{me.Y:F2},{me.Z:F2}) reach={SandboxAvatar.PickupRadius:F2} m -- "
                         + $"fixture[0]={Describe(small)} fixture[1]={Describe(large)} "
                         + $"pressable={(btn == null ? "none" : $"{me.DistanceTo(btn.GlobalPosition):F2} m")}");
                Shot("idle");
                _phase = 1;
                return;
            }

            case 1:
                if (E < CanAim)
                    return;
                AimAt(FirstFixtureProp());
                _phase = 2;
                return;

            case 2:
                if (E < CanGrab)
                    return;
                if (!_hands.IsHolding)
                {
                    RetryGrab(FirstFixtureProp(), deadline: CanDrop);
                    return;
                }
                _phase = 3;
                return;

            case 3:
                if (E < ReachShot)
                    return;
                Shot("reaching");
                // ...and then LOOK UP, which is what a player does the moment something is in
                // their hands. The hold rides the view ray, so raising the look raises the
                // object: the fixture's props rest on the floor, and a capture taken from the
                // grab pose is a steeply-downward shot of a small object on a dark floor. This is
                // the shipped behaviour driven the way a person drives it, not a camera cheat.
                CarryAngle();
                _phase = 4;
                return;

            case 4:
                if (E < CanShot)
                    return;
                Shot("one-hand-can");
                _phase = 5;
                return;

            case 5:
                if (E < ScrollOut)
                    return;
                Scroll(+2);
                _phase = 6;
                return;

            case 6:
                if (E < ScrollIn)
                    return;
                Scroll(-3);
                _phase = 7;
                return;

            case 7:
                // The turn, on the same field HeldPropRotator writes. Continuous, so the frames
                // between here and the drop are the ones that prove the hand RIDES the prop.
                if (E >= RotateFrom && E <= RotateTo)
                {
                    _avatar.HeldPropLocalRotation =
                        new Basis(Vector3.Up, (float)((E - RotateFrom) * 1.4)).Orthonormalized();
                }
                if (E < CanDrop)
                    return;
                if (_hands.IsHolding)
                    _avatar.Props?.ClientRequestDrop();
                _phase = 8;
                return;

            case 8:
                if (E < ReturnShot)
                    return;
                Shot("break-hold-return");
                _phase = 9;
                return;

            case 9:
                if (E < CrateAim)
                    return;
                AimAt(SecondFixtureProp());
                _phase = 10;
                return;

            case 10:
                if (E < CrateGrab)
                    return;
                if (!_hands.IsHolding)
                {
                    RetryGrab(SecondFixtureProp(), deadline: CrateDrop);
                    return;
                }
                _phase = 11;
                return;

            case 11:
                if (E < CrateCarry)
                    return;
                CarryAngle();
                if (E < CrateShot)
                    return;
                Shot("two-hand-crate");
                _phase = 12;
                return;

            case 12:
                if (E < CrateDrop)
                    return;
                if (_hands.IsHolding)
                    _avatar.Props?.ClientRequestDrop();
                _phase = 13;
                return;

            case 13:
                if (E < PressAim)
                    return;
                if (_avatar.FindNearestPressable() is { } pressable)
                    AimAtPoint(pressable.GlobalPosition);
                else
                    Fail($"no pressable in reach at t={_t:F2}s -- the poke beat was never staged");
                _phase = 14;
                return;

            case 14:
                if (E < PressAt)
                    return;
                // The shipped client verb. The round will REFUSE a Confirm outside its phase and
                // that is fine: the poke is the hand moving, not the round's answer, and every
                // press path in the game goes through this one call.
                RoundControls.Instance?.ClientRequestPress(RoundButtonKind.Confirm);
                _phase = 15;
                return;

            case 15:
                if (E < PokeShot)
                    return;
                Shot("poke");
                _phase = 16;
                return;

            case 16:
                if (E < Done)
                    return;
                Report();
                _phase = 17;
                return;
        }
    }

    // ---------------------------------------------------------------------------------- helpers

    private void RetryGrab(NetworkedProp? target, double deadline)
    {
        if (E > deadline)
        {
            Fail($"never got hold of a prop by t={_t:F2}s (E={E:F2}s) -- the grab beat was never staged "
                 + $"(nearest free carryable in reach: {(target == null ? "none" : target.PropId.ToString())})");
            _phase++;
            return;
        }
        if (E < _retry || target == null)
            return;
        _retry = E + 0.35;
        AimAt(target);
        _avatar.Props?.ClientRequestGrab(target.PropId);
    }

    private void Scroll(int notches) => _hands.HeldProp?.ScrollHold(notches);

    /// <summary>Raise the look to a carrying angle, keeping the yaw. <see cref="CarryPitchRad"/>
    /// below eye level, which is where a person holds something they are looking at.</summary>
    private void CarryAngle() => _hands.Camera.SetLook(_hands.Camera.Yaw, CarryPitchRad);

    /// <summary>The pitch a carried object is looked at from, radians below level. 0.30 rad
    /// (17 deg) puts a prop held at the 1.2 m ceiling about 0.35 m below the eye -- chest height,
    /// in frame, and clear of the floor behind it.</summary>
    private const float CarryPitchRad = -0.30f;

    private string Describe(NetworkedProp? prop) =>
        prop == null || !IsInstanceValid(prop.Body)
            ? "none"
            : $"prop {prop.PropId} r={prop.Body.BoundingRadiusM:F3} "
              + $"at {_avatar.GlobalPosition.DistanceTo(prop.Body.GlobalPosition):F2} m";

    /// <summary>The first object the fixture staged (the can), and the second (the crate).</summary>
    private NetworkedProp? FirstFixtureProp() => FixtureProp(0);

    private NetworkedProp? SecondFixtureProp() => FixtureProp(1);

    /// <summary>
    /// The free carryable nearest the point <c>--hands-props</c> named, and inside the client's
    /// own pickup radius.
    ///
    /// <para><b>By POSITION, not by prop id and not by "the biggest thing in reach".</b> An
    /// authored prop's id is a function of every other prop's node NAME (HOLD-1 recorded three
    /// renumberings in two days), so a typed id rots. And "the biggest free carryable in reach"
    /// picks up the ROOM: measured on this suite's first real runs, it came back with
    /// <c>Stock/Bin_0</c> -- a 6 kg floor bin 1.1 m from the fixture's own crate, and a perfectly
    /// legal carryable -- instead of the crate that was seeded for it. Naming the spot is the only
    /// version of this that says what the fixture is about.</para></summary>
    private NetworkedProp? FixtureProp(int index)
    {
        if (index >= _fixture.Count)
            return null;
        Vector3 at = _fixture[index];
        NetworkedProp? best = null;
        float bestD = float.MaxValue;
        foreach (Node node in GetTree().GetNodesInGroup(Carryable.Group))
        {
            if (node is not Carryable c || c.IsHeld)
                continue;
            if (_avatar.GlobalPosition.DistanceTo(c.GlobalPosition) > SandboxAvatar.PickupRadius)
                continue;
            if (c.GetParentOrNull<NetworkedProp>() is not { } prop)
                continue;
            float d = at.DistanceTo(c.GlobalPosition);
            if (d < bestD)
            {
                bestD = d;
                best = prop;
            }
        }
        // Half a metre: the fixture's two objects are a metre apart and the nearest piece of the
        // room is 1.1 m away, so anything further from the named spot than this is a different
        // object and the beat was never staged.
        return bestD <= 0.5f ? best : null;
    }

    private void AimAt(NetworkedProp? prop)
    {
        if (prop != null && IsInstanceValid(prop.Body))
            AimAtPoint(prop.Body.GlobalPosition);
    }

    /// <summary>Point the real lens at a world point. The camera is the aim source
    /// (<c>InteractTargeting.Pick</c> resolves against <c>SandboxAvatar.AimCamera</c>), so this is
    /// how a bot looks at something — FP-1's <c>--fp-look</c>, moved per beat.</summary>
    private void AimAtPoint(Vector3 target)
    {
        Vector3 d = target - _avatar.AimOriginGlobalPosition;
        if (d.LengthSquared() < 1e-6f)
            return;
        float yaw = Mathf.Atan2(-d.X, -d.Z);
        float pitch = Mathf.Atan2(d.Y, new Vector2(d.X, d.Z).Length());
        _hands.Camera.SetLook(yaw, pitch);
    }

    private void Shot(string name)
    {
        if (_captureDir.Length == 0 || _capturing)
            return;
        _capturing = true;
        _ = Save(name);
    }

    private async System.Threading.Tasks.Task Save(string name)
    {
        try
        {
            await Dev.ViewportCapture.SaveAsync(
                this, System.IO.Path.Combine(_captureDir, $"hands-{name}.png"), "hands-selftest");
        }
        catch (System.Exception e)
        {
            GD.PushError($"[hands-selftest] capture '{name}' failed: {e}");
        }
        finally
        {
            _capturing = false;
        }
    }

    private void Fail(string why)
    {
        // Once per distinct sentence: a per-frame bar that is broken is broken for hundreds of
        // frames, and a log of hundreds of copies hides the second failure.
        if (_failures.Contains(why) || _failures.Count > 40)
            return;
        _failures.Add(why);
        GD.Print($"[hands-selftest] FAIL {why}");
    }

    private void Report()
    {
        if (_samples < 60)
        {
            _failures.Add($"only {_samples} held sample(s) -- the bars had almost nothing to "
                          + "measure, so a pass here would prove nothing");
        }
        if (_framesWithNoHand > 0)
        {
            _failures.Add($"{_framesWithNoHand} frame(s) held a prop with NO hand on it");
        }
        if (_holds.Count < 2)
        {
            _failures.Add($"only {_holds.Count} distinct hold(s) -- the one-hand and two-hand "
                          + "beats were not both staged");
        }
        if (_hands.PokeCount == 0)
        {
            _failures.Add("the press never moved a hand (PokeCount 0) -- either the press was "
                          + "never made or FirstPersonHands.Poke is not wired to it");
        }

        GD.Print($"[hands-selftest] samples={_samples} worstLateralM={_worstLateral:F4} "
                 + $"worstOffSurfaceM={_worstOffSurface:F4} holds={_holds.Count} "
                 + $"pokes={_hands.PokeCount} noHandFrames={_framesWithNoHand} "
                 + $"reachFrames={_framesReaching}");
        foreach (string h in _holds)
            GD.Print($"[hands-selftest] HOLDROW {h}");
        foreach (string f in _failures)
            GD.Print($"[hands-selftest] FAILURE {f}");
        GD.Print($"[hands-selftest] SUMMARY failures={_failures.Count} "
                 + $"result={(_failures.Count == 0 ? "PASS" : "FAIL")}");
    }
}
