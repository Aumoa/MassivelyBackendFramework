using Master.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Master.Hosts;

public class MasterConnector<T>(MasterConnection<T> connection, ILogger<MasterConnector<T>> logger, T identifier) : IHostedService where T : ISlaveIdentifier
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
        logger.LogInformation("Connected to master hub with connection ID: {ConnectionId}, slave ID: {SlaveId}", connection.Connection.ConnectionId, identifier.SlaveId);

        Task OnReconnected(string? connectionId)
        {
            logger.LogInformation("Reconnected to master hub with connection ID: {ConnectionId}, slave ID: {SlaveId}", connectionId, identifier.SlaveId);
            return Task.CompletedTask;
        }
    }
}
