using System.Text.Json;
using DiscordBot.Options;
using DiscordBot.Repositories;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace DiscordBot.Services;

internal sealed record AutoResponseSettingsView(
    bool Enabled,
    int IntervalSeconds,
    int CooldownSeconds,
    int MaxBufferedMessages,
    int ClassifierMaxTokens,
    string? ClassifierModel,
    IReadOnlyList<string> BotNameAliases,
    DateTime CreatedAt,
    DateTime? UpdatedAt)
{
    public AutoResponseOptions ToOptions()
    {
        return new AutoResponseOptions
        {
            Enabled = Enabled,
            IntervalSeconds = IntervalSeconds,
            CooldownSeconds = CooldownSeconds,
            MaxBufferedMessages = MaxBufferedMessages,
            ClassifierMaxTokens = ClassifierMaxTokens,
            ClassifierModel = ClassifierModel,
            BotNameAliases = [.. BotNameAliases]
        };
    }
}

internal sealed record AutoResponseSettingsSaveRequest(
    bool Enabled,
    int IntervalSeconds,
    int CooldownSeconds,
    int MaxBufferedMessages,
    int ClassifierMaxTokens,
    string? ClassifierModel,
    IReadOnlyList<string> BotNameAliases);

internal sealed record AutoResponseEventView(
    long Id,
    string? GuildId,
    string ChannelId,
    string? TriggerMessageId,
    IReadOnlyList<string> MessageIds,
    string Decision,
    string Reason,
    string Focus,
    string ErrorStage,
    int? HttpStatusCode,
    string ErrorMessage,
    DateTime CreatedAt);

internal sealed record AutoResponseEventDiagnostic
{
    private AutoResponseEventDiagnostic(
        string stage,
        int? httpStatusCode,
        string errorMessage)
    {
        Stage = stage;
        HttpStatusCode = httpStatusCode;
        ErrorMessage = errorMessage;
    }

    public string Stage { get; }

    public int? HttpStatusCode { get; }

    public string ErrorMessage { get; }

    public static AutoResponseEventDiagnostic FromException(string stage, Exception exception)
    {
        var httpException = FindHttpRequestException(exception);
        return new AutoResponseEventDiagnostic(
            stage,
            httpException?.StatusCode is { } statusCode ? (int)statusCode : null,
            BuildSafeSummary(exception, httpException));
    }

    private static string BuildSafeSummary(
        Exception exception,
        HttpRequestException? httpException)
    {
        if (httpException?.StatusCode is { } statusCode)
        {
            return $"HTTP request failed with status {(int)statusCode}.";
        }

        if (httpException != null)
        {
            return "HTTP transport request failed.";
        }

        return exception is TimeoutException
            ? "Operation timed out."
            : "Unexpected application failure.";
    }

    private static HttpRequestException? FindHttpRequestException(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current is HttpRequestException httpException)
            {
                return httpException;
            }
        }

        return null;
    }
}

internal interface IAutoResponseSettingsService
{
    ValueTask<AutoResponseSettingsView> GetAsync(CancellationToken cancellationToken = default);

    ValueTask SaveAsync(
        AutoResponseSettingsSaveRequest request,
        CancellationToken cancellationToken = default);

