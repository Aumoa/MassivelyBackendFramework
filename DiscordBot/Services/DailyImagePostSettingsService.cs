using DiscordBot.Options;
using DiscordBot.Repositories;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace DiscordBot.Services;

internal sealed record DailyImagePostSettingsView(
    bool Enabled,
    string ChannelId,
    string PostTimeOfDay,
    string ThemePrompt,
    DateTime CreatedAt,
    DateTime? UpdatedAt)
{
    public DailyImagePostOptions ToOptions()
    {
        return new DailyImagePostOptions
        {
            Enabled = Enabled,
            ChannelId = ChannelId,
            PostTimeOfDay = PostTimeOfDay,
            ThemePrompt = ThemePrompt
        };
    }
}

internal sealed record DailyImagePostSettingsSaveRequest(
    bool Enabled,
    string ChannelId,
    string PostTimeOfDay,
    string ThemePrompt);

internal interface IDailyImagePostSettingsService
{
    ValueTask<DailyImagePostSettingsView> GetAsync(CancellationToken cancellationToken = default);

    ValueTask SaveAsync(
        DailyImagePostSettingsSaveRequest request,
        CancellationToken cancellationToken = default);
}

internal sealed class DailyImagePostSettingsService(
    IDailyImagePostSettingsRepository repository,
    IOptions<DailyImagePostOptions> options,
    IMemoryCache cache) : IDailyImagePostSettingsService
{
    private const string CacheKey = "DiscordBot.DailyImagePostSettings";
    private const int MaxThemePromptLength = 1000;

    public async ValueTask<DailyImagePostSettingsView> GetAsync(CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue<DailyImagePostSettingsView>(CacheKey, out var cachedSettings) && cachedSettings != null)
        {
            return cachedSettings;
        }

        var settings = await repository.GetAsync(cancellationToken);
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
        DailyImagePostSettingsSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        var view = Normalize(request);
        await SaveCoreAsync(view, cancellationToken);
        cache.Set(CacheKey, view with { UpdatedAt = DateTime.Now });
    }

    private DailyImagePostSettingsView BuildDefaultSettings()
    {
        var currentOptions = options.Value;
        return Normalize(new DailyImagePostSettingsSaveRequest(
            currentOptions.Enabled,
            currentOptions.ChannelId,
            currentOptions.PostTimeOfDay,
            currentOptions.ThemePrompt));
    }

    private DailyImagePostSettingsView Normalize(DailyImagePostSettingsSaveRequest request)
    {
        return new DailyImagePostSettingsView(
            request.Enabled,
            NormalizeChannelId(request.ChannelId),
            NormalizePostTimeOfDay(request.PostTimeOfDay),
            NormalizeThemePrompt(request.ThemePrompt),
            DateTime.Now,
            null);
    }

    private DailyImagePostSettingsView ToView(DailyImagePostSettingsData data)
    {
        return new DailyImagePostSettingsView(
            data.Enabled,
            NormalizeChannelId(data.ChannelId),
            NormalizePostTimeOfDay(data.PostTimeOfDay),
            NormalizeThemePrompt(data.ThemePrompt),
            data.CreatedAt,
            data.UpdatedAt);
    }

    private async ValueTask SaveCoreAsync(
        DailyImagePostSettingsView view,
        CancellationToken cancellationToken)
    {
        await repository.UpsertAsync(
            view.Enabled,
            view.ChannelId,
            view.PostTimeOfDay,
            view.ThemePrompt,
            cancellationToken);
    }

    private static string NormalizeChannelId(string? value)
    {
        var trimmed = value?.Trim();
        return !string.IsNullOrWhiteSpace(trimmed) && ulong.TryParse(trimmed, out _)
            ? trimmed
            : "";
    }

    private string NormalizePostTimeOfDay(string? value)
    {
        var trimmed = value?.Trim();
        if (!string.IsNullOrWhiteSpace(trimmed)
            && TimeOnly.TryParseExact(trimmed, "HH:mm", out _))
        {
            return trimmed;
        }

        var fallback = options.Value.PostTimeOfDay;
        return TimeOnly.TryParseExact(fallback, "HH:mm", out _) ? fallback : "09:00";
    }

    private static string NormalizeThemePrompt(string? value)
    {
        var trimmed = value?.Trim() ?? "";
        return trimmed.Length <= MaxThemePromptLength ? trimmed : trimmed[..MaxThemePromptLength];
    }
}
