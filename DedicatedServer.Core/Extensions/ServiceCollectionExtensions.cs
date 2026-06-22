using BackendServer.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DedicatedServer.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDedicatedServer(this IServiceCollection services, IConfiguration config)
    {
        services.AddBackendServer(config);

        return services;
    }
}
