namespace MasterAdmin.Services;

public interface IMasterOverviewProvider
{
    event Action<MasterOverviewState>? StateChanged;

    MasterOverviewState GetState();
}
