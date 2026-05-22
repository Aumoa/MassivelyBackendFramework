using System.Globalization;
using System.Reflection;
using System.Text;
using AI;
using DiscordBot.Localizations;
using DiscordBot.Repositories;
using Microsoft.Extensions.Caching.Memory;

namespace DiscordBot.Services;

internal sealed record ToolSettingsView(
    string Name,
    string Description,
    IReadOnlyList<ToolParameterSettingsView> Parameters,
    bool Enabled);

internal sealed record ToolParameterSettingsView(
    string Name,
    string Type,
    string Description,
    bool IsRequired,
    string? DefaultValue,
    IReadOnlyList<string>? EnumValues);

internal interface IToolSettingsService
{
    ValueTask<IReadOnlyList<ToolSettingsView>> GetAllAsync(CancellationToken cancellationToken = default);

    ValueTask<ToolSettingsView?> GetAsync(string toolName, CancellationToken cancellationToken = default);

    ValueTask SetEnabledAsync(string toolName, bool enabled, CancellationToken cancellationToken = default);

    ValueTask ApplyAsync(ToolsProvider toolsProvider, CancellationToken cancellationToken = default);
}

internal sealed class ToolSettingsService(
    IToolSettingsRepository repository,
    IMemoryCache cache) : IToolSettingsService
{
    private const string DisabledToolNamesCacheKey = "DiscordBot.ToolSettings.DisabledToolNames";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);
    private static readonly Type[] ToolTypes = [typeof(DiscordTools), typeof(DiscordImageTools)];
    private static readonly Lazy<IReadOnlyList<ToolCatalogItem>> Catalog = new(BuildCatalog);

    public async ValueTask<IReadOnlyList<ToolSettingsView>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var disabledToolNames = await GetDisabledToolNamesAsync(cancellationToken);
        return Catalog.Value
            .Select(item => ToView(item, !disabledToolNames.Contains(item.Name)))
            .ToList();
    }

    public async ValueTask<ToolSettingsView?> GetAsync(string toolName, CancellationToken cancellationToken = default)
    {
        var normalizedToolName = NormalizeRequired(toolName);
        var item = Catalog.Value.FirstOrDefault(t => t.Name == normalizedToolName);
        if (item is null)
        {
            return null;
        }

        var disabledToolNames = await GetDisabledToolNamesAsync(cancellationToken);
        return ToView(item, !disabledToolNames.Contains(item.Name));
    }

    public async ValueTask SetEnabledAsync(string toolName, bool enabled, CancellationToken cancellationToken = default)
    {
        var normalizedToolName = NormalizeRequired(toolName);
        if (!Catalog.Value.Any(t => t.Name == normalizedToolName))
        {
            throw new ArgumentException("Unknown tool.", nameof(toolName));
        }

        await repository.UpsertAsync(normalizedToolName, enabled, cancellationToken);
        cache.Remove(DisabledToolNamesCacheKey);
    }

    public async ValueTask ApplyAsync(ToolsProvider toolsProvider, CancellationToken cancellationToken = default)
    {
        var disabledToolNames = await GetDisabledToolNamesAsync(cancellationToken);
        toolsProvider.RemoveFunctions(disabledToolNames);
    }

    private async ValueTask<HashSet<string>> GetDisabledToolNamesAsync(CancellationToken cancellationToken)
    {
        if (cache.TryGetValue<HashSet<string>>(DisabledToolNamesCacheKey, out var cached) && cached != null)
        {
            return cached;
        }

        var settings = await repository.GetAllAsync(cancellationToken);
        var disabledToolNames = settings
            .Where(s => !s.Enabled)
            .Select(s => s.ToolName)
            .ToHashSet(StringComparer.Ordinal);

        cache.Set(DisabledToolNamesCacheKey, disabledToolNames, CacheDuration);
        return disabledToolNames;
    }

    private static IReadOnlyList<ToolCatalogItem> BuildCatalog()
    {
        return ToolTypes
            .SelectMany(GetToolsFromType)
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static IEnumerable<ToolCatalogItem> GetToolsFromType(Type type)
    {
        var targetMethods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(m => m.GetCustomAttribute<ToolFunctionAttribute>() != null);

        foreach (var targetMethod in targetMethods)
        {
            var toolFunctionAttribute = targetMethod.GetCustomAttribute<ToolFunctionAttribute>()!;
            var parameters = targetMethod.GetParameters();
            bool hasCancellationToken = parameters.Length > 0 && parameters[^1].ParameterType == typeof(CancellationToken);
            if (hasCancellationToken)
            {
                parameters = parameters[..^1];
            }

            yield return new ToolCatalogItem(
                toolFunctionAttribute.Name,
                toolFunctionAttribute.Description ?? string.Empty,
                parameters.Select(ToCatalogParameter).ToList());
        }
    }

    private static ToolCatalogParameter ToCatalogParameter(ParameterInfo parameter)
    {
        var toolParameterInfo = parameter.GetCustomAttribute<ToolParameterInfoAttribute>();
        return new ToolCatalogParameter(
            toolParameterInfo?.Name ?? parameter.Name!,
            GetSimpleType(parameter.ParameterType),
            toolParameterInfo?.Description ?? string.Empty,
            !parameter.IsOptional,
            parameter.IsOptional ? parameter.DefaultValue : null,
            parameter.ParameterType.IsEnum ? Enum.GetNames(parameter.ParameterType) : null);
    }

    private static ToolFunctionDescription.SimpleType GetSimpleType(Type parameterType)
    {
        return parameterType == typeof(string) ? ToolFunctionDescription.SimpleType.String :
               parameterType == typeof(double) || parameterType == typeof(float) ? ToolFunctionDescription.SimpleType.Number :
               parameterType == typeof(int) || parameterType == typeof(long) ? ToolFunctionDescription.SimpleType.Integer :
               parameterType == typeof(bool) ? ToolFunctionDescription.SimpleType.Boolean :
               throw new InvalidOperationException($"Parameter type {parameterType.FullName} is unsupported.");
    }

    private static ToolSettingsView ToView(ToolCatalogItem item, bool enabled)
    {
        return new ToolSettingsView(
            item.Name,
            Localize($"TOOLS_TOOL_{ToResourceKey(item.Name)}_DESCRIPTION", item.Description),
            item.Parameters.Select(parameter => ToParameterView(item.Name, parameter)).ToList(),
            enabled);
    }

    private static ToolParameterSettingsView ToParameterView(string toolName, ToolCatalogParameter parameter)
    {
        return new ToolParameterSettingsView(
            parameter.Name,
            LocalizeType(parameter.Type),
            Localize($"TOOLS_TOOL_{ToResourceKey(toolName)}_PARAM_{ToResourceKey(parameter.Name)}", parameter.Description),
            parameter.IsRequired,
            FormatDefaultValue(parameter.DefaultValue),
            parameter.EnumValues);
    }

    private static string LocalizeType(ToolFunctionDescription.SimpleType type)
    {
        var key = type switch
        {
            ToolFunctionDescription.SimpleType.String => "TOOLS_TYPE_STRING",
            ToolFunctionDescription.SimpleType.Number => "TOOLS_TYPE_NUMBER",
            ToolFunctionDescription.SimpleType.Integer => "TOOLS_TYPE_INTEGER",
            ToolFunctionDescription.SimpleType.Boolean => "TOOLS_TYPE_BOOLEAN",
            _ => null
        };

        return key == null ? type.ToString() : Localize(key, type.ToString());
    }

    private static string Localize(string key, string fallback)
    {
        var value = Strings.ResourceManager.GetString(key, CultureInfo.CurrentUICulture);
        return string.IsNullOrEmpty(value) ? fallback : value;
    }

    private static string ToResourceKey(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            builder.Append(char.IsLetterOrDigit(c) ? char.ToUpperInvariant(c) : '_');
        }

        return builder.ToString();
    }

    private static string? FormatDefaultValue(object? value)
    {
        return value switch
        {
            null => null,
            DBNull => null,
            bool b => b ? "true" : "false",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        };
    }

    private static string NormalizeRequired(string value)
    {
        var normalized = Uri.UnescapeDataString(value).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("Tool name is required.", nameof(value));
        }

        return normalized;
    }

    private sealed record ToolCatalogItem(
        string Name,
        string Description,
        IReadOnlyList<ToolCatalogParameter> Parameters);

    private sealed record ToolCatalogParameter(
        string Name,
        ToolFunctionDescription.SimpleType Type,
        string Description,
        bool IsRequired,
        object? DefaultValue,
        IReadOnlyList<string>? EnumValues);
}
