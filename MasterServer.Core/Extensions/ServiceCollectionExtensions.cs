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
        services.Configure<ServiceConnectionCredentialOptions>(config.GetSection("ServiceConnectionCredentials"));

        var serviceConnectionCredentials = config.GetSection("ServiceConnectionCredentials").Get<ServiceConnectionCredentialOptions>() ?? new();
        if (serviceConnectionCredentials.Enabled)
        {
            var redisConnectionString = config.GetValue<string>("DataProtection:RedisConnectionString");
            if (string.IsNullOrWhiteSpace(redisConnectionString))
            {
                throw new InvalidOperationException("DataProtection:RedisConnectionString must be configured when service credentials are enabled.");
            }

            services.AddDataProtection()
                .PersistKeysToStackExchangeRedis(ConnectionMultiplexer.Connect(redisConnectionString))
                .SetApplicationName(serviceConnectionCredentials.DataProtectionApplicationName);
            services.AddSingleton<INodeAuthSecretProvider, MySqlNodeAuthSecretProvider>();
        }
        else
        {
            services.AddSingleton<INodeAuthSecretProvider, OptionsNodeAuthSecretProvider>();
        }

        services.AddSingleton<IConnectionManager, ConnectionManager>();
        services.AddHostedService(provider => (ConnectionManager)provider.GetRequiredService<IConnectionManager>());

        return services;
    }
}
