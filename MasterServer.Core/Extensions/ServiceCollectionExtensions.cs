using MasterServer.Options;
using MasterServer.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MasterServer.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMasterServer(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<MasterSocketOptions>(config.GetRequiredSection("MasterSocket"));

        services.AddSingleton<IConnectionManager, ConnectionManager>();
        services.AddHostedService(provider => (ConnectionManager)provider.GetRequiredService<IConnectionManager>());

        return services;
    }
}
