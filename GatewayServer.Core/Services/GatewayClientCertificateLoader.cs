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
            () => Load(options, allowUntrustedCertificate),
            cancellationToken);
    }

    private static X509Certificate2 Load(
        ConnectionManagerOptions options,
        bool allowUntrustedCertificate)
    {
        return string.IsNullOrWhiteSpace(options.CertificatePath)
            ? LoadFromStore(options, allowUntrustedCertificate)
            : LoadFromFile(options, allowUntrustedCertificate);
    }

    private static X509Certificate2 LoadFromFile(
        ConnectionManagerOptions options,
        bool allowUntrustedCertificate)
    {
        var certificatePath = options.CertificatePath?.Trim();
        if (string.IsNullOrWhiteSpace(certificatePath))
        {
            throw new InvalidOperationException("Gateway client TLS certificate path is required.");
        }

        if (!File.Exists(certificatePath))
        {
            throw new InvalidOperationException($"Gateway client TLS certificate file '{certificatePath}' does not exist.");
        }

        var certificate = IsPemCertificate(options, certificatePath)
            ? LoadPemCertificate(options, certificatePath)
            : LoadPkcs12Certificate(options, certificatePath);

        EnsureUsableCertificate(certificate, $"file '{certificatePath}'", allowUntrustedCertificate);
        return certificate;
    }

    private static X509Certificate2 LoadPkcs12Certificate(
        ConnectionManagerOptions options,
        string certificatePath)
    {
        return X509CertificateLoader.LoadPkcs12FromFile(
            certificatePath,
            GetCertificatePassword(options),
            X509KeyStorageFlags.EphemeralKeySet);
    }

    private static X509Certificate2 LoadPemCertificate(
        ConnectionManagerOptions options,
        string certificatePath)
    {
        var certificateKeyPath = string.IsNullOrWhiteSpace(options.CertificateKeyPath)
            ? certificatePath
            : options.CertificateKeyPath.Trim();
        if (!File.Exists(certificateKeyPath))
        {
            throw new InvalidOperationException($"Gateway client TLS certificate key file '{certificateKeyPath}' does not exist.");
        }

        var password = GetCertificatePassword(options);
        return string.IsNullOrEmpty(password)
            ? X509Certificate2.CreateFromPemFile(certificatePath, certificateKeyPath)
            : X509Certificate2.CreateFromEncryptedPemFile(certificatePath, password.AsSpan(), certificateKeyPath);
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

        EnsureUsableCertificate(certificate, $"{options.CertificateStoreLocation}/{options.CertificateStoreName}", allowUntrustedCertificate);
        return certificate;
    }

    private static string? GetCertificatePassword(ConnectionManagerOptions options)
    {
        return string.IsNullOrEmpty(options.CertificatePassword)
            ? null
            : options.CertificatePassword;
    }

    private static bool IsPemCertificate(ConnectionManagerOptions options, string certificatePath)
    {
        return !string.IsNullOrWhiteSpace(options.CertificateKeyPath) ||
               string.Equals(Path.GetExtension(certificatePath), ".pem", StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureUsableCertificate(
        X509Certificate2 certificate,
        string source,
        bool allowUntrustedCertificate)
    {
        if (!certificate.HasPrivateKey)
        {
            certificate.Dispose();
            throw new InvalidOperationException($"Gateway client TLS certificate loaded from {source} does not contain a private key.");
        }

        var now = DateTimeOffset.UtcNow;
        var notBefore = new DateTimeOffset(certificate.NotBefore.ToUniversalTime(), TimeSpan.Zero);
        var notAfter = new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero);
        if (now < notBefore || now > notAfter)
        {
            certificate.Dispose();
            throw new InvalidOperationException($"Gateway client TLS certificate loaded from {source} is outside its validity period.");
        }

        if (!allowUntrustedCertificate && !IsTrusted(certificate))
        {
            certificate.Dispose();
            throw new InvalidOperationException($"Gateway client TLS certificate loaded from {source} is not trusted.");
        }
    }

    private static bool IsTrusted(X509Certificate2 certificate)
    {
        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        return chain.Build(certificate);
    }
}
