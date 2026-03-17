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
        s.Configure<JwtOptions>(config.GetRequiredSection("Jwt"));

        s.AddTransient<IAccounts, MySqlAccounts>();
        s.AddTransient<IAccountClaims, MySqlAccountClaims>();
        s.AddTransient<IClients, MySqlClients>();
        s.AddTransient<IClientClaims, MySqlClientClaims>();
        s.AddTransient<IClientUserGroups, MySqlClientUserGroups>();

        s.AddSingleton<RedisConnection>();
        s.AddHostedService(p => p.GetRequiredService<RedisConnection>());
        s.AddTransient<IAccesses, RedisAccesses>();
        s.AddTransient<IAuthorizationCodes, RedisAuthorizationCodes>();

        s.AddSingleton<IJwt, Jwt>();
        
        return s;
    }
}
