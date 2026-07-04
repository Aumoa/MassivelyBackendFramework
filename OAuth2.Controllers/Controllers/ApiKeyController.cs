using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OAuth2.DTO;
using OAuth2.Options;
using OAuth2.Services;

namespace OAuth2.Controllers;

[ApiController]
[Route("api/v1/apikey")]
public class ApiKeyController(IApiKeys apiKeys, IApiKeyCreationService apiKeyCreation, IAccesses accesses, IOptions<HostOptions> hostOptions) : AuthorizedControllerBase(accesses)
{
    [HttpPost]
    public async ValueTask<IActionResult> PostAsync([FromForm] CreateApiKeyRequest request, CancellationToken cancellationToken)
    {
        return await VerifiedAsync(async access =>
        {
            var result = await apiKeyCreation.CreateApiKeyAsync(access.Id, request.Name, request.AllowedClientId, request.AllowedScope, cancellationToken);
            return result.IsSuccess
                ? Ok(new CreateApiKeyResponse { ApiKey = result.ApiKey! })
                : ToErrorResult(result.Error);
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

    private IActionResult ToErrorResult(ApiKeyCreationError error)
    {
        return error switch
        {
            ApiKeyCreationError.NameRequired => BadRequest(new { error = "invalid_request", error_description = "name is required" }),
            ApiKeyCreationError.AllowedClientIdRequired => BadRequest(new { error = "invalid_request", error_description = "allowed_client_id is required" }),
            ApiKeyCreationError.InternalClientForbidden => BadRequest(new { error = "invalid_request", error_description = "api_key cannot target the internal OAuth2 client" }),
            ApiKeyCreationError.UnknownClient => BadRequest(new { error = "invalid_client", error_description = "allowed_client_id is unknown" }),
            ApiKeyCreationError.AllowedScopeRequired => BadRequest(new { error = "invalid_request", error_description = "allowed_scope is required" }),
            ApiKeyCreationError.InvalidScope => BadRequest(new { error = "invalid_scope" }),
            ApiKeyCreationError.ScopeExceedsClient => BadRequest(new { error = "invalid_scope", error_description = "allowed_scope exceeds the target client's allowed scopes" }),
            _ => BadRequest(new { error = "invalid_request" })
        };
    }
}
