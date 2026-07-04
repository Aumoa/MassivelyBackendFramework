using System;

namespace MasterServer.ControlPlane;

public enum BackendPacketVerifierOperation : byte
{
    ReadPrimitive = 1,
    ReadBytes = 2,
    ReadUtf8String = 3,
    Repeat = 4,
    BreakRepeatIfValueEquals = 5
}

public enum BackendPacketVerifierPrimitive : byte
{
    UInt8 = 1,
    UInt16 = 2,
    UInt32 = 3,
    Int32 = 4,
    Int64 = 5,
    Guid = 6
}

public sealed class BackendPacketVerifierInstruction
{
    public const int NoSlot = -1;
    public const int MaxSlotCount = 32;

    internal BackendPacketVerifierInstruction(
        BackendPacketVerifierOperation operation,
        BackendPacketVerifierPrimitive primitive,
        int targetSlot,
        BackendPacketVerifierValueConstraint? valueConstraint,
        BackendPacketVerifierLengthConstraint? lengthConstraint,
        int repeatCountSlot,
        int minimumRepeatCount,
        int maximumRepeatCount,
        BackendPacketVerifierInstruction[]? body,
        int breakValueSlot,
        long breakValue)
    {
        Operation = operation;
        Primitive = primitive;
        TargetSlot = targetSlot;
        ValueConstraint = valueConstraint;
        LengthConstraint = lengthConstraint;
        RepeatCountSlot = repeatCountSlot;
        MinimumRepeatCount = minimumRepeatCount;
        MaximumRepeatCount = maximumRepeatCount;
        Body = body ?? Array.Empty<BackendPacketVerifierInstruction>();
        BreakValueSlot = breakValueSlot;
        BreakValue = breakValue;
        ValidateShape();
    }

    public BackendPacketVerifierOperation Operation { get; }

    public BackendPacketVerifierPrimitive Primitive { get; }

    public int TargetSlot { get; }

    public BackendPacketVerifierValueConstraint? ValueConstraint { get; }

    public BackendPacketVerifierLengthConstraint? LengthConstraint { get; }

    public int RepeatCountSlot { get; }

    public int MinimumRepeatCount { get; }

    public int MaximumRepeatCount { get; }

    public BackendPacketVerifierInstruction[] Body { get; }

    public int BreakValueSlot { get; }

    public long BreakValue { get; }

    public static BackendPacketVerifierInstruction ReadUInt8(
        int targetSlot = NoSlot,
        BackendPacketVerifierValueConstraint? valueConstraint = null)
    {
        return ReadPrimitive(BackendPacketVerifierPrimitive.UInt8, targetSlot, valueConstraint);
    }

    public static BackendPacketVerifierInstruction ReadUInt16(
        int targetSlot = NoSlot,
        BackendPacketVerifierValueConstraint? valueConstraint = null)
    {
        return ReadPrimitive(BackendPacketVerifierPrimitive.UInt16, targetSlot, valueConstraint);
    }

    public static BackendPacketVerifierInstruction ReadUInt32(
        int targetSlot = NoSlot,
        BackendPacketVerifierValueConstraint? valueConstraint = null)
    {
        return ReadPrimitive(BackendPacketVerifierPrimitive.UInt32, targetSlot, valueConstraint);
    }

    public static BackendPacketVerifierInstruction ReadInt32(
        int targetSlot = NoSlot,
        BackendPacketVerifierValueConstraint? valueConstraint = null)
    {
        return ReadPrimitive(BackendPacketVerifierPrimitive.Int32, targetSlot, valueConstraint);
    }

    public static BackendPacketVerifierInstruction ReadInt64(
        int targetSlot = NoSlot,
        BackendPacketVerifierValueConstraint? valueConstraint = null)
    {
        return ReadPrimitive(BackendPacketVerifierPrimitive.Int64, targetSlot, valueConstraint);
    }

    public static BackendPacketVerifierInstruction ReadGuid()
    {
        return ReadPrimitive(BackendPacketVerifierPrimitive.Guid, NoSlot, null);
    }

    public static BackendPacketVerifierInstruction ReadBytes(
        BackendPacketVerifierLengthConstraint lengthConstraint,
        int lengthTargetSlot = NoSlot)
    {
        return new BackendPacketVerifierInstruction(
            BackendPacketVerifierOperation.ReadBytes,
            BackendPacketVerifierPrimitive.UInt8,
            lengthTargetSlot,
            null,
            lengthConstraint ?? throw new ArgumentNullException(nameof(lengthConstraint)),
            NoSlot,
            0,
            0,
            null,
            NoSlot,
            0);
    }

