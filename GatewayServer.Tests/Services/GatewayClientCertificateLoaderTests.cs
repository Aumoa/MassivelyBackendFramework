using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using GatewayServer.Options;
using GatewayServer.Services;
using Xunit;

namespace GatewayServer.Tests.Services;

public sealed class GatewayClientCertificateLoaderTests
{
    [Fact]
    public async Task LoadAsync_LoadsPkcs12CertificateFromFile()
    {
        using var certificate = CreateServerCertificate();
        using var tempDirectory = new TempDirectory();
        var certificatePath = Path.Combine(tempDirectory.Path, "gateway.pfx");
        var password = "test-password";
        await File.WriteAllBytesAsync(
            certificatePath,
            certificate.Export(X509ContentType.Pkcs12, password));

        using var loaded = await LoadAsync(new ConnectionManagerOptions
        {
            CertificatePath = certificatePath,
            CertificatePassword = password,
            CertificateSubjectName = string.Empty
        });

        Assert.True(loaded.HasPrivateKey);
        Assert.Equal(certificate.Thumbprint, loaded.Thumbprint);
    }

    [Fact]
    public async Task LoadAsync_LoadsPemCertificateAndKeyFromFile()
    {
        using var certificate = CreateServerCertificate();
        using var privateKey = certificate.GetRSAPrivateKey() ?? throw new InvalidOperationException("Test certificate is missing a private key.");
        using var tempDirectory = new TempDirectory();
        var certificatePath = Path.Combine(tempDirectory.Path, "gateway.pem");
        var certificateKeyPath = Path.Combine(tempDirectory.Path, "gateway.key");
        await File.WriteAllTextAsync(certificatePath, certificate.ExportCertificatePem());
        await File.WriteAllTextAsync(certificateKeyPath, privateKey.ExportPkcs8PrivateKeyPem());

        using var loaded = await LoadAsync(new ConnectionManagerOptions
        {
            CertificatePath = certificatePath,
            CertificateKeyPath = certificateKeyPath,
            CertificateSubjectName = string.Empty
        });

        Assert.True(loaded.HasPrivateKey);
        Assert.Equal(certificate.Thumbprint, loaded.Thumbprint);
    }

    [Fact]
    public async Task LoadAsync_RejectsFileCertificateWithoutPrivateKey()
    {
        using var certificate = CreateServerCertificate();
        using var publicCertificate = X509CertificateLoader.LoadCertificate(certificate.RawData);
        using var tempDirectory = new TempDirectory();
        var certificatePath = Path.Combine(tempDirectory.Path, "gateway.pfx");
        await File.WriteAllBytesAsync(
            certificatePath,
            publicCertificate.Export(X509ContentType.Pkcs12));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => LoadAsync(new ConnectionManagerOptions
        {
            CertificatePath = certificatePath,
            CertificateSubjectName = string.Empty
        }));

        Assert.Contains("does not contain a private key", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_RejectsUntrustedFileCertificateOutsideDevelopment()
    {
        using var certificate = CreateServerCertificate();
        using var tempDirectory = new TempDirectory();
        var certificatePath = Path.Combine(tempDirectory.Path, "gateway.pfx");
        await File.WriteAllBytesAsync(
            certificatePath,
            certificate.Export(X509ContentType.Pkcs12));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => LoadAsync(
            new ConnectionManagerOptions
            {
                CertificatePath = certificatePath,
                CertificateSubjectName = string.Empty
            },
            allowUntrustedCertificate: false));

        Assert.Contains("is not trusted", exception.Message, StringComparison.Ordinal);
    }

    private static Task<X509Certificate2> LoadAsync(
        ConnectionManagerOptions options,
        bool allowUntrustedCertificate = true)
    {
        var loader = new GatewayClientCertificateLoader();
        return loader.LoadAsync(options, allowUntrustedCertificate, CancellationToken.None);
    }

    private static X509Certificate2 CreateServerCertificate()
    {
        var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=localhost",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
            false));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection
            {
                new Oid("1.3.6.1.5.5.7.3.1")
            },
            false));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        var subjectAlternativeNames = new SubjectAlternativeNameBuilder();
        subjectAlternativeNames.AddDnsName("localhost");
        request.CertificateExtensions.Add(subjectAlternativeNames.Build());

        var notBefore = DateTimeOffset.UtcNow.AddMinutes(-1);
        var notAfter = DateTimeOffset.UtcNow.AddHours(1);
        var serialNumber = RandomNumberGenerator.GetBytes(16);
        var generator = X509SignatureGenerator.CreateForRSA(rsa, RSASignaturePadding.Pkcs1);
        var certificate = request.Create(
            request.SubjectName,
            generator,
            notBefore,
            notAfter,
            serialNumber);
        return certificate.CopyWithPrivateKey(rsa);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "gateway-client-cert-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
