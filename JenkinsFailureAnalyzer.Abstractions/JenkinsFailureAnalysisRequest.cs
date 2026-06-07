using System.ComponentModel.DataAnnotations;

namespace JenkinsFailureAnalyzer.Abstractions;

public sealed class JenkinsFailureAnalysisRequest
{
    [StringLength(256)]
    public string? JobName { get; set; }

    [StringLength(64)]
    public string? BuildNumber { get; set; }

    [StringLength(2048)]
    public string? BuildUrl { get; set; }

    [StringLength(256)]
    public string? Branch { get; set; }

    [StringLength(128)]
    public string? Commit { get; set; }

    [StringLength(256)]
    public string? FailedStage { get; set; }

    [StringLength(1024)]
    public string? Summary { get; set; }

    [Required]
    public string Content { get; set; } = string.Empty;
}
