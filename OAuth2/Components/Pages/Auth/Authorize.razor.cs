using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using BlazorSharedComponent;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.JSInterop;
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
    ScopedSemaphore semaphore,
    IJwt jwt,
    IJSRuntime js)
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

    [Parameter]
    [SupplyParameterFromQuery(Name = "prompt")]
    public string? Prompt { get; set; }

    [Parameter]
    [SupplyParameterFromQuery(Name = "code_challenge")]
    public string? CodeChallenge { get; set; }

    [Parameter]
    [SupplyParameterFromQuery(Name = "code_challenge_method")]
    public string? CodeChallengeMethod { get; set; }

    private List<JwtSecurityToken> m_CachedJwts = [];
    private bool m_ShowLoginForm = false;

    public bool HasCachedAccounts => m_CachedJwts.Count > 0;
    public bool ShowLoginForm => m_ShowLoginForm || !HasCachedAccounts;

    public static string GetCachedId(JwtSecurityToken jwt) =>
        jwt.Claims.FirstOrDefault(p => p.Type == "id")?.Value ?? string.Empty;
    public static string GetCachedPicture(JwtSecurityToken jwt) =>
        jwt.Claims.FirstOrDefault(p => p.Type == JwtRegisteredClaimNames.Picture)?.Value ?? string.Empty;

    public bool HasParametersSet { get; private set; }

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
        await semaphore.WaitAsync();

        try
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

            // Validate PKCE parameters when code_challenge is provided
            if (!string.IsNullOrEmpty(CodeChallenge))
            {
                if (CodeChallengeMethod != "S256")
                {
                    Error(Strings.ERRORS_BAD_REQUEST);
                    return;
                }
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

            if (Prompt == "login")
            {
                // does not use cached login
                return;
            }

            try
            {
                var httpContext = accessor.HttpContext;
                if (httpContext != null)
                {
                    const string CachedJwtPrefix = "cached_jwt_";
                    foreach (var cookie in httpContext.Request.Cookies)
                    {
                        if (!cookie.Key.StartsWith(CachedJwtPrefix, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        var accountId = cookie.Key[CachedJwtPrefix.Length..];
                        try
                        {
                            var handler = new JwtSecurityTokenHandler();
                        	var validationParams = jwt.GetValidationParameters();
                        	var principal = handler.ValidateToken(cachedJwt, validationParams, out var validatedToken);
                            var cachedJwt = (JwtSecurityToken)validatedToken;

                            var access_token = cachedJwt.Claims.FirstOrDefault(p => p.Type == "access_token")?.Value;
                            if (access_token == null)
                            {
                                DeleteCachedAccount(accountId);
                                continue;
                            }

                            var verified = await accesses.VerifyAsync(access_token);
                            if (verified == null)
                            {
                                var refresh_token = cachedJwt.Claims.FirstOrDefault(p => p.Type == "refresh_token")?.Value;
                                if (refresh_token == null)
                                {
                                    DeleteCachedAccount(accountId);
                                    continue;
                                }

                                var newAccess = await accesses.RefreshAccessAsync(refresh_token, jwt.ExpiresIn, jwt.RefreshTokenExpiresIn);
                                if (newAccess.HasValue == false)
                                {
                                    DeleteCachedAccount(accountId);
                                    continue;
                                }

                                var except = cachedJwt.Claims.Where(p => p.Type is not ("access_token" or "refresh_token"));
                                var newCachedJwt = jwt.Issue(hostOptions.Value.ClientId, [.. except, new Claim("access_token", newAccess.Value.AccessToken), new Claim("refresh_token", newAccess.Value.RefreshToken)]);

                                httpContext.Response.Cookies.Append($"cached_jwt_{accountId}", newCachedJwt, new CookieOptions
                                {
                                    HttpOnly = true,
                                    Secure = true,
                                    SameSite = SameSiteMode.Lax,
                                    Expires = DateTimeOffset.UtcNow.AddYears(10)
                                });

                                cachedJwt = handler.ReadJwtToken(newCachedJwt);
                            }

                            m_CachedJwts.Add(cachedJwt);
                        }
                        catch (Exception e)
                        {
                            logger.LogWarning("Failed to export cached jwt token. {Message}", e.Message);
                        }

                        void DeleteCachedAccount(string id)
                        {
                            httpContext.Response.Cookies.Delete($"cached_jwt_{id}", new CookieOptions
                            {
                                HttpOnly = true,
                                Secure = true,
                                SameSite = SameSiteMode.Lax
                            });
                        }
                    }
                    catch (SecurityTokenException e)
                    {
                        logger.LogWarning("cached_jwt token validation failed: {Message}", e.Message);
                        m_CachedJwt = null;
                        httpContext.Response.Cookies.Delete("cached_jwt", new CookieOptions
                        {
                            HttpOnly = true,
                            Secure = true,
                            SameSite = SameSiteMode.Lax
                        });
                    }
                    catch (Exception e)
                    {
                        logger.LogWarning("Failed to export cached jwt token. {Message}", e.Message);
                        m_CachedJwt = null;
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
            }
            finally
            {
                if (Prompt == "none")
                {
                    if (m_CachedJwts.Count > 0)
                    {
                        await ContinueWithCachedAsync(m_CachedJwts[0]);
                    }
                    else
                    {
                        ContinueWithLoginRequiredAsync();
                    }
                }
                else
                {
                    StateHasChanged();
                }
            }

            return;

            void Error(string message)
            {
                nav.NavigateTo($"/error?error={Uri.EscapeDataString(message)}");
            }
        }
        finally
        {
            HasParametersSet = true;
            semaphore.Release();
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

        await ContinueWithAsync(m_ID, true);
    }

    private async Task ContinueWithCachedAsync(JwtSecurityToken jwt)
    {
        await ContinueWithAsync(GetCachedId(jwt), false);
    }

    private void ContinueWithLoginRequiredAsync()
    {
        var redirect_uri = RedirectUri;
        if (ResponseType == "code")
        {
            redirect_uri += '?';
        }
        else
        {
            redirect_uri += '#';
        }

        redirect_uri += $"error={Uri.EscapeDataString("login_required")}";
        if (string.IsNullOrEmpty(State) == false)
        {
            redirect_uri += $"&state={Uri.EscapeDataString(State)}";
        }

        nav.NavigateTo(redirect_uri, forceLoad: true);
    }

    private void UseAnotherAccount()
    {
        m_ShowLoginForm = true;
        StateHasChanged();
    }

    private async Task RemoveCachedAccountAsync(string id)
    {
        var confirmed = await js.InvokeAsync<bool>("confirm", string.Format(Strings.LOGIN_REMOVE_CACHED_ACCOUNT_CONFIRM, id));
        if (!confirmed)
        {
            return;
        }

        var currentUri = new Uri(nav.Uri);
        nav.NavigateTo($"/authorize/remove-cached-account?id={Uri.EscapeDataString(id)}&return_url={Uri.EscapeDataString(currentUri.PathAndQuery)}", forceLoad: true);
    }

    private async Task ContinueWithAsync(string id, bool refreshCache)
    {
        var query = new Dictionary<string, string?>();
        string? frag = null;
        if (string.IsNullOrEmpty(State) == false)
        {
            query.Add("state", State);
        }

        if (ResponseType == "code")
        {
            var code = await authorizationCodes.PushAsync(new AuthorizationCodeBody(id, ClientId, Scope, RedirectUri, Nonce, CodeChallenge, CodeChallengeMethod));
            query.Add("code", code);
        }
        else if (ResponseType == "token")
        {
            var expiresIn = TimeSpan.FromHours(1);
            var rawAccount = await accounts.GetRawAccountAsync(id);
            var access = await accesses.WriteAccessAsync(id, rawAccount.Value.Sub, Scope, ClientId, expiresIn, jwt.RefreshTokenExpiresIn);
            var claims = await accountClaims.GetClaimsAsync(id);
            var idTokenClaims = jwt.ConfigureClaims(rawAccount.Value, Scope, claims, Nonce, true);
            var idToken = jwt.Issue(ClientId, idTokenClaims);
            frag = $"#access_token={Uri.EscapeDataString(access.AccessToken)}&id_token={Uri.EscapeDataString(idToken)}&token_type=Bearer&expires_in={(int)jwt.ExpiresIn.TotalSeconds}&scope={Uri.EscapeDataString(access.Scope)}";
        }

        var redirect_uri = QueryHelpers.AddQueryString(RedirectUri, query) + frag;
        if (refreshCache)
        {
            var code = await authorizationCodes.PushAsync(new AuthorizationCodeBody(id, hostOptions.Value.ClientId, "all", "/authorize/int", null));

            nav.NavigateTo($"/authorize/int?redirect_uri={Uri.EscapeDataString(redirect_uri)}&code={Uri.EscapeDataString(code)}", forceLoad: true);
        }
        else
        {
            nav.NavigateTo(redirect_uri, forceLoad: true);
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

