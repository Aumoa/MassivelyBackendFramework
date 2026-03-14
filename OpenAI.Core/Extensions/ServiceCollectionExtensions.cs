using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenAI.Options;
using OpenAI.Services;

namespace OpenAI.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOllamaAI(this IServiceCollection s, IConfiguration config)
    {
        s.Configure<MySqlOptions>(config.GetRequiredSection("MySql"));

        s.AddSingleton<IAIMessenger, OllamaAIMessenger>();
        s.AddTransient<IChatRepository, MySqlChatRepository>();
        return s;
    }
}
