using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GatewayServer.Behaviors;
using GatewayServer.Options;
using GatewayServer.Protocols;
using MasterServer.ControlPlane;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace GatewayServer.Services;

internal interface IGatewayOidcAuthenticationService : IGatewayClientOidcCompletionTokenValidator
{
    ValueTask<GatewayClientAuthenticationMethodChallenge> CreateChallengeAsync(
        GatewayAuthenticationMethodDefinition method,
        Client client,
        string backendKind,
        GatewayBackendServerHandle? serverHandle,
        CancellationToken cancellationToken);

    void CancelPendingLogins(Client client);

    ValueTask<GatewayOidcCallbackResult> AcceptCallbackAsync(
        string code,
        string state,
        string redirectUri,
        CancellationToken cancellationToken);
}

internal sealed record GatewayOidcCallbackResult(bool Success, string Message)
{
    public static GatewayOidcCallbackResult Accepted()
    {
        return new GatewayOidcCallbackResult(true, "Gateway OIDC login completed.");
    }

    public static GatewayOidcCallbackResult Rejected(string message)
    {
        return new GatewayOidcCallbackResult(false, message);
    }
}

internal sealed class GatewayOidcAuthenticationService(
    IOptions<GatewayAuthenticationOptions> options,
    IHttpClientFactory httpClientFactory,
    ILogger<GatewayOidcAuthenticationService> logger)
    : IGatewayOidcAuthenticationService
{
    internal const string HttpClientName = "GatewayServer.OIDC";
    private const string CompletionTokenPrefix = "gwo_";
    private const int DefaultLoginLifetimeSeconds = 600;
    private const int DefaultLoginRateLimitWindowMilliseconds = 30000;
    private const int DefaultMaxPendingLogins = 1024;
    private const int DefaultMaxPendingLoginsPerClient = 4;
    private const int DefaultMaxLoginCreationsPerWindow = 256;
    private const int DefaultMaxLoginCreationsPerClientPerWindow = 4;

    private static readonly JwtSecurityTokenHandler TokenHandler = new()
    {
        MapInboundClaims = false
    };

    private readonly ConcurrentDictionary<string, OidcLoginTransaction> m_Transactions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, OidcProviderConfiguration> m_ProviderConfigurations = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Client, FixedWindowRateCounter> m_ClientLoginCreationCounters = new();
    private readonly FixedWindowRateCounter m_GlobalLoginCreationCounter = new();
    private readonly object m_TransactionRegistrationSync = new();
    private readonly SemaphoreSlim m_ProviderConfigurationLock = new(1, 1);

    public async ValueTask<GatewayClientAuthenticationMethodChallenge> CreateChallengeAsync(
        GatewayAuthenticationMethodDefinition method,
        Client client,
        string backendKind,
        GatewayBackendServerHandle? serverHandle,
        CancellationToken cancellationToken)
    {
        if (method == null)
        {
            throw new ArgumentNullException(nameof(method));
        }

        if (client == null)
        {
            throw new ArgumentNullException(nameof(client));
        }

        if (method.Kind != GatewayAuthenticationMethodKind.OidcAuthorizationCode)
        {
            throw new ArgumentException("Only OIDC authentication methods can create OIDC challenges.", nameof(method));
        }

        var redirectUri = GetRedirectUri();
        var transactionId = CreateRandomToken();
        var completionSecret = CreateRandomToken();
        var stateSecret = CreateRandomToken();
        var state = $"{transactionId}.{stateSecret}";
        var nonce = CreateRandomToken();
        var codeVerifier = CreateRandomToken();
        var codeChallenge = CreateCodeChallenge(codeVerifier);
        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(GetLoginLifetimeSeconds());

        var transaction = new OidcLoginTransaction(
            transactionId,
            client,
            method,
            backendKind,
            serverHandle,
            redirectUri,
            codeVerifier,
            nonce,
            HashSecret(stateSecret),
            HashSecret(completionSecret),
            expiresAt);
        RegisterTransaction(client, transaction);

        OidcProviderConfiguration configuration;
        try
        {
            configuration = await GetProviderConfigurationAsync(method.AuthorityUri, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            m_Transactions.TryRemove(transactionId, out _);
            throw;
        }

        var loginUri = QueryHelpers.AddQueryString(configuration.AuthorizationEndpoint, new Dictionary<string, string?>
        {
            ["client_id"] = method.ClientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = method.Scope,
            ["state"] = state,
            ["nonce"] = nonce,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256"
        });

        return new GatewayClientAuthenticationMethodChallenge(
            method.MethodId,
            GatewayClientAuthenticationMethodKind.OidcAuthorizationCode,
            method.DisplayName,
            loginUri,
            $"{CompletionTokenPrefix}{transactionId}.{completionSecret}",
            expiresAt);
    }

    public void CancelPendingLogins(Client client)
    {
        if (client == null)
        {
            throw new ArgumentNullException(nameof(client));
        }

        foreach (var pair in m_Transactions.ToArray())
        {
            if (ReferenceEquals(pair.Value.Client, client))
            {
                m_Transactions.TryRemove(pair.Key, out _);
            }
        }

        m_ClientLoginCreationCounters.TryRemove(client, out _);
    }

    public bool IsOidcCompletionToken(string accessToken)
    {
        return !string.IsNullOrWhiteSpace(accessToken) &&
               accessToken.Trim().StartsWith(CompletionTokenPrefix, StringComparison.Ordinal);
    }

    public ValueTask<GatewayClientTokenValidationResult> ValidateOidcCompletionTokenAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        if (!TryParseToken(accessToken, CompletionTokenPrefix, out var transactionId, out var secret))
        {
            return Rejected("Invalid Gateway OIDC completion token.");
        }

        if (!m_Transactions.TryGetValue(transactionId, out var transaction))
        {
            return Rejected("Gateway OIDC login transaction was not found.");
        }

        var now = DateTimeOffset.UtcNow;
        if (transaction.ExpiresAt <= now)
        {
            m_Transactions.TryRemove(transactionId, out _);
            return Rejected("Gateway OIDC login transaction expired.");
        }

        var candidateHash = HashSecret(secret);
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(candidateHash, transaction.CompletionSecretHash))
            {
                return Rejected("Invalid Gateway OIDC completion token.");
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(candidateHash);
        }

        lock (transaction.Sync)
        {
            if (!string.IsNullOrWhiteSpace(transaction.ErrorMessage))
            {
                m_Transactions.TryRemove(transactionId, out _);
                return Rejected(transaction.ErrorMessage);
            }

            if (string.IsNullOrWhiteSpace(transaction.SubjectId))
            {
                return Rejected("Gateway OIDC login has not completed.");
            }

            m_Transactions.TryRemove(transactionId, out _);
            return ValueTask.FromResult(
                GatewayClientTokenValidationResult.Accepted(
                    new GatewayClientPrincipal(
                        transaction.SubjectId,
                        GatewayAuthenticationMethodKind.OidcAuthorizationCode,
                        transaction.Method.MethodId)));
        }
    }

    public async ValueTask<GatewayOidcCallbackResult> AcceptCallbackAsync(
        string code,
        string state,
        string redirectUri,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return GatewayOidcCallbackResult.Rejected("Authorization code is required.");
        }

        if (!TryParseToken(state, string.Empty, out var transactionId, out var stateSecret))
        {
            return GatewayOidcCallbackResult.Rejected("OIDC state is invalid.");
        }

        if (!m_Transactions.TryGetValue(transactionId, out var transaction))
        {
            return GatewayOidcCallbackResult.Rejected("Gateway OIDC login transaction was not found.");
        }

        if (transaction.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            m_Transactions.TryRemove(transactionId, out _);
            return GatewayOidcCallbackResult.Rejected("Gateway OIDC login transaction expired.");
        }

        if (!string.Equals(transaction.RedirectUri, redirectUri, StringComparison.Ordinal))
        {
            return FailTransaction(transaction, "OIDC redirect URI does not match the login transaction.");
        }

        var candidateHash = HashSecret(stateSecret);
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(candidateHash, transaction.StateSecretHash))
            {
                return FailTransaction(transaction, "OIDC state is invalid.");
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(candidateHash);
        }

        try
        {
            var tokenResponse = await ExchangeAuthorizationCodeAsync(transaction, code, cancellationToken)
                .ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(tokenResponse.IdToken))
            {
                return FailTransaction(transaction, "OIDC token response did not include an id_token.");
            }

            var principal = await ValidateIdTokenAsync(transaction, tokenResponse.IdToken, cancellationToken)
                .ConfigureAwait(false);
            var subjectId = principal.FindFirst(transaction.Method.SubjectClaim)?.Value;
            if (string.IsNullOrWhiteSpace(subjectId))
            {
                return FailTransaction(transaction, "OIDC id_token did not include the required subject claim.");
            }

            lock (transaction.Sync)
            {
                transaction.SubjectId = subjectId.Trim();
            }

            return GatewayOidcCallbackResult.Accepted();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Gateway OIDC login callback failed. MethodId={MethodId}.", transaction.Method.MethodId);
            return FailTransaction(transaction, "Gateway OIDC login was rejected.");
        }
    }

    private async ValueTask<GatewayOidcTokenResponse> ExchangeAuthorizationCodeAsync(
        OidcLoginTransaction transaction,
        string code,
        CancellationToken cancellationToken)
    {
        var configuration = await GetProviderConfigurationAsync(transaction.Method.AuthorityUri, cancellationToken)
            .ConfigureAwait(false);
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = transaction.RedirectUri,
            ["client_id"] = transaction.Method.ClientId,
            ["code_verifier"] = transaction.CodeVerifier
        };

        var clientSecret = GetClientSecret(transaction.Method.MethodId);
        if (!string.IsNullOrWhiteSpace(clientSecret))
        {
            form["client_secret"] = clientSecret;
        }

        using var content = new FormUrlEncodedContent(form);
        var client = httpClientFactory.CreateClient(HttpClientName);
        using var response = await client.PostAsync(configuration.TokenEndpoint, content, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogInformation(
                "Gateway OIDC authorization code exchange failed. StatusCode={StatusCode}, MethodId={MethodId}.",
                response.StatusCode,
                transaction.Method.MethodId);
            throw new InvalidOperationException("OIDC authorization code exchange failed.");
        }

        return await response.Content.ReadFromJsonAsync<GatewayOidcTokenResponse>(cancellationToken)
               ?? throw new InvalidOperationException("OIDC token response could not be parsed.");
    }

    private async ValueTask<ClaimsPrincipal> ValidateIdTokenAsync(
        OidcLoginTransaction transaction,
        string idToken,
        CancellationToken cancellationToken)
    {
        var configuration = await GetProviderConfigurationAsync(transaction.Method.AuthorityUri, cancellationToken)
            .ConfigureAwait(false);
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = configuration.SigningKeys,
            RequireSignedTokens = true,
            ValidateIssuer = true,
            ValidIssuer = configuration.Issuer,
            ValidateAudience = true,
            ValidAudience = transaction.Method.ClientId,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256]
        };

        var principal = TokenHandler.ValidateToken(idToken, validationParameters, out var validatedToken);
        if (validatedToken is not JwtSecurityToken jwtToken)
        {
            throw new SecurityTokenValidationException("Validated OIDC id_token is not a JWT.");
        }

        var nonce = jwtToken.Claims.FirstOrDefault(static claim => claim.Type == JwtRegisteredClaimNames.Nonce)?.Value;
        if (!string.Equals(nonce, transaction.Nonce, StringComparison.Ordinal))
        {
            throw new SecurityTokenValidationException("OIDC id_token nonce does not match the login transaction.");
        }

        return principal;
    }

    private async ValueTask<OidcProviderConfiguration> GetProviderConfigurationAsync(
        string authorityUri,
        CancellationToken cancellationToken)
    {
        var authority = authorityUri.TrimEnd('/');
        var now = DateTimeOffset.UtcNow;
        if (m_ProviderConfigurations.TryGetValue(authority, out var cached) &&
            cached.ExpiresAt > now)
        {
            return cached;
        }

        await m_ProviderConfigurationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (m_ProviderConfigurations.TryGetValue(authority, out cached) &&
                cached.ExpiresAt > DateTimeOffset.UtcNow)
            {
                return cached;
            }

            var loaded = await LoadProviderConfigurationAsync(authority, cancellationToken)
                .ConfigureAwait(false);
            m_ProviderConfigurations[authority] = loaded;
            return loaded;
        }
        finally
        {
            m_ProviderConfigurationLock.Release();
        }
    }

    private async ValueTask<OidcProviderConfiguration> LoadProviderConfigurationAsync(
        string authority,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        using var discoveryResponse = await client.GetAsync($"{authority}/.well-known/openid-configuration", cancellationToken)
            .ConfigureAwait(false);
        discoveryResponse.EnsureSuccessStatusCode();

        await using var discoveryStream = await discoveryResponse.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var discovery = await JsonDocument.ParseAsync(discoveryStream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var issuer = GetString(discovery.RootElement, "issuer") ?? authority;
        var authorizationEndpoint = GetString(discovery.RootElement, "authorization_endpoint") ?? $"{authority}/authorize";
        var tokenEndpoint = GetString(discovery.RootElement, "token_endpoint") ?? $"{authority}/api/v1/token";
        var jwksUri = GetString(discovery.RootElement, "jwks_uri") ?? $"{authority}/.well-known/certs";

        using var jwksResponse = await client.GetAsync(jwksUri, cancellationToken).ConfigureAwait(false);
        jwksResponse.EnsureSuccessStatusCode();
        var jwksJson = await jwksResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var signingKeys = new JsonWebKeySet(jwksJson).Keys.Cast<SecurityKey>().ToArray();
        if (signingKeys.Length == 0)
        {
            throw new InvalidOperationException("OIDC JWKS did not contain any signing keys.");
        }

        return new OidcProviderConfiguration(
            issuer,
            authorizationEndpoint,
            tokenEndpoint,
            signingKeys,
            DateTimeOffset.UtcNow.AddHours(1));
    }

    private GatewayOidcCallbackResult FailTransaction(OidcLoginTransaction transaction, string message)
    {
        lock (transaction.Sync)
        {
            transaction.ErrorMessage = message;
        }

        return GatewayOidcCallbackResult.Rejected(message);
    }

    private string GetRedirectUri()
    {
        return GatewayAuthenticationUriBuilder.BuildOidcRedirectUri(options.Value);
    }

    private void RegisterTransaction(Client client, OidcLoginTransaction transaction)
    {
        var now = DateTimeOffset.UtcNow;
        lock (m_TransactionRegistrationSync)
        {
            RemoveExpiredTransactions(now);
            m_GlobalLoginCreationCounter.IncrementOrThrow(
                GetMaxLoginCreationsPerWindow(),
                GetLoginRateLimitWindowMilliseconds(),
                now,
                "Gateway OIDC login creation rate limit was exceeded.");
            var clientCounter = m_ClientLoginCreationCounters.GetOrAdd(
                client,
                static _ => new FixedWindowRateCounter());
            clientCounter.IncrementOrThrow(
                GetMaxLoginCreationsPerClientPerWindow(),
                GetLoginRateLimitWindowMilliseconds(),
                now,
                "Gateway OIDC login creation rate limit was exceeded for this client.");

            var maxPendingLogins = GetMaxPendingLogins();
            if (maxPendingLogins > 0 &&
                m_Transactions.Count >= maxPendingLogins)
            {
                throw new InvalidOperationException("Gateway OIDC pending login capacity is exhausted.");
            }

            var maxPendingLoginsPerClient = GetMaxPendingLoginsPerClient();
            if (maxPendingLoginsPerClient > 0 &&
                m_Transactions.Values.Count(candidate => ReferenceEquals(candidate.Client, client)) >= maxPendingLoginsPerClient)
            {
                throw new InvalidOperationException("Gateway OIDC pending login capacity is exhausted for this client.");
            }

            if (!m_Transactions.TryAdd(transaction.TransactionId, transaction))
            {
                throw new InvalidOperationException("Duplicate Gateway OIDC login transaction id was generated.");
            }
        }
    }

    private int GetLoginLifetimeSeconds()
    {
        return options.Value.OidcLoginLifetimeSeconds <= 0
            ? DefaultLoginLifetimeSeconds
            : options.Value.OidcLoginLifetimeSeconds;
    }

    private int GetLoginRateLimitWindowMilliseconds()
    {
        return options.Value.OidcLoginRateLimitWindowMilliseconds <= 0
            ? DefaultLoginRateLimitWindowMilliseconds
            : options.Value.OidcLoginRateLimitWindowMilliseconds;
    }

    private int GetMaxPendingLogins()
    {
        return options.Value.MaxOidcPendingLogins < 0
            ? DefaultMaxPendingLogins
            : options.Value.MaxOidcPendingLogins;
    }

    private int GetMaxPendingLoginsPerClient()
    {
        return options.Value.MaxOidcPendingLoginsPerClient < 0
            ? DefaultMaxPendingLoginsPerClient
            : options.Value.MaxOidcPendingLoginsPerClient;
    }

    private int GetMaxLoginCreationsPerWindow()
    {
        return options.Value.MaxOidcLoginCreationsPerWindow < 0
            ? DefaultMaxLoginCreationsPerWindow
            : options.Value.MaxOidcLoginCreationsPerWindow;
    }

    private int GetMaxLoginCreationsPerClientPerWindow()
    {
        return options.Value.MaxOidcLoginCreationsPerClientPerWindow < 0
            ? DefaultMaxLoginCreationsPerClientPerWindow
            : options.Value.MaxOidcLoginCreationsPerClientPerWindow;
    }

    private string GetClientSecret(string methodId)
    {
        return options.Value.OidcClientSecrets
            .FirstOrDefault(secret => string.Equals(secret.MethodId?.Trim(), methodId, StringComparison.Ordinal))
            ?.ClientSecret
            ?.Trim() ?? string.Empty;
    }

    private void RemoveExpiredTransactions()
    {
        RemoveExpiredTransactions(DateTimeOffset.UtcNow);
    }

    private void RemoveExpiredTransactions(DateTimeOffset now)
    {
        foreach (var pair in m_Transactions.ToArray())
        {
            if (pair.Value.ExpiresAt <= now)
            {
                m_Transactions.TryRemove(pair.Key, out _);
            }
        }
    }

    private static ValueTask<GatewayClientTokenValidationResult> Rejected(string errorMessage)
    {
        return ValueTask.FromResult(GatewayClientTokenValidationResult.Rejected(errorMessage));
    }

    private static bool TryParseToken(
        string value,
        string prefix,
        out string transactionId,
        out string secret)
    {
        transactionId = string.Empty;
        secret = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim();
        if (!string.IsNullOrEmpty(prefix))
        {
            if (!normalized.StartsWith(prefix, StringComparison.Ordinal))
            {
                return false;
            }

            normalized = normalized[prefix.Length..];
        }

        var separator = normalized.IndexOf('.');
        if (separator <= 0 ||
            separator == normalized.Length - 1)
        {
            return false;
        }

        transactionId = normalized[..separator];
        secret = normalized[(separator + 1)..];
        return IsValidOpaqueValue(transactionId) && IsValidOpaqueValue(secret);
    }

    private static bool IsValidOpaqueValue(string value)
    {
        return value.Length is >= 22 and <= 128 && value.All(static c =>
            c is >= 'A' and <= 'Z' ||
            c is >= 'a' and <= 'z' ||
            c is >= '0' and <= '9' ||
            c is '-' or '_');
    }

    private static string CreateRandomToken()
    {
        return Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    }

    private static string CreateCodeChallenge(string verifier)
    {
        return Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
    }

    private static byte[] HashSecret(string secret)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(secret));
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private sealed record OidcProviderConfiguration(
        string Issuer,
        string AuthorizationEndpoint,
        string TokenEndpoint,
        SecurityKey[] SigningKeys,
        DateTimeOffset ExpiresAt);

    private sealed record GatewayOidcTokenResponse(
        [property: System.Text.Json.Serialization.JsonPropertyName("id_token")]
        string? IdToken);

    private sealed class OidcLoginTransaction(
        string transactionId,
        Client client,
        GatewayAuthenticationMethodDefinition method,
        string backendKind,
        GatewayBackendServerHandle? serverHandle,
        string redirectUri,
        string codeVerifier,
        string nonce,
        byte[] stateSecretHash,
        byte[] completionSecretHash,
        DateTimeOffset expiresAt)
    {
        public object Sync { get; } = new();

        public string TransactionId { get; } = transactionId;

        public Client Client { get; } = client;

        public GatewayAuthenticationMethodDefinition Method { get; } = method;

        public string BackendKind { get; } = backendKind;

        public GatewayBackendServerHandle? ServerHandle { get; } = serverHandle;

        public string RedirectUri { get; } = redirectUri;

        public string CodeVerifier { get; } = codeVerifier;

        public string Nonce { get; } = nonce;

        public byte[] StateSecretHash { get; } = stateSecretHash;

        public byte[] CompletionSecretHash { get; } = completionSecretHash;

        public DateTimeOffset ExpiresAt { get; } = expiresAt;

        public string? SubjectId { get; set; }

        public string? ErrorMessage { get; set; }
    }
}
