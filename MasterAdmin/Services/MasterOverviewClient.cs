using System.Net.Http.Json;
using MasterServer.ControlPlane;

namespace MasterAdmin.Services;

public sealed class MasterOverviewClient(HttpClient http, ILogger<MasterOverviewClient> logger)
{
    public async ValueTask<MasterOverviewSnapshot?> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await http.GetFromJsonAsync<MasterOverviewSnapshot>("api/master/overview", cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Failed to load Master overview from {BaseAddress}.", http.BaseAddress);
            return null;
        }
    }
}
