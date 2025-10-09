using Microsoft.AspNetCore.Http;

namespace Gateway.Controllers;

internal static class GatewayController
{
    public static IResult Status()
    {
        return Results.Json(new
        {
            status = "ok"
        });
    }
}
