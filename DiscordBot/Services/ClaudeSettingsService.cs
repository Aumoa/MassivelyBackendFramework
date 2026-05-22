using AI.Providers.Claude;
using DiscordBot.Repositories;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace DiscordBot.Services;

public interface IClaudeSettingsService
{
    ValueTask<ClaudeSettingsData> GetAsync(CancellationToken cancellationToken = default);

    ValueTask SaveAsync(
        string model,
        string summaryModel,
        int defaultMaxTokens,
        CancellationToken cancellationToken = default);
}

internal sealed class ClaudeSettingsService(
    IClaudeSettingsRepository repository,
    IOptions<OllamaService.Configuration> botOptions,
    IOptions<ClaudeChatClientOptions> claudeOptions,
    IMemoryCache cache) : IClaudeSettingsService
{
    private const string CacheKey = "DiscordBot.ClaudeSettings";
    private const int MinMaxTokens = 1;
    private const int MaxMaxTokens = 200000;

    public async ValueTask<ClaudeSettingsData> GetAsync(CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue<ClaudeSettingsData>(CacheKey, out var cachedSettings) && cachedSettings != null)
        {
            return cachedSettings;
        }

        var settings = await repository.GetAsync(cancellationToken);
        if (settings == null)
        {
            settings = BuildDefaultSettings();
            await repository.UpsertAsync(
                settings.Model,
                settings.SummaryModel,
                settings.DefaultMaxTokens,
                cancellationToken);
        }

        cache.Set(CacheKey, settings);
        return settings;
    }

    public async ValueTask SaveAsync(
        string model,
        string summaryModel,
        int defaultMaxTokens,
        CancellationToken cancellationToken = default)
    {
        var normalizedModel = NormalizeRequired(model, nameof(model));
        var normalizedSummaryModel = NormalizeRequired(summaryModel, nameof(summaryModel));
        var normalizedMaxTokens = NormalizeMaxTokens(defaultMaxTokens);

        await repository.UpsertAsync(
            normalizedModel,
            normalizedSummaryModel,
            normalizedMaxTokens,
            cancellationToken);

        cache.Set(CacheKey, new ClaudeSettingsData(
            normalizedModel,
            normalizedSummaryModel,
            normalizedMaxTokens,
            DateTime.Now,
            DateTime.Now));
    }

    private ClaudeSettingsData BuildDefaultSettings()
    {
        var options = botOptions.Value;
        var model = NormalizeRequired(options.Model, nameof(options.Model));
        var summaryModel = NormalizeRequired(options.SummaryModel, nameof(options.SummaryModel));
        var defaultMaxTokens = NormalizeMaxTokens(claudeOptions.Value.DefaultMaxTokens);

        return new ClaudeSettingsData(model, summaryModel, defaultMaxTokens, DateTime.Now, null);
    }

    private static string NormalizeRequired(string? value, string parameterName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("Value is required.", parameterName);
        }

        return normalized;
    }

    private static int NormalizeMaxTokens(int value)
    {
        if (value is < MinMaxTokens or > MaxMaxTokens)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                $"Default max tokens must be between {MinMaxTokens} and {MaxMaxTokens}.");
        }

        return value;
    }
}
