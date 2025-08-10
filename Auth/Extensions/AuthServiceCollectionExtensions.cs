using Auth.Options;
using Auth.Services;
using Master.Hosts;
using Master.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Auth.Extensions;

public static class AuthServiceCollectionExtensions
{
    public static void AddAuthService(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<IdentifierOptions>(configuration.GetSection("Identifier"));
        services.AddSingleton<AuthIdentifier>();
        services.AddSingleton<MasterConnection<AuthIdentifier>>();
        services.AddHostedService<MasterConnector<AuthIdentifier>>();

        services.Configure<AuthOptions>(configuration.GetSection(nameof(AuthOptions)));
        services.Configure<JwtOptions>(configuration.GetSection(nameof(JwtOptions)));
        services.AddScoped<JwtTokenGenerator>();
        services.AddScoped<PasswordHash>();
        services.Configure<MySqlAccounts.Configuration>(configuration.GetSection(nameof(MySqlAccounts)));
        services.AddScoped<IAccounts, MySqlAccounts>();
    }
}
