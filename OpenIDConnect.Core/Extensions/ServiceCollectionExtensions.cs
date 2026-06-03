using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenIDConnect.Services;

namespace OpenIDConnect.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOpenIDConnect(this IServiceCollection s, IConfiguration config)
    {
        s.Configure<OIDCOptions>(config.GetRequiredSection("OIDC"));

        s.AddHttpClient(OidcTokenValidator.HttpClientName);
        s.AddSingleton<OidcTokenValidator>();
        s.AddSingleton<OidcTokenCookieManager>();
        s.AddHttpClient<TokenRefreshService>();
        s.AddHttpClient<JwtAuthenticationStateProvider>();
        s.AddScoped<TokenRefreshService>();
        s.AddScoped<IAuthenticationStateProvider, JwtAuthenticationStateProvider>();
        s.AddScoped(p => (AuthenticationStateProvider)p.GetRequiredService<IAuthenticationStateProvider>());

        return s;
    }
}
