using Master.DTO;
using Master.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Scripting.DTO;

namespace Master.Hubs;

internal class MasterHub(ISessionService sessions, ILogger<MasterHub> logger) : Hub<IMasterHubClient>
{
    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
        logger.LogInformation("Gateway session {ConnectionId} connected.", Context.ConnectionId);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await base.OnDisconnectedAsync(exception);
        if (exception == null)
        {
            logger.LogInformation("Gateway session {ConnectionId} disconnected.", Context.ConnectionId);
        }
        else
        {
            logger.LogInformation("Gateway session {ConnectionId} disconnected with {ExceptionName}: {Message}", Context.ConnectionId, exception.GetType().Name, exception.Message);
        }
    }

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
