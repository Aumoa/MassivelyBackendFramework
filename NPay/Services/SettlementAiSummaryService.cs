using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using AI;
using AI.Providers.Claude;
using Microsoft.Extensions.Options;
using NPay.Models;
using NPay.Options;

namespace NPay.Services;

public sealed class SettlementAiSummaryService(
    IServiceProvider services,
    ISettlementService settlementService,
    IOptions<SettlementAiSummaryOptions> options,
    IOptions<ClaudeChatClientOptions> claudeOptions,
    ILogger<SettlementAiSummaryService> logger) : ISettlementAiSummaryService
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> SummaryLocks = new();
    private const int SummaryMaxLength = 180;

    public async Task<string> GetOrGenerateSummaryAsync(Guid settlementId, CancellationToken cancellationToken = default)
    {
        var settlement = await settlementService.GetSettlementAsync(settlementId, cancellationToken);
        if (settlement is null)
            return string.Empty;

        if (TryGetStoredSummary(settlement, out var storedSummary))
            return storedSummary;

        var summaryLock = SummaryLocks.GetOrAdd(settlementId, _ => new SemaphoreSlim(1, 1));
        await summaryLock.WaitAsync(cancellationToken);
        try
        {
            while (true)
            {
                settlement = await settlementService.GetSettlementAsync(settlementId, cancellationToken);
                if (settlement is null)
                    return string.Empty;

                if (TryGetStoredSummary(settlement, out storedSummary))
                    return storedSummary;

                var expectedRevision = settlement.AiSummaryRevision;
                var summary = await GenerateSummaryTextAsync(settlement, cancellationToken);
                if (await settlementService.SaveAiSummaryAsync(settlementId, summary, expectedRevision, cancellationToken))
                    return summary;
            }
        }
        finally
        {
            summaryLock.Release();
        }
    }

    private static bool TryGetStoredSummary(Settlement settlement, out string summary)
    {
        if (!settlement.AiSummaryDirty && !string.IsNullOrWhiteSpace(settlement.AiSummary))
        {
            summary = settlement.AiSummary;
            return true;
        }

        summary = string.Empty;
        return false;
    }

    private async Task<string> GenerateSummaryTextAsync(Settlement settlement, CancellationToken cancellationToken)
    {
        var fallback = CreateFallbackSummary(settlement);
        var summaryOptions = options.Value;
        var chatClient = services.GetService<IChatClient>();

        if (!summaryOptions.Enabled
            || string.IsNullOrWhiteSpace(summaryOptions.Model)
            || string.IsNullOrWhiteSpace(claudeOptions.Value.ApiKey)
            || chatClient is null)
        {
            return fallback;
        }

        try
        {
            var result = await chatClient.GenerateAsync(
                CreatePrompt(settlement),
                new ChatCompletionOptions
                {
                    Model = summaryOptions.Model,
                    Temperature = summaryOptions.Temperature,
                    MaxTokens = summaryOptions.MaxTokens
                },
                system: """
                    너는 정산 앱의 결과 화면에 들어갈 짧은 한국어 요약을 작성한다.
                    전체 분위기와 규모만 1~2문장으로 가볍게 정리한다.
                    180자 이내로 쓰고, 마크다운/목록/제목/따옴표는 쓰지 않는다.
                    없는 사실을 만들지 말고, 송금 지시나 재무 조언은 하지 않는다.
                    """,
                cancellationToken);

            return NormalizeSummary(result, fallback);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to generate NPay settlement AI summary.");
            return fallback;
        }
    }

    private static string CreatePrompt(Settlement settlement)
    {
        var participants = settlement.Participants.ToDictionary(p => p.Id, p => p.Name);
        var paidAmounts = settlement.Participants.ToDictionary(p => p.Id, _ => 0m);
        var shareAmounts = settlement.Participants.ToDictionary(p => p.Id, _ => 0m);
        var allParticipantIds = settlement.Participants.Select(p => p.Id).ToList();

        foreach (var expense in settlement.Expenses)
        {
            if (paidAmounts.ContainsKey(expense.PaidByParticipantId))
                paidAmounts[expense.PaidByParticipantId] += expense.Amount;

            var splitIds = expense.SplitAmongParticipantIds.Count > 0
                ? expense.SplitAmongParticipantIds
                : allParticipantIds;
            var validSplitIds = splitIds.Where(shareAmounts.ContainsKey).Distinct().ToList();
            if (validSplitIds.Count == 0) continue;

            var share = expense.Amount / validSplitIds.Count;
            foreach (var participantId in validSplitIds)
                shareAmounts[participantId] += share;
        }

        var prompt = new StringBuilder();
        prompt.AppendLine(CultureInfo.InvariantCulture, $"제목: {settlement.Title}");
        if (!string.IsNullOrWhiteSpace(settlement.Description))
            prompt.AppendLine(CultureInfo.InvariantCulture, $"설명: {settlement.Description}");
        prompt.AppendLine(CultureInfo.InvariantCulture, $"상태: {settlement.Status}");
        prompt.AppendLine(CultureInfo.InvariantCulture, $"참여자: {string.Join(", ", settlement.Participants.Select(p => p.Name))}");
        prompt.AppendLine(CultureInfo.InvariantCulture, $"지출 건수: {settlement.Expenses.Count}");
        prompt.AppendLine(CultureInfo.InvariantCulture, $"총 지출액: {FormatWon(settlement.Expenses.Sum(e => e.Amount))}");

        if (settlement.Expenses.Count > 0)
        {
            prompt.AppendLine("지출 내역:");
            foreach (var expense in settlement.Expenses.OrderByDescending(e => e.Amount).Take(12))
            {
                var splitNames = expense.SplitAmongParticipantIds.Count > 0
                    ? string.Join(", ", expense.SplitAmongParticipantIds.Select(id => participants.GetValueOrDefault(id, "?")))
                    : "전체";
                prompt.AppendLine(CultureInfo.InvariantCulture,
                    $"- {expense.Label}: {FormatWon(expense.Amount)}, 결제자 {participants.GetValueOrDefault(expense.PaidByParticipantId, "?")}, 정산 인원 {splitNames}");
            }
        }

        prompt.AppendLine("인원별 실제 결제액:");
        foreach (var participant in settlement.Participants)
            prompt.AppendLine(CultureInfo.InvariantCulture, $"- {participant.Name}: {FormatWon(paidAmounts.GetValueOrDefault(participant.Id))}");

        prompt.AppendLine("인원별 최종 부담액:");
        foreach (var participant in settlement.Participants)
            prompt.AppendLine(CultureInfo.InvariantCulture, $"- {participant.Name}: {FormatWon(Math.Round(shareAmounts.GetValueOrDefault(participant.Id), 0))}");

        return prompt.ToString();
    }

    private static string CreateFallbackSummary(Settlement settlement)
    {
        if (settlement.Expenses.Count == 0)
        {
            return settlement.Participants.Count == 0
                ? "아직 참여자와 지출이 없어 요약할 정산 내용이 없습니다."
                : $"{settlement.Participants.Count}명이 참여 중이며, 아직 등록된 지출은 없습니다.";
        }

        var total = settlement.Expenses.Sum(e => e.Amount);
        var largestExpense = settlement.Expenses.MaxBy(e => e.Amount);
        var largestText = largestExpense is null
            ? string.Empty
            : $" 가장 큰 항목은 {largestExpense.Label} {FormatWon(largestExpense.Amount)}입니다.";

        return $"{settlement.Participants.Count}명이 {settlement.Expenses.Count}건의 지출, 총 {FormatWon(total)}을 정산합니다.{largestText}";
    }

    private static string NormalizeSummary(string value, string fallback)
    {
        var summary = string.Join(" ", value
            .ReplaceLineEndings(" ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Trim()
            .Trim('"', '\'', '“', '”');

        if (string.IsNullOrWhiteSpace(summary))
            return fallback;

        return summary.Length <= SummaryMaxLength
            ? summary
            : summary[..SummaryMaxLength].TrimEnd() + "...";
    }

    private static string FormatWon(decimal amount) =>
        string.Create(CultureInfo.InvariantCulture, $"{Math.Round(amount, 0):N0}원");
}
