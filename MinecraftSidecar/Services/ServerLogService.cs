using Microsoft.Extensions.Options;
using MinecraftSidecar.Options;

namespace MinecraftSidecar.Services;

public class ServerLogService : IHostedService, IDisposable
{
    private readonly ILogger<ServerLogService> m_Logger;
    private readonly ServerLogOptions m_Options;

    private readonly List<string> m_Lines = [];
    private readonly SemaphoreSlim m_Lock = new(1);

    private CancellationTokenSource? m_Cts;
    private Task? m_TailTask;

    public event Action<string>? LineReceived;

    public ServerLogService(ILogger<ServerLogService> logger, IOptions<ServerLogOptions> options)
    {
        m_Logger = logger;
        m_Options = options.Value;
    }

    public async Task<IReadOnlyList<string>> GetInitialLinesAsync()
    {
        await m_Lock.WaitAsync();
        try
        {
            return m_Lines.ToList();
        }
        finally
        {
            m_Lock.Release();
        }
    }

    Task IHostedService.StartAsync(CancellationToken cancellationToken)
    {
        m_Cts = new CancellationTokenSource();
        m_TailTask = TailLogFileAsync(m_Cts.Token);
        return Task.CompletedTask;
    }

    async Task IHostedService.StopAsync(CancellationToken cancellationToken)
    {
        if (m_Cts != null)
        {
            await m_Cts.CancelAsync();
        }
        if (m_TailTask != null)
        {
            await m_TailTask;
        }
    }

    void IDisposable.Dispose()
    {
        m_Cts?.Dispose();
        m_Lock.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task TailLogFileAsync(CancellationToken cancellationToken)
    {
        var logPath = m_Options.LogFilePath;
        long position = 0;

        // Wait until the log file appears
        while (!cancellationToken.IsCancellationRequested && !File.Exists(logPath))
        {
            await Task.Delay(m_Options.PollingIntervalMs, cancellationToken).ConfigureAwait(false);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        // Load the last N lines as initial content
        try
        {
            using var initStream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var initReader = new StreamReader(initStream);
            var allLines = new List<string>();
            while (await initReader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                allLines.Add(line);
            }

            var initial = allLines.TakeLast(m_Options.InitialLines).ToList();
            await m_Lock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                m_Lines.AddRange(initial);
            }
            finally
            {
                m_Lock.Release();
            }

            position = initStream.Position;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            m_Logger.LogWarning(ex, "Failed to read initial log content from {Path}", logPath);
        }

        // Tail: poll for new content
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(m_Options.PollingIntervalMs, cancellationToken).ConfigureAwait(false);

                if (!File.Exists(logPath))
                {
                    continue;
                }

                using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

                // Detect log rotation (file shrunk)
                if (stream.Length < position)
                {
                    position = 0;
                    await m_Lock.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        m_Lines.Clear();
                    }
                    finally
                    {
                        m_Lock.Release();
                    }
                }

                if (stream.Length == position)
                {
                    continue;
                }

                stream.Seek(position, SeekOrigin.Begin);
                using var reader = new StreamReader(stream);

                while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
                {
                    await m_Lock.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        m_Lines.Add(line);
                    }
                    finally
                    {
                        m_Lock.Release();
                    }

                    LineReceived?.Invoke(line);
                }

                position = stream.Position;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                m_Logger.LogWarning(ex, "Error while tailing log file {Path}", logPath);
            }
        }
    }
}
