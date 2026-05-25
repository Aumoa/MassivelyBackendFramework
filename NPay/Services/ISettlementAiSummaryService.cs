using NPay.Models;

namespace NPay.Services;

public interface ISettlementAiSummaryService
{
    Task<string> GenerateSummaryAsync(Settlement settlement, CancellationToken cancellationToken = default);
}
