namespace OpenIDConnect;

public static class ScopePolicy
{
    public const string AllScope = "all";
    public const string OfflineAccessScope = "offline_access";

    public static readonly string[] ClaimScopes =
    [
        "openid",
        "profile",
        "email",
        "address",
        "phone",
        "groups",
        "roles"
    ];

    public static readonly string[] SupportedScopes =
    [
        "openid",
        "profile",
        "email",
        "address",
        "phone",
        "groups",
        "roles",
        OfflineAccessScope
    ];

    public static bool TryNormalize(string? scopes, bool allowAll, out string normalized, out string? error)
    {
        normalized = string.Empty;
        error = null;

        var scopeSet = new HashSet<string>(StringComparer.Ordinal);
        foreach (var scope in Split(scopes))
        {
            if (scope == AllScope)
            {
                if (!allowAll)
                {
                    error = "The 'all' scope is reserved for the internal OAuth2 client.";
                    return false;
                }

                normalized = AllScope;
                return true;
            }

            if (!SupportedScopes.Contains(scope, StringComparer.Ordinal))
            {
                error = $"Unsupported scope: {scope}";
                return false;
            }

            scopeSet.Add(scope);
        }

        if (scopeSet.Count == 0)
        {
            error = "At least one scope is required.";
            return false;
        }

        normalized = string.Join(' ', SupportedScopes.Where(scopeSet.Contains));
        return true;
    }

    public static string ExpandAllForExternalClient(string scopes)
    {
        return Split(scopes).Contains(AllScope, StringComparer.Ordinal)
            ? string.Join(' ', ClaimScopes)
            : scopes;
    }

    public static string[] Split(string? scopes)
    {
        return (scopes ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
