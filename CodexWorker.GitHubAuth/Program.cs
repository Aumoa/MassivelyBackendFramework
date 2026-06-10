using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

const string optionsSectionName = "CodexWorkerGitHubAuth";

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

var externalConfigPath = GetArgumentValue(args, "--config")
    ?? builder.Configuration[$"{optionsSectionName}:ConfigPath"];

if (!string.IsNullOrWhiteSpace(externalConfigPath))
{
    builder.Configuration.AddJsonFile(externalConfigPath, optional: false, reloadOnChange: true);
}

builder.Configuration.AddEnvironmentVariables();
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

builder.Services.Configure<CodexWorkerGitHubAuthOptions>(
    builder.Configuration.GetSection(optionsSectionName));
builder.Services.AddSingleton<SharedSecretProvider>();
builder.Services.AddSingleton<GitHubAppTokenService>();
builder.Services.AddHttpClient<GitHubAppTokenService>();

var startupOptions = builder.Configuration
    .GetSection(optionsSectionName)
    .Get<CodexWorkerGitHubAuthOptions>() ?? new CodexWorkerGitHubAuthOptions();

builder.WebHost.UseUrls(startupOptions.ListenUrl);

var app = builder.Build();

ValidateStartupOptions(app.Services.GetRequiredService<IOptions<CodexWorkerGitHubAuthOptions>>().Value);

app.Use(async (context, next) =>
{
    var runtimeOptions = context.RequestServices
        .GetRequiredService<IOptions<CodexWorkerGitHubAuthOptions>>()
        .Value;

    if (!runtimeOptions.AllowNonLoopbackClientAddress && !IsLoopback(context.Connection.RemoteIpAddress))
    {
        await Results.StatusCode(StatusCodes.Status403Forbidden).ExecuteAsync(context);
        return;
    }

    if (context.Request.Headers.ContainsKey("Origin"))
    {
        await Results.StatusCode(StatusCodes.Status403Forbidden).ExecuteAsync(context);
        return;
    }

    await next(context);
});

app.MapGet("/healthz", static () => Results.Ok(new { status = "ok" }));

app.MapPost(
    "/v1/github/installation-token",
    async Task<Results<Ok<BrokerTokenResponse>, BadRequest<ProblemResponse>, UnauthorizedHttpResult, StatusCodeHttpResult, ProblemHttpResult>> (
        TokenBrokerRequest request,
        HttpContext context,
        IOptions<CodexWorkerGitHubAuthOptions> options,
        SharedSecretProvider sharedSecretProvider,
        GitHubAppTokenService tokenService,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("TokenBroker");

        if (!IsAuthorized(context.Request, sharedSecretProvider.GetSharedSecret()))
        {
            return TypedResults.Unauthorized();
        }

        if (!RepositoryName.TryParse(request.Repository, out var repositoryName))
        {
            return TypedResults.BadRequest(new ProblemResponse("invalid_repository", "Repository must be in owner/name form."));
        }

        if (string.IsNullOrWhiteSpace(request.Purpose))
        {
            return TypedResults.BadRequest(new ProblemResponse("missing_purpose", "Purpose is required."));
        }

        var policy = TokenPolicy.Resolve(options.Value, repositoryName, request.Purpose);
        if (policy is null)
        {
            return TypedResults.StatusCode(StatusCodes.Status403Forbidden);
        }

        if (!policy.Purpose.AllowsBranch(request.Branch))
        {
            return TypedResults.StatusCode(StatusCodes.Status403Forbidden);
        }

        try
        {
            var token = await tokenService.CreateInstallationTokenAsync(
                repositoryName,
                policy.Repository,
                policy.Purpose,
                cancellationToken);

            logger.LogInformation(
                "Issued GitHub App installation token for {Repository} purpose {Purpose}. Expires at {ExpiresAt}.",
                repositoryName.FullName,
                request.Purpose,
                token.ExpiresAt);

            return TypedResults.Ok(new BrokerTokenResponse(
                token.Token,
                token.ExpiresAt,
                repositoryName.FullName,
                request.Purpose,
                policy.Purpose.Permissions));
        }
        catch (GitHubTokenException exception)
        {
            logger.LogWarning(
                exception,
                "GitHub token request failed for {Repository} purpose {Purpose}.",
                repositoryName.FullName,
                request.Purpose);

            return TypedResults.Problem(
                title: "GitHub token request failed.",
                detail: exception.Message,
                statusCode: StatusCodes.Status502BadGateway);
        }
    });

