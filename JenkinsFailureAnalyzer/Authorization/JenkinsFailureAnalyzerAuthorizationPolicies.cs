using System.Security.Claims;
using System.Text.Json;

namespace JenkinsFailureAnalyzer.Authorization;

internal static class JenkinsFailureAnalyzerAuthorizationPolicies
{
    public const string Management = "JenkinsFailureAnalyzerManagement";
    public const string GroupsClaim = "groups";

    private static readonly string[] ManagementGroups = ["staff", "admin"];

    public static bool HasManagementGroup(ClaimsPrincipal user)
    {
        return user.FindAll(GroupsClaim)
            .Any(static claim => ManagementGroups.Any(group => ContainsGroup(claim.Value, group)));
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
