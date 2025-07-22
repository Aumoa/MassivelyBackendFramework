using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Auth.Controllers;

internal class AuthController : Controller
{
    public IResult Status()
    {
        return Results.Json(new
        {
            status = "ok"
        });
    }
}
