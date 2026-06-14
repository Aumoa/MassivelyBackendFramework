using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace RemoteDebug;

public sealed class RemoteDebugGatewayClientRunner : IDisposable, IAsyncDisposable
{
    private readonly RemoteDebugGatewayClient m_Client;
    private readonly CancellationTokenSource m_StopCancellation = new CancellationTokenSource();
    private readonly Task m_HeartbeatLoopTask;
    private int m_Disposed;

    private RemoteDebugGatewayClientRunner(RemoteDebugGatewayClient client)
    {
        m_Client = client;
        m_HeartbeatLoopTask = RunHeartbeatLoopAsync();
    }

    public RemoteDebugSessionInfo Session => m_Client.Session;

    public Task Completion => m_HeartbeatLoopTask;

    public static async Task<RemoteDebugGatewayClientRunner> StartAsync(
        RemoteDebugGatewayClientOptions options,
        CancellationToken cancellationToken = default)
    {
        var client = await RemoteDebugGatewayClient.ConnectAsync(options, cancellationToken).ConfigureAwait(false);
        return new RemoteDebugGatewayClientRunner(client);
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        return StopAsync("Client stopped.", cancellationToken);
    }

    public async Task StopAsync(
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (reason == null)
        {
            throw new ArgumentNullException(nameof(reason));
        }

        if (Interlocked.Exchange(ref m_Disposed, 1) != 0)
        {
            return;
        }

        m_StopCancellation.Cancel();
        Exception? heartbeatException = null;
        Exception? stopException = null;
        try
        {
            await WaitWithCancellationAsync(m_HeartbeatLoopTask, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && m_StopCancellation.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException e) when (cancellationToken.IsCancellationRequested)
        {
            stopException = e;
        }
        catch (Exception e)
        {
            heartbeatException = e;
        }

        try
        {
            if (stopException == null)
            {
                await m_Client.DisconnectAsync(reason, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception e)
        {
            stopException = e;
        }
        finally
        {
            await m_Client.DisposeAsync().ConfigureAwait(false);
            m_StopCancellation.Dispose();
        }

        if (stopException != null)
        {
            ExceptionDispatchInfo.Capture(stopException).Throw();
        }

        if (heartbeatException != null)
        {
            throw new InvalidOperationException("RemoteDebug heartbeat loop ended with an error.", heartbeatException);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref m_Disposed, 1) != 0)
        {
            return;
        }

        m_StopCancellation.Cancel();
        m_Client.Dispose();
        m_StopCancellation.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync("Client disposed.").ConfigureAwait(false);
    }

    private async Task RunHeartbeatLoopAsync()
    {
        var heartbeatIntervalMilliseconds = Math.Max(1, Session.HeartbeatIntervalMilliseconds);
        try
        {
            while (true)
            {
                await Task.Delay(
                        TimeSpan.FromMilliseconds(heartbeatIntervalMilliseconds),
                        m_StopCancellation.Token)
                    .ConfigureAwait(false);
                m_StopCancellation.Token.ThrowIfCancellationRequested();
                await m_Client.HeartbeatAsync(m_StopCancellation.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (m_StopCancellation.IsCancellationRequested)
        {
        }
    }

    private static async Task WaitWithCancellationAsync(Task task, CancellationToken cancellationToken)
    {
        if (task.IsCompleted)
        {
            await task.ConfigureAwait(false);
            return;
        }

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (cancellationToken.Register(static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true), completion))
        {
            if (task != await Task.WhenAny(task, completion.Task).ConfigureAwait(false))
            {
                throw new OperationCanceledException(cancellationToken);
            }
        }

        await task.ConfigureAwait(false);
    }
}
