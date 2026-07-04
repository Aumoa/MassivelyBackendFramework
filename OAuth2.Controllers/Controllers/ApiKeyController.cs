using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OAuth2.DTO;
using OAuth2.Options;
using OAuth2.Services;

namespace OAuth2.Controllers;

[ApiController]
[Route("api/v1/apikey")]
public class ApiKeyController(IApiKeys apiKeys, IAccesses accesses, IClients clients, IClientClaims clientClaims, IOptions<HostOptions> hostOptions) : AuthorizedControllerBase(accesses)
{
    [HttpPost]
    public async ValueTask<IActionResult> PostAsync([FromForm] CreateApiKeyRequest request, CancellationToken cancellationToken)
    {
        return await VerifiedAsync(async access =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest(new { error = "invalid_request", error_description = "name is required" });
            }

            if (string.IsNullOrWhiteSpace(request.AllowedClientId))
            {
                return BadRequest(new { error = "invalid_request", error_description = "allowed_client_id is required" });
            }

            if (request.AllowedClientId == hostOptions.Value.ClientId)
            {
                return BadRequest(new { error = "invalid_request", error_description = "api_key cannot target the internal OAuth2 client" });
            }

            var client = await clients.GetClientAsync(request.AllowedClientId, cancellationToken);
            if (!client.HasValue)
            {
                return BadRequest(new { error = "invalid_client", error_description = "allowed_client_id is unknown" });
            }

            var allowedScope = request.AllowedScope;
            if (string.IsNullOrWhiteSpace(allowedScope))
            {
                return BadRequest(new { error = "invalid_request", error_description = "allowed_scope is required" });
            }

            if (!ScopePolicy.TryNormalize(allowedScope, false, out var normalizedScope, out _))
            {
                return BadRequest(new { error = "invalid_scope" });
            }

            var claims = await clientClaims.GetClaimsAsync(request.AllowedClientId, cancellationToken);
            var allowedScopes = claims.Where(c => c.Name == "scope").Select(c => c.Value);
            if (!ScopePolicy.IsAllowedByClient(normalizedScope, allowedScopes))
            {
                return BadRequest(new { error = "invalid_scope", error_description = "allowed_scope exceeds the target client's allowed scopes" });
            }

            var apiKey = await apiKeys.CreateApiKeyAsync(access.Id, request.AllowedClientId, normalizedScope, request.Name, cancellationToken);
            return Ok(new CreateApiKeyResponse { ApiKey = apiKey });
        }, null, cancellationToken, hostOptions.Value.ClientId);
    }

    [HttpGet]
    public async ValueTask<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        return await VerifiedAsync(async access =>
        {
            var keys = await apiKeys.GetApiKeysAsync(access.Id, cancellationToken);
            return Ok(keys);
        }, null, cancellationToken, hostOptions.Value.ClientId);
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

            if (apiKeyInfo.Value.AccountId != access.Id)
            {
                return Forbid();
            }

            await apiKeys.RemoveApiKeyAsync(id, cancellationToken);
            return NoContent();
        }, null, cancellationToken, hostOptions.Value.ClientId);
    }
}
