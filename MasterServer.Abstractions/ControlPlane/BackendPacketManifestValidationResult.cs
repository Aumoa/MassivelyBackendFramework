using System;

namespace MasterServer.ControlPlane;

public sealed class BackendPacketManifestValidationResult
{
    private BackendPacketManifestValidationResult(
        bool success,
        BackendPacketManifestValidationFailure failure,
        BackendPacketManifestEntry? entry)
    {
        if (success && failure != BackendPacketManifestValidationFailure.None)
        {
            throw new ArgumentException("Successful manifest validation cannot include a failure reason.", nameof(failure));
        }

        if (!success && failure == BackendPacketManifestValidationFailure.None)
        {
            throw new ArgumentException("Rejected manifest validation requires a failure reason.", nameof(failure));
        }

        Success = success;
        Failure = failure;
        Entry = entry;
    }

    public bool Success { get; }

    public BackendPacketManifestValidationFailure Failure { get; }

    public BackendPacketManifestEntry? Entry { get; }

    public static BackendPacketManifestValidationResult Accepted(BackendPacketManifestEntry entry)
    {
        return new BackendPacketManifestValidationResult(
            true,
            BackendPacketManifestValidationFailure.None,
            entry ?? throw new ArgumentNullException(nameof(entry)));
    }

    public static BackendPacketManifestValidationResult Rejected(BackendPacketManifestValidationFailure failure)
    {
        return new BackendPacketManifestValidationResult(false, failure, null);
    }
}
