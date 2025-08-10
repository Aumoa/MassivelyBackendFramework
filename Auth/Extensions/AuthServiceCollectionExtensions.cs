using Auth.Options;
using Auth.Services;
using Master.Hosts;
using Master.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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

        var accountsProvider = configuration.GetRequiredSection("Accounts").GetValue<string>("Provider");
        switch (accountsProvider)
        {
            case "MySql":
                services.Configure<MySqlAccounts.Configuration>(configuration.GetSection("Accounts"));
                services.AddScoped<IAccounts, MySqlAccounts>();
                break;
            default:
                throw new OptionsValidationException("Accounts", typeof(IAccounts), ["You're using a provider that does not support Accounts configuration."]);
        }

        var accessesProvider = configuration.GetRequiredSection("Accesses").GetValue<string>("Provider");
        switch (accessesProvider)
        {
            case "MySql":
                services.Configure<MySqlAccesses.Configuration>(configuration.GetSection("Accesses"));
                services.AddScoped<IAccesses, MySqlAccesses>();
                break;
            default:
                throw new OptionsValidationException("Accesses", typeof(IAccesses), ["You're using a provider that does not support Accesses configurations."]);
        }

        services.AddScoped<ISelfProviderAccessCode>(p => p.GetRequiredService<IAccounts>());
    }
}
