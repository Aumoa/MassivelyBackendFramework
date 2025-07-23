using Gateway.Options;
using Master.Services;
using Microsoft.Extensions.Options;

namespace Gateway.Services;

internal class GatewayIdentifier(IOptions<IdentifierOptions> options) : ISlaveIdentifier
{
    public string MasterUrl => options.Value.MasterUrl;

    public string SlaveId => options.Value.SlaveId;
}
