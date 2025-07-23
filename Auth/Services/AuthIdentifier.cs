using Auth.Options;
using Master.Services;
using Microsoft.Extensions.Options;

namespace Auth.Services;

internal class AuthIdentifier(IOptions<IdentifierOptions> options) : ISlaveIdentifier
{
    public string MasterUrl => options.Value.MasterUrl;

    public string SlaveId => options.Value.SlaveId;
}
