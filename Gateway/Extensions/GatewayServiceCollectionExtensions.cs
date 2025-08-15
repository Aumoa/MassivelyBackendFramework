using Gateway.Controllers;
using Gateway.Options;
using Gateway.Services;
using Master.Hosts;
using Master.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Gateway.Extensions;

public static class GatewayServiceCollectionExtensions
{
    public static void AddGatewayService(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<IdentifierOptions>(configuration.GetSection("Identifier"));
        services.AddSingleton<GatewayIdentifier>();
        services.AddSingleton<MasterConnection<GatewayIdentifier>>();
        services.AddHostedService<MasterConnector<GatewayIdentifier>>();

        services.AddScoped<GatewayController>();
    }
}
