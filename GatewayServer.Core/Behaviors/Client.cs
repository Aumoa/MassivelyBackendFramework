using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using PacketCore;
using PacketCore.Utility;

namespace GatewayServer.Behaviors;

internal class Client(NetworkStream networkStream, Stream stream, ILogger logger) : IClient, IAsyncDisposable
{
    private readonly NetworkStream m_NetworkStream = networkStream;
    private readonly CancellationTokenSource m_Cancellation = new();
    private bool m_Completion;
    private readonly Channel<Packet> m_RequestsChannel = Channel.CreateUnbounded<Packet>();
    private ExceptionDispatchInfo? m_ExceptionDispatchInfo;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref m_Completion, true, false))
        {
            Aborted?.Invoke();
            Completed?.Invoke();
        }

        await m_NetworkStream.DisposeAsync().ConfigureAwait(false);
        await stream.DisposeAsync().ConfigureAwait(false);
        m_NetworkStream.Dispose();
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
            return obj.Equals(client.m_NetworkStream);
        }
        else if (obj is Socket socket)
        {
            return m_NetworkStream.Equals(socket);
        }

        return false;
    }

    public async IAsyncEnumerable<Packet> ReadPacketsAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var reader = m_RequestsChannel.Reader;
        
        await foreach (var packet in reader.ReadAllAsync(cancellationToken))
        {
            yield return packet;
            PacketPool.Return(packet);
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
            RentedArray<byte> headerArray = default;
            RentedArray<byte> payloadBuffer = default;

            try
            {
                headerArray = await EnsureReadBytesAsync(Packet.PROTOCOL_HEADER_SIZE_IN_BYTES, cancellationToken);
                Packet.EnsureClientHeaderValidation(headerArray, out int protocolType, out int protocolId, out int protocolVersion, out int payloadSize);

                if (payloadSize == 0)
                {
                    payloadBuffer = default;
                }
                else
                {
                    payloadBuffer = await EnsureReadBytesAsync(payloadSize, cancellationToken);
                }

                var packet = PacketPool.Get();
                packet.Initialize(headerArray, payloadBuffer);
                await requestsWriter.WriteAsync(packet, cancellationToken);
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
            finally
            {
                if (headerArray)
                {
                    headerArray.Dispose();
                }
            }
        }

        requestsWriter.Complete();
    }

    private async ValueTask<RentedArray<byte>> EnsureReadBytesAsync(int bytesToRead, CancellationToken cancellationToken)
    {
        var rentArray = RentedArray<byte>.Get(bytesToRead);
        int writepos = 0;

        try
        {
            while (cancellationToken.IsCancellationRequested == false && writepos < bytesToRead)
            {
                int bytesRead = await stream.ReadAsync(rentArray.AsMemory(writepos), cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    if (Interlocked.CompareExchange(ref m_Completion, true, false))
                    {
                        Disconnected?.Invoke();
                        Completed?.Invoke();
                    }

                    throw new OperationCanceledException();
                }

                writepos += bytesRead;
            }

            var temp = rentArray;
            rentArray = default;
            return temp;
        }
        finally
        {
            if (rentArray)
            {
                rentArray.Dispose();
            }
        }
    }

    public event Action? Disconnected;
    public event Action? Aborted;
    public event Action? Completed;

    public event Action<Exception>? CommunicationError;
}
