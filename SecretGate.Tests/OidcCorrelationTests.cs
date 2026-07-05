using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenIDConnect;
using OpenIDConnect.Extensions;

namespace SecretGate.Tests;

public sealed class OidcPkceStateTests
{
    [Fact]
    public async Task AcceptRejectsCallbackWithoutPkceStateCookie()
    {
        using var services = CreateServices();
        using var scope = services.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var auth = scope.ServiceProvider.GetRequiredService<IAuthenticationStateProvider>();
        var redirectUri = "https://secret.example/auth/redirect";

        accessor.HttpContext = CreateHttpContext();
        var loginUri = auth.GenerateLoginUri(redirectUri, "openid profile email");
        var state = GetState(loginUri);

        accessor.HttpContext = CreateHttpContext();
        await auth.AcceptAsync("authorization-code", redirectUri, state);

        Assert.DoesNotContain(
            accessor.HttpContext.Response.Headers.SetCookie,
            value => value?.Contains("-id-token=", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task AcceptRejectsCallbackWithInvalidPkceStateCookie()
    {
        using var services = CreateServices();
        using var scope = services.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var auth = scope.ServiceProvider.GetRequiredService<IAuthenticationStateProvider>();
        var redirectUri = "https://secret.example/auth/redirect";

        accessor.HttpContext = CreateHttpContext();
        var loginUri = auth.GenerateLoginUri(redirectUri, "openid profile email");
        var state = GetState(loginUri);
        var pkceCookieName = GetPkceStateCookieName(accessor.HttpContext, state);

        accessor.HttpContext = CreateHttpContext();
        accessor.HttpContext.Request.Headers.Cookie = $"{pkceCookieName}=invalid-protected-state";
        await auth.AcceptAsync("authorization-code", redirectUri, state);

        Assert.DoesNotContain(
            accessor.HttpContext.Response.Headers.SetCookie,
            value => value?.Contains("-id-token=", StringComparison.Ordinal) == true);
    }

    private static ServiceProvider CreateServices()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OIDC:Uri"] = "https://oidc.example",
                ["OIDC:ClientId"] = "secret-gate",
                ["OIDC:ClientSecret"] = "client-secret"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddDataProtection()
            .UseEphemeralDataProtectionProvider();
        services.AddOpenIDConnect(configuration);
        return services.BuildServiceProvider();
    }

    private static DefaultHttpContext CreateHttpContext()
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("secret.example");
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static string GetState(string loginUri)
    {
        var query = QueryHelpers.ParseQuery(new Uri(loginUri).Query);
        var state = Assert.Single(query["state"]);
        Assert.NotNull(state);
        return state;
    }

    private static string GetPkceStateCookieName(HttpContext httpContext, string state)
    {
        var setCookie = Assert.Single(
            httpContext.Response.Headers.SetCookie,
            value => value?.Contains("-pkce-" + state + "=", StringComparison.Ordinal) == true);
        Assert.NotNull(setCookie);
        var separatorIndex = setCookie.IndexOf("=", StringComparison.Ordinal);
        Assert.True(separatorIndex > 0);
        return setCookie[..separatorIndex];
    }
}
