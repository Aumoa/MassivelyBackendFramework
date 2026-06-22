using BackendServer.Extensions;
using BackendServer.Runtime;
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
        services.TryAddSingleton<IDedicatedWorldRuntime, NoOpDedicatedWorldRuntime>();
        services.TryAddSingleton<IBackendRuntime, DedicatedBackendRuntime>();
        services.AddBackendServer(config);

        return services;
    }
}
