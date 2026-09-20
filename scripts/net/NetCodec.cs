using System.Buffers.Binary;
using System.Collections.Generic;
using Godot;
using MpFoundation.Game.Aim;
using MpFoundation.Game.Sandbox;

namespace MpFoundation.Net;

/// <summary>
/// Wire formats for the movement authority loop. Everything rides ENet channel
/// <see cref="MoveChannel"/> unreliable: inputs are latest-wins with a redundancy window
/// (each packet re-sends the last few inputs, so a lost datagram costs nothing unless
/// several in a row vanish), and state snapshots are latest-wins by tick. Unreliable
/// keeps movement free of head-of-line blocking; reliability is synthesized where it
/// matters (redundancy for inputs, monotonic-tick filtering for snapshots).
/// All parsing is defensive — a malformed packet from a hostile client yields null,
/// never an exception or a poisoned simulation.
/// </summary>
public static class NetCodec
{
    // Channel + codec-bound aliases over the per-title config (NetProfile). Kept as public
    // consts here so existing [Rpc(TransferChannel = NetCodec.PropChannel)] attributes and
    // call sites compile unchanged; the real values live in one place.
    public const int MoveChannel = NetProfile.MoveChannel;
    public const int PropChannel = NetProfile.PropChannel;
    public const int CycleChannel = NetProfile.CycleChannel;
    public const int RunChannel = NetProfile.RunChannel;
    public const int InputRedundancy = NetProfile.InputRedundancy;
    public const int MaxInputEntries = NetProfile.MaxInputEntries;

    public readonly record struct InputEntry(MoveIntent Intent, float SpeedFactor);

    /// <summary>One authoritative avatar state as broadcast by the server. AimStance/
    /// AimSteadyElapsedSec (WP-L3, additive) default so every pre-existing call site (movement-
    /// only construction, including the round-trip/fuzz tests) keeps compiling unchanged; the
    /// aim substrate is the only code that ever supplies them explicitly.</summary>
    public readonly record struct Snapshot(uint Tick, byte Epoch, uint LastProcessedSeq, MoveState State,
        AimStance AimStance = AimStance.Lowered, float AimSteadyElapsedSec = 0f);

    private const byte FlagJump = 1;
    private const byte FlagInteract = 2;
    private const byte FlagThrow = 4;
    private const byte FlagSprint = 8;
    private const byte FlagAimRaise = 16;
    // Bits 32, 64 and 128 of this byte are FREE (the bits that used them were retired with the
    // since-removed systems that owned them).

    // --- Second input buttons byte (buttons2) — MOVE-3, protocol v12, 2026-08-26 ---------------
    /// <summary>Variable jump height (MOVE-3): <c>MoveIntent.JumpHeld</c>, the LEVEL of the jump
    /// action beside the existing edge in <see cref="FlagJump"/>.
    ///
    /// <para>Added when the first buttons byte was full (v8): <c>InputEntryBytes</c> went 25 -> 26
    /// and <see cref="NetProfile.ProtocolVersion"/> 11 -> 12.</para>
    ///
    /// <para><b>Bits 2, 4, 8, 16, 32, 64 and 128 of this byte are FREE.</b> Documented here so the
    /// next addition knows it costs nothing and does not reach for a third byte.</para>
    ///
    /// <para>The snapshot is untouched at <see cref="SnapshotBytes"/>: the jump cut is a gravity
    /// term derived from the intent, deliberately, so no <c>MoveState</c> field and no snapshot bit
    /// were spent (see <c>AvatarMotor.JumpReleaseGravityMultiplier</c>).</para></summary>
    private const byte Flags2JumpHeld = 1;

