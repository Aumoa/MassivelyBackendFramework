using System;

namespace RemoteDebugServer.Protocols;

[Flags]
public enum RemoteDebugCapabilities : uint
{
    None = 0,
    LogStreaming = 1,
    FileTransfer = 1 << 1,
    RemoteControl = 1 << 2
}
