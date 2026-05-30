using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace MasterServer.ControlPlane;

public static class MasterEndpointResolver
{
    public static async ValueTask<IPAddress> ResolveBindAddressAsync(
        string host,
        CancellationToken cancellationToken = default)
    {
        var addresses = await ResolveHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
        return addresses[0];
    }

    public static async ValueTask<Socket> ConnectTcpAsync(
        string host,
        int port,
        CancellationToken cancellationToken = default)
    {
        var addresses = await ResolveHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
        Exception? lastError = null;

        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true
            };

            try
            {
                using var cancellation = cancellationToken.Register(static state => ((Socket)state!).Dispose(), socket);
                await socket.ConnectAsync(new IPEndPoint(address, port)).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return socket;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }
            catch (SocketException e) when (!cancellationToken.IsCancellationRequested)
            {
                lastError = e;
                socket.Dispose();
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        throw new InvalidOperationException($"Unable to connect to {host}:{port}.", lastError);
    }

    private static async ValueTask<IPAddress[]> ResolveHostAddressesAsync(
        string host,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new ArgumentException("Host is required.", nameof(host));
        }

        if (IPAddress.TryParse(host, out var address))
        {
            return new[] { address };
        }

        var resolved = await WaitWithCancellationAsync(Dns.GetHostAddressesAsync(host), cancellationToken)
            .ConfigureAwait(false);
        var addresses = resolved
            .Where(static item => item.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
            .Distinct()
            .ToArray();

        if (addresses.Length == 0)
        {
            throw new InvalidOperationException($"Host '{host}' did not resolve to an IPv4 or IPv6 address.");
        }

        return addresses;
    }

    private static async Task<T> WaitWithCancellationAsync<T>(Task<T> task, CancellationToken cancellationToken)
    {
        if (!cancellationToken.CanBeCanceled)
        {
            return await task.ConfigureAwait(false);
        }

        var cancellationTask = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(
            static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true),
            cancellationTask);

        var completed = await Task.WhenAny(task, cancellationTask.Task).ConfigureAwait(false);
        if (!ReferenceEquals(completed, task))
        {
            throw new OperationCanceledException(cancellationToken);
        }

        return await task.ConfigureAwait(false);
    }
}
