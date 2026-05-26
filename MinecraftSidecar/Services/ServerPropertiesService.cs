using System.Collections;
using System.Globalization;
using Microsoft.Extensions.Options;
using MinecraftSidecar.Options;

namespace MinecraftSidecar.Services;

public enum ServerPropertyLineKind
{
    Blank,
    Comment,
    Property,
}

public sealed record ServerPropertyLine(
    ServerPropertyLineKind Kind,
    string Raw,
    string Key,
    string Value);

public sealed record ServerPropertyValue(string Key, string Value);

public sealed record ServerPropertiesDocument(
    string FilePath,
    IReadOnlyList<ServerPropertyLine> Lines);

public sealed class ServerPropertiesService(
    ILogger<ServerPropertiesService> logger,
    IOptions<ServerPropertiesOptions> options,
    IOptions<RCONOptions> rconOptions)
{
    private static readonly IReadOnlyDictionary<string, string> BuiltInDefaults =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["allow-flight"] = "false",
            ["allow-nether"] = "true",
            ["broadcast-console-to-ops"] = "true",
            ["broadcast-rcon-to-ops"] = "true",
            ["difficulty"] = "easy",
            ["enable-command-block"] = "false",
            ["enable-jmx-monitoring"] = "false",
            ["enable-query"] = "false",
            ["enable-rcon"] = "true",
            ["enable-status"] = "true",
            ["enforce-secure-profile"] = "true",
            ["enforce-whitelist"] = "false",
            ["entity-broadcast-range-percentage"] = "100",
            ["force-gamemode"] = "false",
            ["function-permission-level"] = "2",
            ["gamemode"] = "survival",
            ["generate-structures"] = "true",
            ["generator-settings"] = "{}",
            ["hardcore"] = "false",
            ["hide-online-players"] = "false",
            ["level-name"] = "world",
            ["level-seed"] = "",
            ["level-type"] = "minecraft:normal",
            ["max-players"] = "20",
            ["max-tick-time"] = "60000",
            ["max-world-size"] = "29999984",
            ["motd"] = "A Minecraft Server",
            ["network-compression-threshold"] = "256",
            ["online-mode"] = "true",
            ["op-permission-level"] = "4",
            ["player-idle-timeout"] = "0",
            ["prevent-proxy-connections"] = "false",
            ["pvp"] = "true",
            ["query.port"] = "25565",
            ["rate-limit"] = "0",
            ["rcon.password"] = "",
            ["rcon.port"] = "25575",
            ["require-resource-pack"] = "false",
            ["resource-pack"] = "",
            ["resource-pack-id"] = "",
            ["resource-pack-prompt"] = "",
            ["resource-pack-sha1"] = "",
            ["server-ip"] = "",
            ["server-port"] = "25565",
            ["simulation-distance"] = "10",
            ["spawn-animals"] = "true",
            ["spawn-monsters"] = "true",
            ["spawn-npcs"] = "true",
            ["spawn-protection"] = "16",
            ["sync-chunk-writes"] = "true",
            ["text-filtering-config"] = "",
            ["use-native-transport"] = "true",
            ["view-distance"] = "10",
            ["white-list"] = "false",
        };

    private readonly SemaphoreSlim m_Lock = new(1, 1);

    public async Task<ServerPropertiesDocument> LoadAsync(CancellationToken cancellationToken = default)
    {
        await m_Lock.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedCoreAsync(cancellationToken);
            return await ReadDocumentCoreAsync(cancellationToken);
        }
        finally
        {
            m_Lock.Release();
        }
    }

    public async Task SaveAsync(IReadOnlyList<ServerPropertyValue> properties, CancellationToken cancellationToken = default)
    {
        var values = ValidateProperties(properties);

        await m_Lock.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedCoreAsync(cancellationToken);
            var document = await ReadDocumentCoreAsync(cancellationToken);
            var lines = RenderDocument(document.Lines, values);
            await WriteLinesAtomicAsync(GetFilePath(), lines, cancellationToken);
        }
        finally
        {
            m_Lock.Release();
        }
    }

    private async Task EnsureInitializedCoreAsync(CancellationToken cancellationToken)
    {
        var filePath = GetFilePath();
        if (File.Exists(filePath))
        {
            return;
        }

        var initialProperties = BuildInitialProperties();
        var seedPath = options.Value.SeedFilePath;

        IReadOnlyList<string> lines;
        if (!string.IsNullOrWhiteSpace(seedPath) && File.Exists(seedPath))
        {
            var seedDocument = ParseDocument(filePath, await File.ReadAllLinesAsync(seedPath, cancellationToken));
            var merged = new Dictionary<string, string>(initialProperties, StringComparer.Ordinal);
            foreach (var line in seedDocument.Lines.Where(static line => line.Kind == ServerPropertyLineKind.Property))
            {
                merged[line.Key] = line.Value;
            }
            ApplyConfiguredDefaults(merged);
            lines = RenderDocument(seedDocument.Lines, merged);
        }
        else
        {
            lines =
            [
                "# Minecraft server properties",
                $"# Generated by Minecraft Sidecar on {DateTimeOffset.UtcNow:O}",
                "",
                .. initialProperties.Select(static pair => $"{pair.Key}={pair.Value}")
            ];
        }

        await WriteLinesAtomicAsync(filePath, lines, cancellationToken);
        logger.LogInformation("Initialized server.properties at {Path}", filePath);
    }

    private async Task<ServerPropertiesDocument> ReadDocumentCoreAsync(CancellationToken cancellationToken)
    {
        var filePath = GetFilePath();
        var lines = await File.ReadAllLinesAsync(filePath, cancellationToken);
        return ParseDocument(filePath, lines);
    }

    private Dictionary<string, string> BuildInitialProperties()
    {
        var result = new Dictionary<string, string>(BuiltInDefaults, StringComparer.Ordinal);
        result["rcon.port"] = rconOptions.Value.Port.ToString(CultureInfo.InvariantCulture);
        result["rcon.password"] = rconOptions.Value.Password;

        ApplyConfiguredDefaults(result);
        return result;
    }

    private void ApplyConfiguredDefaults(Dictionary<string, string> result)
    {
        foreach (var (key, value) in options.Value.DefaultProperties)
        {
            result[key] = value;
        }

        foreach (var (key, value) in ReadEnvironmentProperties(options.Value.EnvironmentVariablePrefix))
        {
            result[key] = value;
        }
    }

    private static IEnumerable<KeyValuePair<string, string>> ReadEnvironmentProperties(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            yield break;
        }

        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var name = entry.Key?.ToString();
            if (string.IsNullOrWhiteSpace(name) || !name.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var encodedKey = name[prefix.Length..];
            if (string.IsNullOrWhiteSpace(encodedKey))
            {
                continue;
            }

            var key = DecodeEnvironmentPropertyKey(encodedKey);
            var value = entry.Value?.ToString() ?? string.Empty;
            yield return new KeyValuePair<string, string>(key, value);
        }
    }

    private static string DecodeEnvironmentPropertyKey(string encodedKey)
    {
        return encodedKey
            .Replace("__", ".", StringComparison.Ordinal)
            .Replace('_', '-')
            .ToLowerInvariant();
    }

    private static ServerPropertiesDocument ParseDocument(string filePath, IReadOnlyList<string> lines)
    {
        return new ServerPropertiesDocument(filePath, [.. lines.Select(ParseLine)]);
    }

    private static ServerPropertyLine ParseLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return new ServerPropertyLine(ServerPropertyLineKind.Blank, string.Empty, string.Empty, string.Empty);
        }

        var trimmed = line.Trim();
        if (trimmed.StartsWith('#') || trimmed.StartsWith('!'))
        {
            return new ServerPropertyLine(ServerPropertyLineKind.Comment, trimmed, string.Empty, string.Empty);
        }

        var separatorIndex = trimmed.IndexOf('=');
        if (separatorIndex < 0)
        {
            return new ServerPropertyLine(ServerPropertyLineKind.Property, trimmed, trimmed, string.Empty);
        }

        return new ServerPropertyLine(
            ServerPropertyLineKind.Property,
            trimmed,
            trimmed[..separatorIndex].Trim(),
            trimmed[(separatorIndex + 1)..]);
    }

    private static Dictionary<string, string> ValidateProperties(IReadOnlyList<ServerPropertyValue> properties)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in properties)
        {
            var key = property.Key.Trim();
            var value = property.Value;

            if (string.IsNullOrWhiteSpace(key))
            {
                throw new InvalidOperationException("Property keys cannot be empty.");
            }

            if (key.Contains('=') || key.Contains('\r') || key.Contains('\n'))
            {
                throw new InvalidOperationException($"Property key '{key}' contains an invalid character.");
            }

            if (value.Contains('\r') || value.Contains('\n'))
            {
                throw new InvalidOperationException($"Property value for '{key}' cannot contain line breaks.");
            }

            if (!values.TryAdd(key, value))
            {
                throw new InvalidOperationException($"Property key '{key}' is duplicated.");
            }
        }

        return values;
    }

    private static IReadOnlyList<string> RenderDocument(
        IReadOnlyList<ServerPropertyLine> lines,
        Dictionary<string, string> values)
    {
        var pending = new Dictionary<string, string>(values, StringComparer.Ordinal);
        var rendered = new List<string>();

        foreach (var line in lines)
        {
            if (line.Kind == ServerPropertyLineKind.Blank)
            {
                rendered.Add(string.Empty);
                continue;
            }

            if (line.Kind == ServerPropertyLineKind.Comment)
            {
                rendered.Add(line.Raw);
                continue;
            }

            if (pending.Remove(line.Key, out var value))
            {
                rendered.Add($"{line.Key}={value}");
            }
        }

        if (pending.Count > 0)
        {
            if (rendered.Count > 0 && rendered[^1].Length > 0)
            {
                rendered.Add(string.Empty);
            }

            foreach (var (key, value) in pending)
            {
                rendered.Add($"{key}={value}");
            }
        }

        return rendered;
    }

    private static async Task WriteLinesAtomicAsync(
        string filePath,
        IReadOnlyList<string> lines,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = Path.Combine(
            string.IsNullOrWhiteSpace(directory) ? Directory.GetCurrentDirectory() : directory,
            $".{Path.GetFileName(filePath)}.{Guid.NewGuid():N}.tmp");

        await File.WriteAllLinesAsync(tempPath, lines, cancellationToken);
        File.Move(tempPath, filePath, true);
    }

    private string GetFilePath()
    {
        return Path.GetFullPath(options.Value.FilePath);
    }
}