    ValueTask RecordEventAsync(
        IReadOnlyList<DiscordAutoResponseMessage> messages,
        string decision,
        string reason,
        string focus,
        AutoResponseEventDiagnostic? diagnostic = null,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<AutoResponseEventView>> GetRecentEventsAsync(
        int limit,
        CancellationToken cancellationToken = default);
}

internal sealed class AutoResponseSettingsService(
    IAutoResponseSettingsRepository settingsRepository,
    IAutoResponseEventRepository eventRepository,
    IOptions<AutoResponseOptions> options,
    IMemoryCache cache) : IAutoResponseSettingsService
{
    private const string CacheKey = "DiscordBot.AutoResponseSettings";
    private const int MaxAliasCount = 16;
    private const int MaxAliasLength = 64;
    private const int MaxModelLength = 128;
    private const int MaxTextLength = 400;
    private const int MinIntervalSeconds = 1;
    private const int MaxIntervalSeconds = 600;
    private const int MinCooldownSeconds = 1;
    private const int MaxCooldownSeconds = 3600;
    private const int MinMaxBufferedMessages = 1;
    private const int MaxMaxBufferedMessages = 50;
    private const int MinClassifierMaxTokens = 1;
    private const int MaxClassifierMaxTokens = 512;
    private const int DefaultRecentEventLimit = 100;
    private const int MaxRecentEventLimit = 500;

    public async ValueTask<AutoResponseSettingsView> GetAsync(CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue<AutoResponseSettingsView>(CacheKey, out var cachedSettings) && cachedSettings != null)
        {
            return cachedSettings;
        }

        var settings = await settingsRepository.GetAsync(cancellationToken);
        var view = settings == null
            ? BuildDefaultSettings()
            : ToView(settings);

        if (settings == null)
        {
            await SaveCoreAsync(view, cancellationToken);
        }

        cache.Set(CacheKey, view);
        return view;
    }

    public async ValueTask SaveAsync(
        AutoResponseSettingsSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        var view = Normalize(request);
        await SaveCoreAsync(view, cancellationToken);
        cache.Set(CacheKey, view with { UpdatedAt = DateTime.Now });
    }

