using GatewayServer.Options;
using GatewayServer.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GatewayServer.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGatewayServer(this IServiceCollection s, IConfiguration config)
    {
        s.Configure<MySqlOptions>(config.GetRequiredSection("MySql"));
        s.Configure<ConnectionManagerOptions>(config.GetSection("ConnectionManager"));
        s.Configure<MasterConnectionOptions>(config.GetSection("MasterConnection"));

        s.AddSingleton<IConnectionManager, ConnectionManager>();
        s.AddHostedService(p => (ConnectionManager)p.GetRequiredService<IConnectionManager>());
        s.AddHostedService<MasterConnectionManager>();

        return s;
    }
}
