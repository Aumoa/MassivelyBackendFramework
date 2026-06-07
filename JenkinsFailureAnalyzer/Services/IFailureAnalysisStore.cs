namespace JenkinsFailureAnalyzer.Services;

public interface IFailureAnalysisStore
{
    ValueTask<FailureAnalysisRecord> SaveAsync(
        FailureAnalysisSubmission submission,
        FailureAnalysisResult analysis,
        string? remoteAddress,
        CancellationToken cancellationToken);

    ValueTask<FailureAnalysisRecord?> GetAsync(string id, CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<FailureAnalysisRecordSummary>> ListAsync(CancellationToken cancellationToken);
}
