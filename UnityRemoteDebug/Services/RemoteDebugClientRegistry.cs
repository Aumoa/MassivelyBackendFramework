using UnityRemoteDebug.Contracts;
using UnityRemoteDebug.Options;

namespace UnityRemoteDebug.Services;

public sealed class RemoteDebugClientRegistry(
    RemoteDebugGatewayRouteClient gatewayRouteClient,
    Microsoft.Extensions.Options.IOptions<GatewayBackendRouteOptions> options,
    ILogger<RemoteDebugClientRegistry> logger)
{
    private readonly GatewayBackendRouteOptions m_Options = options.Value;

    public async Task<RemoteDebugClientListResponse> GetClientsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var status = await gatewayRouteClient.GetStatusAsync(cancellationToken).ConfigureAwait(false);
            var clients = await gatewayRouteClient.ListClientsAsync(cancellationToken).ConfigureAwait(false);
            return new RemoteDebugClientListResponse(
                clients.Clients
                    .Select(RemoteDebugClientContractMapper.ToContract)
                    .ToArray(),
                DateTimeOffset.FromUnixTimeMilliseconds(clients.ObservedAtUnixTimeMilliseconds),
                status.BackendKind,
                status.GatewayConnectionCount,
                ErrorMessage: null);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "Failed to query Unity RemoteDebug Backend clients through Gateway.");
            return new RemoteDebugClientListResponse(
                Array.Empty<RemoteDebugClientSnapshot>(),
                DateTimeOffset.UtcNow,
                m_Options.BackendKind,
                GatewayConnectionCount: 0,
                ErrorMessage: e.Message);
        }
    }
}
