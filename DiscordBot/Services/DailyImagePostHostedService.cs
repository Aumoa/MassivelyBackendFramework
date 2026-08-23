using Microsoft.Extensions.Hosting;

namespace DiscordBot.Services;

internal sealed class DailyImagePostHostedService(
    IDailyImagePostSettingsService settings,
    DailyImagePostRunner runner,
    ILogger<DailyImagePostHostedService> logger) : BackgroundService
{
    private static readonly TimeOnly DefaultTargetTime = new(9, 0);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    // "목표 시각을 이미 지났는가"만으로 판단하면 두 요구사항이 서로 충돌한다:
    // 앱이 계속 떠 있는 상태에서 방금 목표 시각을 바꿨다면 그 즉시(짧은 지연 안에) 반영돼야 하지만,
    // 앱이 재시작됐을 때는(예: 목표 09:00인데 오후에 재시작) 몇 시간 지난 오늘 몫을 뒤늦게
    // 캐치업해서 쏘면 안 된다. 그래서 "목표 시각 이후 유예 시간 안에 들어와 있는가"로 판단한다 -
    // 재시작 타이밍과 무관하게 항상 같은 규칙으로 동작한다.
    private static readonly TimeSpan CatchUpWindow = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 설정을 시작할 때 한 번만 읽어 긴 Task.Delay로 잠들면, 그 사이 관리자 페이지에서
        // 게시 시각을 바꿔도 이미 시작된 대기가 끝날 때까지(최악의 경우 재시작 전까지) 반영되지 않는다.
        // 그래서 짧은 주기로 깨어나 매번 최신 설정을 다시 읽는 방식으로 바꾼다.
        DateOnly? lastRunDate = null;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            var currentSettings = await settings.GetAsync(stoppingToken);
            var targetTime = ParseTargetTime(currentSettings.PostTimeOfDay);
            var now = DateTime.Now;

            if (!ShouldRunNow(targetTime, now, lastRunDate))
            {
                continue;
            }

            lastRunDate = DateOnly.FromDateTime(now);
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

    private static TimeOnly ParseTargetTime(string postTimeOfDay)
    {
        return TimeOnly.TryParseExact(postTimeOfDay, "HH:mm", out var parsed) ? parsed : DefaultTargetTime;
    }

    // 순수 함수 - now를 파라미터로 받아 결정론적으로 테스트 가능.
    // 오늘 아직 실행하지 않았고, 목표 시각으로부터 CatchUpWindow 이내로 지나 있으면 실행해야 한다.
    internal static bool ShouldRunNow(TimeOnly targetTime, DateTime now, DateOnly? lastRunDate)
    {
        if (lastRunDate == DateOnly.FromDateTime(now))
        {
            return false;
        }

        var elapsedSinceTarget = TimeOnly.FromDateTime(now).ToTimeSpan() - targetTime.ToTimeSpan();
        if (elapsedSinceTarget < TimeSpan.Zero)
        {
            elapsedSinceTarget += TimeSpan.FromDays(1);
        }

        return elapsedSinceTarget < CatchUpWindow;
    }
}
