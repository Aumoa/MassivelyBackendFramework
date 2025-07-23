using Master.Controllers;
using Master.Options;
using Master.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Master.Extensions;

public static class MasterServiceCollectionExtensions
{
    public static void AddMasterService(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SlaveIdentifiersOptions>(configuration.GetSection("SlaveIdentifiers"));
        services.AddSingleton<ISessionService, InMemorySessionService>();
        services.AddTransient<MasterController>();
    }
}
