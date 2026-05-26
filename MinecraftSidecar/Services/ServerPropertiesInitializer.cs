namespace MinecraftSidecar.Services;

public sealed class ServerPropertiesInitializer(
    ILogger<ServerPropertiesInitializer> logger,
    ServerPropertiesService serverProperties) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await serverProperties.LoadAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to initialize server.properties.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
