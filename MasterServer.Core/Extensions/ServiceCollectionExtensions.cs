using MasterServer.Options;
using MasterServer.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace MasterServer.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMasterServer(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<MasterSocketOptions>(config.GetRequiredSection("MasterSocket"));
        services.Configure<MasterAdminConnectionOptions>(config.GetRequiredSection("MasterAdminConnection"));
        services.Configure<ServiceConnectionCredentialOptions>(config.GetRequiredSection("ServiceConnectionCredentials"));
        services.Configure<DirectConnectCodeOptions>(config.GetSection("DirectConnectCodes"));

        var serviceConnectionCredentials = config.GetRequiredSection("ServiceConnectionCredentials").Get<ServiceConnectionCredentialOptions>() ?? new();
        var redisConnectionString = config.GetValue<string>("DataProtection:RedisConnectionString");
        if (string.IsNullOrWhiteSpace(redisConnectionString))
        {
            throw new InvalidOperationException("DataProtection:RedisConnectionString must be configured for Master service credentials.");
        }

        var redis = ConnectionMultiplexer.Connect(redisConnectionString);
        services.AddSingleton<IConnectionMultiplexer>(redis);

        services.AddDataProtection()
            .PersistKeysToStackExchangeRedis(redis)
            .SetApplicationName(serviceConnectionCredentials.DataProtectionApplicationName);
        services.AddSingleton<IServiceConnectionCredentials, MySqlServiceConnectionCredentials>();
        services.AddSingleton<IGatewayBackendRoutePolicy, MySqlGatewayBackendRoutePolicy>();
        services.AddSingleton<IGatewayClientSecretCredentials, MySqlGatewayClientSecretCredentials>();
        services.AddSingleton<INodeAuthSecretProvider, MySqlNodeAuthSecretProvider>();
        services.AddSingleton<IDirectConnectCodeStore, RedisDirectConnectCodeStore>();

        services.AddSingleton<IConnectionManager, ConnectionManager>();
        services.AddHostedService(provider => (ConnectionManager)provider.GetRequiredService<IConnectionManager>());

        return services;
    }
}
