using JenkinsFailureAnalyzer.Abstractions;

namespace JenkinsFailureAnalyzer.Services;

public sealed record FailureAnalysisSubmission(
    string? JobName,
    string? BuildNumber,
    string? BuildUrl,
    string? Branch,
    string? Commit,
    string? FailedStage,
    string? Summary,
    string Content)
{
    public static FailureAnalysisSubmission FromRequest(JenkinsFailureAnalysisRequest request)
    {
        return new FailureAnalysisSubmission(
            Normalize(request.JobName),
            Normalize(request.BuildNumber),
            Normalize(request.BuildUrl),
            Normalize(request.Branch),
            Normalize(request.Commit),
            Normalize(request.FailedStage),
            Normalize(request.Summary),
            request.Content);
    }

    private static string? Normalize(string? value)
    {
        value = value?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
