using System.Security.Cryptography.X509Certificates;
using GatewayServer.Options;

namespace GatewayServer.Services;

internal interface IGatewayClientCertificateLoader
{
    Task<X509Certificate2> LoadAsync(
        ConnectionManagerOptions options,
        bool allowUntrustedCertificate,
        CancellationToken cancellationToken);
}

internal sealed class GatewayClientCertificateLoader : IGatewayClientCertificateLoader
{
    public Task<X509Certificate2> LoadAsync(
        ConnectionManagerOptions options,
        bool allowUntrustedCertificate,
        CancellationToken cancellationToken)
    {
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        return Task.Run(
            () => LoadFromStore(options, allowUntrustedCertificate),
            cancellationToken);
    }

    private static X509Certificate2 LoadFromStore(
        ConnectionManagerOptions options,
        bool allowUntrustedCertificate)
    {
        if (string.IsNullOrWhiteSpace(options.CertificateSubjectName))
        {
            throw new InvalidOperationException("Gateway client TLS certificate subject name is required.");
        }

        using var store = new X509Store(options.CertificateStoreName, options.CertificateStoreLocation);
        store.Open(OpenFlags.ReadOnly);

        var candidates = store.Certificates.Find(
            X509FindType.FindBySubjectName,
            options.CertificateSubjectName,
            validOnly: !allowUntrustedCertificate);
        var now = DateTimeOffset.UtcNow;
        var certificate = candidates
            .OfType<X509Certificate2>()
            .Where(candidate => candidate.HasPrivateKey)
            .Where(candidate => now >= candidate.NotBefore && now <= candidate.NotAfter)
            .OrderByDescending(static candidate => candidate.NotAfter)
            .FirstOrDefault();

        if (certificate == null)
        {
            var trustMode = allowUntrustedCertificate
                ? "development certificates are allowed"
                : "only trusted certificates are allowed";
            throw new InvalidOperationException(
                $"No usable Gateway client TLS certificate was found for subject '{options.CertificateSubjectName}' in {options.CertificateStoreLocation}/{options.CertificateStoreName}; {trustMode}.");
        }

        return certificate;
    }
}
