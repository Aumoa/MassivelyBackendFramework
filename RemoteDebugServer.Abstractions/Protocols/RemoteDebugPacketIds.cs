namespace RemoteDebugServer.Protocols;

public static class RemoteDebugPacketIds
{
    public const ushort HandshakeChallenge = 2000;
    public const ushort HandshakeHello = 2001;
    public const ushort HandshakeProof = 2002;
    public const ushort HandshakeAccepted = 2003;

    public const ushort BackendStatusRequest = 2100;
    public const ushort BackendStatusResponse = 2101;
    public const ushort BackendClientListRequest = 2102;
    public const ushort BackendClientListResponse = 2103;
    public const ushort BackendErrorResponse = 2199;
}
