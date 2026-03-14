using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenAI.Services;

namespace OpenAI.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOllamaAI(this IServiceCollection s, IConfiguration config)
    {
        s.AddSingleton<IAIMessenger, OllamaAIMessenger>();
        return s;
    }
}
