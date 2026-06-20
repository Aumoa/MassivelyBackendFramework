using System.Security.Cryptography.X509Certificates;

namespace GatewayServer.Options;

public record ConnectionManagerOptions
{
    public string IPAddress { get; set; } = "::1";

    public int Port { get; set; } = 11501;

    public bool UseTls { get; set; } = true;

    public string CertificateSubjectName { get; set; } = "localhost";

    public StoreName CertificateStoreName { get; set; } = StoreName.My;

    public StoreLocation CertificateStoreLocation { get; set; } = StoreLocation.CurrentUser;
}