await app.RunAsync();

static string? GetArgumentValue(string[] args, string name)
{
    for (var i = 0; i < args.Length; i++)
    {
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
        {
            return args[i + 1];
        }

        var prefix = name + "=";
        if (args[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return args[i][prefix.Length..];
        }
    }

    return null;
}

static bool IsLoopback(IPAddress? address)
{
    if (address is null)
    {
        return false;
    }

    if (IPAddress.IsLoopback(address))
    {
        return true;
    }

    return address.IsIPv4MappedToIPv6 && IPAddress.IsLoopback(address.MapToIPv4());
}

static bool IsAuthorized(HttpRequest request, string expectedSecret)
{
    var actualSecret = GetBearerToken(request)
        ?? request.Headers["X-Codex-Worker-Secret"].ToString();

    if (string.IsNullOrEmpty(actualSecret) || actualSecret.Length != expectedSecret.Length)
    {
        return false;
    }

    return CryptographicOperations.FixedTimeEquals(
        Encoding.UTF8.GetBytes(actualSecret),
        Encoding.UTF8.GetBytes(expectedSecret));
}

static string? GetBearerToken(HttpRequest request)
{
    var authorization = request.Headers.Authorization.ToString();
    const string bearerPrefix = "Bearer ";

    if (!authorization.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
    {
        return null;
    }

    return authorization[bearerPrefix.Length..].Trim();
}

static void ValidateStartupOptions(CodexWorkerGitHubAuthOptions options)
{
    var errors = new List<string>();

    if (string.IsNullOrWhiteSpace(options.AppId) && string.IsNullOrWhiteSpace(options.Issuer))
    {
        errors.Add($"{optionsSectionName}:AppId or {optionsSectionName}:Issuer is required.");
    }

    if (!options.AllowNonLoopbackListenUrl && !UsesLoopbackListenUrl(options.ListenUrl))
    {
        errors.Add($"{optionsSectionName}:ListenUrl must bind only to localhost, 127.0.0.1, or ::1 unless {optionsSectionName}:AllowNonLoopbackListenUrl is true.");
    }

    if (options.InstallationId <= 0)
    {
        errors.Add($"{optionsSectionName}:InstallationId is required.");
    }

    if (string.IsNullOrWhiteSpace(options.PrivateKeyPath))
    {
        errors.Add($"{optionsSectionName}:PrivateKeyPath is required.");
    }
    else if (!File.Exists(options.PrivateKeyPath))
    {
        errors.Add($"{optionsSectionName}:PrivateKeyPath does not exist.");
    }

    if (string.IsNullOrWhiteSpace(options.SharedSecret) && string.IsNullOrWhiteSpace(options.SharedSecretPath))
    {
        errors.Add($"{optionsSectionName}:SharedSecret or {optionsSectionName}:SharedSecretPath is required.");
    }
    else if (!string.IsNullOrWhiteSpace(options.SharedSecretPath) && !File.Exists(options.SharedSecretPath))
    {
        errors.Add($"{optionsSectionName}:SharedSecretPath does not exist.");
    }

    if (options.AllowedRepositories.Count == 0)
    {
        errors.Add($"{optionsSectionName}:AllowedRepositories must include at least one repository.");
    }

    foreach (var repository in options.AllowedRepositories)
    {
        if (!RepositoryName.TryParse(repository.Name, out _))
        {
            errors.Add($"Allowed repository '{repository.Name}' must be in owner/name form.");
        }

        if (repository.Purposes.Count == 0)
        {
            errors.Add($"Allowed repository '{repository.Name}' must include at least one purpose.");
        }

        foreach (var purpose in repository.Purposes)
        {
            if (purpose.Value.Permissions.Count == 0)
            {
                errors.Add($"Purpose '{purpose.Key}' for repository '{repository.Name}' must include permissions.");
            }
        }
    }

    if (errors.Count > 0)
    {
        throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
    }
}

static bool UsesLoopbackListenUrl(string listenUrl)
{
    foreach (var value in listenUrl.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        if (!IPAddress.TryParse(uri.Host, out var address) || !IPAddress.IsLoopback(address))
        {
            return false;
        }
    }

    return true;
}

sealed record TokenBrokerRequest(
    [property: JsonPropertyName("repository")] string? Repository,
    [property: JsonPropertyName("purpose")] string? Purpose,
    [property: JsonPropertyName("branch")] string? Branch);

sealed record BrokerTokenResponse(
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("expiresAt")] DateTimeOffset ExpiresAt,
    [property: JsonPropertyName("repository")] string Repository,
    [property: JsonPropertyName("purpose")] string Purpose,
    [property: JsonPropertyName("permissions")] IReadOnlyDictionary<string, string> Permissions);

sealed record ProblemResponse(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message);

sealed class CodexWorkerGitHubAuthOptions
{
    public string ListenUrl { get; set; } = "http://127.0.0.1:5657";

    public bool AllowNonLoopbackListenUrl { get; set; }

    public bool AllowNonLoopbackClientAddress { get; set; }

    public string GitHubApiBaseUrl { get; set; } = "https://api.github.com";

    public string GitHubApiVersion { get; set; } = "2026-03-10";

    public string? AppId { get; set; }

    public string? Issuer { get; set; }

    public long InstallationId { get; set; }

    public string? PrivateKeyPath { get; set; }

    public string? SharedSecret { get; set; }

    public string? SharedSecretPath { get; set; }

    public List<AllowedRepositoryOptions> AllowedRepositories { get; set; } = [];
}

sealed class AllowedRepositoryOptions
{
    public string Name { get; set; } = string.Empty;

    public long? RepositoryId { get; set; }

    public Dictionary<string, PurposeOptions> Purposes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

sealed class PurposeOptions
{
    public Dictionary<string, string> Permissions { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> BranchPrefixes { get; set; } = [];

    public bool RequireBranch { get; set; }
}

sealed class SharedSecretProvider(IOptions<CodexWorkerGitHubAuthOptions> options)
{
    public string GetSharedSecret()
    {
        var configuredSecret = options.Value.SharedSecret;
        if (!string.IsNullOrWhiteSpace(configuredSecret))
        {
            return configuredSecret;
        }

        if (string.IsNullOrWhiteSpace(options.Value.SharedSecretPath))
        {
            throw new InvalidOperationException("Shared secret is not configured.");
        }

        return File.ReadAllText(options.Value.SharedSecretPath).Trim();
    }
}

sealed class GitHubAppTokenService(
    HttpClient httpClient,
    IOptions<CodexWorkerGitHubAuthOptions> options)
{
    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<GitHubInstallationToken> CreateInstallationTokenAsync(
        RepositoryName repositoryName,
        AllowedRepositoryOptions repository,
        PurposeOptions purpose,
        CancellationToken cancellationToken)
    {
        var configuredOptions = options.Value;
        var jwt = await CreateJwtAsync(configuredOptions, cancellationToken);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            BuildUri(configuredOptions.GitHubApiBaseUrl, $"/app/installations/{configuredOptions.InstallationId}/access_tokens"));

        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Authorization = new("Bearer", jwt);
        request.Headers.UserAgent.ParseAdd("CodexWorker-GitHubAuth/1.0");
        request.Headers.Add("X-GitHub-Api-Version", configuredOptions.GitHubApiVersion);

        var body = new GitHubInstallationTokenRequest(
            repository.RepositoryId is null ? [repositoryName.Name] : null,
            repository.RepositoryId is null ? null : [repository.RepositoryId.Value],
            purpose.Permissions);

        request.Content = JsonContent.Create(body, options: s_jsonOptions);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new GitHubTokenException(
                $"GitHub returned {(int)response.StatusCode} {response.ReasonPhrase}. {TrimResponse(responseBody)}");
        }

        var tokenResponse = JsonSerializer.Deserialize<GitHubInstallationTokenResponse>(responseBody, s_jsonOptions);
        if (tokenResponse is null || string.IsNullOrWhiteSpace(tokenResponse.Token))
        {
            throw new GitHubTokenException("GitHub returned an installation token response without a token.");
        }

        return new GitHubInstallationToken(tokenResponse.Token, tokenResponse.ExpiresAt);
    }

    private static Uri BuildUri(string baseUrl, string path)
    {
        var baseUri = new Uri(baseUrl.TrimEnd('/') + "/");
        return new Uri(baseUri, path.TrimStart('/'));
    }

    private static async Task<string> CreateJwtAsync(
        CodexWorkerGitHubAuthOptions options,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.PrivateKeyPath))
        {
            throw new GitHubTokenException("Private key path is not configured.");
        }

        var privateKey = await File.ReadAllTextAsync(options.PrivateKeyPath, cancellationToken);
        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKey);

        var now = DateTimeOffset.UtcNow;
        var issuer = options.Issuer ?? options.AppId;
        var headerJson = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["alg"] = "RS256",
            ["typ"] = "JWT"
        });
        var payloadJson = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["iat"] = now.AddSeconds(-60).ToUnixTimeSeconds(),
            ["exp"] = now.AddMinutes(9).ToUnixTimeSeconds(),
            ["iss"] = issuer
        });

        var unsignedToken = string.Join(
            '.',
            Base64UrlEncode(Encoding.UTF8.GetBytes(headerJson)),
            Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson)));

        var signature = rsa.SignData(
            Encoding.UTF8.GetBytes(unsignedToken),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return unsignedToken + "." + Base64UrlEncode(signature);
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string TrimResponse(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return string.Empty;
        }

        return responseBody.Length <= 512
            ? responseBody
            : responseBody[..512] + "...";
    }
}

