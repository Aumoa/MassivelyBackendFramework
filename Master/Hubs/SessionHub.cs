using Master.DTO;
using Master.Services;
using Microsoft.AspNetCore.SignalR;
using Scripting.DTO;

namespace Master.Hubs;

internal class SessionHub(ISessionService sessions) : Hub<ISessionHubClient>
{
    public async ValueTask<SessionRegisterResponse> RegisterSession(SessionRegisterRequest request)
    {
        if (string.IsNullOrEmpty(request.ClientId))
        {
            return new SessionRegisterResponse
            {
                Code = ResponseCode.InvalidClientId
            };
        }

        bool success = await sessions.AddClientAsync(Context.ConnectionId, request.ClientId);
        if (success == false)
        {
            return new SessionRegisterResponse
            {
                Code = ResponseCode.DuplicateClientId
            };
        }

        return new SessionRegisterResponse
        {
            Code = ResponseCode.Success
        };
    }

    public async ValueTask<SessionUnregisterResponse> UnregisterSession(SessionUnregisterRequest request)
    {
        if (string.IsNullOrEmpty(request.ClientId))
        {
            return new SessionUnregisterResponse
            {
                Code = ResponseCode.InvalidClientId
            };
        }

        bool success = await sessions.RemoveClientAsync(Context.ConnectionId, request.ClientId);
        if (success == false)
        {
            return new SessionUnregisterResponse
            {
                Code = ResponseCode.ClientNotFound
            };
        }

        return new SessionUnregisterResponse
        {
            Code = ResponseCode.Success
        };
    }
}
