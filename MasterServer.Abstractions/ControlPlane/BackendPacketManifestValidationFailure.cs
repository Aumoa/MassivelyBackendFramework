namespace MasterServer.ControlPlane;

public enum BackendPacketManifestValidationFailure
{
    None = 0,
    UnknownPacketId,
    UnknownVersion,
    PacketKindMismatch,
    PayloadTooSmall,
    PayloadTooLarge,
    PayloadLengthMismatch
}
