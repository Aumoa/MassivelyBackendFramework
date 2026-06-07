using System.Security.Cryptography;
using System.Text;
using JenkinsFailureAnalyzer.Options;
using Microsoft.Extensions.Options;

namespace JenkinsFailureAnalyzer.Services;

public sealed class ConfiguredIngestionSecretValidator(IOptionsMonitor<FailureAnalysisIngestionOptions> options) : IIngestionSecretValidator
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.CurrentValue.SharedSecret);

    public bool Validate(string? providedSecret)
    {
        var expectedSecret = options.CurrentValue.SharedSecret;
        if (string.IsNullOrWhiteSpace(expectedSecret) || string.IsNullOrWhiteSpace(providedSecret))
        {
            return false;
        }

        var expectedBytes = Encoding.UTF8.GetBytes(expectedSecret);
        var providedBytes = Encoding.UTF8.GetBytes(providedSecret);
        return providedBytes.Length == expectedBytes.Length &&
            CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
    }
}
