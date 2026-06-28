namespace OAuth2;

public enum ClientIdValidationError
{
    None,
    Required,
    TooLong,
    InvalidCharacter
}

public static class ClientIdPolicy
{
    public const int MaxLength = 128;

    public static bool TryNormalize(string? clientId, out string normalizedClientId, out ClientIdValidationError error)
    {
        normalizedClientId = string.Empty;

        if (string.IsNullOrWhiteSpace(clientId))
        {
            error = ClientIdValidationError.Required;
            return false;
        }

        normalizedClientId = clientId.Trim();
        if (normalizedClientId.Length > MaxLength)
        {
            error = ClientIdValidationError.TooLong;
            return false;
        }

        foreach (var value in normalizedClientId)
        {
            if (!IsAllowedCharacter(value))
            {
                error = ClientIdValidationError.InvalidCharacter;
                return false;
            }
        }

        error = ClientIdValidationError.None;
        return true;
    }

    public static string NormalizeOrThrow(string clientId)
    {
        if (TryNormalize(clientId, out var normalizedClientId, out var error))
        {
            return normalizedClientId;
        }

        throw new ArgumentException($"Invalid client ID: {error}.", nameof(clientId));
    }

    private static bool IsAllowedCharacter(char value)
    {
        return value >= 'a' && value <= 'z' ||
               value >= 'A' && value <= 'Z' ||
               value >= '0' && value <= '9' ||
               value is '-' or '_' or '.';
    }
}
