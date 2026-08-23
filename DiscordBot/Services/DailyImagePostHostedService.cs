using Microsoft.Extensions.Hosting;

namespace DiscordBot.Services;

internal sealed class DailyImagePostHostedService(
    IDailyImagePostSettingsService settings,
    DailyImagePostRunner runner,
    ILogger<DailyImagePostHostedService> logger) : BackgroundService
{
    private static readonly TimeOnly DefaultTargetTime = new(9, 0);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 설정을 시작할 때 한 번만 읽어 긴 Task.Delay로 잠들면, 그 사이 관리자 페이지에서
        // 게시 시각을 바꿔도 이미 시작된 대기가 끝날 때까지(최악의 경우 재시작 전까지) 반영되지 않는다.
        // 그래서 짧은 주기로 깨어나 매번 최신 설정을 다시 읽는 방식으로 바꾼다.
        //
        // 앱 시작/재시작 시점에 이미 오늘 목표 시각이 지나 있었다면 캐치업 없이 다음날까지 기다리게
        // 하는 방안도 고려했지만, 코드를 반영하려고 앱을 재시작하는 타이밍에 따라 "방금 설정한
        // 시각"조차 캐치업 방지 대상으로 잡혀버리는 문제가 반복적으로 발생해 제거했다. 규칙은 단순하게
        // "활성화 + 목표 시각 도달 + 오늘 아직 실행 안 함"이면 무조건 실행한다 - 재시작 타이밍과 무관하다.
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
    // 오늘 아직 실행하지 않았고 목표 시각에 도달했으면 실행해야 한다.
    internal static bool ShouldRunNow(TimeOnly targetTime, DateTime now, DateOnly? lastRunDate)
    {
        return lastRunDate != DateOnly.FromDateTime(now) && TimeOnly.FromDateTime(now) >= targetTime;
    }
}
