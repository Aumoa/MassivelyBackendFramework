using GatewayServer.Options;
using GatewayServer.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GatewayServer.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGatewayServer(this IServiceCollection s, IConfiguration config)
    {
        s.Configure<ConnectionManagerOptions>(config.GetSection("ConnectionManager"));
        s.Configure<MasterConnectionOptions>(config.GetSection("MasterConnection"));
        s.Configure<BackendConnectionOptions>(config.GetSection("BackendConnection"));
        s.Configure<BackendRouteOptions>(config.GetSection("BackendRoute"));

        s.AddSingleton<IGatewayClientAuthenticationContextFactory, GatewayClientAuthenticationContextFactory>();
        s.AddSingleton<IGatewayClientAuthenticationChallengeIssuer, GatewayClientAuthenticationChallengeIssuer>();
        s.AddSingleton<GatewayClientSecretCredentialCatalog>();
        s.TryAddSingleton<IGatewayClientTokenValidator>(p => p.GetRequiredService<GatewayClientSecretCredentialCatalog>());
        s.AddSingleton<IGatewayClientSecretCredentialWriter>(p => p.GetRequiredService<GatewayClientSecretCredentialCatalog>());
        s.AddSingleton<GatewayBackendRoutePolicyCatalog>();
        s.AddSingleton<IGatewayBackendRoutePolicyProvider>(p => p.GetRequiredService<GatewayBackendRoutePolicyCatalog>());
        s.AddSingleton<IGatewayBackendRoutePolicyWriter>(p => p.GetRequiredService<GatewayBackendRoutePolicyCatalog>());
        s.AddSingleton<BackendPacketManifestCatalog>();
        s.AddSingleton<IBackendPacketManifestProvider>(p => p.GetRequiredService<BackendPacketManifestCatalog>());
        s.AddSingleton<IBackendPacketManifestWriter>(p => p.GetRequiredService<BackendPacketManifestCatalog>());
        s.AddSingleton<IGatewayClientCertificateLoader, GatewayClientCertificateLoader>();
        s.AddSingleton<GatewayClientCertificateProvider>();
        s.AddSingleton<IGatewayClientCertificateProvider>(p => p.GetRequiredService<GatewayClientCertificateProvider>());
        s.AddHostedService(p => p.GetRequiredService<GatewayClientCertificateProvider>());
        s.AddSingleton<IConnectionManager, ConnectionManager>();
        s.AddSingleton<IBackendRouteStatusProvider>(p => (ConnectionManager)p.GetRequiredService<IConnectionManager>());
        s.AddHostedService(p => (ConnectionManager)p.GetRequiredService<IConnectionManager>());
        s.AddSingleton<IGatewayClientStreamAuthenticator, GatewayClientTlsStreamAuthenticator>();
        s.AddSingleton<IGatewayBackendRouteTokenGenerator, GatewayBackendRouteTokenGenerator>();
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
        s.AddSingleton<BackendConnectionManager>();
        s.AddSingleton<IBackendRouteManager>(p => p.GetRequiredService<BackendConnectionManager>());
        s.AddSingleton<IBackendConnectionStatusProvider>(p => p.GetRequiredService<BackendConnectionManager>());
        s.AddSingleton<IGatewayMasterConnectionIdentitySink>(p => p.GetRequiredService<BackendConnectionManager>());
        s.AddHostedService(p => p.GetRequiredService<BackendConnectionManager>());

        return s;
    }
}
