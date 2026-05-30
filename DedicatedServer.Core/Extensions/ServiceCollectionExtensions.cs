using DedicatedServer.Options;
using DedicatedServer.Runtime;
using DedicatedServer.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DedicatedServer.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDedicatedServer(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<MasterConnectionOptions>(config.GetSection("MasterConnection"));
        services.Configure<GatewayListenerOptions>(config.GetSection("GatewayListener"));

        services.TryAddSingleton<IDedicatedWorldRuntime, NoOpDedicatedWorldRuntime>();
        services.AddHostedService<DedicatedWorldHostedService>();
        services.AddSingleton<GatewayConnectionManager>();
        services.AddHostedService(static provider => provider.GetRequiredService<GatewayConnectionManager>());
        services.AddSingleton<MasterConnectionManager>();
        services.AddHostedService(static provider => provider.GetRequiredService<MasterConnectionManager>());

        return services;
    }
}
