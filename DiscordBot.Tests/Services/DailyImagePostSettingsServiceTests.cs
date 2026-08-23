using DiscordBot.Options;
using DiscordBot.Repositories;
using DiscordBot.Services;
using Microsoft.Extensions.Caching.Memory;

namespace DiscordBot.Tests.Services;

public sealed class DailyImagePostSettingsServiceTests
{
    [Fact]
    public async Task GetAsync_SeedsDefaultSettingsFromOptions()
    {
        var repository = new FakeDailyImagePostSettingsRepository();
        var options = new DailyImagePostOptions
        {
            Enabled = true,
            ChannelId = "123456789012345678",
            PostTimeOfDay = "21:30",
            ThemePrompt = "우주 배경"
        };
        var service = CreateService(repository, options);

        var settings = await service.GetAsync();

        Assert.True(settings.Enabled);
        Assert.Equal("123456789012345678", settings.ChannelId);
        Assert.Equal("21:30", settings.PostTimeOfDay);
        Assert.Equal("우주 배경", settings.ThemePrompt);
        Assert.NotNull(repository.Settings);
    }

    [Fact]
    public async Task SaveAsync_FallsBackToEmptyChannelId_WhenValueIsNotNumeric()
    {
        var repository = new FakeDailyImagePostSettingsRepository();
        var service = CreateService(repository, new DailyImagePostOptions());

        await service.SaveAsync(new DailyImagePostSettingsSaveRequest(true, "not-a-number", "09:00", ""));

        Assert.NotNull(repository.Settings);
        Assert.Equal("", repository.Settings.ChannelId);
    }

    [Fact]
    public async Task SaveAsync_FallsBackToOptionsDefault_WhenPostTimeOfDayIsInvalid()
    {
        var repository = new FakeDailyImagePostSettingsRepository();
        var service = CreateService(repository, new DailyImagePostOptions { PostTimeOfDay = "07:15" });

        await service.SaveAsync(new DailyImagePostSettingsSaveRequest(true, "1", "not-a-time", ""));

        Assert.NotNull(repository.Settings);
        Assert.Equal("07:15", repository.Settings.PostTimeOfDay);
    }

    [Fact]
    public async Task SaveAsync_TruncatesThemePromptToMaxLength()
    {
        var repository = new FakeDailyImagePostSettingsRepository();
        var service = CreateService(repository, new DailyImagePostOptions());
        var longTheme = new string('a', 1500);

        await service.SaveAsync(new DailyImagePostSettingsSaveRequest(true, "1", "09:00", longTheme));

        Assert.NotNull(repository.Settings);
        Assert.Equal(1000, repository.Settings.ThemePrompt.Length);
    }

    private static DailyImagePostSettingsService CreateService(
        FakeDailyImagePostSettingsRepository repository,
        DailyImagePostOptions? options = null)
    {
        return new DailyImagePostSettingsService(
            repository,
            Microsoft.Extensions.Options.Options.Create(options ?? new DailyImagePostOptions()),
            new MemoryCache(new MemoryCacheOptions()));
    }

    private sealed class FakeDailyImagePostSettingsRepository : IDailyImagePostSettingsRepository
    {
        public DailyImagePostSettingsData? Settings { get; private set; }

        public ValueTask<DailyImagePostSettingsData?> GetAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(Settings);
        }

        public ValueTask UpsertAsync(
            bool enabled,
            string channelId,
            string postTimeOfDay,
            string themePrompt,
            CancellationToken cancellationToken = default)
        {
            Settings = new DailyImagePostSettingsData(
                enabled,
                channelId,
                postTimeOfDay,
                themePrompt,
                DateTime.Now,
                DateTime.Now);
            return ValueTask.CompletedTask;
        }
    }
}
