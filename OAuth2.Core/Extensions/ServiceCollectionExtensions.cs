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
        s.Configure<AccountPictureOptions>(config.GetSection("AccountPictures"));
        s.AddOptions<HostOptions>()
            .Bind(config.GetRequiredSection("Host"))
            .Validate(static options => !string.IsNullOrWhiteSpace(options.ClientId), "OAuth2:Host:ClientId is required.")
            .Validate(static options => !string.IsNullOrWhiteSpace(options.Secret), "OAuth2:Host:Secret is required.")
            .Validate(static options => IsAbsoluteHttpUri(options.Uri), "OAuth2:Host:Uri must be an absolute HTTP or HTTPS URI without query or fragment.")
            .ValidateOnStart();
        s.Configure<JwtOptions>(config.GetRequiredSection("Jwt"));

        s.AddTransient<IAccounts, MySqlAccounts>();
        s.AddTransient<IAccountClaims, MySqlAccountClaims>();
        s.AddHttpClient<IAccountPictures, MySqlAccountPictures>()
            .ConfigurePrimaryHttpMessageHandler(AccountPictureRemoteConnection.CreateHandler);
        s.AddTransient<IClients, MySqlClients>();
        s.AddTransient<IClientClaims, MySqlClientClaims>();
        s.AddTransient<IClientUserGroups, MySqlClientUserGroups>();
        s.AddTransient<IOAuthGrants, MySqlOAuthGrants>();
        s.AddTransient<IApiKeys, MySqlApiKeys>();
        s.AddTransient<IApiKeyCreationService, ApiKeyCreationService>();
        s.AddSingleton(TimeProvider.System);
        s.AddSingleton<ILoginAttemptLimiter, LoginAttemptLimiter>();

        s.AddSingleton<RedisConnection>();
        s.AddHostedService(p => p.GetRequiredService<RedisConnection>());
        s.AddTransient<IAccesses, RedisAccesses>();
        s.AddTransient<IAuthorizationCodes, RedisAuthorizationCodes>();
        s.AddTransient<ITokenIssuer, TokenIssuer>();

        s.AddSingleton<IJwt, Jwt>();
        
        return s;
    }

    private static bool IsAbsoluteHttpUri(string uri)
    {
        return Uri.TryCreate(uri, UriKind.Absolute, out var parsedUri) &&
               (parsedUri.Scheme == Uri.UriSchemeHttp || parsedUri.Scheme == Uri.UriSchemeHttps) &&
               string.IsNullOrEmpty(parsedUri.Query) &&
               string.IsNullOrEmpty(parsedUri.Fragment);
    }
}
