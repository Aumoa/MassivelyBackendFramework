using System;
using PacketCore;

namespace MasterServer.ControlPlane;

public sealed class BackendPacketPayloadConstraint
{
    public BackendPacketPayloadConstraint(
        int minimumLength,
        int maximumLength,
        int? fixedLength = null,
        string? schemaId = null,
        BackendPacketManifestHash? schemaHash = null,
        BackendPacketVerifierProgram? verifierProgram = null)
    {
        if (minimumLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumLength));
        }

        if (maximumLength < minimumLength || maximumLength > PacketHeader.MaxPayloadLength)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumLength));
        }

        if (fixedLength.HasValue &&
            (fixedLength.Value < minimumLength || fixedLength.Value > maximumLength))
        {
            throw new ArgumentOutOfRangeException(nameof(fixedLength));
        }

        var normalizedSchemaId = string.IsNullOrWhiteSpace(schemaId)
            ? string.Empty
            : schemaId.Trim();
        if (normalizedSchemaId.Length > BackendPacketManifestId.MaxLength)
        {
            throw new ArgumentException($"Backend packet schema id must be {BackendPacketManifestId.MaxLength} characters or fewer.", nameof(schemaId));
        }

        MinimumLength = minimumLength;
        MaximumLength = maximumLength;
        FixedLength = fixedLength;
        SchemaId = normalizedSchemaId;
        SchemaHash = schemaHash;
        VerifierProgram = verifierProgram;
    }

    public static BackendPacketPayloadConstraint Any { get; } = new(0, PacketHeader.MaxPayloadLength);

    public int MinimumLength { get; }

    public int MaximumLength { get; }

    public int? FixedLength { get; }

    public string SchemaId { get; }

    public BackendPacketManifestHash? SchemaHash { get; }

    public BackendPacketVerifierProgram? VerifierProgram { get; }

    public BackendPacketManifestValidationFailure ValidatePayloadLength(int payloadLength)
    {
        if (payloadLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(payloadLength));
        }

        if (FixedLength.HasValue && payloadLength != FixedLength.Value)
        {
            return BackendPacketManifestValidationFailure.PayloadLengthMismatch;
        }

        if (payloadLength < MinimumLength)
        {
            return BackendPacketManifestValidationFailure.PayloadTooSmall;
        }

        if (payloadLength > MaximumLength)
        {
            return BackendPacketManifestValidationFailure.PayloadTooLarge;
        }

        return BackendPacketManifestValidationFailure.None;
    }

    public BackendPacketManifestValidationFailure ValidatePayload(ReadOnlySpan<byte> payload)
    {
        var lengthFailure = ValidatePayloadLength(payload.Length);
        if (lengthFailure != BackendPacketManifestValidationFailure.None)
        {
            return lengthFailure;
        }

        if (VerifierProgram == null)
        {
            return BackendPacketManifestValidationFailure.None;
        }

        var verifierResult = VerifierProgram.Verify(payload);
        return verifierResult.Success
            ? BackendPacketManifestValidationFailure.None
            : verifierResult.Failure;
    }
}
