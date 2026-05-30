using MasterServer.ControlPlane;
using MasterServer.Services;
using Microsoft.AspNetCore.Mvc;

namespace MasterServer.Controllers;

[ApiController]
[Route("api/master")]
public sealed class MasterStatusController(IConnectionManager connectionManager) : ControllerBase
{
    [HttpGet("overview")]
    public MasterOverviewSnapshot GetOverview()
    {
        return new MasterOverviewSnapshot(
            connectionManager.GetSocketEndpoint(),
            [.. connectionManager.GetConnectionSnapshots()],
            DateTimeOffset.UtcNow);
    }
}
