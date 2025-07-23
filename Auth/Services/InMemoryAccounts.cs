using System.IdentityModel.Tokens.Jwt;
using Auth.DTO;
using Auth.Exceptions;
using Scripting.DTO;

namespace Auth.Services;

internal class InMemoryAccounts(JwtTokenGenerator jwt) : IAccounts
{
    private static readonly Dictionary<string, AccountRecord> s_Accounts = [];

    public ValueTask<string> RegisterAccountAsync(string provider, string id, string name, string email, CancellationToken cancellationToken)
    {
        string pid = $"{provider}:{id}";
        AccountRecord? existingAccount;
        lock (s_Accounts)
        {
            if (s_Accounts.TryGetValue(pid, out existingAccount))
            {
                throw new ResponseCodeException(ResponseCode.AccountAlreadyRegistered);
            }

            existingAccount = new AccountRecord
            {
                Provider = provider,
                Id = id,
                Name = name,
                Email = email
            };
            s_Accounts[pid] = existingAccount;
        }

        string accessJwt = jwt.Generate(existingAccount.Provider, existingAccount.Id, existingAccount.Name, existingAccount.Email);
        return ValueTask.FromResult(accessJwt);
    }

    public ValueTask<string> GetAccessAsync(string provider, string id, CancellationToken cancellationToken)
    {
        string pid = $"{provider}:{id}";
        lock (s_Accounts)
        {
            if (s_Accounts.TryGetValue(pid, out var account) == false)
            {
                throw new ResponseCodeException(ResponseCode.AccountNotFound);
            }

            string accessJwt = jwt.Generate(account.Provider, account.Id, account.Name, account.Email);
            return ValueTask.FromResult(accessJwt);
        }
    }

    public ValueTask<string> RegisterOrGetAccessAsync(string provider, string id, string name, string email, CancellationToken cancellationToken)
    {
        string pid = $"{provider}:{id}";
        lock (s_Accounts)
        {
            if (s_Accounts.TryGetValue(pid, out var account) == false)
            {
                account = new AccountRecord
                {
                    Provider = provider,
                    Id = id,
                    Name = name,
                    Email = email
                };
                s_Accounts[pid] = account;
            }

            string accessJwt = jwt.Generate(account.Provider, account.Id, account.Name, account.Email);
            return ValueTask.FromResult(accessJwt);
        }
    }

    public ValueTask<bool> ValidateAccessAsync(string accessJwt, CancellationToken cancellationToken)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var token = handler.ReadJwtToken(accessJwt);
            string? provider = token.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Sub)?.Value;
            string? id = token.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Jti)?.Value;
            if (provider == null || id == null)
            {
                return ValueTask.FromResult(false);
            }

            string pid = $"{provider}:{id}";
            lock (s_Accounts)
            {
                return ValueTask.FromResult(s_Accounts.ContainsKey(pid));
            }
        }
        catch
        {
            return ValueTask.FromResult(false);
        }
    }
}
