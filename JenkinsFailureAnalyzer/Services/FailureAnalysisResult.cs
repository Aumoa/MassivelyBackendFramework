namespace JenkinsFailureAnalyzer.Services;

public sealed record FailureAnalysisResult(
    string Summary,
    IReadOnlyList<string> LikelyCauses,
    IReadOnlyList<string> SuggestedActions);
