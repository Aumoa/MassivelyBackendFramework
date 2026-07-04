using BackendServer.Options;
using BackendServer.Runtime;
using BackendServer.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BackendServer.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBackendServer(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<MasterConnectionOptions>(config.GetSection("MasterConnection"));
        services.Configure<GatewayListenerOptions>(config.GetSection("GatewayListener"));
        services.Configure<SidecarControlOptions>(config.GetSection("SidecarControl"));

        services.TryAddSingleton<IBackendRuntime, NoOpBackendRuntime>();
        services.AddSingleton<GatewayChannelSender>();
        services.AddSingleton<IBackendGatewayChannelSender>(static provider => provider.GetRequiredService<GatewayChannelSender>());
        services.AddHostedService<BackendRuntimeHostedService>();
        services.AddSingleton<GatewayConnectionManager>();
        services.AddSingleton<IGatewayConnectionStatusProvider>(static provider => provider.GetRequiredService<GatewayConnectionManager>());
        services.AddHostedService(static provider => provider.GetRequiredService<GatewayConnectionManager>());
        services.AddSingleton<MasterConnectionManager>();
        services.AddSingleton<IDirectConnectCodeValidator>(static provider => provider.GetRequiredService<MasterConnectionManager>());
        services.AddHostedService(static provider => provider.GetRequiredService<MasterConnectionManager>());
        services.AddSingleton<SidecarControlServer>();
        services.AddSingleton<ISidecarControlStatusProvider>(static provider => provider.GetRequiredService<SidecarControlServer>());
        services.AddSingleton<IBackendEndpointReadiness>(static provider => provider.GetRequiredService<SidecarControlServer>());
        services.AddSingleton<IBackendManifestIdentityProvider>(static provider => provider.GetRequiredService<SidecarControlServer>());
        services.AddSingleton<IBackendPacketManifestSnapshotSink>(static provider => provider.GetRequiredService<SidecarControlServer>());
        services.AddHostedService(static provider => provider.GetRequiredService<SidecarControlServer>());

        return services;
    }
}
