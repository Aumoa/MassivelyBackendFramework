using System.Threading;
using System.Threading.Tasks;

namespace MasterServer.Services;

public interface IGatewayBackendRoutePolicy
{
    ValueTask<string[]> GetAllowedBackendKindsAsync(CancellationToken cancellationToken = default);

    ValueTask<GatewayBackendRoutePolicyEntryInfo[]> GetEntriesAsync(CancellationToken cancellationToken = default);

    ValueTask<GatewayBackendRoutePolicyEntryInfo> CreateEntryAsync(
        GatewayBackendRoutePolicyEntryInput input,
        CancellationToken cancellationToken = default);

    ValueTask UpdateEntryAsync(
        long id,
        GatewayBackendRoutePolicyEntryInput input,
        CancellationToken cancellationToken = default);

    ValueTask RemoveEntryAsync(long id, CancellationToken cancellationToken = default);
}
