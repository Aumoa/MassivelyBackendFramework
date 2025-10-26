using Microsoft.AspNetCore.Mvc;
using OAuth2.DTO;
using OAuth2.Services;

namespace OAuth2.Controllers;

[ApiController]
[Route("api/v1/client")]
public class ClientController(IClients clients, IAccesses accesses) : AuthorizedControllerBase(accesses)
{
    [HttpPost]
    public async ValueTask<IActionResult> PostAsync([FromForm] CreateClientRequest request, CancellationToken cancellationToken)
    {
        return await VerifiedAsync(async ownerId =>
        {
            string clientId = await clients.AddClientAsync(request.Name, ownerId, [], cancellationToken);
            return Ok(new CreateClientResponse
            {
                Id = clientId
            });
        }, null, cancellationToken);
    }
}
