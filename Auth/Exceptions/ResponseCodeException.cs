using Microsoft.AspNetCore.Http;
using Scripting.DTO;

namespace Auth.Exceptions;

internal class ResponseCodeException(ResponseCode code) : Exception($"Service thrown response code {code}.")
{
    public readonly ResponseCode Code = code;

    public IResult MakeResult()
    {
        return Code switch
        {
            ResponseCode.InvalidClientId => Results.BadRequest("Invalid client ID."),
            ResponseCode.DuplicateClientId => Results.Conflict("Duplicate client ID."),
            ResponseCode.ClientNotFound => Results.NotFound("Client not found."),
            ResponseCode.AccountAlreadyRegistered => Results.Conflict("Account already registered."),
            ResponseCode.AccountOrPasswordInvalid => Results.BadRequest("Account or password invalid."),
            ResponseCode.AccountEmailDuplicated => Results.BadRequest("Account email duplicated."),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
        };
    }
}
