namespace JenkinsFailureAnalyzer.Services;

public interface IIngestionSecretValidator
{
    bool IsConfigured { get; }

    bool Validate(string? providedSecret);
}
