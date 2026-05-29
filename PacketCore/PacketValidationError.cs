namespace PacketCore;

public enum PacketValidationError
{
    None = 0,
    HeaderTooSmall,
    PacketKindNotAllowed,
    UnknownFlags,
    PacketIdZero,
    VersionZero,
    PayloadLengthTooLarge,
    PayloadLengthMismatch,
    PayloadTooSmall,
    InvalidStringLength,
    TrailingPayload
}
