using System.Net.Http.Json;
using System.Text.Json;
using JenkinsFailureAnalyzer.Abstractions;

return await RunAsync(args);

static async Task<int> RunAsync(string[] args)
{
    CliOptions options;
    try
    {
        options = CliOptions.Parse(args);
    }
    catch (ArgumentException ex)
    {
        Console.Error.WriteLine(ex.Message);
        PrintUsage();
        return 1;
    }

    if (options.ShowHelp)
    {
        PrintUsage();
        return 0;
    }

    var validationError = options.GetValidationError();
    if (validationError is not null)
    {
        Console.Error.WriteLine(validationError);
        PrintUsage();
        return 1;
    }

    var content = await options.ReadContentAsync();
    var request = new JenkinsFailureAnalysisRequest
    {
        JobName = options.JobName,
        BuildNumber = options.BuildNumber,
        BuildUrl = options.BuildUrl,
        Branch = options.Branch,
        Commit = options.Commit,
        FailedStage = options.FailedStage,
        Summary = options.Summary,
        Content = content
    };

    using var httpClient = new HttpClient();
    using var httpRequest = new HttpRequestMessage(HttpMethod.Post, BuildEndpoint(options.Endpoint!));
    httpRequest.Headers.Add(JenkinsFailureAnalyzerHeaders.Secret, options.Secret);
    httpRequest.Content = JsonContent.Create(request);

    using var response = await httpClient.SendAsync(httpRequest);
    var responseBody = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode)
    {
        Console.Error.WriteLine($"Server returned {(int)response.StatusCode} {response.ReasonPhrase}.");
        Console.Error.WriteLine(responseBody);
        return 1;
    }

    var result = JsonSerializer.Deserialize<JenkinsFailureAnalysisResponse>(
        responseBody,
        new JsonSerializerOptions(JsonSerializerDefaults.Web));
    if (result is null)
    {
        Console.Error.WriteLine("Server response could not be parsed.");
        return 1;
    }

    Console.WriteLine($"Analysis ID: {result.Id}");
    Console.WriteLine($"Received At: {result.ReceivedAt:O}");
    Console.WriteLine();
    Console.WriteLine(result.Summary);
    Console.WriteLine();
    Console.WriteLine("Likely causes:");
    foreach (var cause in result.LikelyCauses)
    {
        Console.WriteLine($"- {cause}");
    }

    Console.WriteLine();
    Console.WriteLine("Suggested actions:");
    foreach (var action in result.SuggestedActions)
    {
        Console.WriteLine($"- {action}");
    }

    return 0;
}

static Uri BuildEndpoint(string endpoint)
{
    var uri = new Uri(endpoint, UriKind.Absolute);
    if (uri.AbsolutePath.TrimEnd('/').EndsWith("/api/jenkins/failures", StringComparison.OrdinalIgnoreCase))
    {
        return uri;
    }

    return new Uri($"{endpoint.TrimEnd('/')}/api/jenkins/failures");
}

static void PrintUsage()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  JenkinsFailureAnalyzer.Cli --endpoint https://service.example --secret <secret> --content-file build.log [options]");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --endpoint <url>       Server base URL or /api/jenkins/failures endpoint.");
    Console.WriteLine("  --secret <secret>      Ingestion secret. Defaults to JFA_SECRET when omitted.");
    Console.WriteLine("  --content-file <path>  Failure content file.");
    Console.WriteLine("  --content <text>       Failure content text.");
    Console.WriteLine("  --stdin                Read failure content from standard input.");
    Console.WriteLine("  --job <name>           Jenkins job name.");
    Console.WriteLine("  --build-number <num>   Jenkins build number.");
    Console.WriteLine("  --build-url <url>      Jenkins build URL.");
    Console.WriteLine("  --branch <name>        Source branch.");
    Console.WriteLine("  --commit <sha>         Source commit.");
    Console.WriteLine("  --stage <name>         Failed stage.");
    Console.WriteLine("  --summary <text>       Short human summary.");
}

internal sealed class CliOptions
{
    public string? Endpoint { get; private set; }
    public string? Secret { get; private set; }
    public string? ContentFile { get; private set; }
    public string? Content { get; private set; }
    public bool ReadFromStandardInput { get; private set; }
    public bool ShowHelp { get; private set; }
    public string? JobName { get; private set; }
    public string? BuildNumber { get; private set; }
    public string? BuildUrl { get; private set; }
    public string? Branch { get; private set; }
    public string? Commit { get; private set; }
    public string? FailedStage { get; private set; }
    public string? Summary { get; private set; }

    public static CliOptions Parse(string[] args)
    {
        var options = new CliOptions();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "-h":
                case "--help":
                    options.ShowHelp = true;
                    break;
                case "--endpoint":
                    options.Endpoint = ReadValue(args, ref i, arg);
                    break;
                case "--secret":
                    options.Secret = ReadValue(args, ref i, arg);
                    break;
                case "--content-file":
                    options.ContentFile = ReadValue(args, ref i, arg);
                    break;
                case "--content":
                    options.Content = ReadValue(args, ref i, arg);
                    break;
                case "--stdin":
                    options.ReadFromStandardInput = true;
                    break;
                case "--job":
                    options.JobName = ReadValue(args, ref i, arg);
                    break;
                case "--build-number":
                    options.BuildNumber = ReadValue(args, ref i, arg);
                    break;
                case "--build-url":
                    options.BuildUrl = ReadValue(args, ref i, arg);
                    break;
                case "--branch":
                    options.Branch = ReadValue(args, ref i, arg);
                    break;
                case "--commit":
                    options.Commit = ReadValue(args, ref i, arg);
                    break;
                case "--stage":
                    options.FailedStage = ReadValue(args, ref i, arg);
                    break;
                case "--summary":
                    options.Summary = ReadValue(args, ref i, arg);
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {arg}");
            }
        }

        options.Secret ??= Environment.GetEnvironmentVariable("JFA_SECRET");
        return options;
    }

    public string? GetValidationError()
    {
        if (string.IsNullOrWhiteSpace(Endpoint))
        {
            return "--endpoint is required.";
        }

        if (string.IsNullOrWhiteSpace(Secret))
        {
            return "--secret or JFA_SECRET is required.";
        }

        var contentSources = 0;
        if (!string.IsNullOrWhiteSpace(ContentFile))
        {
            contentSources++;
        }

        if (!string.IsNullOrWhiteSpace(Content))
        {
            contentSources++;
        }

        if (ReadFromStandardInput)
        {
            contentSources++;
        }

        return contentSources == 1 ? null : "Provide exactly one content source: --content-file, --content, or --stdin.";
    }

    public async ValueTask<string> ReadContentAsync()
    {
        if (!string.IsNullOrWhiteSpace(ContentFile))
        {
            return await File.ReadAllTextAsync(ContentFile);
        }

        if (Content is not null)
        {
            return Content;
        }

        return await Console.In.ReadToEndAsync();
    }

    private static string ReadValue(string[] args, ref int index, string name)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"{name} requires a value.");
        }

        index++;
        return args[index];
    }
}
