using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Options;
using OpenIDConnect;

namespace MasterAdmin.Authentication;

internal sealed class MasterAdminAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    AuthenticationStateProvider authenticationStateProvider,
    IAuthenticationStateProvider oidcAuthenticationStateProvider)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "MasterAdminOidc";
    private const string LoginRedirectPath = "/auth/redirect";
    private const string LoginScope = "openid profile email groups offline_access";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authenticationState = await authenticationStateProvider.GetAuthenticationStateAsync();
        var user = authenticationState.User;
        if (user.Identity?.IsAuthenticated != true)
        {
            return AuthenticateResult.NoResult();
        }

        return AuthenticateResult.Success(new AuthenticationTicket(user, SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var redirectUri = UriHelper.BuildAbsolute(
            Request.Scheme,
            Request.Host,
            Request.PathBase,
            LoginRedirectPath);
        Response.Redirect(oidcAuthenticationStateProvider.GenerateLoginUri(Context, redirectUri, LoginScope));
        return Task.CompletedTask;
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.Redirect("/access-denied");
        return Task.CompletedTask;
    }
}
