namespace JenkinsFailureAnalyzer.Services;

public interface IFailureAnalysisService
{
    ValueTask<FailureAnalysisResult> AnalyzeAsync(FailureAnalysisSubmission submission, CancellationToken cancellationToken);
}
