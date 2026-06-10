using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

var parseResult = CommandLine.Parse(args);
if (!parseResult.IsSuccess)
{
    Console.Error.WriteLine(parseResult.Error);
    PrintUsage();
    return 2;
}

var command = parseResult.Value;
if (command.ShowHelp)
{
    PrintUsage();
    return 0;
}

try
{
    if (command.Format is OutputFormat.GitAskPass && IsUsernamePrompt(command.Prompt))
    {
        Console.WriteLine("x-access-token");
        return 0;
    }

    switch (command.Format)
    {
        case OutputFormat.Token:
        {
            var token = await RequestTokenAsync(command, CancellationToken.None);
            Console.WriteLine(token.Token);
            break;
        }
        case OutputFormat.Json:
        {
            var token = await RequestTokenAsync(command, CancellationToken.None);
            Console.WriteLine(JsonSerializer.Serialize(token, JsonSerialization.Options));
            break;
        }
        case OutputFormat.GitAskPass:
        {
            var token = await RequestTokenAsync(command, CancellationToken.None);
            Console.WriteLine(IsUsernamePrompt(command.Prompt) ? "x-access-token" : token.Token);
            break;
        }
        case OutputFormat.GhEnv:
        {
            var token = await RequestTokenAsync(command, CancellationToken.None);
            Console.WriteLine("GH_TOKEN=" + token.Token);
            break;
        }
        case OutputFormat.IdentityJson:
        {
            var identity = await RequestIdentityAsync(command, CancellationToken.None);
            Console.WriteLine(JsonSerializer.Serialize(identity, JsonSerialization.Options));
            break;
        }
        case OutputFormat.GitEnv:
        {
            var identity = await RequestIdentityAsync(command, CancellationToken.None);
            Console.WriteLine("GIT_AUTHOR_NAME=" + identity.GitUserName);
            Console.WriteLine("GIT_AUTHOR_EMAIL=" + identity.GitUserEmail);
            Console.WriteLine("GIT_COMMITTER_NAME=" + identity.GitUserName);
            Console.WriteLine("GIT_COMMITTER_EMAIL=" + identity.GitUserEmail);
            break;
        }
        default:
            throw new InvalidOperationException("Unsupported output format.");
    }

    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}

static async Task<BrokerTokenResponse> RequestTokenAsync(
    ClientCommand command,
    CancellationToken cancellationToken)
{
    using var httpClient = new HttpClient
    {
        BaseAddress = new Uri(command.BrokerUrl.TrimEnd('/') + "/")
    };

    using var request = new HttpRequestMessage(HttpMethod.Post, "v1/github/installation-token");
    request.Headers.Authorization = new("Bearer", command.Secret);
    request.Content = JsonContent.Create(new TokenBrokerRequest(
        command.Repository!,
        command.Purpose!,
        command.Branch));

    using var response = await httpClient.SendAsync(request, cancellationToken);
    var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

    if (!response.IsSuccessStatusCode)
    {
        throw new InvalidOperationException(
            $"Broker returned {(int)response.StatusCode} {response.ReasonPhrase}. {Trim(responseBody)}");
    }

    var token = JsonSerializer.Deserialize<BrokerTokenResponse>(responseBody, JsonSerialization.Options);
    if (token is null || string.IsNullOrWhiteSpace(token.Token))
    {
        throw new InvalidOperationException("Broker response did not include a token.");
    }

    return token;
}

static async Task<BrokerAppIdentityResponse> RequestIdentityAsync(
    ClientCommand command,
    CancellationToken cancellationToken)
{
    using var httpClient = new HttpClient
    {
        BaseAddress = new Uri(command.BrokerUrl.TrimEnd('/') + "/")
    };

    using var request = new HttpRequestMessage(HttpMethod.Get, "v1/github/app-identity");
    request.Headers.Authorization = new("Bearer", command.Secret);

    using var response = await httpClient.SendAsync(request, cancellationToken);
    var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

    if (!response.IsSuccessStatusCode)
    {
        throw new InvalidOperationException(
            $"Broker returned {(int)response.StatusCode} {response.ReasonPhrase}. {Trim(responseBody)}");
    }

    var identity = JsonSerializer.Deserialize<BrokerAppIdentityResponse>(responseBody, JsonSerialization.Options);
    if (identity is null
        || string.IsNullOrWhiteSpace(identity.GitUserName)
        || string.IsNullOrWhiteSpace(identity.GitUserEmail))
    {
        throw new InvalidOperationException("Broker response did not include a git identity.");
    }

    return identity;
}

static bool IsUsernamePrompt(string? prompt)
{
    return prompt is not null
        && prompt.Contains("username", StringComparison.OrdinalIgnoreCase);
}

static string Trim(string value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return string.Empty;
    }

    return value.Length <= 512 ? value : value[..512] + "...";
}

static void PrintUsage()
{
    Console.Error.WriteLine(
        """
        Usage:
          CodexWorker.GitHubAuth.Client token --repo OWNER/REPO --purpose PURPOSE [--branch BRANCH]
          CodexWorker.GitHubAuth.Client json --repo OWNER/REPO --purpose PURPOSE [--branch BRANCH]
          CodexWorker.GitHubAuth.Client gh-env --repo OWNER/REPO --purpose PURPOSE [--branch BRANCH]
          CodexWorker.GitHubAuth.Client askpass --repo OWNER/REPO --purpose PURPOSE [--branch BRANCH] [git prompt]
          CodexWorker.GitHubAuth.Client identity
          CodexWorker.GitHubAuth.Client git-env

        Options:
          --broker-url URL       Defaults to CODEX_WORKER_GITHUB_AUTH_URL or http://127.0.0.1:5657
          --secret VALUE         Broker shared secret. Prefer --secret-file or CODEX_WORKER_GITHUB_AUTH_SECRET.
          --secret-file PATH     File containing the broker shared secret.
          --repo OWNER/REPO      Repository requested from the broker.
          --purpose PURPOSE      Server-side policy purpose.
          --branch BRANCH        Branch name for policies that require branch allowlists.
        """);
}

