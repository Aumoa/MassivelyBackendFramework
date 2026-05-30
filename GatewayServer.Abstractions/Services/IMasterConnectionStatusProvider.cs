using System;
using GatewayServer.ControlPlane;

namespace GatewayServer.Services;

public interface IMasterConnectionStatusProvider
{
    MasterConnectionStatus GetStatus();

    event Action<MasterConnectionStatus>? StatusChanged;
}
