using Gateway.Options;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace Gateway.Services;

internal class MasterConnection
{
    private static readonly TimeSpan[] kReconnectIntervals = [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4),
        TimeSpan.FromSeconds(8),
        TimeSpan.FromSeconds(16),
        TimeSpan.FromSeconds(32)
    ];

    private class LoggingRetryPolicy(TimeSpan[] intervals, ILogger logger) : IRetryPolicy
    {
        public TimeSpan? NextRetryDelay(RetryContext retryContext)
        {
            long attempt = retryContext.PreviousRetryCount;
            TimeSpan delay = attempt < intervals.Length
                ? intervals[attempt]
                : intervals[^1];

            logger.LogWarning("Trying to reconnect #{Attempt} after {Delay} seconds ago.", attempt + 1, delay.TotalSeconds);

            return delay;
        }
    }

    public readonly HubConnection Connection;

    public MasterConnection(IOptions<MasterConnectionOptions> options, ILogger<MasterConnection> logger)
    {
        Connection = new HubConnectionBuilder()
            .WithUrl(options.Value.Url + "/hub/master")
            .AddMessagePackProtocol()
            .WithAutomaticReconnect(new LoggingRetryPolicy(kReconnectIntervals, logger))
            .Build();
    }
}
