using System.Collections.Generic;
using MasterServer.ControlPlane;

namespace MasterServer.Services;

public interface IConnectionManager
{
    MasterSocketEndpoint GetSocketEndpoint();

    IReadOnlyCollection<MasterConnectionSnapshot> GetConnectionSnapshots();
}
