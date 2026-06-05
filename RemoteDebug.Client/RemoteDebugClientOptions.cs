using System;
using System.Net.Security;
using System.Security.Authentication;
using RemoteDebugServer.Protocols;

namespace RemoteDebug;

public sealed class RemoteDebugClientOptions
{
    public string Host { get; set; } = string.Empty;

    public int Port { get; set; }

    public bool UseTls { get; set; } = true;

    public string? ServerName { get; set; }

    public bool CheckCertificateRevocation { get; set; } = true;

    public SslProtocols EnabledSslProtocols { get; set; } = SslProtocols.None;

    public RemoteCertificateValidationCallback? ServerCertificateValidationCallback { get; set; }

    public int HandshakeTimeoutMilliseconds { get; set; } = 10000;

    public string ClientId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string ClientVersion { get; set; } = string.Empty;

    public string UnityVersion { get; set; } = string.Empty;

    public RemoteDebugCapabilities Capabilities { get; set; } =
        RemoteDebugCapabilities.LogStreaming |
        RemoteDebugCapabilities.FileTransfer |
        RemoteDebugCapabilities.RemoteControl;

    public string SharedSecret { get; set; } = string.Empty;

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Host))
        {
            throw new InvalidOperationException("RemoteDebug host is required.");
        }

        if (Port <= 0 || Port > 65535)
        {
            throw new InvalidOperationException("RemoteDebug port must be between 1 and 65535.");
        }

        if (HandshakeTimeoutMilliseconds <= 0)
        {
            throw new InvalidOperationException("RemoteDebug handshake timeout must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(ClientId))
        {
            throw new InvalidOperationException("RemoteDebug client id is required.");
        }

        if (DisplayName == null)
        {
            throw new InvalidOperationException("RemoteDebug display name cannot be null.");
        }

        if (ClientVersion == null)
        {
            throw new InvalidOperationException("RemoteDebug client version cannot be null.");
        }

        if (UnityVersion == null)
        {
            throw new InvalidOperationException("RemoteDebug Unity version cannot be null.");
        }

        if (string.IsNullOrWhiteSpace(SharedSecret))
        {
            throw new InvalidOperationException("RemoteDebug shared secret is required.");
        }
    }
}
