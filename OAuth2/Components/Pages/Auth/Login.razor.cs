using System.IdentityModel.Tokens.Jwt;
using System.Globalization;
using BlazorSharedComponent;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using OAuth2.DTO;
using OAuth2.Localizations;
using OAuth2.Services;
using HostOptions = OAuth2.Options.HostOptions;

namespace OAuth2.Components.Pages.Auth;

public partial class Login(
    IAccounts accounts,
    IClients clients,
    IClientClaims clientClaims,
    IOAuthGrants oauthGrants,
    IAuthorizationCodes authorizationCodes,
    IOptions<HostOptions> hostOptions,
    NavigationManager nav,
    IHttpContextAccessor accessor,
    ILogger<Login> logger,
    ILoginAttemptLimiter loginAttemptLimiter,
    CachedAuthorizationSessionService cachedSessions,
    IJSRuntime js)
{
    private static readonly TimeSpan kFailedLoginDelay = TimeSpan.FromMilliseconds(500);

    private enum RenderStates
    {
        Id,
        Login,
        Consent
    }

    private readonly struct RequestScope : IDisposable
    {
        private readonly Login m_Component;

        public RequestScope(Login component)
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
    [SupplyParameterFromQuery(Name = "max_age")]
    public string? MaxAge { get; set; }

    [Parameter]
    [SupplyParameterFromQuery(Name = "acr_values")]
    public string? AcrValues { get; set; }

    [Parameter]
    [SupplyParameterFromQuery(Name = "claims")]
    public string? Claims { get; set; }

    [Parameter]
    [SupplyParameterFromQuery(Name = "code_challenge")]
    public string? CodeChallenge { get; set; }

    [Parameter]
    [SupplyParameterFromQuery(Name = "code_challenge_method")]
    public string? CodeChallengeMethod { get; set; }

    [Parameter]
    [SupplyParameterFromQuery(Name = "client_name")]
    public string ClientName { get; set; } = null!;

    private readonly List<JwtSecurityToken> m_CachedJwts = [];
    private bool m_ShowLoginForm = false;

    public bool HasCachedAccounts => m_CachedJwts.Count > 0;
    public bool ShowLoginForm => m_ShowLoginForm || !HasCachedAccounts;

    public static string GetCachedId(JwtSecurityToken jwt) =>
        jwt.Claims.FirstOrDefault(p => p.Type == "id")?.Value ?? string.Empty;
    public static string GetCachedPicture(JwtSecurityToken jwt) =>
        jwt.Claims.FirstOrDefault(p => p.Type == JwtRegisteredClaimNames.Picture)?.Value ?? string.Empty;

    private static long? GetCachedAuthTime(JwtSecurityToken jwt)
    {
        var authTime = jwt.Claims.FirstOrDefault(p => p.Type == "auth_time")?.Value;
        return long.TryParse(authTime, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private RenderStates m_RenderState = RenderStates.Id;
    private string m_ID = string.Empty;
    private string m_Password = string.Empty;
    private string? m_ConsentAccountId;
    private long? m_ConsentAuthTime;
    private bool m_ConsentRefreshCache;
    private string[] m_ConsentScopes = [];
    private long? m_MaxAge;
    private int m_Requesting = 0;
    private string m_ErrorMessageId = string.Empty;
    private string m_ErrorMessagePassword = string.Empty;
    private FloatingInput m_InputPassword = null!;

    private bool IdError => string.IsNullOrEmpty(m_ErrorMessageId) == false;
    private bool PasswordError => string.IsNullOrEmpty(m_ErrorMessagePassword) == false;

    private bool CanSubmit => string.IsNullOrEmpty(m_ID) == false;
    private bool Requesting => m_Requesting > 0;

    protected override async Task OnInitializedAsync()
    {
        if (string.IsNullOrWhiteSpace(ResponseType) || ResponseType != "code" ||
            string.IsNullOrWhiteSpace(RedirectUri) ||
            string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(Scope) ||
            string.IsNullOrWhiteSpace(ClientName))
        {
            Error(Strings.ERRORS_BAD_REQUEST);
            return;
        }

        if (!TryParseMaxAge(MaxAge, out m_MaxAge))
        {
            Error(Strings.ERRORS_BAD_REQUEST);
            return;
        }

        var allowAllScope = ClientId == hostOptions.Value.ClientId;
        if (!ScopePolicy.TryNormalize(Scope, allowAllScope, out var normalizedScope, out _))
        {
            Error(Strings.ERRORS_BAD_REQUEST);
            return;
        }

        if (allowAllScope)
        {
            if (RedirectUri != hostOptions.Value.Uri.TrimEnd('/') + "/redirect")
            {
                Error(Strings.ERRORS_INVALID_REDIRECT_URI);
                return;
            }

            ClientName = "OAuth2";

            if (!IsValidPkceChallenge(CodeChallenge, CodeChallengeMethod))
            {
                Error(Strings.ERRORS_BAD_REQUEST);
                return;
            }
        }
        else
        {
            var client = await clients.GetClientAsync(ClientId);
            if (!client.HasValue)
            {
                Error(Strings.ERRORS_INVALID_CLIENT_ID);
                return;
            }

            var claims = await clientClaims.GetClaimsAsync(ClientId);
            if (!claims.Where(c => c.Name == "redirect_uri").Any(c => c.Value == RedirectUri))
            {
                Error(Strings.ERRORS_INVALID_REDIRECT_URI);
                return;
            }

            var allowedScopes = claims.Where(c => c.Name == "scope").Select(c => c.Value);
            if (!ScopePolicy.IsAllowedByClient(normalizedScope, allowedScopes))
            {
                Error(Strings.ERRORS_BAD_REQUEST);
                return;
            }

            var hasPkce = HasPkceParameters(CodeChallenge, CodeChallengeMethod);
            var hasClientSecret = claims.Any(c => c.Name == "secret");
            if ((hasPkce && !IsValidPkceChallenge(CodeChallenge, CodeChallengeMethod)) ||
                (!hasPkce && !hasClientSecret))
            {
                Error(Strings.ERRORS_BAD_REQUEST);
                return;
            }

            ClientName = client.Value.Name;
        }

        Scope = normalizedScope;

        if (HasPrompt("login"))
        {
            return;
        }

        try
        {
            var httpContext = accessor.HttpContext;
            if (httpContext != null)
            {
                foreach (var cookie in httpContext.Request.Cookies)
                {
                    if (!cachedSessions.TryGetAccountId(cookie.Key, out _))
                    {
                        continue;
                    }

                    var session = await cachedSessions.TryGetSessionAsync(httpContext, cookie.Key, cookie.Value);
                    if (session != null && IsAuthenticationFresh(session.AuthTime))
                    {
                        m_CachedJwts.Add(session.Token);
                    }
                }
            }
        }
        finally
        {
            if (HasPrompt("none"))
            {
                ContinueWithLoginRequiredAsync();
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

        var origin = GetLoginOrigin();
        if (!loginAttemptLimiter.IsAllowed(m_ID, origin, out var retryAfter))
        {
            logger.LogWarning("Login password step rate limited. Identifier: {Identifier}, Origin: {Origin}, RetryAfter: {RetryAfter}", m_ID, origin, retryAfter);
            m_ErrorMessagePassword = Strings.LOGIN_VALIDATION_ERROR_TOO_MANY_ATTEMPTS;
            return;
        }

        var verified = await accounts.LoginAsync(m_ID, m_Password);
        if (verified == null)
        {
            loginAttemptLimiter.RecordFailure(m_ID, origin);
            logger.LogWarning("Login password verification failed. Identifier: {Identifier}, Origin: {Origin}", m_ID, origin);
            await DelayFailedLoginAsync();
            m_ErrorMessagePassword = Strings.LOGIN_VALIDATION_ERROR_PW_INVALID;
            return;
        }

        if (verified.Value == false)
        {
            nav.NavigateTo($"/email-verify/required?id={Uri.EscapeDataString(m_ID)}");
            return;
        }

        loginAttemptLimiter.RecordSuccess(m_ID, origin);
        await ContinueWithAsync(m_ID, true, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    private async Task ContinueWithCachedAsync(JwtSecurityToken jwt)
    {
        await ContinueWithAsync(GetCachedId(jwt), false, GetCachedAuthTime(jwt));
    }

    private void ContinueWithLoginRequiredAsync()
    {
        RedirectWithOAuthError("login_required");
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

    private async Task ContinueWithAsync(string id, bool refreshCache, long? authTime)
    {
        if (!IsAuthenticationFresh(authTime))
        {
            m_ShowLoginForm = true;
            StateHasChanged();
            return;
        }

        var consentScopes = await GetConsentScopesAsync(id);
        if (consentScopes.Length > 0)
        {
            if (HasPrompt("none"))
            {
                RedirectWithOAuthError("consent_required");
                return;
            }

            m_ConsentAccountId = id;
            m_ConsentAuthTime = authTime;
            m_ConsentRefreshCache = refreshCache;
            m_ConsentScopes = consentScopes;
            m_RenderState = RenderStates.Consent;
            m_ShowLoginForm = true;
            StateHasChanged();
            return;
        }

        await IssueAuthorizationCodeAsync(id, refreshCache, authTime);
    }

    private async Task IssueAuthorizationCodeAsync(string id, bool refreshCache, long? authTime)
    {
        var query = new Dictionary<string, string?>();
        if (string.IsNullOrEmpty(State) == false)
        {
            query.Add("state", State);
        }

        var authorizationCode = await authorizationCodes.PushAsync(new AuthorizationCodeBody(id, ClientId, Scope, RedirectUri, Nonce, CodeChallenge, CodeChallengeMethod, authTime, OidcPolicy.SelectAcrValue(AcrValues), OidcPolicy.SelectUserInfoClaims(Claims)));
        query.Add("code", authorizationCode);

        var redirect_uri = QueryHelpers.AddQueryString(RedirectUri, query);
        if (refreshCache)
        {
            var cacheCode = await authorizationCodes.PushAsync(new AuthorizationCodeBody(id, hostOptions.Value.ClientId, "all", redirect_uri, null, AuthTime: authTime));

            nav.NavigateTo($"/authorize/int?redirect_uri={Uri.EscapeDataString(redirect_uri)}&code={Uri.EscapeDataString(cacheCode)}", forceLoad: true);
        }
        else
        {
            nav.NavigateTo(redirect_uri, forceLoad: true);
        }
    }

    private async Task OnConsentApproveAsync()
    {
        if (string.IsNullOrWhiteSpace(m_ConsentAccountId))
        {
            RedirectWithOAuthError("access_denied");
            return;
        }

        using var scope1 = new RequestScope(this);

        var grantScope = string.Join(' ', m_ConsentScopes);
        await oauthGrants.GrantScopesAsync(m_ConsentAccountId, ClientId, grantScope);
        await IssueAuthorizationCodeAsync(m_ConsentAccountId, m_ConsentRefreshCache, m_ConsentAuthTime);
    }

    private void OnConsentDeny()
    {
        RedirectWithOAuthError("access_denied");
    }

    private async Task OnContinue_StateIdAsync()
    {
        using var scope1 = new RequestScope(this);

        if (string.IsNullOrEmpty(m_ID))
        {
            m_ErrorMessageId = Strings.LOGIN_VALIDATION_ERROR_ID_REQUIRED;
            return;
        }

        var origin = GetLoginOrigin();
        if (!loginAttemptLimiter.IsAllowed(m_ID, origin, out var retryAfter))
        {
            logger.LogWarning("Login identifier step rate limited. Identifier: {Identifier}, Origin: {Origin}, RetryAfter: {RetryAfter}", m_ID, origin, retryAfter);
            m_ErrorMessageId = Strings.LOGIN_VALIDATION_ERROR_TOO_MANY_ATTEMPTS;
            return;
        }

        if (await accounts.ExistsAsync(m_ID) == false)
        {
            loginAttemptLimiter.RecordFailure(m_ID, origin);
            logger.LogInformation("Login identifier was not found. Identifier: {Identifier}, Origin: {Origin}", m_ID, origin);
            await DelayFailedLoginAsync();
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

    private string? GetLoginOrigin()
    {
        var httpContext = accessor.HttpContext;
        return httpContext?.Connection.RemoteIpAddress?.ToString();
    }

    private static Task DelayFailedLoginAsync()
    {
        return Task.Delay(kFailedLoginDelay);
    }

    private async ValueTask<string[]> GetConsentScopesAsync(string accountId)
    {
        if (ClientId == hostOptions.Value.ClientId)
        {
            return [];
        }

        var requestedScopes = ScopePolicy.Split(Scope);
        if (HasPrompt("consent"))
        {
            return requestedScopes;
        }

        var grantedScopes = await oauthGrants.GetGrantedScopesAsync(accountId, ClientId);
        var grantedSet = new HashSet<string>(grantedScopes, StringComparer.Ordinal);
        return [.. requestedScopes.Where(scope => !grantedSet.Contains(scope))];
    }

    private void RedirectWithOAuthError(string error)
    {
        var query = new Dictionary<string, string?>
        {
            ["error"] = error
        };

        if (string.IsNullOrEmpty(State) == false)
        {
            query.Add("state", State);
        }

        nav.NavigateTo(QueryHelpers.AddQueryString(RedirectUri, query), forceLoad: true);
    }

    private bool HasPrompt(string value)
    {
        return Prompt?
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(value, StringComparer.Ordinal) == true;
    }

    private bool IsAuthenticationFresh(long? authTime)
    {
        if (!m_MaxAge.HasValue)
        {
            return true;
        }

        if (m_MaxAge.Value <= 0 || !authTime.HasValue)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return now - authTime.Value < m_MaxAge.Value;
    }

    private static bool TryParseMaxAge(string? value, out long? maxAge)
    {
        maxAge = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0)
        {
            maxAge = parsed;
            return true;
        }

        return false;
    }

    private static string GetScopeDescription(string scope)
    {
        return scope switch
        {
            "openid" => Strings.LOGIN_CONSENT_SCOPE_OPENID,
            "offline_access" => Strings.LOGIN_CONSENT_SCOPE_OFFLINE_ACCESS,
            "profile" => Strings.LOGIN_CONSENT_SCOPE_PROFILE,
            "email" => Strings.LOGIN_CONSENT_SCOPE_EMAIL,
            "address" => Strings.LOGIN_CONSENT_SCOPE_ADDRESS,
            "phone" => Strings.LOGIN_CONSENT_SCOPE_PHONE,
            "groups" => Strings.LOGIN_CONSENT_SCOPE_GROUPS,
            _ => Strings.LOGIN_CONSENT_SCOPE_DEFAULT
        };
    }

    private static bool HasPkceParameters(string? codeChallenge, string? codeChallengeMethod)
    {
        return !string.IsNullOrWhiteSpace(codeChallenge) || !string.IsNullOrWhiteSpace(codeChallengeMethod);
    }

    private static bool IsValidPkceChallenge(string? codeChallenge, string? codeChallengeMethod)
    {
        return codeChallengeMethod == "S256" &&
               !string.IsNullOrWhiteSpace(codeChallenge) &&
               IsValidPkceParameter(codeChallenge);
    }

    private static bool IsValidPkceParameter(string value)
    {
        return value.Length is >= 43 and <= 128 && value.All(static c =>
            c is >= 'A' and <= 'Z' ||
            c is >= 'a' and <= 'z' ||
            c is >= '0' and <= '9' ||
            c is '-' or '.' or '_' or '~');
    }
}
