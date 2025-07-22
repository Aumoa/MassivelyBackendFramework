using Gateway.Hosts;
using Gateway.Options;
using Gateway.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Gateway.Extensions;

public static class GatewayServiceCollectionExtensions
{
    public static void AddGatewayService(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MasterConnectionOptions>(configuration.GetSection(nameof(MasterConnection)));
        services.AddSingleton<MasterConnection>();
        services.AddHostedService<MasterConnector>();
    }
}
