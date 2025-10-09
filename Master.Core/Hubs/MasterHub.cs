using Gateway.DTO;
using Master.DTO;
using Master.Options;
using Master.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Scripting.DTO;

namespace Master.Hubs;

internal class MasterHub(ISessionService sessions, ILogger<MasterHub> logger, IOptions<SlaveIdentifiersOptions> slaveIdentifiersOptions) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var httpContext = Context.GetHttpContext();
        if (httpContext == null)
        {
            logger.LogError("Failed to get HTTP context for connection {ConnectionId}. Aborting connection.", Context.ConnectionId);
            Context.Abort();
            return;
        }

        var slaveId = httpContext.Request.Query["slave_id"];
        if (slaveId == slaveIdentifiersOptions.Value.GatewayId)
        {
            await base.OnConnectedAsync();
            logger.LogInformation("Gateway session {ConnectionId} connected.", Context.ConnectionId);
        }
        else if (slaveId == slaveIdentifiersOptions.Value.AuthId)
        {
            await base.OnConnectedAsync();
            logger.LogInformation("Auth session {ConnectionId} connected.", Context.ConnectionId);
        }
        else
        {
            logger.LogError("Connection {ConnectionId} is not a valid slave session. Received slave_id: {ReceivedSlaveId}. Aborting connection.", Context.ConnectionId, slaveId);
            Context.Abort();
        }
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

        bool success = await sessions.AddClientAsync(Context.ConnectionId, request.ClientId, Context.ConnectionAborted);
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

        bool success = await sessions.RemoveClientAsync(Context.ConnectionId, request.ClientId, Context.ConnectionAborted);
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

    public async ValueTask LoginResponse(LoginResponseNotify notify)
    {
        var connectionId = await sessions.FindConnectionIdAsync(notify.ClientId, Context.ConnectionAborted);
        if (string.IsNullOrEmpty(connectionId))
        {
            logger.LogError("Failed to find connection ID for client {ClientId}.", notify.ClientId);
            return;
        }

        var gateway = Clients.Client(connectionId);
        await gateway.SendAsync("LoginResponse", notify);
    }
}
