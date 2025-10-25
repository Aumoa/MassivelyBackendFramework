using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OAuth2.Options;

namespace OAuth2.Services;

internal class Jwt : IJwt
{
    private readonly SecurityKey m_Key;
    private readonly SigningCredentials m_Credentials;
    private readonly string m_Issuer;
    private readonly TimeSpan m_ExpiresIn;
    private readonly JwtSecurityTokenHandler m_Handler = new();
    private readonly string m_Modulus;
    private readonly string m_Exponent;
    private readonly string m_KId;

    public Jwt(IOptions<JwtOptions> options)
    {
        var rsa = RSA.Create();
        rsa.ImportFromPem(File.ReadAllText(options.Value.PrivateKeyPath));
        m_Key = new RsaSecurityKey(rsa);
        m_Credentials = new SigningCredentials(m_Key, SecurityAlgorithms.RsaSha256);
        m_Issuer = options.Value.Issuer;
        m_ExpiresIn = options.Value.ExpiresIn;

        rsa = RSA.Create();
        rsa.ImportFromPem(File.ReadAllText(options.Value.PublicKeyPath));
        var rsaParameters = rsa.ExportParameters(false);
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
