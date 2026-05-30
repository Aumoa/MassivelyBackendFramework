using DedicatedServer.Runtime;
using Microsoft.Extensions.Hosting;

namespace DedicatedServer.Services;

internal sealed class DedicatedWorldHostedService(IDedicatedWorldRuntime runtime) : IHostedService
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
