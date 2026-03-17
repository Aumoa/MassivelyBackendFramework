using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenAI.DapperUtility;
using OpenAI.Options;
using OpenAI.Services;
using OpenAI.Tools;

namespace OpenAI.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOllamaAI(this IServiceCollection s, IConfiguration config)
    {
        s.Configure<OllamaOptions>(config.GetRequiredSection("Ollama"));
        s.Configure<MySqlOptions>(config.GetRequiredSection("MySql"));

        s.AddHttpClient<OllamaAIMessenger>();
        s.AddSingleton<IAIMessenger, OllamaAIMessenger>();
        s.AddTransient<IChatRepository, MySqlChatRepository>();

        s.AddSingleton<ToolsProvider>();
        s.AddSingleton<StableDiffusion>();
        s.AddHostedService(p => p.GetRequiredService<StableDiffusion>());

        SqlMapper.AddTypeHandler(new MessageRoleHandler());
        return s;
    }
}
