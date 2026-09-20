using Godot;
using MpFoundation.Game.Sandbox;

namespace MpFoundation.Net;

/// <summary>
/// Deterministic checksum of an authoritative <see cref="MoveState"/>, for desync detection: the
/// server can stamp each snapshot with the checksum of the state it simulated, and the client can
/// compare it against the checksum of its own predicted state at the acked tick. A mismatch that
/// persists (not a one-off from ordinary correction) is a genuine divergence between the two
/// simulations — the class of bug that otherwise ships as "it feels wrong sometimes" with no
/// reproducible signature.
///
/// Position/velocity/yaw are QUANTIZED before hashing so that floating-point noise below the
/// visible/physical threshold doesn't read as a desync — only a divergence large enough to matter
/// changes the checksum. FNV-1a over the quantized integers keeps it order-sensitive, cheap, and
/// identical on every platform (no reliance on float bit patterns).
///
/// This is the pure building block; wiring it into the live snapshot exchange (an extra byte or
/// two on the wire + a client-side compare in Reconcile) is a protocol change that must be
/// validated against the multiplayer suite on real hardware — see DesyncMonitor for the
/// zero-wire-change client-side detector that ships enabled today.
/// </summary>
public static class StateChecksum
{
    // Quantization steps: 1 mm of position, 1 mm/s of velocity, ~0.35° of yaw. Below these,
    // client and server are "the same state" for desync purposes.
    private const float PosStep = 0.001f;
    private const float VelStep = 0.001f;
    private const float YawStep = 0.006f;

    public static uint Of(in MoveState s)
    {
        uint h = 2166136261u; // FNV-1a offset basis
        h = Mix(h, Quantize(s.Position.X, PosStep));
        h = Mix(h, Quantize(s.Position.Y, PosStep));
        h = Mix(h, Quantize(s.Position.Z, PosStep));
        h = Mix(h, Quantize(s.Velocity.X, VelStep));
        h = Mix(h, Quantize(s.Velocity.Y, VelStep));
        h = Mix(h, Quantize(s.Velocity.Z, VelStep));
        h = Mix(h, Quantize(s.Yaw, YawStep));
        // --- THE DISCRETE FACTS (MOVE-5e) ---------------------------------------------------------
        // Everything below this line is a discrete replicated fact rather than a continuous one, and
        // until MOVE-5e this method hashed exactly one of them (Grounded) out of the eleven that
        // exist. That was not a MOVE-5 omission — Water, Soaked, ControlLocked, Incapacity,
        // ImpulseRagdoll and the skid all predate the verbs and were all missing too. The class doc
        // calls itself a "checksum of an authoritative MoveState"; a checksum that ignores whether
        // the body is frozen, locked, swimming, sliding or ducked cannot support that sentence.
        //
        // Two states differing ONLY in a discrete fact used to hash identically, which is the same
        // blind spot PredictionMatches had before this packet, in the tool that exists to make a
        // desync legible after the fact. Extended together, as one set, rather than bolting on the
        // five verb fields and leaving the six older ones out — that would have made the class look
        // finished while still missing more than it caught.
        //
        // Packed into one word rather than mixed one-by-one: all eleven are enums, bools or small
        // bytes, they cost eleven rounds of FNV otherwise, and this method is meant to be cheap
        // enough to run per snapshot. The shifts are disjoint and nothing here is wider than its
        // slot (Verb and Incapacity are 2 bits, Water 2, ChainDepth 3, AirJumpsUsed 2), so no two
        // fields can alias into the same bits — checked, not assumed.
        uint discrete = (uint)(s.Grounded ? 1 : 0)
            | (uint)(s.Soaked ? 1 : 0) << 1
            | (uint)(s.ControlLocked ? 1 : 0) << 2
            | (uint)(s.ImpulseRagdoll ? 1 : 0) << 3
            // The skid enters as the BOOLEAN, deliberately, and for the identical reason
            // SandboxAvatar.PredictionMatches compares it that way: SkidRemaining is a float
            // decremented by dt, so hashing the raw value would make the checksum differ on the
            // sub-tick offset between two clocks that agree about everything that matters. The
            // verb counters below need no such treatment — they are integers by construction
            // (MoveState §10.3) and are hashed exactly.
            | (uint)(AvatarMotor.IsSkidding(s) ? 1 : 0) << 4
            | ((uint)s.Water & 0x3u) << 5
            | ((uint)s.Incapacity & 0x3u) << 7
            | ((uint)s.Verb & 0x3u) << 9
            | ((uint)s.ChainDepth & 0x7u) << 11
            | ((uint)s.AirJumpsUsed & 0x3u) << 14;
        h = Mix(h, discrete);
        // The two tick clocks do not fit beside the bit field and are mixed as their own word. They
        // are what decide WHEN the verb and the chain change, so a checksum blind to them is blind
        // to a divergence one tick before it becomes visible.
        h = Mix(h, (uint)s.VerbClockTicks | ((uint)s.ChainTimerTicks << 8));
        return h;
    }

    // Round to a quantization step, then bias into unsigned so negatives hash distinctly.
    private static uint Quantize(float value, float step) =>
        float.IsFinite(value) ? unchecked((uint)(int)Mathf.Round(value / step)) : 0u;

    private static uint Mix(uint h, uint data)
    {
        unchecked
        {
            h ^= data & 0xFF; h *= 16777619u;
            h ^= (data >> 8) & 0xFF; h *= 16777619u;
            h ^= (data >> 16) & 0xFF; h *= 16777619u;
            h ^= (data >> 24) & 0xFF; h *= 16777619u;
        }
        return h;
    }
}
