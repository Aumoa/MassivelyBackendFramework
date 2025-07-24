using Gateway.Services;
using Master.DTO;
using Master.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Scripting.DTO;

namespace Gateway.Hubs;

internal class GatewayHub(MasterConnection<GatewayIdentifier> master, ILogger<GatewayHub> logger) : Hub<IGatewayHubClient>
{
    private static readonly object s_Sync = new();
    private static readonly Dictionary<string, string> s_ClientIdToConnectionId = [];
    private static readonly Dictionary<string, string> s_ConnectionIdToClientId = [];

    public static string? FindConnectionId(string clientId)
    {
        lock (s_Sync)
        {
            if (s_ClientIdToConnectionId.TryGetValue(clientId, out string? connectionId))
            {
                return connectionId;
            }
        }

        return null;
    }

    public override async Task OnConnectedAsync()
    {
        var httpContext = Context.GetHttpContext();
        if (httpContext == null)
        {
            logger.LogError("Failed to get HTTP context for connection {ConnectionId}. Aborting connection.", Context.ConnectionId);
            Context.Abort();
            return;
        }

        string? clientId = httpContext.Request.Query["client_id"];
        if (string.IsNullOrEmpty(clientId))
        {
            logger.LogError("Missing client_id in query for connection {ConnectionId}. Aborting connection.", Context.ConnectionId);
            Context.Abort();
            return;
        }

        var response = await master.Connection.InvokeAsync<SessionRegisterResponse>("RegisterSession", new SessionRegisterRequest
        {
            ClientId = clientId
        });
        if (response.Code != ResponseCode.Success)
        {
            logger.LogError("Failed to register session: {Code}", response.Code);
            Context.Abort();
            return;
        }

        lock (s_Sync)
        {
            s_ClientIdToConnectionId[clientId] = Context.ConnectionId;
            s_ConnectionIdToClientId[Context.ConnectionId] = clientId;
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await base.OnDisconnectedAsync(exception);

        string? clientId;
        lock (s_Sync)
        {
            if (s_ConnectionIdToClientId.Remove(Context.ConnectionId, out clientId) == false)
            {
                logger.LogError("Connection {ConnectionId} is not registered. Cannot unregister session.", Context.ConnectionId);
                return;
            }

            s_ClientIdToConnectionId.Remove(clientId);
        }

        var response = await master.Connection.InvokeAsync<SessionUnregisterResponse>("UnregisterSession", new SessionUnregisterRequest
        {
            ClientId = clientId
        });
        if (response.Code != ResponseCode.Success)
        {
            logger.LogError("Failed to unregister session: {Code}", response.Code);
        }
    }
}
