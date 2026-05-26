using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace OpenIDConnect.Services;

internal sealed class OidcTokenValidator(
    IOptions<OIDCOptions> options,
    IHttpClientFactory httpClientFactory,
    ILogger<OidcTokenValidator> logger)
{
    internal const string HttpClientName = "OpenIDConnect.TokenValidation";

    private static readonly JwtSecurityTokenHandler TokenHandler = new()
    {
        MapInboundClaims = false
    };

    private readonly SemaphoreSlim m_ConfigurationLock = new(1, 1);
    private OidcValidationConfiguration? m_Configuration;

    public async ValueTask<ValidatedIdToken> ValidateAsync(string idToken, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idToken);

        var configuration = await GetConfigurationAsync(false, cancellationToken);
        try
        {
            return Validate(idToken, configuration);
        }
        catch (SecurityTokenSignatureKeyNotFoundException)
        {
            logger.LogInformation("OIDC signing key was not found in cache. Refreshing JWKS and retrying token validation.");
            configuration = await GetConfigurationAsync(true, cancellationToken);
            return Validate(idToken, configuration);
        }
    }

    private ValidatedIdToken Validate(string idToken, OidcValidationConfiguration configuration)
    {
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = configuration.SigningKeys,
            RequireSignedTokens = true,
            ValidateIssuer = true,
            ValidIssuer = configuration.Issuer,
            ValidateAudience = true,
            ValidAudience = options.Value.ClientId,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256]
        };

        var principal = TokenHandler.ValidateToken(idToken, validationParameters, out var validatedToken);
        if (validatedToken is not JwtSecurityToken jwtToken)
        {
            throw new SecurityTokenValidationException("Validated token is not a JWT.");
        }

        return new ValidatedIdToken(principal, jwtToken);
    }

    private async ValueTask<OidcValidationConfiguration> GetConfigurationAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var cached = m_Configuration;
        if (!forceRefresh && cached != null && cached.ExpiresAt > now)
        {
            return cached;
        }

        await m_ConfigurationLock.WaitAsync(cancellationToken);
        try
        {
            cached = m_Configuration;
            if (!forceRefresh && cached != null && cached.ExpiresAt > DateTimeOffset.UtcNow)
            {
                return cached;
            }

            m_Configuration = await LoadConfigurationAsync(cancellationToken);
            return m_Configuration;
        }
        finally
        {
            m_ConfigurationLock.Release();
        }
    }

    private async ValueTask<OidcValidationConfiguration> LoadConfigurationAsync(CancellationToken cancellationToken)
    {
        var authority = NormalizeAuthority(options.Value.Uri);
        var client = httpClientFactory.CreateClient(HttpClientName);

        using var discoveryResponse = await client.GetAsync($"{authority}/.well-known/openid-configuration", cancellationToken);
        discoveryResponse.EnsureSuccessStatusCode();

        await using var discoveryStream = await discoveryResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var discovery = await JsonDocument.ParseAsync(discoveryStream, cancellationToken: cancellationToken);
        var issuer = GetString(discovery.RootElement, "issuer") ?? authority;
        var jwksUri = GetString(discovery.RootElement, "jwks_uri") ?? $"{authority}/.well-known/certs";

        using var jwksResponse = await client.GetAsync(jwksUri, cancellationToken);
        jwksResponse.EnsureSuccessStatusCode();

        var jwksJson = await jwksResponse.Content.ReadAsStringAsync(cancellationToken);
        var signingKeys = new JsonWebKeySet(jwksJson).Keys.Cast<SecurityKey>().ToArray();
        if (signingKeys.Length == 0)
        {
            throw new InvalidOperationException("OIDC JWKS did not contain any signing keys.");
        }

        logger.LogInformation("Loaded {Count} OIDC signing key(s) for issuer {Issuer}.", signingKeys.Length, issuer);
        return new OidcValidationConfiguration(issuer, signingKeys, DateTimeOffset.UtcNow.AddHours(1));
    }

    private static string NormalizeAuthority(string uri)
    {
        return uri.TrimEnd('/');
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private sealed record OidcValidationConfiguration(string Issuer, SecurityKey[] SigningKeys, DateTimeOffset ExpiresAt);
}

internal sealed record ValidatedIdToken(ClaimsPrincipal Principal, JwtSecurityToken Token);
