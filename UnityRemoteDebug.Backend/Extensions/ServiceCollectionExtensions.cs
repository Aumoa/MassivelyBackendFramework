using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UnityRemoteDebug.Backend.Options;
using UnityRemoteDebug.Backend.Services;

namespace UnityRemoteDebug.Backend.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddUnityRemoteDebugBackend(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<BackendRegistrationOptions>(config.GetSection("BackendRegistration"));
        services.Configure<GatewayListenerOptions>(config.GetSection("GatewayListener"));
        services.Configure<MasterConnectionOptions>(config.GetSection("MasterConnection"));

        services.AddSingleton<MasterConnectionManager>();
        services.AddHostedService(static provider => provider.GetRequiredService<MasterConnectionManager>());

        return services;
    }
}