    // --- Snapshot flags byte (span[5]) — a SEPARATE byte from the input buttons bytes above ---
    private const byte FlagGrounded = 1;
    /// <summary>Water state (W2, lake-water contract §4) as a two-bit field in bits 1–2 of the
    /// snapshot flags byte: the enum has three values and the byte had seven free bits, so the
    /// whole water contract rides the wire for zero extra bytes and the 51-byte snapshot layout
    /// is byte-for-byte unchanged. The byte's MEANING changed, so it cost a protocol bump
    /// (v8 -> v9) even though the length did not.</summary>
    private const byte WaterStateShift = 1;
    private const byte WaterStateMask = 0b0000_0110;
    /// <summary>Soaked (§7). Bit 3.</summary>
    private const byte FlagSoaked = 8;
    /// <summary>Control locked by a sputter-out (§6). Bit 4. Replicated rather than inferred,
    /// because the owning client must stop predicting its own steering the same tick the server
    /// stopped accepting it — inferring it locally from chill would put the two a round-trip
    /// apart and hand the player a moment of ghost control (MECHANICS-BIBLE §2).</summary>
    private const byte FlagControlLocked = 16;
    /// <summary>Incapacitation state (phase 1c, beta plan §10) as a two-bit field in bits 5–6 of
    /// the snapshot flags byte. Same spare-bit economy as the water field above it, same
    /// zero-length-change result, same protocol bump (v9 -> v10) because the byte's MEANING
    /// changed. Two bits hold four values: Active / KnockedOut / Frozen and <b>one reserved</b>
    /// for the third state beta plan §3.2 leaves to Talon — so adding it later is a doc line, not
    /// a wire-layout change. That headroom was bought deliberately.</summary>
    private const byte IncapacityShift = 5;
    private const byte IncapacityMask = 0b0110_0000;
    /// <summary>Momentary impulse ragdoll (beta plan §8.1). Bit 7 — <b>the LAST free bit of the
    /// snapshot flags byte.</b>
    ///
    /// <para><b>This byte is now full.</b> Stated here in the same words the input buttons byte
    /// uses, and for the same reason: the next snapshot flag needs a second byte and therefore a
    /// real layout change, not another spare-bit reuse. Discovering that at design time is worth
    /// this comment; discovering it by finding 128 already taken is not.</para>
    ///
    /// <para>It is on the wire rather than inferred because control denial must begin on the
    /// client the same tick the server stopped accepting steering — the identical argument
    /// <see cref="FlagControlLocked"/> makes, and inferring a 1.2-second effect locally would put
    /// the two a full round-trip apart for most of its duration.</para></summary>
    private const byte FlagImpulseRagdoll = 128;

    // --- Second SNAPSHOT flags byte (flags2, span[55]) — MOVE-5, protocol v13, 2026-08-27 -------
    /// <summary><c>MoveState.Verb</c> — Normal / Tuck / Slide / DuckWalk — as a two-bit field in
    /// bits 0-1 of the second snapshot flags byte.
    ///
    /// <para><b>This is the addition the flags byte's own comment predicted.</b> That byte has been
    /// documented full since v10 and says the next snapshot flag needs a second byte and a real
    /// layout change rather than another spare-bit reuse. This is that flag, and MOVE-5 spends the
    /// new byte on all three of its bit fields at once, deliberately, so the wave costs one layout
    /// change rather than three.</para>
    ///
    /// <para><b>All four two-bit values are legal</b>, so — unlike <see cref="WaterStateMask"/> and
    /// <see cref="IncapacityMask"/>, which each have a value to fold — there is nothing a doctored
    /// packet could land on here that needs folding to a boring legal state.</para></summary>
    private const byte Flags2VerbShift = 0;
    private const byte Flags2VerbMask = 0b0000_0011;
    /// <summary><c>MoveState.ChainDepth</c>, bits 2-4. <b>Three bits, and that width is the reason
    /// <c>MotorTuningKnobs.ChainMaxDepth</c>'s max of 7 is a hard validator clamp rather than a
    /// widget bound</b> (spec §11.3): past it the field truncates and a peer's chain depth
    /// disagrees with the server's, which is a divergence rather than a cosmetic gap.</summary>
    private const byte Flags2ChainDepthShift = 2;
    private const byte Flags2ChainDepthMask = 0b0001_1100;
    /// <summary><c>MoveState.AirJumpsUsed</c>, bits 5-6. Two bits, same wire-width argument as
    /// <see cref="Flags2ChainDepthMask"/> against <c>AirJumpCountMax</c>'s max of 3.</summary>
    private const byte Flags2AirJumpsShift = 5;
    private const byte Flags2AirJumpsMask = 0b0110_0000;
    /// <summary><b>Bit 7 of the snapshot's <c>flags2</c> byte is FREE — one spare, and exactly
    /// one.</b> Stated here in the same words the two full bytes above use, and for the same
    /// reason: spec §10.2 costed this byte at 2 + 3 + 2 = 7 bits and said so at design time rather
    /// than leaving the ninth flag's cost to be discovered by somebody finding 128 already taken.
    /// <b>It is not the bit fork O1 would spend</b> — that one is a second INPUT binding and would
    /// come out of <c>buttons2</c>, which still has seven free.</summary>
    private const byte Flags2SnapshotSpare = 128;

