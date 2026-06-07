namespace JenkinsFailureAnalyzer.Options;

public sealed class FailureAnalysisStorageOptions
{
    public string RootPath { get; set; } = "App_Data/analysis";

    public int MaxContentLength { get; set; } = 2 * 1024 * 1024;
}