    public static BackendPacketVerifierInstruction ReadUtf8String(
        BackendPacketVerifierLengthConstraint lengthConstraint,
        int lengthTargetSlot = NoSlot)
    {
        return new BackendPacketVerifierInstruction(
            BackendPacketVerifierOperation.ReadUtf8String,
            BackendPacketVerifierPrimitive.UInt8,
            lengthTargetSlot,
            null,
            lengthConstraint ?? throw new ArgumentNullException(nameof(lengthConstraint)),
            NoSlot,
            0,
            0,
            null,
            NoSlot,
            0);
    }

    public static BackendPacketVerifierInstruction Repeat(
        int countSlot,
        int minimumCount,
        int maximumCount,
        BackendPacketVerifierInstruction[] body)
    {
        return new BackendPacketVerifierInstruction(
            BackendPacketVerifierOperation.Repeat,
            BackendPacketVerifierPrimitive.UInt8,
            NoSlot,
            null,
            null,
            countSlot,
            minimumCount,
            maximumCount,
            body,
            NoSlot,
            0);
    }

    public static BackendPacketVerifierInstruction BreakRepeatIfValueEquals(
        int valueSlot,
        long value)
    {
        return new BackendPacketVerifierInstruction(
            BackendPacketVerifierOperation.BreakRepeatIfValueEquals,
            BackendPacketVerifierPrimitive.UInt8,
            NoSlot,
            null,
            null,
            NoSlot,
            0,
            0,
            null,
            valueSlot,
            value);
    }

    internal static bool IsValidSlot(int slot)
    {
        return slot >= 0 && slot < MaxSlotCount;
    }

    private static BackendPacketVerifierInstruction ReadPrimitive(
        BackendPacketVerifierPrimitive primitive,
        int targetSlot,
        BackendPacketVerifierValueConstraint? valueConstraint)
    {
        return new BackendPacketVerifierInstruction(
            BackendPacketVerifierOperation.ReadPrimitive,
            primitive,
            targetSlot,
            valueConstraint ?? BackendPacketVerifierValueConstraint.Any,
            null,
            NoSlot,
            0,
            0,
            null,
            NoSlot,
            0);
    }

    private void ValidateShape()
    {
        if (!Enum.IsDefined(typeof(BackendPacketVerifierOperation), Operation))
        {
            throw new ArgumentOutOfRangeException(nameof(Operation));
        }

        switch (Operation)
        {
            case BackendPacketVerifierOperation.ReadPrimitive:
                RequireEmptyBody();
                if (!Enum.IsDefined(typeof(BackendPacketVerifierPrimitive), Primitive))
                {
                    throw new ArgumentOutOfRangeException(nameof(Primitive));
                }

                if (Primitive == BackendPacketVerifierPrimitive.Guid &&
                    TargetSlot != NoSlot)
                {
                    throw new ArgumentException("Guid reads cannot store a scalar verifier slot.", nameof(TargetSlot));
                }

                if (TargetSlot != NoSlot && !IsValidSlot(TargetSlot))
                {
                    throw new ArgumentOutOfRangeException(nameof(TargetSlot));
                }
                break;

            case BackendPacketVerifierOperation.ReadBytes:
            case BackendPacketVerifierOperation.ReadUtf8String:
                RequireEmptyBody();
                if (LengthConstraint == null)
                {
                    throw new ArgumentNullException(nameof(LengthConstraint));
                }

                if (TargetSlot != NoSlot && !IsValidSlot(TargetSlot))
                {
                    throw new ArgumentOutOfRangeException(nameof(TargetSlot));
                }
                break;

            case BackendPacketVerifierOperation.Repeat:
                if (!IsValidSlot(RepeatCountSlot))
                {
                    throw new ArgumentOutOfRangeException(nameof(RepeatCountSlot));
                }

                if (MinimumRepeatCount < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(MinimumRepeatCount));
                }

                if (MaximumRepeatCount < MinimumRepeatCount)
                {
                    throw new ArgumentOutOfRangeException(nameof(MaximumRepeatCount));
                }

                if (Body.Length == 0)
                {
                    throw new ArgumentException("Repeat verifier instructions require a non-empty body.", nameof(Body));
                }
                break;

            case BackendPacketVerifierOperation.BreakRepeatIfValueEquals:
                RequireEmptyBody();
                if (!IsValidSlot(BreakValueSlot))
                {
                    throw new ArgumentOutOfRangeException(nameof(BreakValueSlot));
                }
                break;
        }
    }

    private void RequireEmptyBody()
    {
        if (Body.Length != 0)
        {
            throw new ArgumentException("Only repeat verifier instructions can contain a body.", nameof(Body));
        }
    }
}