    /// <summary>
    /// seq(4) + dirX(4) + dirZ(4) + buttons(1) + <b>buttons2(1)</b> + speedFactor(4) + aimYaw(4) +
    /// aimPitch(4) = <b>26</b>.
    ///
    /// <para>The two aim floats are WP-L3 additions (aim substrate) appended after the pre-existing
    /// fields so the layout of everything before them is byte-for-byte unchanged. A later verb
    /// (L4) reused a spare bit in the existing buttons byte, so this count was unchanged by it.</para>
    ///
    /// <para><b>MOVE-3 (2026-08-26) is the first input-side addition that had to cost a byte:</b>
    /// the buttons byte has been documented full since v8, so <c>MoveIntent.JumpHeld</c> gets
    /// <c>buttons2</c>, written and read immediately after <c>buttons</c> so the pack and unpack
    /// stay a pure mirror and everything after it keeps its order. 25 -> 26,
    /// <see cref="NetProfile.ProtocolVersion"/> 11 -> 12.</para>
    ///
    /// <para>Public so tests assert against the constant rather than a literal somebody has to
    /// remember to update — the same reason, and the same churn, <see cref="SnapshotBytes"/>
    /// records.</para>
    /// </summary>
    public const int InputEntryBytes = 26;
    /// <summary>
    /// The 46-byte movement layout plus AimStance(1) + AimSteadyElapsedSec(4) (WP-L3) plus
    /// SkidRemaining(4) (SKID-1) — every one of them appended at the END, so the layout of
    /// everything before each addition is byte-for-byte unchanged.
    ///
    /// <para><b>SKID-1 is the first snapshot addition that had to cost bytes.</b> Water, Soaked,
    /// ControlLocked, Incapacity and ImpulseRagdoll all rode spare bits, and both this byte and the
    /// input buttons byte have said "now full" in their own comments since. The skid needs a
    /// <i>duration</i>, not a bit, so there was nothing to economise — see
    /// <c>MoveState.SkidRemaining</c> for why a quantized byte was refused. Protocol v10 -> v11.</para>
    ///
    /// <para>Public so tests assert against the constant rather than against a literal somebody has
    /// to remember to update — the churn this change caused in four test files is the argument.</para>
    ///
    /// <para><b>MOVE-5 (2026-08-27) costs three more: 55 -> 58 (+5.45%), protocol v12 -> v13.</b>
    /// The verb state machine needs five new <c>MoveState</c> fields and they pack into
    /// <c>flags2</c> (1 byte: Verb 2 bits + ChainDepth 3 + AirJumpsUsed 2, one spare) plus
    /// <c>VerbClockTicks</c> (1) plus <c>ChainTimerTicks</c> (1), all three appended at the END so
    /// everything before them is byte-for-byte unchanged. <b>The two clocks are quantized where
    /// <c>SkidRemaining</c> refused to be</b>, and the difference is exact-versus-approximate: they
    /// are integers by construction (set from a knob at <c>round(sec x 60)</c>, decremented by
    /// exactly 1 per fixed tick, compared against integers), so a byte holds them EXACTLY. See
    /// <c>MoveState.VerbClockTicks</c> for the one dependency that argument rests on.
    /// <b><see cref="InputEntryBytes"/> is unchanged at 26 and no new input bit was spent</b> —
    /// every verb in the wave is carried by <c>MoveIntent.Jump</c>, <c>JumpHeld</c> and
    /// <c>MoveDir</c>, all three already on the wire, and <c>buttons2</c> keeps its seven free
    /// bits.</para>
    /// </summary>
    public const int SnapshotBytes = 58;

    // These write/read directly into the wire byte[] with BinaryPrimitives (explicit
    // little-endian), replacing the per-packet MemoryStream + BinaryWriter/BinaryReader churn
    // (3+ allocations each) at 60 Hz per client. The layout — field order, widths, and
    // little-endian byte order — is byte-for-byte identical to the previous BinaryWriter
    // output, so the wire format is unchanged and no protocol bump is needed. The single
    // remaining allocation per call is the result buffer/list itself, which crosses the Godot
    // RPC boundary and cannot be pooled.

    // --- Inputs: client -> server --------------------------------------------------

