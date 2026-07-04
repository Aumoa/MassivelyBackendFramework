using Microsoft.AspNetCore.Mvc;
using OAuth2.DTO;
using OAuth2.Services;
using HostOptions = OAuth2.Options.HostOptions;

namespace OAuth2.Controllers;

[ApiController]
[Route("api/v1/client")]
public class ClientController(IClients clients, IAccesses accesses, Microsoft.Extensions.Options.IOptions<HostOptions> hostOptions) : AuthorizedControllerBase(accesses)
{
    [HttpPost]
    public async ValueTask<IActionResult> PostAsync([FromForm] CreateClientRequest request, CancellationToken cancellationToken)
    {
        return await VerifiedAsync(async access =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest(new { error = "invalid_request", error_description = "name is required" });
            }

            try
            {
                string clientId;
                if (string.IsNullOrWhiteSpace(request.ClientId))
                {
                    clientId = await clients.AddClientAsync(request.Name, access.Id, [], cancellationToken);
                }
                else
                {
                    if (!ClientIdPolicy.TryNormalize(request.ClientId, out var normalizedClientId, out var validationError))
                    {
                        return BadRequest(new { error = "invalid_request", error_description = GetClientIdValidationMessage(validationError) });
                    }

                    clientId = await clients.AddClientAsync(normalizedClientId, request.Name, access.Id, [], cancellationToken);
                }

                return Ok(new CreateClientResponse
                {
                    Id = clientId
                });
            }
            catch (ClientIdAlreadyExistsException)
            {
                return Conflict(new { error = "client_id_already_exists", error_description = "client_id already exists" });
            }
        }, null, cancellationToken, hostOptions.Value.ClientId);
    }

    private static string GetClientIdValidationMessage(ClientIdValidationError error)
    {
        return error switch
        {
            ClientIdValidationError.Required => "client_id is required",
            ClientIdValidationError.TooLong => $"client_id must be {ClientIdPolicy.MaxLength} characters or fewer",
            ClientIdValidationError.InvalidCharacter => "client_id can only contain letters, numbers, '.', '_' and '-'",
            _ => "client_id is invalid"
        };
    }
}
