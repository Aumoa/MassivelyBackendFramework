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
    public void ShouldRunNow_ReturnsTrue_WhenJustReachedTargetTime_AndNotYetRunToday()
    {
        var now = new DateTime(2026, 8, 23, 10, 48, 5);
        var target = new TimeOnly(10, 48);

        var result = DailyImagePostHostedService.ShouldRunNow(target, now, lastRunDate: null);

        Assert.True(result);
    }

    [Fact]
    public void ShouldRunNow_ReturnsTrue_StillWithinCatchUpWindow()
    {
        // 앱이 계속 떠 있는 상태에서 폴링 주기 안에 목표 시각이 지난 경우 - 몇 분 늦어도 잡아줘야 한다.
        var now = new DateTime(2026, 8, 23, 10, 52, 0);
        var target = new TimeOnly(10, 48);

        var result = DailyImagePostHostedService.ShouldRunNow(target, now, lastRunDate: null);

        Assert.True(result);
    }

    [Fact]
    public void ShouldRunNow_ReturnsFalse_WhenTargetTimePassedLongAgo()
    {
        // 회귀 테스트: 목표 시각이 09:00인데 앱이 14:00에 재시작된 경우, 몇 시간 지난 오늘 몫을
        // 뒤늦게 캐치업해서 쏘면 안 된다.
        var now = new DateTime(2026, 8, 23, 14, 0, 0);
        var target = new TimeOnly(9, 0);

        var result = DailyImagePostHostedService.ShouldRunNow(target, now, lastRunDate: null);

        Assert.False(result);
    }

    [Fact]
    public void ShouldRunNow_ReturnsTrue_WhenTargetTimeChangedWhileAppKeepsRunning_AndReached()
    {
        // 앱을 재시작하지 않고 관리자 페이지에서 목표 시각을 바꾼 경우, 재시작 시점과 무관하게
        // 그 시각이 되면 바로 실행돼야 한다.
        var now = new DateTime(2026, 8, 23, 11, 14, 5);
        var newTarget = new TimeOnly(11, 14);

        var result = DailyImagePostHostedService.ShouldRunNow(newTarget, now, lastRunDate: null);

        Assert.True(result);
    }

    [Fact]
    public void ShouldRunNow_ReturnsFalse_WhenAlreadyRunToday()
    {
        var now = new DateTime(2026, 8, 23, 10, 49, 0);
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
    public void ShouldRunNow_HandlesMidnightWraparound_WithinWindow()
    {
        var now = new DateTime(2026, 8, 24, 0, 2, 0);
        var target = new TimeOnly(23, 58);

        var result = DailyImagePostHostedService.ShouldRunNow(target, now, lastRunDate: null);

        Assert.True(result);
    }

    [Fact]
    public void ShouldRunNow_HandlesMidnightWraparound_OutsideWindow()
    {
        var now = new DateTime(2026, 8, 24, 6, 0, 0);
        var target = new TimeOnly(23, 58);

        var result = DailyImagePostHostedService.ShouldRunNow(target, now, lastRunDate: null);

        Assert.False(result);
    }
}
