namespace OAuth2;

public static class OidcPolicy
{
    public static readonly string[] SupportedAcrValues = ["1", "2"];

    public static string? SelectAcrValue(string? acrValues)
    {
        return acrValues?
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(acr => SupportedAcrValues.Contains(acr, StringComparer.Ordinal));
    }
}