    public static byte[] PackInputs(IReadOnlyList<InputEntry> entries)
    {
        // The writer must never produce a packet its own reader rejects: UnpackInputs bounds
        // count to [1, MaxInputEntries], and a >255 count would silently wrap the byte.
        if (entries.Count == 0 || entries.Count > MaxInputEntries)
            throw new System.ArgumentOutOfRangeException(nameof(entries),
                $"input packet must carry 1..{MaxInputEntries} entries, got {entries.Count}");
        var buf = new byte[1 + entries.Count * InputEntryBytes];
        var span = buf.AsSpan();
        span[0] = (byte)entries.Count;
        int o = 1;
        // Indexed, not foreach: enumerating through the IReadOnlyList<T> INTERFACE boxes the
        // underlying List<T>'s struct enumerator onto the heap, once per call — and this runs at
        // 60 Hz on every client. Indexing is the same reads in the same order with no allocation
        // (perf audit 2026-08-07). The parameter type stays IReadOnlyList so no call site moves.
        for (int i = 0; i < entries.Count; i++)
        {
            InputEntry e = entries[i];
            BinaryPrimitives.WriteUInt32LittleEndian(span[o..], e.Intent.Seq); o += 4;
            BinaryPrimitives.WriteSingleLittleEndian(span[o..], e.Intent.MoveDir.X); o += 4;
            BinaryPrimitives.WriteSingleLittleEndian(span[o..], e.Intent.MoveDir.Z); o += 4;
            byte buttons = 0;
            if (e.Intent.Jump) buttons |= FlagJump;
            if (e.Intent.Interact) buttons |= FlagInteract;
            if (e.Intent.Throw) buttons |= FlagThrow;
            if (e.Intent.Sprint) buttons |= FlagSprint;
            if (e.Intent.AimRaise) buttons |= FlagAimRaise;
            span[o++] = buttons;
            byte buttons2 = 0;
            if (e.Intent.JumpHeld) buttons2 |= Flags2JumpHeld;
            span[o++] = buttons2;
            BinaryPrimitives.WriteSingleLittleEndian(span[o..], e.SpeedFactor); o += 4;
            BinaryPrimitives.WriteSingleLittleEndian(span[o..], e.Intent.AimYaw); o += 4;
            BinaryPrimitives.WriteSingleLittleEndian(span[o..], e.Intent.AimPitch); o += 4;
        }
        return buf;
    }

    public static List<InputEntry>? UnpackInputs(byte[]? packet)
    {
        if (packet == null || packet.Length < 1)
            return null;
        int count = packet[0];
        if (count == 0 || count > MaxInputEntries || packet.Length != 1 + count * InputEntryBytes)
            return null;

        var span = packet.AsSpan();
        var entries = new List<InputEntry>(count);
        int o = 1;
        for (int i = 0; i < count; i++)
        {
            uint seq = BinaryPrimitives.ReadUInt32LittleEndian(span[o..]); o += 4;
            float dirX = BinaryPrimitives.ReadSingleLittleEndian(span[o..]); o += 4;
            float dirZ = BinaryPrimitives.ReadSingleLittleEndian(span[o..]); o += 4;
            byte buttons = span[o++];
            byte buttons2 = span[o++];
            float speedFactor = BinaryPrimitives.ReadSingleLittleEndian(span[o..]); o += 4;
            float aimYaw = BinaryPrimitives.ReadSingleLittleEndian(span[o..]); o += 4;
            float aimPitch = BinaryPrimitives.ReadSingleLittleEndian(span[o..]); o += 4;
            entries.Add(new InputEntry(
                new MoveIntent
                {
                    // Sanitized again by AvatarMotor.Step; scrubbing here too keeps
                    // garbage floats from ever entering a buffer.
                    MoveDir = AvatarMotor.SanitizeMoveDir(new Vector3(dirX, 0, dirZ)),
                    Jump = (buttons & FlagJump) != 0,
                    Interact = (buttons & FlagInteract) != 0,
                    Throw = (buttons & FlagThrow) != 0,
                    Sprint = (buttons & FlagSprint) != 0,
                    JumpHeld = (buttons2 & Flags2JumpHeld) != 0,
                    AimRaise = (buttons & FlagAimRaise) != 0,
                    AimYaw = AvatarMotor.SanitizeAimYaw(aimYaw),
                    AimPitch = AvatarMotor.SanitizeAimPitch(aimPitch),
                    Seq = seq,
                },
                AvatarMotor.SanitizeSpeedFactor(speedFactor)));
        }
        return entries;
    }

