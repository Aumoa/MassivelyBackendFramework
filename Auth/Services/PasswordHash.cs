using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace Auth.Services;

internal class PasswordHash
{
    public record Configuration
    {
        public required int HashIterations { get; set; } = 100000;
    }

    private readonly Configuration m_Options;

    public PasswordHash(IOptions<Configuration> options)
    {
        m_Options = options.Value;
    }

    public void HashPassword(string password, out string hash, out string salt)
    {
        byte[] saltBytes = RandomNumberGenerator.GetBytes(16);
        salt = Convert.ToBase64String(saltBytes);

        using var pbkdf2 = new Rfc2898DeriveBytes(password, saltBytes, m_Options.HashIterations, HashAlgorithmName.SHA256);
        hash = Convert.ToBase64String(pbkdf2.GetBytes(32));
    }

    public bool VerifyPassword(string password, string hash, string salt)
    {
        byte[] saltBytes = Convert.FromBase64String(salt);
        using var pbkdf2 = new Rfc2898DeriveBytes(password, saltBytes, m_Options.HashIterations, HashAlgorithmName.SHA256);
        string computedHash = Convert.ToBase64String(pbkdf2.GetBytes(32));
        return hash == computedHash;
    }
}
