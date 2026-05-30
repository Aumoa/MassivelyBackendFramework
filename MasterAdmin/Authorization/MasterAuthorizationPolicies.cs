using System.Security.Claims;
using System.Text.Json;

namespace MasterAdmin.Authorization;

internal static class MasterAuthorizationPolicies
{
    public const string Admin = "MasterAdmin";
    public const string AdminGroup = "admin";
    public const string GroupsClaim = "groups";

    public static bool HasAdminGroup(ClaimsPrincipal user)
    {
        return user.FindAll(GroupsClaim)
            .Any(static claim => ContainsGroup(claim.Value, AdminGroup));
    }

    private static bool ContainsGroup(string value, string group)
    {
        if (string.Equals(value, group, StringComparison.Ordinal))
        {
            return true;
        }

        try
        {
            using var document = JsonDocument.Parse(value);
            return document.RootElement.ValueKind switch
            {
                JsonValueKind.Array => document.RootElement.EnumerateArray()
                    .Any(element => element.ValueKind == JsonValueKind.String && string.Equals(element.GetString(), group, StringComparison.Ordinal)),
                JsonValueKind.String => string.Equals(document.RootElement.GetString(), group, StringComparison.Ordinal),
                _ => false
            };
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
