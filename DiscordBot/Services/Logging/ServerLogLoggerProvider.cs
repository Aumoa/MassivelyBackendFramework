using Microsoft.Extensions.Logging;

namespace DiscordBot.Services.Logging;

public sealed class ServerLogLoggerProvider(ServerLogStore store) : ILoggerProvider, ISupportExternalScope
{
    private IExternalScopeProvider? scopeProvider;

    public ILogger CreateLogger(string categoryName)
    {
        return new ServerLogLogger(categoryName, store, () => scopeProvider);
    }

    public void SetScopeProvider(IExternalScopeProvider scopeProvider)
    {
        this.scopeProvider = scopeProvider;
    }

    public void Dispose()
    {
    }

    private sealed class ServerLogLogger(
        string categoryName,
        ServerLogStore store,
        Func<IExternalScopeProvider?> getScopeProvider) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel != LogLevel.None;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter(state, exception);
            if (string.IsNullOrWhiteSpace(message) && exception is null)
            {
                return;
            }

            store.Append(
                logLevel,
                categoryName,
                eventId,
                message,
                exception,
                CaptureScopes(getScopeProvider()));
        }

        private static IReadOnlyList<string> CaptureScopes(IExternalScopeProvider? scopeProvider)
        {
            if (scopeProvider is null)
            {
                return Array.Empty<string>();
            }

            var scopes = new List<string>();
            scopeProvider.ForEachScope(static (scope, state) =>
            {
                var value = scope?.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    state.Add(value);
                }
            }, scopes);

            return scopes;
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
