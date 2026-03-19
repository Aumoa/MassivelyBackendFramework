using System.Net;
using System.Threading.Channels;
using CoreRCON;
using Microsoft.Extensions.Options;
using MinecraftSidecar.Options;

namespace MinecraftSidecar.Services;

public class RCONClient : IHostedService, IDisposable
{
    private readonly ILogger m_Logger;
    private readonly RCONOptions m_Options;

    private readonly CancellationTokenSource m_BackgroundConnectorCancellation = new();
    private Task? m_BackgroundConnector;

    private int m_Retry;
    private readonly TimeSpan[] m_RetryIntervals =
    [
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(1),
    ];

    private readonly record struct CommandRequest
    {
        public required string Command { get; init; }

        public TaskCompletionSource<string> CompletionSource { get; init; }

    }

    private readonly ChannelWriter<CommandRequest> m_CommandWriter;
    private readonly ChannelReader<CommandRequest> m_CommandReader;

    public event Action? Connected;
    public event Action? Disconnected;

    public RCONClient(ILogger<RCONClient> logger, IOptions<RCONOptions> options)
    {
        m_Logger = logger;
        m_Options = options.Value;

        var channel = Channel.CreateBounded<CommandRequest>(new BoundedChannelOptions(m_Options.MaxRequestBuffer)
        {
            SingleReader = true,
            SingleWriter = false,
        });
        m_CommandWriter = channel.Writer;
        m_CommandReader = channel.Reader;
    }

    public bool IsConnected { get; private set; }

    Task IHostedService.StartAsync(CancellationToken cancellationToken)
    {
        m_BackgroundConnector = BackgroundConnector(m_BackgroundConnectorCancellation.Token);
        return Task.CompletedTask;
    }

    async Task IHostedService.StopAsync(CancellationToken cancellationToken)
    {
        await m_BackgroundConnectorCancellation.CancelAsync();
        if (m_BackgroundConnector != null)
        {
            await m_BackgroundConnector;
        }
    }

    void IDisposable.Dispose()
    {
        m_BackgroundConnectorCancellation.Dispose();
        GC.SuppressFinalize(this);
    }

    public async Task<string> SendCommandAsync(string command, CancellationToken cancellationToken = default)
    {
        var taskCompletionSource = new TaskCompletionSource<string>(cancellationToken);

        await m_CommandWriter.WriteAsync(new CommandRequest
        {
            Command = command,
            CompletionSource = taskCompletionSource
        }, cancellationToken);

        return await taskCompletionSource.Task;
    }

    private async Task BackgroundConnector(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var rcon = new RCON(new IPEndPoint(IPAddress.Parse(m_Options.Host), m_Options.Port), m_Options.Password);
                await rcon.ConnectAsync().WaitAsync(cancellationToken);
                if (rcon.Connected == false || rcon.Authenticated == false)
                {
                    throw new Exception("RCON authentication failed.");
                }

                using var disconnectedCancellationTokenSource = new CancellationTokenSource();
                rcon.OnDisconnected += disconnectedCancellationTokenSource.Cancel;

                using var linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, disconnectedCancellationTokenSource.Token);
                var linkedCancellationToken = linkedCancellationTokenSource.Token;

                IsConnected = true;
                Connected?.Invoke();

                while (rcon.Connected && rcon.Authenticated)
                {
                    while (m_CommandReader.TryRead(out var command))
                    {
                        try
                        {
                            var response = await rcon.SendCommandAsync(command.Command);
                            command.CompletionSource.SetResult(response);
                        }
                        catch (Exception e)
                        {
                            command.CompletionSource.SetException(e);
                            break;
                        }
                    }

                    await m_CommandReader.WaitToReadAsync(linkedCancellationToken);
                }
            }
            catch (Exception e)
            {
                if (m_Logger.IsEnabled(LogLevel.Error))
                {
                    m_Logger.LogError("RCON connection error: {Message}", e.Message);
                }
            }

            IsConnected = false;
            Disconnected?.Invoke();

            var interval = m_RetryIntervals[Math.Min(m_Retry++, m_RetryIntervals.Length - 1)];
            await Task.Delay(interval, cancellationToken);
        }
    }
}
