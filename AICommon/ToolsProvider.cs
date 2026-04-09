using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AI;

public class ToolsProvider
{
    private readonly Dictionary<string, ToolFunctionDescription> m_Tools = [];

    public ToolsProvider(ILogger<ToolsProvider> logger, IServiceProvider sp, ToolsProviderOptions options)
    {
        Stopwatch? timer = null;
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Loading tools...");
            timer = Stopwatch.StartNew();
        }

        foreach (var assembly in options.Assemblies)
        {
            ScanTypes(assembly.GetTypes(), type => sp.GetRequiredService(type));
        }

        if (logger.IsEnabled(LogLevel.Information))
        {
            timer!.Stop();
            logger.LogInformation("Finished loading tools in {ElapsedMilliseconds} ms.", timer.ElapsedMilliseconds);
        }
    }

    private ToolsProvider(IEnumerable<object> instances)
    {
        foreach (var instance in instances)
        {
            ScanTypes([instance.GetType()], _ => instance);
        }
    }

    public static ToolsProvider CreateFrom(params object[] instances)
    {
        return new ToolsProvider(instances);
    }

    public IReadOnlyCollection<ToolFunctionDescription> GetToolFunctions()
    {
        return m_Tools.Values;
    }

    public ToolFunctionDescription? FindFunction(string functionName)
    {
        return m_Tools.GetValueOrDefault(functionName);
    }

    private void ScanTypes(Type[] types, Func<Type, object> instanceResolver)
    {
        foreach (var type in types)
        {
            var targetMethods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(m => m.GetCustomAttribute<ToolFunctionAttribute>() != null);

            foreach (var targetMethod in targetMethods)
            {
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
                               parameter.ParameterType == typeof(double) || parameter.ParameterType == typeof(float) ? ToolFunctionDescription.SimpleType.Number :
                               parameter.ParameterType == typeof(int) || parameter.ParameterType == typeof(long) ? ToolFunctionDescription.SimpleType.Integer :
                               parameter.ParameterType == typeof(bool) ? ToolFunctionDescription.SimpleType.Boolean :
                               throw new InvalidOperationException($"Parameter {parameter.Name} in method {targetMethod.Name} has unsupported type {parameter.ParameterType.FullName}."),
                        Description = toolParameterInfo?.Description ?? string.Empty,
                        IsRequired = !parameter.IsOptional,
                        Enum = parameter.ParameterType.IsEnum ? System.Enum.GetNames(parameter.ParameterType) : null,
                        DefaultValue = parameter.IsOptional ? parameter.DefaultValue : null
                    });
                }

                var toolFunctionAttribute = targetMethod.GetCustomAttribute<ToolFunctionAttribute>()!;
                var capturedType = type;
                var capturedMethod = targetMethod;

                var toolFunctionDescription = new ToolFunctionDescription
                {
                    Name = toolFunctionAttribute.Name,
                    Invocable = args =>
                    {
                        var instance = instanceResolver(capturedType);
                        return capturedMethod.Invoke(instance, args)!;
                    },
                    Description = toolFunctionAttribute.Description ?? string.Empty,
                    Parameters = [.. parameterInfos],
                    HasCancellationTokenParameter = hasCancellationToken
                };

                m_Tools.Add(toolFunctionDescription.Name, toolFunctionDescription);
            }
        }
    }
}