sealed record TokenBrokerRequest(
    [property: JsonPropertyName("repository")] string Repository,
    [property: JsonPropertyName("purpose")] string Purpose,
    [property: JsonPropertyName("branch")] string? Branch);

sealed record BrokerTokenResponse(
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("expiresAt")] DateTimeOffset ExpiresAt,
    [property: JsonPropertyName("repository")] string Repository,
    [property: JsonPropertyName("purpose")] string Purpose,
    [property: JsonPropertyName("permissions")] IReadOnlyDictionary<string, string> Permissions);

sealed record BrokerAppIdentityResponse(
    [property: JsonPropertyName("appSlug")] string AppSlug,
    [property: JsonPropertyName("appName")] string AppName,
    [property: JsonPropertyName("botLogin")] string BotLogin,
    [property: JsonPropertyName("botUserId")] long BotUserId,
    [property: JsonPropertyName("gitUserName")] string GitUserName,
    [property: JsonPropertyName("gitUserEmail")] string GitUserEmail);

sealed record ClientCommand(
    string Command,
    string BrokerUrl,
    string Secret,
    string? Repository,
    string? Purpose,
    string? Branch,
    OutputFormat Format,
    string? Prompt,
    bool ShowHelp);

enum OutputFormat
{
    Token,
    Json,
    GitAskPass,
    GhEnv,
    IdentityJson,
    GitEnv
}

sealed record ParseResult(bool IsSuccess, ClientCommand Value, string? Error)
{
    public static ParseResult Success(ClientCommand value)
    {
        return new ParseResult(true, value, null);
    }

    public static ParseResult Failure(string error)
    {
        return new ParseResult(false, EmptyCommand, error);
    }

    private static readonly ClientCommand EmptyCommand = new(
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        null,
        OutputFormat.Token,
        null,
        false);
}

static class CommandLine
{
    public static ParseResult Parse(string[] args)
    {
        if (args.Length == 0)
        {
            return ParseResult.Failure("Missing command.");
        }

        var commandName = args[0];
        if (commandName is "-h" or "--help" or "help")
        {
            return ParseResult.Success(new ClientCommand(
                commandName,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                null,
                OutputFormat.Token,
                null,
                true));
        }

        var format = commandName.ToLowerInvariant() switch
        {
            "token" => OutputFormat.Token,
            "json" => OutputFormat.Json,
            "askpass" => OutputFormat.GitAskPass,
            "gh-env" => OutputFormat.GhEnv,
            "identity" => OutputFormat.IdentityJson,
            "git-env" => OutputFormat.GitEnv,
            _ => (OutputFormat?)null
        };

        if (format is null)
        {
            return ParseResult.Failure($"Unknown command '{commandName}'.");
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? prompt = null;

        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                prompt = arg;
                continue;
            }

            var separator = arg.IndexOf('=');
            if (separator >= 0)
            {
                values[arg[..separator]] = arg[(separator + 1)..];
                continue;
            }

            if (i + 1 >= args.Length)
            {
                return ParseResult.Failure($"Missing value for {arg}.");
            }

            values[arg] = args[++i];
        }

        try
        {
            var brokerUrl = GetValue(values, "--broker-url")
                ?? Environment.GetEnvironmentVariable("CODEX_WORKER_GITHUB_AUTH_URL")
                ?? "http://127.0.0.1:5657";
            var secret = GetValue(values, "--secret")
                ?? ReadSecretFile(GetValue(values, "--secret-file"))
                ?? Environment.GetEnvironmentVariable("CODEX_WORKER_GITHUB_AUTH_SECRET");
            var repository = GetValue(values, "--repo");
            var purpose = GetValue(values, "--purpose");
            var branch = GetValue(values, "--branch");

            if (string.IsNullOrWhiteSpace(secret))
            {
                return ParseResult.Failure("Broker shared secret is required.");
            }

            if (RequiresRepository(format.Value) && string.IsNullOrWhiteSpace(repository))
            {
                return ParseResult.Failure("--repo is required.");
            }

            if (RequiresRepository(format.Value) && string.IsNullOrWhiteSpace(purpose))
            {
                return ParseResult.Failure("--purpose is required.");
            }

            return ParseResult.Success(new ClientCommand(
                commandName,
                brokerUrl,
                secret,
                repository,
                purpose,
                branch,
                format.Value,
                prompt,
                false));
        }
        catch (Exception exception)
        {
            return ParseResult.Failure(exception.Message);
        }
    }

    private static string? GetValue(IReadOnlyDictionary<string, string> values, string key)
    {
        return values.TryGetValue(key, out var value) ? value : null;
    }

    private static string? ReadSecretFile(string? path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? null
            : File.ReadAllText(path).Trim();
    }

    private static bool RequiresRepository(OutputFormat format)
    {
        return format is OutputFormat.Token
            or OutputFormat.Json
            or OutputFormat.GitAskPass
            or OutputFormat.GhEnv;
    }
}

static class JsonSerialization
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };
}
