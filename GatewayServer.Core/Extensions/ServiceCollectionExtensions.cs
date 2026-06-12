using GatewayServer.Options;
using GatewayServer.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GatewayServer.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGatewayServer(this IServiceCollection s, IConfiguration config)
    {
        s.Configure<ConnectionManagerOptions>(config.GetSection("ConnectionManager"));
        s.Configure<MasterConnectionOptions>(config.GetSection("MasterConnection"));
        s.Configure<DedicatedConnectionOptions>(config.GetSection("DedicatedConnection"));

        s.AddSingleton<IConnectionManager, ConnectionManager>();
        s.AddHostedService(p => (ConnectionManager)p.GetRequiredService<IConnectionManager>());
        s.AddSingleton<DedicatedNodeCatalog>();
        s.AddSingleton<IDedicatedNodeCatalog>(p => p.GetRequiredService<DedicatedNodeCatalog>());
        s.AddSingleton<IDedicatedNodeCatalogWriter>(p => p.GetRequiredService<DedicatedNodeCatalog>());
        s.AddSingleton<BackendNodeCatalog>();
        s.AddSingleton<IBackendNodeCatalog>(p => p.GetRequiredService<BackendNodeCatalog>());
        s.AddSingleton<IBackendNodeCatalogWriter>(p => p.GetRequiredService<BackendNodeCatalog>());
        s.AddSingleton<MasterConnectionManager>();
        s.AddSingleton<IMasterConnectionStatusProvider>(p => p.GetRequiredService<MasterConnectionManager>());
        s.AddSingleton<IDirectConnectCodeIssuer>(p => p.GetRequiredService<MasterConnectionManager>());
        s.AddHostedService(p => p.GetRequiredService<MasterConnectionManager>());
        s.AddSingleton<DedicatedConnectionManager>();
        s.AddSingleton<IDedicatedConnectionStatusProvider>(p => p.GetRequiredService<DedicatedConnectionManager>());
        s.AddSingleton<IGatewayMasterConnectionIdentitySink>(p => p.GetRequiredService<DedicatedConnectionManager>());
        s.AddHostedService(p => p.GetRequiredService<DedicatedConnectionManager>());

        return s;
    }
}
