using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OAuth2.DTO;
using OAuth2.Options;

namespace OAuth2.Services;

internal class Jwt : IJwt
{
    private readonly SecurityKey m_Key;
    private readonly RsaSecurityKey m_PublicKey;
    private readonly SigningCredentials m_Credentials;
    private readonly string m_Issuer;
    private readonly TimeSpan m_ExpiresIn;
    private readonly TimeSpan m_RefreshTokenExpiresIn;
    private readonly JwtSecurityTokenHandler m_Handler = new();
    private readonly string m_Modulus;
    private readonly string m_Exponent;
    private readonly string m_KId;
    private readonly string m_DefaultPictureUrl;

    public Jwt(IOptions<JwtOptions> options)
    {
        var rsa = RSA.Create();
        rsa.ImportFromPem(File.ReadAllText(options.Value.PrivateKeyPath));
        m_Key = new RsaSecurityKey(rsa);
        m_Credentials = new SigningCredentials(m_Key, SecurityAlgorithms.RsaSha256);
        m_Issuer = options.Value.Issuer;
        m_ExpiresIn = options.Value.ExpiresIn;
        m_RefreshTokenExpiresIn = options.Value.RefreshTokenExpiresIn;
        m_DefaultPictureUrl = new Uri(new Uri(m_Issuer.TrimEnd('/') + "/"), "default-profile.png").ToString();

        var rsaPublic = RSA.Create();
        rsaPublic.ImportFromPem(File.ReadAllText(options.Value.PublicKeyPath));
        m_PublicKey = new RsaSecurityKey(rsaPublic);

        var rsaParameters = rsaPublic.ExportParameters(false);
        byte[] modulus = rsaParameters.Modulus!;
        byte[] exponent = rsaParameters.Exponent!;
        m_Modulus = Convert.ToBase64String(modulus)
            .Replace('+', '-').Replace('/', '_').Replace("=", "");
        m_Exponent = Convert.ToBase64String(exponent)
            .Replace('+', '-').Replace('/', '_').Replace("=", "");

        byte[] keyBytes = [.. modulus, .. exponent];
        var hash = SHA256.HashData(keyBytes);
        m_KId = Convert.ToBase64String(hash)
            .Replace('+', '-').Replace('/', '_').Replace("=", "");
    }

    public string Issuer => m_Issuer;

    public string Modulus => m_Modulus;

    public string Exponent => m_Exponent;

    public string KId => m_KId;

    public TimeSpan ExpiresIn => m_ExpiresIn;

    public TimeSpan RefreshTokenExpiresIn => m_RefreshTokenExpiresIn;

    public TokenValidationParameters GetValidationParameters()
    {
        return new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = m_PublicKey,
            ValidateIssuer = true,
            ValidIssuer = m_Issuer,
            ValidateAudience = false,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    }

