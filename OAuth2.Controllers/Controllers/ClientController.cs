using Microsoft.AspNetCore.Mvc;
using OAuth2.DTO;
using OAuth2.Services;

namespace OAuth2.Controllers;

[ApiController]
[Route("api/v1/client")]
internal class ClientController(IClients clients, IAccesses accesses) : ControllerBase
{
    [HttpPost]
    public async ValueTask<IActionResult> PostAsync([FromForm] CreateClientRequest request, CancellationToken cancellationToken)
    {
        var token = Request.Headers.Authorization.ToString();
        if (token.StartsWith("Bearer "))
        {
            token = token["Bearer ".Length..];
        }

        if (string.IsNullOrEmpty(token))
        {
            return Unauthorized("Authorization header is not included.");
        }

        var ownerId = await accesses.VerifyAsync(token, cancellationToken);
        if (ownerId == null)
        {
            return Unauthorized("access_token is expired.");
        }

        string clientId = await clients.AddClientAsync(request.Name, ownerId, [], cancellationToken);
        return Ok(new CreateClientResponse
        {
            Id = clientId
        });
    }
}
