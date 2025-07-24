using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Master.Services;

public class MasterConnection<T> where T : ISlaveIdentifier
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

    public MasterConnection(T identifier, ILogger<MasterConnection<T>> logger)
    {
        Connection = new HubConnectionBuilder()
            .WithUrl(identifier.MasterUrl + $"/hub/master?slave_id={Uri.EscapeDataString(identifier.SlaveId)}")
            .AddMessagePackProtocol()
            .WithAutomaticReconnect(new LoggingRetryPolicy(kReconnectIntervals, logger))
            .Build();
        identifier.RegisterHandlers(Connection);
    }
}
