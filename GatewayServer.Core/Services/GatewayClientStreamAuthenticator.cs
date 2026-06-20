using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace GatewayServer.Services;

internal interface IGatewayClientStreamAuthenticator
{
    ValueTask<Stream> AuthenticateAsync(
        NetworkStream networkStream,
        X509Certificate2 serverCertificate,
        CancellationToken cancellationToken);
}

internal sealed class GatewayClientTlsStreamAuthenticator : IGatewayClientStreamAuthenticator
{
    public async ValueTask<Stream> AuthenticateAsync(
        NetworkStream networkStream,
        X509Certificate2 serverCertificate,
        CancellationToken cancellationToken)
    {
        if (networkStream == null)
        {
            throw new ArgumentNullException(nameof(networkStream));
        }

        if (serverCertificate == null)
        {
            throw new ArgumentNullException(nameof(serverCertificate));
        }

        var sslStream = new SslStream(networkStream, leaveInnerStreamOpen: false);
        try
        {
            await sslStream.AuthenticateAsServerAsync(
                serverCertificate,
                clientCertificateRequired: false,
                enabledSslProtocols: SslProtocols.Tls12 | SslProtocols.Tls13,
                checkCertificateRevocation: true).ConfigureAwait(false);
            return sslStream;
        }
        catch
        {
            await sslStream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
