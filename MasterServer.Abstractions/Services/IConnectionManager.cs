using System;
using System.Collections.Generic;
using MasterServer.ControlPlane;

namespace MasterServer.Services;

public interface IConnectionManager
{
    event Action? ConnectionsChanged;

    MasterSocketEndpoint GetSocketEndpoint();

    IReadOnlyCollection<MasterConnectionSnapshot> GetConnectionSnapshots();
}
