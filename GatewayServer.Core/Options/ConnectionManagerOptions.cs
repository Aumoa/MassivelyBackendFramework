using System.Security.Cryptography.X509Certificates;

namespace GatewayServer.Options;

public record ConnectionManagerOptions
{
    public string IPAddress { get; set; } = "::1";

    public int Port { get; set; } = 11501;

    public bool UseTls { get; set; } = true;

    public int MaxQueuedClientPackets { get; set; } = 1024;

    public int ClientIdleTimeoutMilliseconds { get; set; } = 120000;

    public int ClientAuthenticationTimeoutMilliseconds { get; set; } = 30000;

    public string CertificateSubjectName { get; set; } = "localhost";

    public string? CertificatePath { get; set; }

    public string? CertificateKeyPath { get; set; }

    public string? CertificatePassword { get; set; }

    public StoreName CertificateStoreName { get; set; } = StoreName.My;

    public StoreLocation CertificateStoreLocation { get; set; } = StoreLocation.CurrentUser;
}