    public Claim[] ConfigureClaims(in RawAccount account, string scopes, AccountClaim[] accountClaims, string? nonce, bool idToken)
    {
        var idTokenClaims = new List<Claim>();

        if (idToken)
        {
            // Note: iss and exp are set by JwtSecurityToken constructor in Issue(),
            // so they must NOT be added here to avoid duplicate claims in the JWT payload.
            idTokenClaims.AddRange([
                new(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
                new(JwtRegisteredClaimNames.Nbf, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
            ]);
        }

        HashSet<string> expectedClaims = [];
        expectedClaims.Add(JwtRegisteredClaimNames.Sub);

        HashSet<string> scopesSet = [.. scopes.Split(' ')];

        foreach (var scope in scopesSet)
        {
            switch (scope)
            {
                case "profile":
                    AddProfile();
                    break;
                case "email":
                    AddEmail();
                    break;
                case "address":
                    AddAddress();
                    break;
                case "phone":
                    AddPhone();
                    break;
                case "groups":
                    AddGroups();
                    break;
                case "all":
                    AddProfile();
                    AddEmail();
                    AddAddress();
                    AddPhone();
                    AddGroups();
                    break;
            }

            continue;

            void AddProfile()
            {
                expectedClaims.Add(JwtRegisteredClaimNames.Name);
                expectedClaims.Add(JwtRegisteredClaimNames.Picture);
                expectedClaims.Add(JwtRegisteredClaimNames.FamilyName);
                expectedClaims.Add(JwtRegisteredClaimNames.GivenName);
                expectedClaims.Add(JwtRegisteredClaimNames.MiddleName);
                expectedClaims.Add(JwtRegisteredClaimNames.Nickname);
                expectedClaims.Add(JwtRegisteredClaimNames.PreferredUsername);
                expectedClaims.Add(JwtRegisteredClaimNames.Profile);
                expectedClaims.Add(JwtRegisteredClaimNames.Website);
                expectedClaims.Add(JwtRegisteredClaimNames.Gender);
                expectedClaims.Add(JwtRegisteredClaimNames.Birthdate);
                expectedClaims.Add(JwtRegisteredClaimNames.ZoneInfo);
                expectedClaims.Add(JwtRegisteredClaimNames.Locale);
                expectedClaims.Add(JwtRegisteredClaimNames.UpdatedAt);
            }

            void AddEmail()
            {
                if (idToken == false || scopesSet.Contains("all") || scopesSet.Contains("profile"))
                {
                    expectedClaims.Add(JwtRegisteredClaimNames.Email);
                    expectedClaims.Add(JwtRegisteredClaimNames.EmailVerified);
                }
            }

            void AddAddress()
            {
                expectedClaims.Add(JwtRegisteredClaimNames.Address);
            }

            void AddPhone()
            {
                expectedClaims.Add(JwtRegisteredClaimNames.PhoneNumber);
                expectedClaims.Add(JwtRegisteredClaimNames.PhoneNumberVerified);
            }

            void AddGroups()
            {
                expectedClaims.Add("groups");
            }
        }

        var claimNames = accountClaims.ToDictionary(v => v.Name, v => v.Value);
        claimNames.Add(JwtRegisteredClaimNames.Email, account.Email);
        claimNames.Add(JwtRegisteredClaimNames.EmailVerified, "true");

        foreach (var expectedClaim in expectedClaims)
        {
            switch (expectedClaim)
            {
                case JwtRegisteredClaimNames.Sub:
                    idTokenClaims.Add(new(JwtRegisteredClaimNames.Sub, account.Sub));
                    break;
                case JwtRegisteredClaimNames.Name:
                    idTokenClaims.Add(new(JwtRegisteredClaimNames.Name, account.Name));
                    break;
                case JwtRegisteredClaimNames.UpdatedAt:
                    var updatedAt = (DateTimeOffset)accountClaims.Select(p => p.CreatedAt).Append(account.CreatedAt).Max();
                    idTokenClaims.Add(new Claim(JwtRegisteredClaimNames.UpdatedAt, updatedAt.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64));
                    break;
                default:
                    if (claimNames.TryGetValue(expectedClaim, out var value))
                    {
                        idTokenClaims.Add(new Claim(expectedClaim, value, ValueTypeMatch.GetValueOrDefault(expectedClaim, ClaimValueTypes.String)));
                    }
                    else
                    {
                        switch (expectedClaim)
                        {
                            case JwtRegisteredClaimNames.Picture:
                                idTokenClaims.Add(new Claim(expectedClaim, m_DefaultPictureUrl));
                                break;
                        }
                    }
                    break;
            }
        }

        if (!string.IsNullOrEmpty(nonce))
        {
            idTokenClaims.Add(new Claim(JwtRegisteredClaimNames.Nonce, nonce));
        }

        return [.. idTokenClaims];
    }

    private static readonly IReadOnlyDictionary<string, string> ValueTypeMatch = new Dictionary<string, string>()
    {
        [JwtRegisteredClaimNames.EmailVerified] = ClaimValueTypes.Boolean,
        [JwtRegisteredClaimNames.PhoneNumberVerified] = ClaimValueTypes.Boolean,
        [JwtRegisteredClaimNames.Address] = JsonClaimValueTypes.Json,
        ["groups"] = JsonClaimValueTypes.Json
    };

    public string Issue(string audience, params Claim[] claims)
    {
        var expireAt = DateTime.UtcNow.Add(m_ExpiresIn);

        var token = new JwtSecurityToken(
            issuer: m_Issuer,
            audience: audience,
            claims: [.. claims],
            expires: expireAt,
            signingCredentials: m_Credentials
            );

        return m_Handler.WriteToken(token);
    }
}
