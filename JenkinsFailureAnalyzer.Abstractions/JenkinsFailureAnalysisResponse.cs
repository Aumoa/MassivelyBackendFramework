namespace JenkinsFailureAnalyzer.Abstractions;

public sealed class JenkinsFailureAnalysisResponse
{
    public string Id { get; set; } = string.Empty;

    public DateTimeOffset ReceivedAt { get; set; }

    public string Summary { get; set; } = string.Empty;

    public IReadOnlyList<string> LikelyCauses { get; set; } = [];

    public IReadOnlyList<string> SuggestedActions { get; set; } = [];
}
