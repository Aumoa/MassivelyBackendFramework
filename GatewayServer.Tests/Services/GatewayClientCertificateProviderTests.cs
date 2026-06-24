using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using GatewayServer.Options;
using GatewayServer.Services;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GatewayServer.Tests.Services;

public sealed class GatewayClientCertificateProviderTests
{
    [Fact]
    public async Task StartAsync_LoadsPemCertificateAndKeyFromFile()
    {
        using var certificate = CreateServerCertificate();
        using var tempDirectory = new TempDirectory();
        var (certificatePath, keyPath) = await WritePemCertificateAsync(tempDirectory.Path, certificate);
        var provider = CreateProvider(new ConnectionManagerOptions
        {
            CertificatePath = certificatePath,
            CertificateKeyPath = keyPath,
            CertificateSubjectName = string.Empty
        });

        await provider.StartAsync(CancellationToken.None);

        try
        {
            var status = provider.GetStatus();
            Assert.Equal(certificate.Thumbprint, status.Thumbprint);

            using var lease = provider.AcquireLease();
            Assert.True(lease.Certificate.HasPrivateKey);
            Assert.Equal(certificate.Thumbprint, lease.Certificate.Thumbprint);
        }
        finally
        {
            await provider.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ReloadAsync_ReplacesCurrentCertificateAfterFileChange()
    {
        using var firstCertificate = CreateServerCertificate();
        using var secondCertificate = CreateServerCertificate();
        using var tempDirectory = new TempDirectory();
        var (certificatePath, keyPath) = await WritePemCertificateAsync(tempDirectory.Path, firstCertificate);
        var provider = CreateProvider(new ConnectionManagerOptions
        {
            CertificatePath = certificatePath,
            CertificateKeyPath = keyPath,
            CertificateSubjectName = string.Empty
        });

        await provider.StartAsync(CancellationToken.None);

        try
        {
            await WritePemCertificateAsync(certificatePath, keyPath, secondCertificate);
            await provider.ReloadAsync(CancellationToken.None);

            Assert.Equal(secondCertificate.Thumbprint, provider.GetStatus().Thumbprint);
        }
        finally
        {
            await provider.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ReloadAsync_KeepsCurrentCertificateWhenReplacementKeyDoesNotMatch()
    {
        using var firstCertificate = CreateServerCertificate();
        using var secondCertificate = CreateServerCertificate();
        using var tempDirectory = new TempDirectory();
        var (certificatePath, keyPath) = await WritePemCertificateAsync(tempDirectory.Path, firstCertificate);
        var provider = CreateProvider(new ConnectionManagerOptions
        {
            CertificatePath = certificatePath,
            CertificateKeyPath = keyPath,
            CertificateSubjectName = string.Empty
        });

        await provider.StartAsync(CancellationToken.None);

        try
        {
            await WritePemCertificateAsync(certificatePath, keyPath, secondCertificate, firstCertificate);
            await provider.ReloadAsync(CancellationToken.None);

            var status = provider.GetStatus();
            Assert.Equal(firstCertificate.Thumbprint, status.Thumbprint);
            Assert.False(string.IsNullOrWhiteSpace(status.LastReloadFailureMessage));
        }
        finally
        {
            await provider.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ReloadAsync_DoesNotDisposeLeasedCertificateDuringReplacement()
    {
        using var firstCertificate = CreateServerCertificate();
        using var secondCertificate = CreateServerCertificate();
        using var tempDirectory = new TempDirectory();
        var (certificatePath, keyPath) = await WritePemCertificateAsync(tempDirectory.Path, firstCertificate);
        var provider = CreateProvider(new ConnectionManagerOptions
        {
            CertificatePath = certificatePath,
            CertificateKeyPath = keyPath,
            CertificateSubjectName = string.Empty
        });

        await provider.StartAsync(CancellationToken.None);

        try
        {
            using var lease = provider.AcquireLease();
            await WritePemCertificateAsync(certificatePath, keyPath, secondCertificate);
            await provider.ReloadAsync(CancellationToken.None);

            Assert.Equal(firstCertificate.Thumbprint, lease.Certificate.Thumbprint);
            Assert.Equal(secondCertificate.Thumbprint, provider.GetStatus().Thumbprint);
        }
        finally
        {
            await provider.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task StartAsync_PollingReloadsCertificateWhenWatcherMissesFileChange()
    {
        using var firstCertificate = CreateServerCertificate();
        using var secondCertificate = CreateServerCertificate();
        using var tempDirectory = new TempDirectory();
        var (certificatePath, keyPath) = await WritePemCertificateAsync(tempDirectory.Path, firstCertificate);
        var provider = CreateProvider(
            new ConnectionManagerOptions
            {
                CertificatePath = certificatePath,
                CertificateKeyPath = keyPath,
                CertificateSubjectName = string.Empty,
                CertificateReloadPollIntervalMilliseconds = 25,
                CertificateReloadDebounceMilliseconds = 10000
            },
            enableFileWatchers: false);

        await provider.StartAsync(CancellationToken.None);

        try
        {
            await WritePemCertificateAsync(certificatePath, keyPath, secondCertificate);
            await WaitForThumbprintAsync(provider, secondCertificate.Thumbprint);
        }
        finally
        {
            await provider.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task StartAsync_FailsWhenInitialCertificateCannotBeLoaded()
    {
        using var tempDirectory = new TempDirectory();
        var provider = CreateProvider(new ConnectionManagerOptions
        {
            CertificatePath = Path.Combine(tempDirectory.Path, "missing.pem"),
            CertificateKeyPath = Path.Combine(tempDirectory.Path, "missing.key"),
            CertificateSubjectName = string.Empty
        });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await provider.StartAsync(CancellationToken.None));

        Assert.Contains("does not exist", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartAsync_RejectsExpiredCertificateInProduction()
    {
        using var certificate = CreateServerCertificate(
            DateTimeOffset.UtcNow.AddHours(-2),
            DateTimeOffset.UtcNow.AddHours(-1));
        using var tempDirectory = new TempDirectory();
        var (certificatePath, keyPath) = await WritePemCertificateAsync(tempDirectory.Path, certificate);
        var provider = CreateProvider(
            new ConnectionManagerOptions
            {
                CertificatePath = certificatePath,
                CertificateKeyPath = keyPath,
                CertificateSubjectName = string.Empty
            },
            new TestHostEnvironment
            {
                EnvironmentName = Environments.Production
            });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await provider.StartAsync(CancellationToken.None));

        Assert.Contains("outside its validity period", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartAsync_RejectsUntrustedCertificateInProduction()
    {
        using var certificate = CreateServerCertificate();
        using var tempDirectory = new TempDirectory();
        var (certificatePath, keyPath) = await WritePemCertificateAsync(tempDirectory.Path, certificate);
        var provider = CreateProvider(
            new ConnectionManagerOptions
            {
                CertificatePath = certificatePath,
                CertificateKeyPath = keyPath,
                CertificateSubjectName = string.Empty
            },
            new TestHostEnvironment
            {
                EnvironmentName = Environments.Production
            });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await provider.StartAsync(CancellationToken.None));

        Assert.Contains("is not trusted", exception.Message, StringComparison.Ordinal);
    }

    private static GatewayClientCertificateProvider CreateProvider(
        ConnectionManagerOptions options,
        IHostEnvironment? environment = null,
        bool enableFileWatchers = false,
        IGatewayClientCertificateLoader? certificateLoader = null)
    {
        return new GatewayClientCertificateProvider(
            Microsoft.Extensions.Options.Options.Create(options),
            certificateLoader ?? new GatewayClientCertificateLoader(),
            NullLogger<GatewayClientCertificateProvider>.Instance,
            environment ?? new TestHostEnvironment
            {
                EnvironmentName = Environments.Development
            },
            enableFileWatchers);
    }

    private static async Task WaitForThumbprintAsync(
        GatewayClientCertificateProvider provider,
        string thumbprint)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!timeout.IsCancellationRequested)
        {
            if (string.Equals(provider.GetStatus().Thumbprint, thumbprint, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                await Task.Delay(25, timeout.Token);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                break;
            }
        }

        throw new TimeoutException($"Gateway client TLS certificate thumbprint did not become {thumbprint}.");
    }

    private static async Task<(string CertificatePath, string KeyPath)> WritePemCertificateAsync(
        string directory,
        X509Certificate2 certificate)
    {
        var certificatePath = Path.Combine(directory, "tls.crt");
        var keyPath = Path.Combine(directory, "tls.key");
        await WritePemCertificateAsync(certificatePath, keyPath, certificate);
        return (certificatePath, keyPath);
    }

    private static async Task WritePemCertificateAsync(
        string certificatePath,
        string keyPath,
        X509Certificate2 certificate,
        X509Certificate2? keySourceCertificate = null)
    {
        using var privateKey = (keySourceCertificate ?? certificate).GetRSAPrivateKey() ??
            throw new InvalidOperationException("Test certificate is missing a private key.");
        await File.WriteAllTextAsync(certificatePath, certificate.ExportCertificatePem());
        await File.WriteAllTextAsync(keyPath, privateKey.ExportPkcs8PrivateKeyPem());
    }

    private static X509Certificate2 CreateServerCertificate()
    {
        return CreateServerCertificate(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddHours(1));
    }

    private static X509Certificate2 CreateServerCertificate(
        DateTimeOffset notBefore,
        DateTimeOffset notAfter)
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

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;

        public string ApplicationName { get; set; } = "GatewayServer.Tests";

        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "gateway-client-cert-provider-" + Guid.NewGuid().ToString("N"));
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
