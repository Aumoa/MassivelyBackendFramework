using Gateway.DTO;
using Gateway.Hubs;
using Gateway.Options;
using Master.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gateway.Services;

internal class GatewayIdentifier(IOptions<IdentifierOptions> options, IHubContext<GatewayHub> gatewayContext, ILogger<GatewayIdentifier> logger) : ISlaveIdentifier
{
    public string MasterUrl => options.Value.MasterUrl;

    public string SlaveId => options.Value.SlaveId;

    public void RegisterHandlers(HubConnection connection)
    {
        connection.On("LoginResponse", (LoginResponseNotify response) =>
        {
            if (string.IsNullOrEmpty(response.ClientId))
            {
                logger.LogError("Received LoginResponseNotify with empty ClientId.");
                return;
            }

            if (GatewayHub.FindConnectionId(response.ClientId) is not { } connectionId)
            {
                logger.LogError("Received LoginResponseNotify for unknown ClientId: {ClientId}", response.ClientId);
                return;
            }

            gatewayContext.Clients.Client(connectionId).SendAsync("LoginResponse", response);
        });
    }
}
