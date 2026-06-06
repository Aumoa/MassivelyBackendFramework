namespace RemoteDebugServer.Protocols;

public static class RemoteDebugPacketIds
{
    public const ushort HandshakeChallenge = 2000;
    public const ushort HandshakeHello = 2001;
    public const ushort HandshakeProof = 2002;
    public const ushort HandshakeAccepted = 2003;
}
