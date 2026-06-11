using System.Security.Claims;
using System.Text.Json;

namespace UnityRemoteDebug.Authorization;

internal static class RemoteDebugAuthorizationPolicies
{
    public const string Management = "UnityRemoteDebugManagement";
    public const string GroupsClaim = "groups";
    private const string StaffGroup = "staff";
    private const string AdminGroup = "admin";

    private static readonly string[] ManagementGroups = [StaffGroup, AdminGroup];

    public static bool HasManagementGroup(ClaimsPrincipal user)
    {
        return ManagementGroups.Any(group => HasGroup(user, group));
    }

    public static bool HasStaffGroup(ClaimsPrincipal user)
    {
        return HasGroup(user, StaffGroup);
    }

    public static bool HasAdminGroup(ClaimsPrincipal user)
    {
        return HasGroup(user, AdminGroup);
    }

    private static bool HasGroup(ClaimsPrincipal user, string group)
    {
        return user.FindAll(GroupsClaim)
            .Any(claim => ContainsGroup(claim.Value, group));
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
