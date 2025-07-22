using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Master.Controllers;

internal class MasterController : Controller
{
    public IResult Status()
    {
        return Results.Json(new
        {
            status = "ok"
        });
    }
}
