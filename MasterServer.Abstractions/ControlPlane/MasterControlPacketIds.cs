namespace MasterServer.ControlPlane;

public static class MasterControlPacketIds
{
    public const ushort NodeAuthChallenge = 100;
    public const ushort NodeHello = 101;
    public const ushort NodeAuthProof = 102;
    public const ushort NodeAccepted = 103;
}
