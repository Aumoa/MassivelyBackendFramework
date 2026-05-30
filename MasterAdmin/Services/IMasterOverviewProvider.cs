using MasterServer.ControlPlane;

namespace MasterAdmin.Services;

public interface IMasterOverviewProvider
{
    event Action<MasterOverviewState>? StateChanged;

    MasterOverviewState GetState();

    Task<ServiceAdminStatusResponse> RequestServiceAdminStatusAsync(
        string targetConnectionId,
        CancellationToken cancellationToken = default);
}
