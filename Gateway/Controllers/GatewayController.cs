using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Gateway.Controllers;

internal class GatewayController : Controller
{
    public IResult Status()
    {
        return Results.Json(new
        {
            status = "ok"
        });
    }
}
