using AI;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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
        s.Configure<StableDiffusionOptions>(config.GetRequiredSection("StableDiffusion"));

        s.AddHttpClient<OllamaAIMessenger>();
        s.AddSingleton<IAIMessenger, OllamaAIMessenger>();
        s.AddTransient<IChatRepository, MySqlChatRepository>();

        s.AddSingleton(new ToolsProviderOptions().AddAssemblyOf<StableDiffusion>());
        s.AddSingleton<ToolsProvider>();
        s.AddHttpClient<StableDiffusion>((p, client) =>
        {
            var options = p.GetRequiredService<IOptions<StableDiffusionOptions>>();
            client.BaseAddress = new Uri(options.Value.Uri);
            client.Timeout = TimeSpan.FromMinutes(5);
        });
        s.AddSingleton<StableDiffusion>();
        s.AddHostedService(p => p.GetRequiredService<StableDiffusion>());

        SqlMapper.AddTypeHandler(new MessageRoleHandler());
        return s;
    }
}
