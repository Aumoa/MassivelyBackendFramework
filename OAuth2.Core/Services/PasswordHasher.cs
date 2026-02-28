using System.Security.Cryptography;

namespace OAuth2.Services;

public static class PasswordHasher
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

        if (!int.TryParse(components[2], out var iterations))
        {
            return false;
        }

        try
        {
            byte[] saltBytes = Convert.FromBase64String(salt);
            using var pbkdf2 = new Rfc2898DeriveBytes(password, saltBytes, iterations, HashAlgorithmName.SHA256);
            byte[] computedHashBytes = pbkdf2.GetBytes(32);
            byte[] savedHashBytes = Convert.FromBase64String(hash);
            return CryptographicOperations.FixedTimeEquals(computedHashBytes, savedHashBytes);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
