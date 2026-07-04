using System.Threading;
using System.Threading.Tasks;
using MasterServer.ControlPlane;

namespace MasterServer.Services;

public interface IBackendPacketManifestStore
{
    ValueTask<BackendPacketManifest[]> GetGatewayManifestsAsync(CancellationToken cancellationToken = default);

    ValueTask<BackendPacketManifestInfo[]> GetManifestInfosAsync(CancellationToken cancellationToken = default);

    ValueTask<BackendPacketManifestInfo?> FindApprovedManifestAsync(
        string backendKind,
        BackendPacketManifestId manifestId,
        BackendPacketManifestHash hash,
        CancellationToken cancellationToken = default);

    ValueTask<BackendPacketManifestInfo> CreateManifestAsync(
        BackendPacketManifestInput input,
        CancellationToken cancellationToken = default);

    ValueTask UpdateManifestAsync(
        long id,
        BackendPacketManifestInput input,
        CancellationToken cancellationToken = default);

    ValueTask DeprecateManifestAsync(
        long id,
        string auditNote,
        CancellationToken cancellationToken = default);

    ValueTask RemoveManifestAsync(long id, CancellationToken cancellationToken = default);
}
