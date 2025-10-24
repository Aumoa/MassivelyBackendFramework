using System.Security.Cryptography;

namespace OAuth2.Services;

internal static class PasswordHasher
{
    private const int Iterations = 100000;

    public static string Hash(string password)
    {
        byte[] saltBytes = RandomNumberGenerator.GetBytes(32);
        var salt = Convert.ToBase64String(saltBytes);

        using var pbkdf2 = new Rfc2898DeriveBytes(password, saltBytes, Iterations, HashAlgorithmName.SHA256);
        var hash = Convert.ToBase64String(pbkdf2.GetBytes(32));

        return $"{hash}${salt}${Iterations}";
    }

    public static bool Verify(string password, string saved)
    {
        var components = saved.Split('$', 3);
        if (components.Length != 3)
        {
            return false;
        }

        var hash = components[0];
        var salt = components[1];
        var iterations = int.Parse(components[2]);

        byte[] saltBytes = Convert.FromBase64String(salt);
        using var pbkdf2 = new Rfc2898DeriveBytes(password, saltBytes, iterations, HashAlgorithmName.SHA256);
        string computedHash = Convert.ToBase64String(pbkdf2.GetBytes(32));
        return hash == computedHash;
    }
}
