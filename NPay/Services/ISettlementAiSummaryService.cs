using NPay.Models;

namespace NPay.Services;

public interface ISettlementAiSummaryService
{
    Task<string> GetOrGenerateSummaryAsync(Guid settlementId, CancellationToken cancellationToken = default);
}
