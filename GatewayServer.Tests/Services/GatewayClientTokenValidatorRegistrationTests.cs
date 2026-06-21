using GatewayServer.Extensions;
using GatewayServer.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GatewayServer.Tests.Services;

public sealed class GatewayClientTokenValidatorRegistrationTests
{
    [Fact]
    public void ClientTokenValidator_ContractTypes_ArePublicProductionIntegrationSurface()
    {
        Assert.True(typeof(IGatewayClientTokenValidator).IsPublic);
        Assert.True(typeof(GatewayClientTokenValidationResult).IsPublic);
        Assert.True(typeof(GatewayClientPrincipal).IsPublic);
    }

    [Fact]
    public void AddGatewayServer_DoesNotOverrideExistingClientTokenValidator()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IGatewayClientTokenValidator, ExternalGatewayClientTokenValidator>();

        services.AddGatewayServer(CreateConfiguration());

        using var provider = services.BuildServiceProvider();
        var validator = provider.GetRequiredService<IGatewayClientTokenValidator>();

        Assert.IsType<ExternalGatewayClientTokenValidator>(validator);
    }

    private static IConfiguration CreateConfiguration()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionManager:IPAddress"] = "127.0.0.1",
                ["ConnectionManager:Port"] = "0"
            })
            .Build();
    }

    private sealed class ExternalGatewayClientTokenValidator : IGatewayClientTokenValidator
    {
        public ValueTask<GatewayClientTokenValidationResult> ValidateAsync(
            string accessToken,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(
                GatewayClientTokenValidationResult.Accepted(new GatewayClientPrincipal("external-client")));
        }
    }
}
