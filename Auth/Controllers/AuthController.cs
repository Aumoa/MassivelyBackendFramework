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

    public static IResult Status()
    {
        return Results.Ok();
    }
}
