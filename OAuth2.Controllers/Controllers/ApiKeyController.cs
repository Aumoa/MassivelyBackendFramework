using Microsoft.AspNetCore.Mvc;
using OAuth2.DTO;
using OAuth2.Services;

namespace OAuth2.Controllers;

[ApiController]
[Route("api/v1/apikey")]
public class ApiKeyController(IApiKeys apiKeys, IAccesses accesses) : AuthorizedControllerBase(accesses)
{
    [HttpPost]
    public async ValueTask<IActionResult> PostAsync([FromForm] CreateApiKeyRequest request, CancellationToken cancellationToken)
    {
        return await VerifiedAsync(async access =>
        {
            var allowedScope = request.AllowedScope;
            if (!string.IsNullOrWhiteSpace(allowedScope))
            {
                if (!ScopePolicy.TryNormalize(allowedScope, true, out var normalizedScope, out _))
                {
                    return BadRequest(new { error = "invalid_scope" });
                }

                allowedScope = normalizedScope;
            }

            var apiKey = await apiKeys.CreateApiKeyAsync(access.Id, request.AllowedClientId, allowedScope, request.Name, cancellationToken);
            return Ok(new CreateApiKeyResponse { ApiKey = apiKey });
        }, null, cancellationToken);
    }

    [HttpGet]
    public async ValueTask<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        return await VerifiedAsync(async access =>
        {
            var keys = await apiKeys.GetApiKeysAsync(access.Id, cancellationToken);
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

            if (apiKeyInfo.Value.AccountId != access.Id)
            {
                return Forbid();
            }

            await apiKeys.RemoveApiKeyAsync(id, cancellationToken);
            return NoContent();
        }, null, cancellationToken);
    }
}
