namespace MasterServer.ControlPlane;

public sealed class BackendPacketVerifierResult
{
    private BackendPacketVerifierResult(
        bool success,
        BackendPacketManifestValidationFailure failure)
    {
        Success = success;
        Failure = failure;
    }

    public bool Success { get; }

    public BackendPacketManifestValidationFailure Failure { get; }

    public static BackendPacketVerifierResult Accepted { get; } = new(
        true,
        BackendPacketManifestValidationFailure.None);

    public static BackendPacketVerifierResult Rejected(BackendPacketManifestValidationFailure failure)
    {
        return new BackendPacketVerifierResult(false, failure);
    }
}
