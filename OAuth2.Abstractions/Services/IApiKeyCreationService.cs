namespace OAuth2.Services;

public interface IApiKeyCreationService
{
    ValueTask<ApiKeyCreationResult> CreateApiKeyAsync(
        string accountId,
        string name,
        string? allowedClientId,
        string? allowedScope,
        CancellationToken cancellationToken = default);
}

public readonly record struct ApiKeyCreationResult(string? ApiKey, ApiKeyCreationError Error)
{
    public bool IsSuccess => Error == ApiKeyCreationError.None;

    public static ApiKeyCreationResult Success(string apiKey)
    {
        return new ApiKeyCreationResult(apiKey, ApiKeyCreationError.None);
    }

    public static ApiKeyCreationResult Failure(ApiKeyCreationError error)
    {
        return new ApiKeyCreationResult(null, error);
    }
}

public enum ApiKeyCreationError
{
    None,
    NameRequired,
    AllowedClientIdRequired,
    InternalClientForbidden,
    UnknownClient,
    AllowedScopeRequired,
    InvalidScope,
    ScopeExceedsClient
}