    // --- Snapshots: server -> clients ------------------------------------------------

    public static byte[] PackSnapshot(in Snapshot snap)
    {
        var buf = new byte[SnapshotBytes];
        var span = buf.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(span[0..], snap.Tick);
        span[4] = snap.Epoch;
        byte stateFlags = (byte)(snap.State.Grounded ? FlagGrounded : 0);
        stateFlags |= (byte)(((byte)snap.State.Water << WaterStateShift) & WaterStateMask);
        if (snap.State.Soaked) stateFlags |= FlagSoaked;
        if (snap.State.ControlLocked) stateFlags |= FlagControlLocked;
        stateFlags |= (byte)(((byte)snap.State.Incapacity << IncapacityShift) & IncapacityMask);
        if (snap.State.ImpulseRagdoll) stateFlags |= FlagImpulseRagdoll;
        span[5] = stateFlags;
        BinaryPrimitives.WriteUInt32LittleEndian(span[6..], snap.LastProcessedSeq);
        BinaryPrimitives.WriteSingleLittleEndian(span[10..], snap.State.Position.X);
        BinaryPrimitives.WriteSingleLittleEndian(span[14..], snap.State.Position.Y);
        BinaryPrimitives.WriteSingleLittleEndian(span[18..], snap.State.Position.Z);
        BinaryPrimitives.WriteSingleLittleEndian(span[22..], snap.State.Velocity.X);
        BinaryPrimitives.WriteSingleLittleEndian(span[26..], snap.State.Velocity.Y);
        BinaryPrimitives.WriteSingleLittleEndian(span[30..], snap.State.Velocity.Z);
        BinaryPrimitives.WriteSingleLittleEndian(span[34..], snap.State.Yaw);
        BinaryPrimitives.WriteSingleLittleEndian(span[38..], snap.State.CoyoteRemaining);
        BinaryPrimitives.WriteSingleLittleEndian(span[42..], snap.State.JumpBufferRemaining);
        span[46] = (byte)snap.AimStance;
        BinaryPrimitives.WriteSingleLittleEndian(span[47..], snap.AimSteadyElapsedSec);
        BinaryPrimitives.WriteSingleLittleEndian(span[51..], snap.State.SkidRemaining);
        // MOVE-5 (protocol v13). Masked on the way OUT as well as on the way in, so a state carrying
        // an out-of-range depth from anywhere at all cannot corrupt the neighbouring bit fields —
        // the same total-function discipline StepSkid's own cap takes.
        byte flags2 = (byte)(((byte)snap.State.Verb << Flags2VerbShift) & Flags2VerbMask);
        flags2 |= (byte)((snap.State.ChainDepth << Flags2ChainDepthShift) & Flags2ChainDepthMask);
        flags2 |= (byte)((snap.State.AirJumpsUsed << Flags2AirJumpsShift) & Flags2AirJumpsMask);
        span[55] = flags2;
        span[56] = snap.State.VerbClockTicks;
        span[57] = snap.State.ChainTimerTicks;
        return buf;
    }

