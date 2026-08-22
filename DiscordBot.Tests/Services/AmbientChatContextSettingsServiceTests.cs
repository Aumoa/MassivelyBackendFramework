using DiscordBot.Options;
using DiscordBot.Repositories;
using DiscordBot.Services;
using Microsoft.Extensions.Caching.Memory;

namespace DiscordBot.Tests.Services;

public sealed class AmbientChatContextSettingsServiceTests
{
    [Fact]
    public async Task GetAsync_SeedsDefaultSettingsFromOptions()
    {
        var repository = new FakeAmbientChatContextSettingsRepository();
        var options = new AmbientChatContextOptions
        {
            Enabled = true,
            WindowMessageCount = 8,
            WindowMaxChars = 1500,
            LookbackMinutes = 30
        };
        var service = CreateService(repository, options);

        var settings = await service.GetAsync();

        Assert.True(settings.Enabled);
        Assert.Equal(8, settings.WindowMessageCount);
        Assert.Equal(1500, settings.WindowMaxChars);
        Assert.Equal(30, settings.LookbackMinutes);
        Assert.NotNull(repository.Settings);
    }

    [Fact]
    public async Task SaveAsync_ClampsValuesToAllowedRanges()
    {
        var repository = new FakeAmbientChatContextSettingsRepository();
        var service = CreateService(repository, new AmbientChatContextOptions());

        await service.SaveAsync(new AmbientChatContextSettingsSaveRequest(
            true,
            9999,
            99999,
            99999));

        Assert.NotNull(repository.Settings);
        Assert.True(repository.Settings.Enabled);
        Assert.Equal(50, repository.Settings.WindowMessageCount);
        Assert.Equal(8000, repository.Settings.WindowMaxChars);
        Assert.Equal(1440, repository.Settings.LookbackMinutes);
    }

    [Fact]
    public async Task SaveAsync_FallsBackToOptionsDefault_WhenValueIsZeroOrNegative()
    {
        var repository = new FakeAmbientChatContextSettingsRepository();
        var service = CreateService(repository, new AmbientChatContextOptions
        {
            WindowMessageCount = 12,
            WindowMaxChars = 2000,
            LookbackMinutes = 60
        });

        await service.SaveAsync(new AmbientChatContextSettingsSaveRequest(false, 0, -1, 0));

        Assert.NotNull(repository.Settings);
        Assert.False(repository.Settings.Enabled);
        Assert.Equal(12, repository.Settings.WindowMessageCount);
        Assert.Equal(2000, repository.Settings.WindowMaxChars);
        Assert.Equal(60, repository.Settings.LookbackMinutes);
    }

    private static AmbientChatContextSettingsService CreateService(
        FakeAmbientChatContextSettingsRepository repository,
        AmbientChatContextOptions? options = null)
    {
        return new AmbientChatContextSettingsService(
            repository,
            Microsoft.Extensions.Options.Options.Create(options ?? new AmbientChatContextOptions()),
            new MemoryCache(new MemoryCacheOptions()));
    }

    private sealed class FakeAmbientChatContextSettingsRepository : IAmbientChatContextSettingsRepository
    {
        public AmbientChatContextSettingsData? Settings { get; private set; }

        public ValueTask<AmbientChatContextSettingsData?> GetAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(Settings);
        }

        public ValueTask UpsertAsync(
            bool enabled,
            int windowMessageCount,
            int windowMaxChars,
            int lookbackMinutes,
            CancellationToken cancellationToken = default)
        {
            Settings = new AmbientChatContextSettingsData(
                enabled,
                windowMessageCount,
                windowMaxChars,
                lookbackMinutes,
                DateTime.Now,
                DateTime.Now);
            return ValueTask.CompletedTask;
        }
    }
}
