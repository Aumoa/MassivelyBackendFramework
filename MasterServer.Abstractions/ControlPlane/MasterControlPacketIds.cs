namespace MasterServer.ControlPlane;

public static class MasterControlPacketIds
{
    public const ushort NodeAuthChallenge = 100;
    public const ushort NodeHello = 101;
    public const ushort NodeAuthProof = 102;
    public const ushort NodeAccepted = 103;
    public const ushort OverviewSnapshot = 104;
    public const ushort DedicatedEndpointAdvertise = 105;
    public const ushort DedicatedNodeSnapshot = 106;
    public const ushort ServiceAdminStatusRequest = 107;
    public const ushort ServiceAdminStatusResponse = 108;
    public const ushort BackendEndpointAdvertise = 109;
    public const ushort BackendNodeSnapshot = 110;
    public const ushort DirectConnectCodeRequest = 111;
    public const ushort DirectConnectCodeResponse = 112;
    public const ushort DirectConnectCodeValidationRequest = 113;
    public const ushort DirectConnectCodeValidationResponse = 114;
    public const ushort DirectConnectCode = 115;
    public const ushort ServiceConnectionCredentialManagementRequest = 116;
    public const ushort ServiceConnectionCredentialManagementResponse = 117;
    public const ushort GatewayBackendRoutePolicySnapshot = 118;
    public const ushort GatewayBackendRoutePolicyManagementRequest = 119;
    public const ushort GatewayBackendRoutePolicyManagementResponse = 120;
}