sealed class GitHubTokenException(string message) : Exception(message);

sealed record GitHubInstallationToken(string Token, DateTimeOffset ExpiresAt);

sealed record GitHubInstallationTokenRequest(
    [property: JsonPropertyName("repositories")] IReadOnlyList<string>? Repositories,
    [property: JsonPropertyName("repository_ids")] IReadOnlyList<long>? RepositoryIds,
    [property: JsonPropertyName("permissions")] IReadOnlyDictionary<string, string> Permissions);

sealed record GitHubInstallationTokenResponse(
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("expires_at")] DateTimeOffset ExpiresAt);

sealed record RepositoryName(string Owner, string Name)
{
    public string FullName => Owner + "/" + Name;

    public static bool TryParse(string? value, out RepositoryName repositoryName)
    {
        repositoryName = new RepositoryName(string.Empty, string.Empty);

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            return false;
        }

        repositoryName = new RepositoryName(parts[0], parts[1]);
        return true;
    }
}

sealed record ResolvedTokenPolicy(
    AllowedRepositoryOptions Repository,
    PurposeOptions Purpose);

static class TokenPolicy
{
    public static ResolvedTokenPolicy? Resolve(
        CodexWorkerGitHubAuthOptions options,
        RepositoryName repositoryName,
        string purpose)
    {
        var repository = options.AllowedRepositories.FirstOrDefault(
            candidate => string.Equals(candidate.Name, repositoryName.FullName, StringComparison.OrdinalIgnoreCase));

        if (repository is null || !repository.Purposes.TryGetValue(purpose, out var purposeOptions))
        {
            return null;
        }

        return new ResolvedTokenPolicy(repository, purposeOptions);
    }
}

static class PurposeOptionsExtensions
{
    public static bool AllowsBranch(this PurposeOptions purpose, string? branch)
    {
        if (purpose.RequireBranch && string.IsNullOrWhiteSpace(branch))
        {
            return false;
        }

        if (purpose.BranchPrefixes.Count == 0)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(branch))
        {
            return false;
        }

        return purpose.BranchPrefixes.Any(prefix => branch.StartsWith(prefix, StringComparison.Ordinal));
    }
}
