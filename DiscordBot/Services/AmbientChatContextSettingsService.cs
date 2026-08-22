using DiscordBot.Options;
using DiscordBot.Repositories;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace DiscordBot.Services;

public sealed record AmbientChatContextSettingsView(
    bool Enabled,
    int WindowMessageCount,
    int WindowMaxChars,
    int LookbackMinutes,
    DateTime CreatedAt,
    DateTime? UpdatedAt)
{
    public AmbientChatContextOptions ToOptions()
    {
        return new AmbientChatContextOptions
        {
            Enabled = Enabled,
            WindowMessageCount = WindowMessageCount,
            WindowMaxChars = WindowMaxChars,
            LookbackMinutes = LookbackMinutes
        };
    }
}

public sealed record AmbientChatContextSettingsSaveRequest(
    bool Enabled,
    int WindowMessageCount,
    int WindowMaxChars,
    int LookbackMinutes);

public interface IAmbientChatContextSettingsService
{
    ValueTask<AmbientChatContextSettingsView> GetAsync(CancellationToken cancellationToken = default);

    ValueTask SaveAsync(
        AmbientChatContextSettingsSaveRequest request,
        CancellationToken cancellationToken = default);
}

internal sealed class AmbientChatContextSettingsService(
    IAmbientChatContextSettingsRepository repository,
    IOptions<AmbientChatContextOptions> options,
    IMemoryCache cache) : IAmbientChatContextSettingsService
{
    private const string CacheKey = "DiscordBot.AmbientChatContextSettings";
    private const int MinWindowMessageCount = 1;
    private const int MaxWindowMessageCount = 50;
    private const int MinWindowMaxChars = 200;
    private const int MaxWindowMaxChars = 8000;
    private const int MinLookbackMinutes = 1;
    private const int MaxLookbackMinutes = 24 * 60;

    public async ValueTask<AmbientChatContextSettingsView> GetAsync(CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue<AmbientChatContextSettingsView>(CacheKey, out var cachedSettings) && cachedSettings != null)
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
        AmbientChatContextSettingsSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        var view = Normalize(request);
        await SaveCoreAsync(view, cancellationToken);
        cache.Set(CacheKey, view with { UpdatedAt = DateTime.Now });
    }

    private AmbientChatContextSettingsView BuildDefaultSettings()
    {
        var currentOptions = options.Value;
        return Normalize(new AmbientChatContextSettingsSaveRequest(
            currentOptions.Enabled,
            currentOptions.WindowMessageCount,
            currentOptions.WindowMaxChars,
            currentOptions.LookbackMinutes));
    }

    private AmbientChatContextSettingsView Normalize(AmbientChatContextSettingsSaveRequest request)
    {
        return new AmbientChatContextSettingsView(
            request.Enabled,
            NormalizeRange(
                request.WindowMessageCount,
                options.Value.WindowMessageCount,
                MinWindowMessageCount,
                MaxWindowMessageCount),
            NormalizeRange(
                request.WindowMaxChars,
                options.Value.WindowMaxChars,
                MinWindowMaxChars,
                MaxWindowMaxChars),
            NormalizeRange(
                request.LookbackMinutes,
                options.Value.LookbackMinutes,
                MinLookbackMinutes,
                MaxLookbackMinutes),
            DateTime.Now,
            null);
    }

    private AmbientChatContextSettingsView ToView(AmbientChatContextSettingsData data)
    {
        return new AmbientChatContextSettingsView(
            data.Enabled,
            NormalizeRange(
                data.WindowMessageCount,
                options.Value.WindowMessageCount,
                MinWindowMessageCount,
                MaxWindowMessageCount),
            NormalizeRange(
                data.WindowMaxChars,
                options.Value.WindowMaxChars,
                MinWindowMaxChars,
                MaxWindowMaxChars),
            NormalizeRange(
                data.LookbackMinutes,
                options.Value.LookbackMinutes,
                MinLookbackMinutes,
                MaxLookbackMinutes),
            data.CreatedAt,
            data.UpdatedAt);
    }

    private async ValueTask SaveCoreAsync(
        AmbientChatContextSettingsView view,
        CancellationToken cancellationToken)
    {
        await repository.UpsertAsync(
            view.Enabled,
            view.WindowMessageCount,
            view.WindowMaxChars,
            view.LookbackMinutes,
            cancellationToken);
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
