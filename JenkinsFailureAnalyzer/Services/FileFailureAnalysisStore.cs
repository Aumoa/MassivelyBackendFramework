using System.Text.Json;
using JenkinsFailureAnalyzer.Options;
using Microsoft.Extensions.Options;

namespace JenkinsFailureAnalyzer.Services;

public sealed class FileFailureAnalysisStore : IFailureAnalysisStore
{
    private static readonly JsonSerializerOptions s_JsonSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string m_RootPath;
    private readonly ILogger<FileFailureAnalysisStore> m_Logger;

    public FileFailureAnalysisStore(
        IWebHostEnvironment environment,
        IOptions<FailureAnalysisStorageOptions> options,
        ILogger<FileFailureAnalysisStore> logger)
    {
        var rootPath = options.Value.RootPath;
        m_RootPath = Path.IsPathRooted(rootPath)
            ? rootPath
            : Path.Combine(environment.ContentRootPath, rootPath);
        m_Logger = logger;
    }

    public async ValueTask<FailureAnalysisRecord> SaveAsync(
        FailureAnalysisSubmission submission,
        FailureAnalysisResult analysis,
        string? remoteAddress,
        CancellationToken cancellationToken)
    {
        var receivedAt = DateTimeOffset.UtcNow;
        var record = new FailureAnalysisRecord(
            Guid.NewGuid().ToString("N"),
            receivedAt,
            remoteAddress,
            submission,
            analysis);

        var directoryPath = Path.Combine(m_RootPath, receivedAt.UtcDateTime.ToString("yyyyMMdd"));
        Directory.CreateDirectory(directoryPath);

        var filePath = Path.Combine(directoryPath, $"{record.Id}.json");
        await using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, record, s_JsonSerializerOptions, cancellationToken);
        return record;
    }

    public async ValueTask<FailureAnalysisRecord?> GetAsync(string id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        foreach (var filePath in EnumerateRecordFiles())
        {
            if (!string.Equals(Path.GetFileNameWithoutExtension(filePath), id, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return await ReadRecordAsync(filePath, cancellationToken);
        }

        return null;
    }

    public async ValueTask<IReadOnlyList<FailureAnalysisRecordSummary>> ListAsync(CancellationToken cancellationToken)
    {
        var records = new List<FailureAnalysisRecordSummary>();
        foreach (var filePath in EnumerateRecordFiles())
        {
            var record = await ReadRecordAsync(filePath, cancellationToken);
            if (record is null)
            {
                continue;
            }

            records.Add(new FailureAnalysisRecordSummary(
                record.Id,
                record.ReceivedAt,
                record.Submission.JobName ?? "Unknown job",
                record.Submission.BuildNumber ?? "Unknown build",
                record.Analysis.Summary));
        }

        return records
            .OrderByDescending(static record => record.ReceivedAt)
            .ToArray();
    }

    private IEnumerable<string> EnumerateRecordFiles()
    {
        if (!Directory.Exists(m_RootPath))
        {
            return [];
        }

        return Directory.EnumerateFiles(m_RootPath, "*.json", SearchOption.AllDirectories);
    }

    private async ValueTask<FailureAnalysisRecord?> ReadRecordAsync(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(filePath);
            return await JsonSerializer.DeserializeAsync<FailureAnalysisRecord>(stream, s_JsonSerializerOptions, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            m_Logger.LogWarning(ex, "Failed to read Jenkins failure analysis record at {FilePath}.", filePath);
            return null;
        }
    }
}