    public async ValueTask RecordEventAsync(
        IReadOnlyList<DiscordAutoResponseMessage> messages,
        string decision,
        string reason,
        string focus,
        AutoResponseEventDiagnostic? diagnostic = null,
        CancellationToken cancellationToken = default)
    {
        if (messages.Count == 0)
        {
            return;
        }

        var orderedMessages = messages
            .OrderBy(message => message.Timestamp)
            .ToArray();
        var triggerMessage = orderedMessages[^1];
        var messageIds = orderedMessages
            .Select(message => message.MessageId)
            .Where(messageId => !string.IsNullOrWhiteSpace(messageId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var input = new AutoResponseEventInput(
            triggerMessage.GuildId,
            triggerMessage.ChannelId,
            triggerMessage.MessageId,
            JsonSerializer.Serialize(messageIds),
            NormalizeText(decision, "unknown", 32),
            NormalizeText(reason, string.Empty, MaxTextLength),
            NormalizeText(focus, string.Empty, MaxTextLength),
            NormalizeText(diagnostic?.Stage, string.Empty, 32),
            diagnostic?.HttpStatusCode,
            NormalizeErrorMessage(diagnostic?.ErrorMessage));

        await eventRepository.AddAsync(input, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<AutoResponseEventView>> GetRecentEventsAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        var normalizedLimit = limit <= 0
            ? DefaultRecentEventLimit
            : Math.Clamp(limit, 1, MaxRecentEventLimit);
        var events = await eventRepository.GetRecentAsync(normalizedLimit, cancellationToken);
        return events
            .Select(ToView)
            .ToList();
    }

    private AutoResponseSettingsView BuildDefaultSettings()
    {
        var currentOptions = options.Value;
        return Normalize(new AutoResponseSettingsSaveRequest(
            currentOptions.Enabled,
            currentOptions.IntervalSeconds,
            currentOptions.CooldownSeconds,
            currentOptions.MaxBufferedMessages,
            currentOptions.ClassifierMaxTokens,
            currentOptions.ClassifierModel,
            currentOptions.BotNameAliases));
    }

    private AutoResponseSettingsView Normalize(AutoResponseSettingsSaveRequest request)
    {
        return new AutoResponseSettingsView(
            request.Enabled,
            NormalizeRange(
                request.IntervalSeconds,
                options.Value.IntervalSeconds,
                MinIntervalSeconds,
                MaxIntervalSeconds),
            NormalizeRange(
                request.CooldownSeconds,
                options.Value.CooldownSeconds,
                MinCooldownSeconds,
                MaxCooldownSeconds),
            NormalizeRange(
                request.MaxBufferedMessages,
                options.Value.MaxBufferedMessages,
                MinMaxBufferedMessages,
                MaxMaxBufferedMessages),
            NormalizeRange(
                request.ClassifierMaxTokens,
                options.Value.ClassifierMaxTokens,
                MinClassifierMaxTokens,
                MaxClassifierMaxTokens),
            NormalizeOptional(request.ClassifierModel, MaxModelLength),
            NormalizeAliases(request.BotNameAliases),
            DateTime.Now,
            null);
    }

    private AutoResponseSettingsView ToView(AutoResponseSettingsData data)
    {
        return new AutoResponseSettingsView(
            data.Enabled,
            NormalizeRange(
                data.IntervalSeconds,
                options.Value.IntervalSeconds,
                MinIntervalSeconds,
                MaxIntervalSeconds),
            NormalizeRange(
                data.CooldownSeconds,
                options.Value.CooldownSeconds,
                MinCooldownSeconds,
                MaxCooldownSeconds),
            NormalizeRange(
                data.MaxBufferedMessages,
                options.Value.MaxBufferedMessages,
                MinMaxBufferedMessages,
                MaxMaxBufferedMessages),
            NormalizeRange(
                data.ClassifierMaxTokens,
                options.Value.ClassifierMaxTokens,
                MinClassifierMaxTokens,
                MaxClassifierMaxTokens),
            NormalizeOptional(data.ClassifierModel, MaxModelLength),
            ParseAliases(data.BotNameAliasesJson),
            data.CreatedAt,
            data.UpdatedAt);
    }

    private AutoResponseEventView ToView(AutoResponseEventData data)
    {
        return new AutoResponseEventView(
            data.Id,
            data.GuildId,
            data.ChannelId,
            data.TriggerMessageId,
            ParseMessageIds(data.MessageIdsJson),
            data.Decision,
            data.Reason,
            data.Focus,
            data.ErrorStage,
            data.HttpStatusCode,
            data.ErrorMessage,
            data.CreatedAt);
    }

    private async ValueTask SaveCoreAsync(
        AutoResponseSettingsView view,
        CancellationToken cancellationToken)
    {
        await settingsRepository.UpsertAsync(
            view.Enabled,
            view.IntervalSeconds,
            view.CooldownSeconds,
            view.MaxBufferedMessages,
            view.ClassifierMaxTokens,
            view.ClassifierModel,
            JsonSerializer.Serialize(view.BotNameAliases),
            cancellationToken);
    }

    private IReadOnlyList<string> ParseAliases(string aliasesJson)
    {
        try
        {
            var aliases = JsonSerializer.Deserialize<string[]>(aliasesJson);
            if (aliases is { Length: > 0 })
            {
                return NormalizeAliases(aliases);
            }
        }
        catch (JsonException)
        {
        }

        return NormalizeAliases(options.Value.BotNameAliases);
    }

    private static IReadOnlyList<string> ParseMessageIds(string messageIdsJson)
    {
        try
        {
            return JsonSerializer.Deserialize<string[]>(messageIdsJson) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private IReadOnlyList<string> NormalizeAliases(IReadOnlyList<string>? aliases)
    {
        var normalized = (aliases ?? [])
            .Select(alias => NormalizeOptional(alias, MaxAliasLength))
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxAliasCount)
            .ToArray();

        return normalized.Length == 0
            ? ["봇", "AI", "에이아이", "인공지능"]
            : normalized;
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static string NormalizeText(string? value, string fallback, int maxLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            normalized = fallback;
        }

        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    internal static string NormalizeErrorMessage(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var characters = value
            .Trim()
            .Select(character => char.IsControl(character) || char.IsWhiteSpace(character) ? ' ' : character)
            .ToArray();
        var normalized = string.Join(' ', new string(characters)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= MaxTextLength ? normalized : normalized[..MaxTextLength];
    }

    private static int NormalizeRange(int value, int fallback, int min, int max)
    {
        if (value <= 0)
        {
            value = fallback;
        }

        return Math.Clamp(value, min, max);
    }
}
