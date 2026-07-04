using System;

namespace MasterServer.ControlPlane;

public sealed class BackendPacketVerifierLengthConstraint
{
    private BackendPacketVerifierLengthConstraint(
        int fixedLength,
        int sourceSlot,
        int minimumLength,
        int maximumLength)
    {
        if (fixedLength < -1)
        {
            throw new ArgumentOutOfRangeException(nameof(fixedLength));
        }

        if (sourceSlot != BackendPacketVerifierInstruction.NoSlot &&
            !BackendPacketVerifierInstruction.IsValidSlot(sourceSlot))
        {
            throw new ArgumentOutOfRangeException(nameof(sourceSlot));
        }

        if (fixedLength >= 0 &&
            sourceSlot != BackendPacketVerifierInstruction.NoSlot)
        {
            throw new ArgumentException("Length constraints cannot use both a fixed length and a source slot.", nameof(sourceSlot));
        }

        if (fixedLength < 0 &&
            sourceSlot == BackendPacketVerifierInstruction.NoSlot)
        {
            throw new ArgumentException("Length constraints require a fixed length or a source slot.", nameof(sourceSlot));
        }

        if (minimumLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumLength));
        }

        if (maximumLength < minimumLength)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumLength));
        }

        if (fixedLength >= 0 &&
            (fixedLength < minimumLength || fixedLength > maximumLength))
        {
            throw new ArgumentOutOfRangeException(nameof(fixedLength));
        }

        FixedLength = fixedLength;
        SourceSlot = sourceSlot;
        MinimumLength = minimumLength;
        MaximumLength = maximumLength;
    }

    public int FixedLength { get; }

    public int SourceSlot { get; }

    public int MinimumLength { get; }

    public int MaximumLength { get; }

    public bool IsDynamic => SourceSlot != BackendPacketVerifierInstruction.NoSlot;

    public static BackendPacketVerifierLengthConstraint Fixed(int length)
    {
        return new BackendPacketVerifierLengthConstraint(length, BackendPacketVerifierInstruction.NoSlot, length, length);
    }

    public static BackendPacketVerifierLengthConstraint Dynamic(
        int sourceSlot,
        int minimumLength,
        int maximumLength)
    {
        return new BackendPacketVerifierLengthConstraint(-1, sourceSlot, minimumLength, maximumLength);
    }

    public BackendPacketManifestValidationFailure Resolve(
        ReadOnlySpan<long> slots,
        out int length)
    {
        length = FixedLength;
        if (!IsDynamic)
        {
            return BackendPacketManifestValidationFailure.None;
        }

        var value = slots[SourceSlot];
        if (value < 0 || value > int.MaxValue)
        {
            length = 0;
            return BackendPacketManifestValidationFailure.VerifierLengthOutOfRange;
        }

        length = (int)value;
        if (length < MinimumLength || length > MaximumLength)
        {
            return BackendPacketManifestValidationFailure.VerifierLengthOutOfRange;
        }

        return BackendPacketManifestValidationFailure.None;
    }
}
