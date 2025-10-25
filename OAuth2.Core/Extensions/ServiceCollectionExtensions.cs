using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OAuth2.Options;
using OAuth2.Services;

namespace OAuth2.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOAuth2(this IServiceCollection s, IConfiguration config)
    {
        s.Configure<MySqlOptions>(config.GetRequiredSection("MySql"));
        s.Configure<RedisOptions>(config.GetRequiredSection("Redis"));
        s.Configure<HostOptions>(config.GetRequiredSection("Host"));

        s.AddTransient<IAccounts, MySqlAccounts>();
        s.AddTransient<IAccountClaims, MySqlAccountClaims>();
        s.AddTransient<IClients, MySqlClients>();

        s.AddSingleton<RedisAccesses>();
        s.AddHostedService(p => p.GetRequiredService<RedisAccesses>());
        s.AddSingleton<IAccesses, RedisAccesses>(p => p.GetRequiredService<RedisAccesses>());

        s.AddSingleton<RedisAuthorizationCodes>();
        s.AddHostedService(p => p.GetRequiredService<RedisAuthorizationCodes>());
        s.AddSingleton<IAuthorizationCodes, RedisAuthorizationCodes>(p => p.GetRequiredService<RedisAuthorizationCodes>());
        return s;
    }
}
