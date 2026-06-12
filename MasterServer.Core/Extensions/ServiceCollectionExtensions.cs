using MasterServer.Options;
using MasterServer.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace MasterServer.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMasterServiceConnectionCredentialManagement(
        this IServiceCollection services,
        IConfiguration config)
    {
        services.Configure<ServiceConnectionCredentialOptions>(config.GetRequiredSection("ServiceConnectionCredentials"));
        services.AddTransient<IServiceConnectionCredentials, MySqlServiceConnectionCredentials>();
        return services;
    }

    public static IServiceCollection AddMasterServer(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<MasterSocketOptions>(config.GetRequiredSection("MasterSocket"));
        services.Configure<MasterAdminConnectionOptions>(config.GetRequiredSection("MasterAdminConnection"));
        services.Configure<ServiceConnectionCredentialOptions>(config.GetRequiredSection("ServiceConnectionCredentials"));

        var serviceConnectionCredentials = config.GetRequiredSection("ServiceConnectionCredentials").Get<ServiceConnectionCredentialOptions>() ?? new();
        var redisConnectionString = config.GetValue<string>("DataProtection:RedisConnectionString");
        if (string.IsNullOrWhiteSpace(redisConnectionString))
        {
            throw new InvalidOperationException("DataProtection:RedisConnectionString must be configured for Master service credentials.");
        }

        services.AddDataProtection()
            .PersistKeysToStackExchangeRedis(ConnectionMultiplexer.Connect(redisConnectionString))
            .SetApplicationName(serviceConnectionCredentials.DataProtectionApplicationName);
        services.AddSingleton<INodeAuthSecretProvider, MySqlNodeAuthSecretProvider>();

        services.AddSingleton<IConnectionManager, ConnectionManager>();
        services.AddHostedService(provider => (ConnectionManager)provider.GetRequiredService<IConnectionManager>());

        return services;
    }
}
