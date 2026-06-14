using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UnityRemoteDebug.Authorization;
using UnityRemoteDebug.Contracts;
using UnityRemoteDebug.Services;

namespace UnityRemoteDebug.Controllers;

[ApiController]
[Route("api/clients")]
public sealed class RemoteDebugClientsController(RemoteDebugClientRegistry clientRegistry) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = RemoteDebugAuthorizationPolicies.Management)]
    public async Task<RemoteDebugClientListResponse> ListClients(CancellationToken cancellationToken)
    {
        return await clientRegistry.GetClientsAsync(cancellationToken);
    }
}
