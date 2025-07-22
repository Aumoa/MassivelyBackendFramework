using Auth.Controllers;
using Auth.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Auth.Extensions;

public static class AuthServiceCollectionExtensions
{
    public static void AddAuthService(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AuthOptions>(configuration.GetSection(nameof(AuthController)));
    }
}
