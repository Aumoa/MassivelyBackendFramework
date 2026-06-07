namespace JenkinsFailureAnalyzer.Services;

public sealed record FailureAnalysisRecord(
    string Id,
    DateTimeOffset ReceivedAt,
    string? RemoteAddress,
    FailureAnalysisSubmission Submission,
    FailureAnalysisResult Analysis);
