using System.IdentityModel.Tokens.Jwt;
using Auth.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Scripting.DTO;

namespace Auth.Controllers;

internal class AuthController
{
    public static async ValueTask<IResult> ContainsAsync(
        [FromServices] IAccounts accounts,
        [FromQuery] string id,
        CancellationToken cancellationToken
        )
    {
        bool contains = await accounts.ContainsAsync(id, cancellationToken);
        return contains ? Results.Ok() : Results.NotFound();
    }

    public static async ValueTask<IResult> RegisterAsync(
        [FromServices] IAccounts accounts,
        [FromRoute] string id,
        [FromQuery] string password,
        [FromQuery] string email,
        CancellationToken cancellationToken
        )
    {
        var responseCode = await accounts.RegisterAsync(id, password, email, cancellationToken);
        switch (responseCode)
        {
            case ResponseCode.Success:
                return Results.Ok();
            case ResponseCode.AccountEmailDuplicated:
            case ResponseCode.AccountAlreadyRegistered:
                return Results.Conflict(responseCode);
            default:
                return Results.BadRequest(responseCode);
        }
    }

    public static async ValueTask<IResult> LoginAsync(
        [FromServices] IAccounts accounts,
        [FromServices] IAccesses accesses,
        HttpContext context,
        [FromRoute] string provider,
        [FromQuery] string code,
        CancellationToken cancellationToken
        )
    {
        var jwtToken = await accesses.GetAccessAsync(provider, code, cancellationToken);
        if (jwtToken == null)
        {
            return Results.Unauthorized();
        }

        context.Response.Cookies.Append("jwt_token", jwtToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict
        });

        return Results.Ok();
    }

    public static async ValueTask LogoutAsync(
        [FromServices] IAccesses accesses,
        HttpContext context,
        CancellationToken cancellationToken
        )
    {
        var jwt_token = context.Request.Cookies["jwt_token"];
        if (jwt_token != null)
        {
            var handler = new JwtSecurityTokenHandler();
            var token = handler.ReadJwtToken(jwt_token);
            var accessToken = token.Claims.FirstOrDefault(c => c.Type == "access_token");
            if (accessToken?.Value != null)
            {
                await accesses.LogoutAsync(accessToken.Value, cancellationToken);
            }
        }

        context.Response.Cookies.Append("jwt_token", string.Empty, new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.AddDays(-1),
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict
        });
    }

    public static IResult Status()
    {
        return Results.Ok();
    }
}
