using System;
using System.Linq;

namespace MasterServer.ControlPlane;

public sealed class BackendPacketVerifierValueConstraint
{
    public BackendPacketVerifierValueConstraint(
        long? minimumValue = null,
        long? maximumValue = null,
        long[]? allowedValues = null,
        long? flagsMask = null)
    {
        if (minimumValue.HasValue &&
            maximumValue.HasValue &&
            maximumValue.Value < minimumValue.Value)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumValue));
        }

        if (flagsMask.HasValue &&
            flagsMask.Value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(flagsMask));
        }

        var normalizedAllowedValues = allowedValues == null || allowedValues.Length == 0
            ? Array.Empty<long>()
            : allowedValues.Distinct().OrderBy(static value => value).ToArray();
        foreach (var value in normalizedAllowedValues)
        {
            if (minimumValue.HasValue &&
                value < minimumValue.Value)
            {
                throw new ArgumentOutOfRangeException(nameof(allowedValues));
            }

            if (maximumValue.HasValue &&
                value > maximumValue.Value)
            {
                throw new ArgumentOutOfRangeException(nameof(allowedValues));
            }
        }

        MinimumValue = minimumValue;
        MaximumValue = maximumValue;
        AllowedValues = normalizedAllowedValues;
        FlagsMask = flagsMask;
    }

    public static BackendPacketVerifierValueConstraint Any { get; } = new();

    public long? MinimumValue { get; }

    public long? MaximumValue { get; }

    public long[] AllowedValues { get; }

    public long? FlagsMask { get; }

    public BackendPacketManifestValidationFailure Validate(long value)
    {
        if (MinimumValue.HasValue &&
            value < MinimumValue.Value)
        {
            return BackendPacketManifestValidationFailure.VerifierValueOutOfRange;
        }

        if (MaximumValue.HasValue &&
            value > MaximumValue.Value)
        {
            return BackendPacketManifestValidationFailure.VerifierValueOutOfRange;
        }

        if (AllowedValues.Length > 0 &&
            Array.BinarySearch(AllowedValues, value) < 0)
        {
            return BackendPacketManifestValidationFailure.VerifierValueNotAllowed;
        }

        if (FlagsMask.HasValue &&
            (value < 0 || (value & ~FlagsMask.Value) != 0))
        {
            return BackendPacketManifestValidationFailure.VerifierFlagsMaskMismatch;
        }

        return BackendPacketManifestValidationFailure.None;
    }
}
