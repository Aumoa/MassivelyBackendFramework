using Scripting.DTO;

namespace Auth.Exceptions;

internal class ResponseCodeException(ResponseCode code) : Exception($"Service thrown response code {code}.")
{
    public readonly ResponseCode Code = code;
}
