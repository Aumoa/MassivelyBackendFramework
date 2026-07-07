using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using OAuth2.Controllers;
using OAuth2.DTO;
using OAuth2.Services;

namespace OAuth2.Controllers.Tests.Controllers;

public sealed class UserInfoControllerTests
{
    [Fact]
    public async Task GetAsync_ReturnsUnauthorizedWhenTokenAccountIsMissing()
    {
        var access = new Access
        {
            Id = "missing-account",
            Sub = "sub",
            AccessToken = "access-token",
            RefreshToken = "refresh-token",
            Scope = "profile",
            ClientId = "client"
        };
        var controller = new UserInfoController(
            new AccessesStub(access),
            new MissingAccountsStub(),
            new AccountClaimsStub(),
            new JwtStub(),
            new ClientUserGroupsStub(),
            new AccountPicturesStub())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        controller.ControllerContext.HttpContext.Request.Headers.Authorization = "Bearer access-token";

        var result = await controller.GetAsync(CancellationToken.None);

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal("access_token account is invalid.", unauthorized.Value);
    }

    private sealed class AccessesStub(Access access) : IAccesses
    {
        public ValueTask<Access> WriteAccessAsync(string id, string sub, string scope, string clientId, TimeSpan expire, TimeSpan refreshTokenExpire, CancellationToken cancellationToken = default, long? authTime = null, string? userInfoClaims = null)
        {
            throw new NotSupportedException();
        }

        public ValueTask<Access?> VerifyAsync(string accessToken, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult<Access?>(access);
        }

        public ValueTask<Access?> VerifyRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<Access?> RefreshAccessAsync(string refreshToken, TimeSpan expire, TimeSpan refreshTokenExpire, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<Access?> RefreshAccessAsync(string refreshToken, string clientId, TimeSpan expire, TimeSpan refreshTokenExpire, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask RevokeAsync(string accessToken, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask InvalidateAllTokensAsync(string sub, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask InvalidateClientTokensAsync(string sub, string clientId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class MissingAccountsStub : IAccounts
    {
        public ValueTask<bool> ExistsAsync(string id, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<bool?> LoginAsync(string id, string password, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<string> AddAsync(string id, string password, string name, string email, string verifyValue, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<bool> RefreshVerifyCodeAsync(string sub, string verifyCode, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<bool> VerifyAsync(string sub, string verifyCode, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<RawAccount?> GetRawAccountAsync(string id, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult<RawAccount?>(null);
        }

        public ValueTask<string?> GetIdFromSubAsync(string sub, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<bool> UpdateNameAsync(string id, string name, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<bool> ChangePasswordAsync(string sub, string previousPassword, string newPassword, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<string?> ResolveSubAsync(string identifier, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class AccountClaimsStub : IAccountClaims
    {
        public ValueTask<AccountClaim[]> GetClaimsAsync(string accountId, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(Array.Empty<AccountClaim>());
        }

        public ValueTask AddClaimAsync(string accountId, string name, string value, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask SetUniqueClaimAsync(string accountId, string name, string value, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask RemoveClaimAsync(long id, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask RemoveClaimsAsync(string accountId, string name, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class ClientUserGroupsStub : IClientUserGroups
    {
        public ValueTask<AccountClaim[]> GetClientUserGroupsAsync(string id, string accountId, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(Array.Empty<AccountClaim>());
        }

        public ValueTask<ClientUserGroup[]> GetAllClientUserGroupsAsync(string clientId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask AddClientUserGroupAsync(string clientId, string accountId, string group, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask ModifyClientUserGroupAsync(long id, string newGroup, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask RemoveClientUserGroupAsync(long id, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class AccountPicturesStub : IAccountPictures
    {
        public ValueTask<AccountPicture?> GetPictureAsync(string accountId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<AccountPicture> GetOrCreateDefaultPictureAsync(string accountId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask EnsurePictureAsync(string accountId, string? legacyPictureUrl = null, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<AccountPictureUpdateResult> SetPictureAsync(string accountId, Stream image, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class JwtStub : IJwt
    {
        public string Issuer => "issuer";

        public string Modulus => string.Empty;

        public string Exponent => string.Empty;

        public string KId => string.Empty;

        public TimeSpan ExpiresIn => TimeSpan.FromMinutes(5);

        public TimeSpan RefreshTokenExpiresIn => TimeSpan.FromDays(1);

        public Claim[] ConfigureClaims(in RawAccount account, string scopes, AccountClaim[] accountClaims, string? nonce, bool idToken, long? authTime = null, string? acr = null, string? additionalClaims = null)
        {
            throw new NotSupportedException();
        }

        public string Issue(string audience, params Claim[] claims)
        {
            throw new NotSupportedException();
        }

        public TokenValidationParameters GetValidationParameters()
        {
            throw new NotSupportedException();
        }
    }
}
