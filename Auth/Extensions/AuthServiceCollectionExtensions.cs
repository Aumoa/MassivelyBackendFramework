using Auth.Controllers;
using Microsoft.Extensions.DependencyInjection;

namespace Auth.Extensions;

public static class AuthServiceCollectionExtensions
{
    public static void AddAuthService(this IServiceCollection services)
    {
        services.AddTransient<AuthController>();
    }
}
