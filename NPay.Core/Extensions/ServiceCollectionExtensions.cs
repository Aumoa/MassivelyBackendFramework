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
    /// Registers NPay business logic services with the DI container using a MySQL backend.
    /// </summary>
    public static IServiceCollection AddNPay(this IServiceCollection services, string connectionString)
    {
        services.AddSingleton<ISettlementService>(new MySqlSettlementService(connectionString));
        services.AddSingleton<IParticipantService>(new MySqlParticipantService(connectionString));
        services.AddSingleton<IExpenseService>(sp =>
            new MySqlExpenseService(connectionString, (MySqlSettlementService)sp.GetRequiredService<ISettlementService>()));
        return services;
    }
}
