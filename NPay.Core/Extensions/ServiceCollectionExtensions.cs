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
    /// Registers NPay business logic services backed by MySQL.
    /// Requires a "NPay" connection string in configuration.
    /// </summary>
    public static IServiceCollection AddNPay(this IServiceCollection services, string connectionString)
    {
        services.AddSingleton(new MySqlSettlementService(connectionString));
        services.AddSingleton<ISettlementService>(sp => sp.GetRequiredService<MySqlSettlementService>());
        services.AddSingleton<IParticipantService>(new MySqlParticipantService(connectionString));
        services.AddSingleton<IExpenseService>(sp =>
            new MySqlExpenseService(connectionString, sp.GetRequiredService<MySqlSettlementService>()));
        return services;
    }
}
