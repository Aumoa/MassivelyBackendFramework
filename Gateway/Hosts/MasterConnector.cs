using Gateway.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gateway.Hosts;

internal class MasterConnector(MasterConnection connection, ILogger<MasterConnector> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        BootstrapAsync(cancellationToken);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await connection.Connection.StopAsync(cancellationToken);
    }

    private async void BootstrapAsync(CancellationToken cancellationToken)
    {
        connection.Connection.Reconnected += OnReconnected;
        await connection.Connection.StartAsync(cancellationToken);
        logger.LogInformation("Connected to master hub with connection ID: {ConnectionId}", connection.Connection.ConnectionId);

        Task OnReconnected(string? connectionId)
        {
            logger.LogInformation("Reconnected to master hub with connection ID: {ConnectionId}", connectionId);
            return Task.CompletedTask;
        }
    }
}
