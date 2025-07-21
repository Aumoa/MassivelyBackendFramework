using Master.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Master.Extensions;

public static class MasterServiceCollectionExtensions
{
    public static void AddMasterService(this IServiceCollection services)
    {
        services.AddSingleton<ISessionService, InMemorySessionService>();
    }
}
