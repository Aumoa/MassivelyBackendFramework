using Microsoft.AspNetCore.SignalR;

namespace Gateway.Hubs;

internal class GatewayHub : Hub<IGatewayHubClient>
{
}
