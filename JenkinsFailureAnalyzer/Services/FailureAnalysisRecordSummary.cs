namespace JenkinsFailureAnalyzer.Services;

public sealed record FailureAnalysisRecordSummary(
    string Id,
    DateTimeOffset ReceivedAt,
    string JobName,
    string BuildNumber,
    string Summary);
