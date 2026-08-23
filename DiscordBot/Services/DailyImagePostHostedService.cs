using Microsoft.Extensions.Hosting;

namespace DiscordBot.Services;

internal sealed class DailyImagePostHostedService(
    IDailyImagePostSettingsService settings,
    DailyImagePostRunner runner,
    ILogger<DailyImagePostHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var currentSettings = await settings.GetAsync(stoppingToken);
            var targetTime = TimeOnly.TryParseExact(currentSettings.PostTimeOfDay, "HH:mm", out var parsed)
                ? parsed
                : new TimeOnly(9, 0);
            var delay = ComputeNextRunDelay(targetTime, DateTime.Now);

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                await runner.RunOnceAsync(force: false, stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogError(e, "Daily image post run failed.");
            }
        }
    }

    // 순수 함수 - now를 파라미터로 받아 결정론적으로 테스트 가능
    internal static TimeSpan ComputeNextRunDelay(TimeOnly targetTime, DateTime now)
    {
        var todayAt = now.Date + targetTime.ToTimeSpan();
        var next = todayAt > now ? todayAt : todayAt.AddDays(1);
        return next - now;
    }
}