    public static Snapshot? UnpackSnapshot(byte[]? packet)
    {
        if (packet == null || packet.Length != SnapshotBytes)
            return null;
        var span = packet.AsSpan();
        uint tick = BinaryPrimitives.ReadUInt32LittleEndian(span[0..]);
        byte epoch = span[4];
        byte flags = span[5];
        uint ack = BinaryPrimitives.ReadUInt32LittleEndian(span[6..]);
        var pos = new Vector3(
            BinaryPrimitives.ReadSingleLittleEndian(span[10..]),
            BinaryPrimitives.ReadSingleLittleEndian(span[14..]),
            BinaryPrimitives.ReadSingleLittleEndian(span[18..]));
        var vel = new Vector3(
            BinaryPrimitives.ReadSingleLittleEndian(span[22..]),
            BinaryPrimitives.ReadSingleLittleEndian(span[26..]),
            BinaryPrimitives.ReadSingleLittleEndian(span[30..]));
        float yaw = BinaryPrimitives.ReadSingleLittleEndian(span[34..]);
        float coyote = BinaryPrimitives.ReadSingleLittleEndian(span[38..]);
        float jumpBuffer = BinaryPrimitives.ReadSingleLittleEndian(span[42..]);
        byte aimStanceByte = span[46];
        float aimSteadyElapsedSec = BinaryPrimitives.ReadSingleLittleEndian(span[47..]);
        float skidRemaining = BinaryPrimitives.ReadSingleLittleEndian(span[51..]);
        if (!IsFinite(pos) || !IsFinite(vel) || !float.IsFinite(yaw)
            || !float.IsFinite(coyote) || !float.IsFinite(jumpBuffer) || !float.IsFinite(aimSteadyElapsedSec)
            || !float.IsFinite(skidRemaining)
            || aimStanceByte > (byte)AimStance.Lowering)
            return null;
        // Clamped, not merely finiteness-checked. The timer is only ever produced by
        // AvatarMotor.StepSkid and therefore only ever lands in [0, SkidMaxSec]; a doctored packet
        // claiming 900 s would otherwise park that peer's PROXY POSE in a permanent brake for as
        // long as it liked. Clamping here means an out-of-range claim decays like any other skid
        // instead of becoming a state the sender chose. (The value never reaches an authoritative
        // simulation — servers read inputs, not snapshots — so this is a presentation guard, and
        // "boring and legal" is the same reading the water and incapacity folds above take.)
        skidRemaining = Mathf.Clamp(skidRemaining, 0f, AvatarMotor.SkidMaxSec);
        // The two water bits can encode 3, which is not a WaterState. Folded to Dry rather than
        // cast blind: a doctored packet must produce a boring legal state, never a switch that
        // falls through to an undefined branch on every subsequent tick.
        var water = (Sail.Game.Water.WaterState)((flags & WaterStateMask) >> WaterStateShift);
        if (water > Sail.Game.Water.WaterState.Swimming)
            water = Sail.Game.Water.WaterState.Dry;
        // The two incapacity bits can encode 3, which is the RESERVED value — no state is
        // declared for it yet. Folded to Active for exactly the reason the water fold above
        // gives, plus one more that is specific to this field: the reserved slot exists so a
        // third state can be added later, which means a v10 client will one day meet a packet
        // from a build that HAS one. "Boring and legal" is the only safe reading of a state you
        // do not know, and Active is the state in which the player can still act.
        var incapacity = (Sail.Game.Failure.IncapacityState)((flags & IncapacityMask) >> IncapacityShift);
        if (incapacity > Sail.Game.Failure.IncapacityState.Frozen)
            incapacity = Sail.Game.Failure.IncapacityState.Active;

        // MOVE-5 (protocol v13). The verb needs no fold — all four two-bit values are declared. The
        // two counters DO get clamped to the live tuning's caps, for the reason SkidRemaining's
        // clamp above gives: a doctored packet must produce a boring legal state, and a claimed
        // depth of 7 against a ChainMaxDepth of 4 would otherwise park that peer's PROXY POSE at a
        // tell the sender chose. It never reaches an authoritative simulation — servers read inputs,
        // not snapshots — so this is a presentation guard, exactly like the three above it.
        byte flags2 = span[55];
        var verb = (MoveVerb)((flags2 & Flags2VerbMask) >> Flags2VerbShift);
        var chainDepth = (byte)Mathf.Min((flags2 & Flags2ChainDepthMask) >> Flags2ChainDepthShift,
            AvatarMotor.ChainMaxDepth);
        var airJumpsUsed = (byte)Mathf.Min((flags2 & Flags2AirJumpsMask) >> Flags2AirJumpsShift,
            AvatarMotor.AirJumpCountMax);

        return new Snapshot(tick, epoch, ack, new MoveState
        {
            Verb = verb,
            VerbClockTicks = span[56],
            ChainDepth = chainDepth,
            ChainTimerTicks = span[57],
            AirJumpsUsed = airJumpsUsed,
            Position = pos,
            Velocity = vel,
            Yaw = yaw,
            CoyoteRemaining = coyote,
            JumpBufferRemaining = jumpBuffer,
            Grounded = (flags & FlagGrounded) != 0,
            SkidRemaining = skidRemaining,
            Water = water,
            Soaked = (flags & FlagSoaked) != 0,
            ControlLocked = (flags & FlagControlLocked) != 0,
            Incapacity = incapacity,
            ImpulseRagdoll = (flags & FlagImpulseRagdoll) != 0,
        }, (AimStance)aimStanceByte, aimSteadyElapsedSec);
    }

    private static bool IsFinite(Vector3 v) =>
        float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
