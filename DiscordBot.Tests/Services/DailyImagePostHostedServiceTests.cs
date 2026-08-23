using DiscordBot.Services;

namespace DiscordBot.Tests.Services;

public sealed class DailyImagePostHostedServiceTests
{
    [Fact]
    public void ComputeNextRunDelay_ReturnsDelayUntilTodaysTargetTime_WhenTargetHasNotPassedYet()
    {
        var now = new DateTime(2026, 8, 23, 8, 0, 0);
        var target = new TimeOnly(9, 0);

        var delay = DailyImagePostHostedService.ComputeNextRunDelay(target, now);

        Assert.Equal(TimeSpan.FromHours(1), delay);
    }

    [Fact]
    public void ComputeNextRunDelay_RollsOverToTomorrow_WhenTargetHasAlreadyPassedToday()
    {
        var now = new DateTime(2026, 8, 23, 10, 0, 0);
        var target = new TimeOnly(9, 0);

        var delay = DailyImagePostHostedService.ComputeNextRunDelay(target, now);

        Assert.Equal(TimeSpan.FromHours(23), delay);
    }

    [Fact]
    public void ComputeNextRunDelay_RollsOverToTomorrow_WhenExactlyAtTargetTime()
    {
        var now = new DateTime(2026, 8, 23, 9, 0, 0);
        var target = new TimeOnly(9, 0);

        var delay = DailyImagePostHostedService.ComputeNextRunDelay(target, now);

        Assert.Equal(TimeSpan.FromHours(24), delay);
    }
}
