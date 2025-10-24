namespace OAuth2.Misc;

internal static class KeyNames
{
    private const string Prefix = "massively_backend_framework:oauth2";

    public const string Accesses = $"{Prefix}:access";
    public static string Access(string id) => $"{Accesses}:{id}";
    public static string UnwrapAccess(string key) => key[(Accesses.Length + 1)..];

    public const string Refreshes = $"{Prefix}:refresh";
    public static string Refresh(string id) => $"{Refreshes}:{id}";
    public static string UnwrapRefresh(string key) => key[(Refreshes.Length + 1)..];

    public const string AuthorizationCodes = $"{Prefix}:authorization_code";
    public static string AuthorizationCode(string code) => $"{AuthorizationCodes}:{code}";
    public static string UnwrapAuthorizationCode(string key) => key[(AuthorizationCodes.Length + 1)..];
}
