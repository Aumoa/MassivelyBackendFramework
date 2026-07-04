using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OAuth2.DTO;
using OAuth2.Extensions;
using OAuth2.Services;

namespace OAuth2.Controllers.Tests;

public sealed class JwtTests
{
    [Fact]
    public void Issue_WithExplicitExpiry_UsesProvidedExpiry()
    {
        using var fixture = new JwtFixture();
        var jwt = fixture.CreateJwt();
        var expires = new DateTime(2030, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        var token = new JwtSecurityTokenHandler().ReadJwtToken(jwt.Issue("client", expires));

        Assert.Equal(expires, token.ValidTo);
    }

    [Theory]
    [MemberData(nameof(UpdatedAtCases))]
    public void ConfigureClaims_UsesNewestAccountOrClaimTimestamp(
        DateTime accountUpdatedAt,
        DateTime claimCreatedAt,
        DateTime expectedUpdatedAt)
    {
        using var fixture = new JwtFixture();
        var jwt = fixture.CreateJwt();
        var account = new RawAccount
        {
            Sub = "subject",
            Name = "User Name",
            Email = "user@example.test",
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = accountUpdatedAt
        };

        var claims = jwt.ConfigureClaims(
            account,
            "profile",
            [new AccountClaim(JwtRegisteredClaimNames.Nickname, "nickname", claimCreatedAt)],
            nonce: null,
            idToken: false);

        var updatedAt = Assert.Single(claims, claim => claim.Type == JwtRegisteredClaimNames.UpdatedAt);
        var expectedSeconds = new DateTimeOffset(expectedUpdatedAt).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        Assert.Equal(expectedSeconds, updatedAt.Value);
        Assert.Equal(ClaimValueTypes.Integer64, updatedAt.ValueType);
    }

    public static TheoryData<DateTime, DateTime, DateTime> UpdatedAtCases => new()
    {
        {
            new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc)
        },
        {
            new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 4, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 4, 0, 0, 0, DateTimeKind.Utc)
        }
    };

    private sealed class JwtFixture : IDisposable
    {
        private readonly string m_TempDirectory;
        private readonly string m_PrivateKeyPath;
        private readonly string m_PublicKeyPath;

        public JwtFixture()
        {
            m_TempDirectory = Path.Combine(Path.GetTempPath(), $"oauth2-jwt-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(m_TempDirectory);
            m_PrivateKeyPath = Path.Combine(m_TempDirectory, "private.pem");
            m_PublicKeyPath = Path.Combine(m_TempDirectory, "public.pem");

            using var rsa = RSA.Create(2048);
            File.WriteAllText(m_PrivateKeyPath, rsa.ExportRSAPrivateKeyPem());
            File.WriteAllText(m_PublicKeyPath, rsa.ExportSubjectPublicKeyInfoPem());
        }

        public IJwt CreateJwt()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MySql:User"] = "test",
                    ["MySql:Password"] = "test",
                    ["Redis:ConnectionString"] = "localhost:6379",
                    ["Host:ClientId"] = "oauth2",
                    ["Host:Secret"] = "secret",
                    ["Host:Uri"] = "https://oauth.example.test",
                    ["Jwt:Issuer"] = "https://oauth.example.test",
                    ["Jwt:ExpiresIn"] = "00:05:00",
                    ["Jwt:RefreshTokenExpiresIn"] = "1.00:00:00",
                    ["Jwt:PrivateKeyPath"] = m_PrivateKeyPath,
                    ["Jwt:PublicKeyPath"] = m_PublicKeyPath
                })
                .Build();
            var services = new ServiceCollection();
            services.AddOAuth2(configuration);
            using var provider = services.BuildServiceProvider();
            return provider.GetRequiredService<IJwt>();
        }

        public void Dispose()
        {
            Directory.Delete(m_TempDirectory, recursive: true);
        }
    }
}
