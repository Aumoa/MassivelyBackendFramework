namespace MasterServer.ControlPlane;

public enum BackendPacketManifestValidationFailure
{
    None = 0,
    UnknownPacketId,
    UnknownVersion,
    PacketKindMismatch,
    PayloadTooSmall,
    PayloadTooLarge,
    PayloadLengthMismatch,
    VerifierRejected,
    VerifierPayloadTruncated,
    VerifierTrailingBytes,
    VerifierInvalidUtf8,
    VerifierValueOutOfRange,
    VerifierValueNotAllowed,
    VerifierFlagsMaskMismatch,
    VerifierLengthOutOfRange,
    VerifierRepeatCountOutOfRange,
    VerifierInstructionBudgetExceeded
}
