using Microsoft.Extensions.DependencyInjection;
using NPay.Core.Services;
using NPay.Services;

namespace NPay.Core.Extensions;

/// <summary>
/// Extension methods for registering NPay Core services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers NPay business logic services with the DI container.
    /// </summary>
    public static IServiceCollection AddNPay(this IServiceCollection services)
    {
        // Register in-memory implementations for now.
        // Swap these with MySQL-backed implementations when the database schema is ready.
        services.AddSingleton<InMemorySettlementService>();
        services.AddSingleton<ISettlementService>(sp => sp.GetRequiredService<InMemorySettlementService>());
        services.AddSingleton<IParticipantService, InMemoryParticipantService>();
        services.AddSingleton<IExpenseService, InMemoryExpenseService>();
        return services;
    }
}
