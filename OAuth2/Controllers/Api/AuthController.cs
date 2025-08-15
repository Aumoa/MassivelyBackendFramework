using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using OAuth2.DTO;
using OAuth2.Services;

namespace OAuth2.Controllers.Api;

[ApiController]
[Route("api/v1/oauth2")]
public class AuthController(IAccounts Accounts, IAccesses Accesses, JwtTokenGenerator Jwt) : ControllerBase
{
    public record SimpleResponse
    {
        [JsonPropertyName("code")]
        public required ResponseCode Code { get; set; }

        [JsonPropertyName("message")]
        public required string Message { get; set; }
    }

    [HttpGet("accounts")]
    public async ValueTask<IActionResult> ContainsAsync([FromQuery] string id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(id))
        {
            return BadRequest();
        }

        if (await Accounts.ContainsAsync(id, cancellationToken))
        {
            return Ok(new SimpleResponse
            {
                Code = ResponseCode.Success,
                Message = "Account exists."
            });
        }
        else
        {
            return NotFound(new SimpleResponse
            {
                Code = ResponseCode.AccountNotFound,
                Message = "Account not found."
            });
        }
    }

    [HttpPost("accounts")]
    public async ValueTask<IActionResult> LoginAsync([FromQuery] string id, [FromQuery] string password, [FromQuery] string scope, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(password))
        {
            return BadRequest();
        }

        bool accept = await Accounts.AcceptAsync(id, password, cancellationToken);
        if (accept)
        {
            var accessToken = await Accesses.GetAccessAsync(id, scope, TimeSpan.FromHours(1), cancellationToken);
            var roles = await Accesses.QueryRolesAsync(accessToken, cancellationToken);
            var jwtToken = Jwt.Generate(id, roles, accessToken);
            Response.Cookies.Append("jwt_token", jwtToken);

            return Ok(new SimpleResponse
            {
                Code = ResponseCode.Success,
                Message = "Login successful."
            });
        }
        else
        {
            return Unauthorized(new SimpleResponse
            {
                Code = ResponseCode.AccountNotFound,
                Message = "Invalid account or password."
            });
        }
    }
}
