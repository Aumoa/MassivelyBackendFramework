using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace OpenAI.Tools;

internal class ToolsProvider
{
    private readonly Dictionary<string, ToolFunctionDescription> m_Tools = [];

    public ToolsProvider(ILogger<ToolsProvider> logger, IServiceProvider sp)
    {
        Stopwatch? timer = null;
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Loading tools...");
            timer = Stopwatch.StartNew();
        }

        foreach (var type in typeof(ToolsProvider).Assembly.GetTypes())
        {
            var targetMethods = type.GetMethods().Where(m => m.GetCustomAttribute<ToolFunctionAttribute>() != null);
            foreach (var targetMethod in targetMethods)
            {
                if (targetMethod.ReturnType != typeof(IAsyncEnumerable<ChunkedResponse>))
                {
                    throw new InvalidOperationException($"Method {targetMethod.Name} in type {type.FullName} is marked with ToolFunctionAttribute but does not return IAsyncEnumerable<ChunkedResponse>.");
                }

                var parameters = targetMethod.GetParameters();
                bool hasCancellationToken = parameters.Length > 0 && parameters[^1].ParameterType == typeof(CancellationToken);
                if (hasCancellationToken)
                {
                    parameters = parameters[..^1];
                }

                List<ToolFunctionDescription.ParameterInfo> parameterInfos = [];
                foreach (var parameter in parameters)
                {
                    var toolParameterInfo = parameter.GetCustomAttribute<ToolParameterInfoAttribute>();
                    parameterInfos.Add(new ToolFunctionDescription.ParameterInfo
                    {
                        Name = toolParameterInfo?.Name ?? parameter.Name!,
                        Type = parameter.ParameterType == typeof(string) ? ToolFunctionDescription.SimpleType.String :
                               parameter.ParameterType == typeof(double) || parameter.ParameterType == typeof(int) ? ToolFunctionDescription.SimpleType.Number :
                               parameter.ParameterType == typeof(bool) ? ToolFunctionDescription.SimpleType.Boolean :
                               throw new InvalidOperationException($"Parameter {parameter.Name} in method {targetMethod.Name} has unsupported type {parameter.ParameterType.FullName}."),
                        Description = toolParameterInfo?.Description ?? string.Empty,
                        IsRequired = !parameter.IsOptional,
                        Enum = parameter.ParameterType.IsEnum ? Enum.GetNames(parameter.ParameterType) : null
                    });
                }

                var toolFunctionAttribute = targetMethod.GetCustomAttribute<ToolFunctionAttribute>()!;

                var toolFunctionDescription = new ToolFunctionDescription
                {
                    Name = toolFunctionAttribute.Name,
                    Invocable = args =>
                    {
                        var instance = sp.GetRequiredService(type);
                        return (IAsyncEnumerable<ChunkedResponse>)targetMethod.Invoke(instance, args)!;
                    },
                    Description = toolFunctionAttribute.Description ?? string.Empty,
                    Parameters = [.. parameterInfos],
                    HasCancellationTokenParameter = hasCancellationToken
                };

                m_Tools.Add(toolFunctionDescription.Name, toolFunctionDescription);
            }
        }

        if (logger.IsEnabled(LogLevel.Information))
        {
            timer!.Stop();
            logger.LogInformation("Finished loading tools in {ElapsedMilliseconds} ms.", timer.ElapsedMilliseconds);
        }
    }

    public IReadOnlyCollection<ToolFunctionDescription> GetToolFunctions()
    {
        return m_Tools.Values;
    }

    public ToolFunctionDescription? FindFunction(string functionName)
    {
        return m_Tools.GetValueOrDefault(functionName);
    }
}
