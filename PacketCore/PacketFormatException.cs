using System;

namespace PacketCore;

public sealed class PacketFormatException : Exception
{
    public PacketFormatException(PacketValidationError error)
        : this(error, $"Invalid packet format: {error}.")
    {
    }

    public PacketFormatException(PacketValidationError error, string message)
        : base(message)
    {
        Error = error;
    }

    public PacketValidationError Error { get; }
}
