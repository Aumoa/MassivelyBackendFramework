using Microsoft.AspNetCore.Mvc;
using OAuth2.DTO;
using OAuth2.Services;

namespace OAuth2.Controllers;

[ApiController]
[Route("api/v1/apikey")]
public class ApiKeyController(IApiKeys apiKeys, IClients clients, IAccesses accesses) : AuthorizedControllerBase(accesses)
{
    [HttpPost]
    public async ValueTask<IActionResult> PostAsync([FromForm] CreateApiKeyRequest request, CancellationToken cancellationToken)
    {
        return await VerifiedAsync(async access =>
        {
            var client = await clients.GetClientAsync(request.ClientId, cancellationToken);
            if (!client.HasValue)
            {
                return NotFound(new { error = "client_not_found" });
            }

            if (client.Value.OwnerId != access.Id)
            {
                return Forbid();
            }

            var apiKey = await apiKeys.CreateApiKeyAsync(request.ClientId, request.Name, cancellationToken);
            return Ok(new CreateApiKeyResponse { ApiKey = apiKey });
        }, null, cancellationToken);
    }

    [HttpGet("{clientId}")]
    public async ValueTask<IActionResult> GetAsync(string clientId, CancellationToken cancellationToken)
    {
        return await VerifiedAsync(async access =>
        {
            var client = await clients.GetClientAsync(clientId, cancellationToken);
            if (!client.HasValue)
            {
                return NotFound(new { error = "client_not_found" });
            }

            if (client.Value.OwnerId != access.Id)
            {
                return Forbid();
            }

            var keys = await apiKeys.GetApiKeysAsync(clientId, cancellationToken);
            return Ok(keys);
        }, null, cancellationToken);
    }

    [HttpDelete("{id:long}")]
    public async ValueTask<IActionResult> DeleteAsync(long id, CancellationToken cancellationToken)
    {
        return await VerifiedAsync(async access =>
        {
            var apiKeyInfo = await apiKeys.GetApiKeyAsync(id, cancellationToken);
            if (!apiKeyInfo.HasValue)
            {
                return NotFound(new { error = "api_key_not_found" });
            }

            var client = await clients.GetClientAsync(apiKeyInfo.Value.ClientId, cancellationToken);
            if (!client.HasValue || client.Value.OwnerId != access.Id)
            {
                return Forbid();
            }

            await apiKeys.RemoveApiKeyAsync(id, cancellationToken);
            return NoContent();
        }, null, cancellationToken);
    }
}
