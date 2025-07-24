using Microsoft.AspNetCore.SignalR.Client;

namespace Master.Services;

public interface ISlaveIdentifier
{
    string MasterUrl { get; }

    string SlaveId { get; }

    void RegisterHandlers(HubConnection connection);
}
