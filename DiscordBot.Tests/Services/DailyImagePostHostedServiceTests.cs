using DiscordBot.Services;

namespace DiscordBot.Tests.Services;

public sealed class DailyImagePostHostedServiceTests
{
    [Fact]
    public void ShouldRunNow_ReturnsFalse_WhenTargetTimeHasNotBeenReachedYet()
    {
        var now = new DateTime(2026, 8, 23, 10, 47, 0);
        var target = new TimeOnly(10, 48);

        var result = DailyImagePostHostedService.ShouldRunNow(target, now, lastRunDate: null);

        Assert.False(result);
    }

    [Fact]
    public void ShouldRunNow_ReturnsTrue_WhenTargetTimeHasBeenReached_AndNotYetRunToday()
    {
        var now = new DateTime(2026, 8, 23, 10, 48, 5);
        var target = new TimeOnly(10, 48);

        var result = DailyImagePostHostedService.ShouldRunNow(target, now, lastRunDate: null);

        Assert.True(result);
    }

    [Fact]
    public void ShouldRunNow_ReturnsFalse_WhenAlreadyRunToday()
    {
        var now = new DateTime(2026, 8, 23, 11, 0, 0);
        var target = new TimeOnly(10, 48);
        var lastRunDate = new DateOnly(2026, 8, 23);

        var result = DailyImagePostHostedService.ShouldRunNow(target, now, lastRunDate);

        Assert.False(result);
    }

    [Fact]
    public void ShouldRunNow_ReturnsFalse_OnNextDay_AfterAlreadyRunningYesterday()
    {
        var now = new DateTime(2026, 8, 24, 10, 47, 0);
        var target = new TimeOnly(10, 48);
        var lastRunDate = new DateOnly(2026, 8, 23);

        var result = DailyImagePostHostedService.ShouldRunNow(target, now, lastRunDate);

        Assert.False(result);
    }

    [Fact]
    public void ShouldRunNow_ReturnsTrue_RegardlessOfHowLongAgoTargetTimePassed()
    {
        // 캐치업 방지 로직을 제거했으므로, 재시작 타이밍과 무관하게 "오늘 아직 실행 안 했고
        // 목표 시각을 지났다"는 조건만으로 실행돼야 한다.
        var now = new DateTime(2026, 8, 23, 23, 16, 0);
        var target = new TimeOnly(11, 14);

        var result = DailyImagePostHostedService.ShouldRunNow(target, now, lastRunDate: null);

        Assert.True(result);
    }
}
