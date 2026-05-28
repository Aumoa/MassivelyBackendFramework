using System.Text.Json;

namespace OAuth2;

public static class OidcPolicy
{
    public static readonly string[] SupportedAcrValues = ["1", "2"];

    private static readonly HashSet<string> SupportedUserInfoClaims = new(StringComparer.Ordinal)
    {
        "address",
        "birthdate",
        "email",
        "email_verified",
        "family_name",
        "gender",
        "given_name",
        "locale",
        "middle_name",
        "name",
        "nickname",
        "phone_number",
        "phone_number_verified",
        "picture",
        "preferred_username",
        "profile",
        "sub",
        "updated_at",
        "website",
        "zoneinfo"
    };

    public static string? SelectAcrValue(string? acrValues)
    {
        return acrValues?
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(acr => SupportedAcrValues.Contains(acr, StringComparer.Ordinal));
    }

    public static string? SelectUserInfoClaims(string? claims)
    {
        if (string.IsNullOrWhiteSpace(claims))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(claims);
            if (!document.RootElement.TryGetProperty("userinfo", out var userInfo) ||
                userInfo.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var requestedClaims = userInfo.EnumerateObject()
                .Select(static property => property.Name)
                .Where(SupportedUserInfoClaims.Contains)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            return requestedClaims.Length == 0 ? null : string.Join(' ', requestedClaims);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
