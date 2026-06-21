using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using PacketCore;

namespace GatewayServer.Behaviors;

internal class Client(
    NetworkStream networkStream,
    Stream stream,
    ILogger logger,
    int maxQueuedPackets = 1024,
    int idleTimeoutMilliseconds = 0) : IClient, IAsyncDisposable
{
    private readonly NetworkStream m_NetworkStream = networkStream;
    private readonly CancellationTokenSource m_Cancellation = new();
    private readonly Channel<PacketFrame> m_RequestsChannel = CreateRequestsChannel(maxQueuedPackets);
    private readonly SemaphoreSlim m_WriteLock = new(1, 1);
    private ExceptionDispatchInfo? m_ExceptionDispatchInfo;
    private int m_Completion;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref m_Completion, 1, 0) == 0)
        {
            await m_Cancellation.CancelAsync().ConfigureAwait(false);
            Aborted?.Invoke();
            Completed?.Invoke();
        }

        await stream.DisposeAsync().ConfigureAwait(false);
        await m_NetworkStream.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
        m_ExceptionDispatchInfo?.Throw();
    }

    public override int GetHashCode()
    {
        return m_NetworkStream.GetHashCode();
    }

    public override bool Equals(object? obj)
    {
        if (obj is Client client)
        {
            return m_NetworkStream.Equals(client.m_NetworkStream);
        }

        if (obj is Socket socket)
        {
            return m_NetworkStream.Equals(socket);
        }

        return false;
    }

    public async IAsyncEnumerable<PacketFrame> ReadPacketsAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var reader = m_RequestsChannel.Reader;

        await foreach (var packet in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                yield return packet;
            }
            finally
            {
                packet.Dispose();
            }
        }
    }

    public ValueTask WriteAsync(PacketFrame frame, CancellationToken cancellationToken)
    {
        return WriteInternalAsync(frame, cancellationToken);
    }

    private async ValueTask WriteInternalAsync(PacketFrame frame, CancellationToken cancellationToken)
    {
        await m_WriteLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await PacketFrameWriter.WriteAsync(stream, frame, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            m_WriteLock.Release();
        }
    }

    public async void Start()
    {
        try
        {
            await StartInternalAsync(m_Cancellation.Token).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Unhandled exception in client communication loop.");
            m_ExceptionDispatchInfo = ExceptionDispatchInfo.Capture(e);
        }
        finally
        {
            try
            {
                await DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error occurred while disposing client.");
            }
        }
    }

    private async Task StartInternalAsync(CancellationToken cancellationToken)
    {
        var requestsWriter = m_RequestsChannel.Writer;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var readCancellation = CreateReadCancellation(cancellationToken);
                var packet = await PacketFrameReader.ReadAsync(
                    stream,
                    PacketReadPolicy.UntrustedClient,
                    readCancellation.Token).ConfigureAwait(false);

                if (packet == null)
                {
                    MarkDisconnected();
                    requestsWriter.Complete();
                    return;
                }

                bool queued = false;
                try
                {
                    await requestsWriter.WriteAsync(packet, cancellationToken).ConfigureAwait(false);
                    queued = true;
                }
                finally
                {
                    if (!queued)
                    {
                        packet.Dispose();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                requestsWriter.Complete();
                return;
            }
            catch (Exception e)
            {
                requestsWriter.Complete(e);
                CommunicationError?.Invoke(e);
                logger.LogError(e, "Error occurred while reading packet from client.");
                return;
            }
        }

        requestsWriter.Complete();
    }

    private void MarkDisconnected()
    {
        if (Interlocked.CompareExchange(ref m_Completion, 1, 0) == 0)
        {
            Disconnected?.Invoke();
            Completed?.Invoke();
        }
    }

    public event Action? Disconnected;
    public event Action? Aborted;
    public event Action? Completed;

    public event Action<Exception>? CommunicationError;

    internal static Channel<PacketFrame> CreateRequestsChannel(int maxQueuedPackets)
    {
        if (maxQueuedPackets <= 0)
        {
            return Channel.CreateUnbounded<PacketFrame>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = false
            });
        }

        return Channel.CreateBounded<PacketFrame>(new BoundedChannelOptions(maxQueuedPackets)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = false
        });
    }

    private CancellationTokenSource CreateReadCancellation(CancellationToken cancellationToken)
    {
        var readCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (idleTimeoutMilliseconds > 0)
        {
            readCancellation.CancelAfter(TimeSpan.FromMilliseconds(idleTimeoutMilliseconds));
        }

        return readCancellation;
    }
}
