using BackendServer.Runtime;
using Microsoft.Extensions.Hosting;

namespace BackendServer.Services;

internal sealed class BackendRuntimeHostedService(IBackendRuntime runtime) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await runtime.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await runtime.StopAsync(cancellationToken).ConfigureAwait(false);
    }
}
