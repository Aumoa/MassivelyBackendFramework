using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using BlazorSharedComponent;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using OAuth2.DTO;
using OAuth2.Localizations;
using OAuth2.Services;
using HostOptions = OAuth2.Options.HostOptions;

namespace OAuth2.Components.Pages.Auth;

public partial class Authorize(
    IAccounts accounts,
    IAccountClaims accountClaims,
    IAccesses accesses,
    IAuthorizationCodes authorizationCodes,
    IOptions<HostOptions> hostOptions,
    IClients clients,
    IClientClaims clientClaims,
    NavigationManager nav,
    IHttpContextAccessor accessor,
    ILogger<Authorize> logger,
    IJwt jwt)
{
    private enum RenderStates
    {
        Id,
        Login
    }

    private readonly struct RequestScope : IDisposable
    {
        private readonly Authorize m_Component;

        public RequestScope(Authorize component)
        {
            m_Component = component;
            Interlocked.Increment(ref component.m_Requesting);
            component.StateHasChanged();
        }

        void IDisposable.Dispose()
        {
            Interlocked.Decrement(ref m_Component.m_Requesting);
            m_Component.StateHasChanged();
        }
    }

    [Parameter]
    [SupplyParameterFromQuery(Name = "response_type")]
    public string ResponseType { get; set; } = null!;

    [Parameter]
    [SupplyParameterFromQuery(Name = "redirect_uri")]
    public string RedirectUri { get; set; } = null!;

    [Parameter]
    [SupplyParameterFromQuery(Name = "client_id")]
    public string ClientId { get; set; } = null!;

    [Parameter]
    [SupplyParameterFromQuery(Name = "scope")]
    public string Scope { get; set; } = "profile";

    [Parameter]
    [SupplyParameterFromQuery(Name = "state")]
    public string? State { get; set; }

    [Parameter]
    [SupplyParameterFromQuery(Name = "nonce")]
    public string? Nonce { get; set; }

    private JwtSecurityToken? m_CachedJwt;

    public string CachedId => m_CachedJwt?.Claims.FirstOrDefault(p => p.Type == "id")?.Value ?? string.Empty;
    public string CachedPicture => m_CachedJwt?.Claims.FirstOrDefault(p => p.Type == JwtRegisteredClaimNames.Picture)?.Value ?? string.Empty;

    private RenderStates m_RenderState = RenderStates.Id;
    private string m_ClientName = string.Empty;
    private string m_ID = string.Empty;
    private string m_Password = string.Empty;
    private int m_Requesting = 0;
    private string m_ErrorMessageId = string.Empty;
    private string m_ErrorMessagePassword = string.Empty;
    private FloatingInput m_InputPassword = null!;

    private bool IdError => string.IsNullOrEmpty(m_ErrorMessageId) == false;
    private bool PasswordError => string.IsNullOrEmpty(m_ErrorMessagePassword) == false;

    private bool CanSubmit => string.IsNullOrEmpty(m_ID) == false;
    private bool Requesting => m_Requesting > 0;

    protected override async Task OnParametersSetAsync()
    {
        if (string.IsNullOrEmpty(m_ClientName) == false)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(ResponseType) || string.IsNullOrWhiteSpace(RedirectUri) || string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(Scope))
        {
            Error(Strings.ERRORS_BAD_REQUEST);
            return;
        }

        if (ResponseType is not ("code" or "token"))
        {
            Error(Strings.ERRORS_UNSUPPORTED_RESPONSE_TYPE);
            return;
        }

        // hosting service
        if (ClientId == hostOptions.Value.ClientId)
        {
            if (RedirectUri == hostOptions.Value.Uri + "/redirect")
            {
                m_ClientName = "OAuth2";
            }
            else
            {
                Error(Strings.ERRORS_INVALID_REDIRECT_URI);
                return;
            }
        }
        else
        {
            // TODO: Support openid-conformance
            if (ClientId == "kwU lD6MR5dXMTEJ9DwrBfBFtUUO6hNWv7sleWGZ1ww=")
            {
                ClientId = "kwU+lD6MR5dXMTEJ9DwrBfBFtUUO6hNWv7sleWGZ1ww=";
            }

            var targetClient = await clients.GetClientAsync(ClientId);
            if (targetClient == null)
            {
                Error(Strings.ERRORS_INVALID_CLIENT_ID);
                return;
            }

            var claims = await clientClaims.GetClaimsAsync(ClientId);
            var allowedUris = claims.Where(p => p.Name == "redirect_uri");
            if (allowedUris.Any(p => p.Value == RedirectUri) == false)
            {
                Error(Strings.ERRORS_INVALID_REDIRECT_URI);
                return;
            }

            m_ClientName = targetClient.Value.Name;
        }

        try
        {
            var httpContext = accessor.HttpContext;
            if (httpContext != null)
            {
                try
                {
                    if (httpContext.Request.Cookies.TryGetValue("cached_jwt", out var cachedJwt) == false)
                    {
                        return;
                    }

                    var handler = new JwtSecurityTokenHandler();
                    m_CachedJwt = handler.ReadJwtToken(cachedJwt);

                    var access_token = m_CachedJwt.Claims.FirstOrDefault(p => p.Type == "access_token")?.Value;
                    if (access_token == null)
                    {
                        DeleteCache();
                        return;
                    }

                    var verified = await accesses.VerifyAsync(access_token);
                    if (verified == null)
                    {
                        var refresh_token = m_CachedJwt.Claims.FirstOrDefault(p => p.Type == "refresh_token")?.Value;
                        if (refresh_token == null)
                        {
                            DeleteCache();
                            return;
                        }

                        var newAccess = await accesses.RefreshAccessAsync(refresh_token, jwt.ExpiresIn);
                        if (newAccess.HasValue == false)
                        {
                            DeleteCache();
                            return;
                        }

                        var except = m_CachedJwt.Claims.Where(p => p.Type != "access_token");
                        var newJwt = jwt.Issue(hostOptions.Value.ClientId, [.. except, new Claim("access_token", newAccess.Value.AccessToken)]);

                        httpContext.Response.Cookies.Append("cached_jwt", newJwt, new CookieOptions
                        {
                            HttpOnly = true,
                            Secure = true,
                            SameSite = SameSiteMode.Lax,
                            Expires = DateTimeOffset.UtcNow.Add(jwt.ExpiresIn)
                        });
                    }

                    void DeleteCache()
                    {
                        m_CachedJwt = null;
                        httpContext.Response.Cookies.Delete("cached_jwt", new CookieOptions
                        {
                            HttpOnly = true,
                            Secure = true,
                            SameSite = SameSiteMode.Lax,
                            Expires = DateTimeOffset.UtcNow.Add(jwt.ExpiresIn)
                        });
                    }
                }
                catch (Exception e)
                {
                    logger.LogWarning("Failed to export cached jwt token. {Message}", e.Message);
                    m_CachedJwt = null;
                }
            }
        }
        finally
        {
            StateHasChanged();
        }

        return;

        void Error(string message)
        {
            nav.NavigateTo($"/error?error={Uri.EscapeDataString(message)}");
        }
    }

    private IEnumerable<string> GetErrorMessages()
    {
        if (string.IsNullOrEmpty(m_ErrorMessageId) == false)
        {
            yield return m_ErrorMessageId;
        }

        if (string.IsNullOrEmpty(m_ErrorMessagePassword) == false)
        {
            yield return m_ErrorMessagePassword;
        }
    }

    private void OnRegister(MouseEventArgs _)
    {
        var uri = new Uri(nav.Uri);
        nav.NavigateTo($"/register?return_url={Uri.EscapeDataString(uri.PathAndQuery)}");
    }

    private void OnReturn(MouseEventArgs _)
    {
        m_RenderState = RenderStates.Id;
    }

    private async Task OnContinueAsync()
    {
        switch (m_RenderState)
        {
            case RenderStates.Id:
                await OnContinue_StateIdAsync();
                break;
            case RenderStates.Login:
                await OnContinue_StateLoginAsync();
                break;
        }
    }

    private async ValueTask OnContinue_StateLoginAsync()
    {
        using var scope1 = new RequestScope(this);

        if (string.IsNullOrEmpty(m_ID))
        {
            m_ErrorMessageId = Strings.LOGIN_VALIDATION_ERROR_ID_REQUIRED;
            return;
        }

        if (string.IsNullOrEmpty(m_Password))
        {
            m_ErrorMessagePassword = Strings.LOGIN_VALIDATION_ERROR_PW_REQUIRED;
            return;
        }

        var verified = await accounts.LoginAsync(m_ID, m_Password);
        if (verified == null)
        {
            m_ErrorMessagePassword = Strings.LOGIN_VALIDATION_ERROR_PW_INVALID;
            return;
        }

        if (verified.Value == false)
        {
            nav.NavigateTo($"/email-verify/required?id={Uri.EscapeDataString(m_ID)}");
            return;
        }

        await ContinueWithAsync(m_ID);
    }

    private async Task ContinueWithCachedAsync()
    {
        await ContinueWithAsync(CachedId);
    }

    private void ResetCached()
    {
        m_CachedJwt = null;
        StateHasChanged();
    }

    private async Task ContinueWithAsync(string id)
    {
        var query = new Dictionary<string, string?>();
        if (string.IsNullOrEmpty(State) == false)
        {
            query.Add("state", State);
        }

        if (ResponseType == "code")
        {
            var code = await authorizationCodes.PushAsync(new AuthorizationCodeBody(id, ClientId, Scope, RedirectUri, Nonce));
            query.Add("code", code);
        }
        else if (ResponseType == "token")
        {
            var expiresIn = TimeSpan.FromHours(1);
            var access = await accesses.WriteAccessAsync(id, Scope, ClientId, expiresIn);
            var rawAccount = await accounts.GetRawAccountAsync(id);
            var claims = await accountClaims.GetClaimsAsync(id);
            var idTokenClaims = jwt.ConfigureClaims(rawAccount.Value, Scope, claims, Nonce);
            var idToken = jwt.Issue(ClientId, idTokenClaims);
            query.Add("access_token", idToken);
            query.Add("token_type", "Bearer");
            query.Add("expires_in", ((int)jwt.ExpiresIn.TotalSeconds).ToString());
            query.Add("scope", access.Scope);
        }

        {
            var redirect_uri = QueryHelpers.AddQueryString(RedirectUri, query);
            var code = await authorizationCodes.PushAsync(new AuthorizationCodeBody(id, hostOptions.Value.ClientId, "all", "/authorize/int", null));

            nav.NavigateTo($"/authorize/int?redirect_uri={Uri.EscapeDataString(redirect_uri)}&code={Uri.EscapeDataString(code)}", forceLoad: true);
        }
    }

    private async Task OnContinue_StateIdAsync()
    {
        using var scope1 = new RequestScope(this);

        if (string.IsNullOrEmpty(m_ID))
        {
            m_ErrorMessageId = Strings.LOGIN_VALIDATION_ERROR_ID_REQUIRED;
            return;
        }

        if (await accounts.ExistsAsync(m_ID) == false)
        {
            m_ErrorMessageId = Strings.LOGIN_VALIDATION_ERROR_ID_NOT_FOUND;
            return;
        }

        m_RenderState = RenderStates.Login;
        StateHasChanged();

        _ = InvokeAsync(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(0.4));
            await m_InputPassword.FocusAsync();
        });
    }

    private Task OnChangedAsync()
    {
        m_ErrorMessageId = string.Empty;
        m_ErrorMessagePassword = string.Empty;
        return Task.CompletedTask;
    }
}