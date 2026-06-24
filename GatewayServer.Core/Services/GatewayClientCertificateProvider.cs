using System.Security.Cryptography.X509Certificates;
using GatewayServer.Options;
using MasterServer.ControlPlane;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GatewayServer.Services;

internal interface IGatewayClientCertificateProvider
{
    GatewayClientCertificateLease AcquireLease();

    GatewayClientCertificateStatus GetStatus();

    ServiceAdminStatusItem[] GetStatusItems();
}

internal sealed class GatewayClientCertificateProvider : IHostedService, IGatewayClientCertificateProvider, IDisposable
{
    private readonly IOptions<ConnectionManagerOptions> m_Options;
    private readonly IGatewayClientCertificateLoader m_CertificateLoader;
    private readonly ILogger<GatewayClientCertificateProvider> m_Logger;
    private readonly IHostEnvironment m_Environment;
    private readonly bool m_EnableFileWatchers;
    private readonly object m_Gate = new();
    private readonly List<FileSystemWatcher> m_FileWatchers = [];
    private readonly SemaphoreSlim m_ReloadLock = new(1, 1);

    private CancellationTokenSource? m_ReloadCancellation;
    private Task? m_PollTask;
    private CertificateEntry? m_Current;
    private DateTimeOffset? m_LastReloadSucceededAt;
    private string? m_LastReloadFailureMessage;
    private int m_ReloadDebounceGeneration;
    private bool m_ReloadEnabled;
    private bool m_Started;

    public GatewayClientCertificateProvider(
        IOptions<ConnectionManagerOptions> options,
        IGatewayClientCertificateLoader certificateLoader,
        ILogger<GatewayClientCertificateProvider> logger,
        IHostEnvironment environment)
        : this(options, certificateLoader, logger, environment, enableFileWatchers: true)
    {
    }

    internal GatewayClientCertificateProvider(
        IOptions<ConnectionManagerOptions> options,
        IGatewayClientCertificateLoader certificateLoader,
        ILogger<GatewayClientCertificateProvider> logger,
        IHostEnvironment environment,
        bool enableFileWatchers)
    {
        m_Options = options ?? throw new ArgumentNullException(nameof(options));
        m_CertificateLoader = certificateLoader ?? throw new ArgumentNullException(nameof(certificateLoader));
        m_Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        m_Environment = environment ?? throw new ArgumentNullException(nameof(environment));
        m_EnableFileWatchers = enableFileWatchers;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var connectionOptions = m_Options.Value;
        if (!connectionOptions.UseTls)
        {
            return;
        }

        var certificate = await m_CertificateLoader
            .LoadAsync(connectionOptions, m_Environment.IsDevelopment(), cancellationToken)
            .ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        lock (m_Gate)
        {
            m_Current = new CertificateEntry(certificate);
            m_LastReloadSucceededAt = now;
            m_LastReloadFailureMessage = null;
            m_ReloadEnabled = connectionOptions.CertificateReloadEnabled &&
                !string.IsNullOrWhiteSpace(connectionOptions.CertificatePath);
            m_Started = true;
        }

        if (!m_ReloadEnabled)
        {
            return;
        }

        m_ReloadCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (m_EnableFileWatchers)
        {
            StartFileWatchers(connectionOptions);
        }

        m_PollTask = PollForCertificateChangesAsync(m_ReloadCancellation.Token);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        CancellationTokenSource? reloadCancellation;
        Task? pollTask;
        CertificateEntry? current;

        lock (m_Gate)
        {
            reloadCancellation = m_ReloadCancellation;
            pollTask = m_PollTask;
            current = m_Current;
            m_ReloadCancellation = null;
            m_PollTask = null;
            m_Current = null;
            m_Started = false;
        }

        if (reloadCancellation != null)
        {
            await reloadCancellation.CancelAsync().ConfigureAwait(false);
        }

        StopFileWatchers();

        if (pollTask != null)
        {
            await WaitForShutdownAsync(pollTask, cancellationToken).ConfigureAwait(false);
        }

        reloadCancellation?.Dispose();
        current?.Retire();
    }

    public GatewayClientCertificateLease AcquireLease()
    {
        lock (m_Gate)
        {
            if (m_Current == null)
            {
                throw new InvalidOperationException("Gateway client TLS certificate has not been loaded.");
            }

            m_Current.AddLease();
            return new GatewayClientCertificateLease(m_Current);
        }
    }

    public GatewayClientCertificateStatus GetStatus()
    {
        lock (m_Gate)
        {
            if (m_Current == null)
            {
                return GatewayClientCertificateStatus.NotLoaded(m_ReloadEnabled, m_LastReloadFailureMessage);
            }

            var certificate = m_Current.Certificate;
            return new GatewayClientCertificateStatus(
                certificate.Thumbprint,
                new DateTimeOffset(certificate.NotBefore.ToUniversalTime(), TimeSpan.Zero),
                new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero),
                m_LastReloadSucceededAt,
                m_LastReloadFailureMessage,
                m_ReloadEnabled);
        }
    }

    internal async Task ReloadAsync(CancellationToken cancellationToken)
    {
        await m_ReloadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var connectionOptions = m_Options.Value;
            var certificate = await m_CertificateLoader
                .LoadAsync(connectionOptions, m_Environment.IsDevelopment(), cancellationToken)
                .ConfigureAwait(false);

            CertificateEntry? previous;
            var replaced = false;
            lock (m_Gate)
            {
                if (!m_Started)
                {
                    certificate.Dispose();
                    return;
                }

                if (m_Current != null &&
                    IsSameCertificate(m_Current.Certificate, certificate))
                {
                    certificate.Dispose();
                    m_LastReloadFailureMessage = null;
                    return;
                }

                previous = m_Current;
                m_Current = new CertificateEntry(certificate);
                m_LastReloadSucceededAt = DateTimeOffset.UtcNow;
                m_LastReloadFailureMessage = null;
                replaced = true;
            }

            previous?.Retire();

            if (replaced)
            {
                m_Logger.LogInformation(
                    "Gateway client TLS certificate was reloaded. Thumbprint={Thumbprint}, NotAfter={NotAfter}.",
                    certificate.Thumbprint,
                    certificate.NotAfter.ToUniversalTime().ToString("O"));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            lock (m_Gate)
            {
                m_LastReloadFailureMessage = e.Message;
            }

            m_Logger.LogWarning(
                e,
                "Gateway client TLS certificate reload failed; continuing to use the current certificate.");
        }
        finally
        {
            m_ReloadLock.Release();
        }
    }

    public ServiceAdminStatusItem[] GetStatusItems()
    {
        var status = GetStatus();
        var items = new List<ServiceAdminStatusItem>
        {
            new("Gateway TLS certificate", "Reload enabled", status.ReloadEnabled ? "Yes" : "No")
        };

        if (!string.IsNullOrWhiteSpace(status.Thumbprint))
        {
            items.Add(new ServiceAdminStatusItem("Gateway TLS certificate", "Thumbprint", status.Thumbprint));
            items.Add(new ServiceAdminStatusItem("Gateway TLS certificate", "Not before", status.NotBefore?.LocalDateTime.ToString("O") ?? string.Empty));
            items.Add(new ServiceAdminStatusItem("Gateway TLS certificate", "Not after", status.NotAfter?.LocalDateTime.ToString("O") ?? string.Empty));
        }

        if (status.LastReloadSucceededAt.HasValue)
        {
            items.Add(new ServiceAdminStatusItem("Gateway TLS certificate", "Last reload success", status.LastReloadSucceededAt.Value.LocalDateTime.ToString("O")));
        }

        if (!string.IsNullOrWhiteSpace(status.LastReloadFailureMessage))
        {
            items.Add(new ServiceAdminStatusItem("Gateway TLS certificate", "Last reload failure", status.LastReloadFailureMessage));
        }

        return [.. items];
    }

    public void Dispose()
    {
        StopFileWatchers();
        m_ReloadCancellation?.Cancel();
        m_ReloadCancellation?.Dispose();

        CertificateEntry? current;
        lock (m_Gate)
        {
            current = m_Current;
            m_Current = null;
            m_Started = false;
        }

        current?.Retire();
    }

    private void StartFileWatchers(ConnectionManagerOptions options)
    {
        foreach (var directory in GetWatchedDirectories(options))
        {
            try
            {
                var watcher = new FileSystemWatcher(directory)
                {
                    IncludeSubdirectories = false,
                    NotifyFilter =
                        NotifyFilters.CreationTime |
                        NotifyFilters.DirectoryName |
                        NotifyFilters.FileName |
                        NotifyFilters.LastWrite |
                        NotifyFilters.Size
                };

                watcher.Changed += OnCertificateFileChanged;
                watcher.Created += OnCertificateFileChanged;
                watcher.Deleted += OnCertificateFileChanged;
                watcher.Renamed += OnCertificateFileRenamed;
                watcher.Error += OnCertificateFileWatcherError;
                watcher.EnableRaisingEvents = true;
                m_FileWatchers.Add(watcher);
            }
            catch (Exception e)
            {
                m_Logger.LogWarning(
                    e,
                    "Gateway client TLS certificate directory watcher could not be started; polling fallback remains active. Directory={Directory}.",
                    directory);
            }
        }
    }

    private void StopFileWatchers()
    {
        foreach (var watcher in m_FileWatchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Changed -= OnCertificateFileChanged;
            watcher.Created -= OnCertificateFileChanged;
            watcher.Deleted -= OnCertificateFileChanged;
            watcher.Renamed -= OnCertificateFileRenamed;
            watcher.Error -= OnCertificateFileWatcherError;
            watcher.Dispose();
        }

        m_FileWatchers.Clear();
    }

    private void OnCertificateFileChanged(object sender, FileSystemEventArgs e)
    {
        QueueDebouncedReload();
    }

    private void OnCertificateFileRenamed(object sender, RenamedEventArgs e)
    {
        QueueDebouncedReload();
    }

    private void OnCertificateFileWatcherError(object sender, ErrorEventArgs e)
    {
        m_Logger.LogWarning(
            e.GetException(),
            "Gateway client TLS certificate file watcher failed; polling fallback remains active.");
    }

    private void QueueDebouncedReload()
    {
        CancellationToken cancellationToken;
        lock (m_Gate)
        {
            if (m_ReloadCancellation == null ||
                m_ReloadCancellation.IsCancellationRequested)
            {
                return;
            }

            cancellationToken = m_ReloadCancellation.Token;
        }

        var generation = Interlocked.Increment(ref m_ReloadDebounceGeneration);
        _ = ReloadAfterDebounceAsync(generation, cancellationToken);
    }

    private async Task ReloadAfterDebounceAsync(
        int generation,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(
                TimeSpan.FromMilliseconds(Math.Max(1, m_Options.Value.CertificateReloadDebounceMilliseconds)),
                cancellationToken).ConfigureAwait(false);
            if (Volatile.Read(ref m_ReloadDebounceGeneration) != generation)
            {
                return;
            }

            await ReloadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task PollForCertificateChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(GetPollInterval());
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await ReloadAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private TimeSpan GetPollInterval()
    {
        return TimeSpan.FromMilliseconds(Math.Max(1, m_Options.Value.CertificateReloadPollIntervalMilliseconds));
    }

    private static string[] GetWatchedDirectories(ConnectionManagerOptions options)
    {
        var directories = new HashSet<string>(StringComparer.Ordinal);
        AddWatchedDirectory(directories, options.CertificatePath);
        AddWatchedDirectory(directories, options.CertificateKeyPath);
        return [.. directories];
    }

    private static void AddWatchedDirectory(HashSet<string> directories, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var fullPath = Path.GetFullPath(path.Trim());
        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        directories.Add(directory);
    }

    private static bool IsSameCertificate(
        X509Certificate2 left,
        X509Certificate2 right)
    {
        return string.Equals(left.Thumbprint, right.Thumbprint, StringComparison.OrdinalIgnoreCase) &&
            left.NotBefore.ToUniversalTime() == right.NotBefore.ToUniversalTime() &&
            left.NotAfter.ToUniversalTime() == right.NotAfter.ToUniversalTime();
    }

    private static async Task WaitForShutdownAsync(Task task, CancellationToken cancellationToken)
    {
        try
        {
            await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    internal sealed class CertificateEntry(X509Certificate2 certificate)
    {
        private readonly object m_Gate = new();
        private int m_LeaseCount;
        private bool m_Retired;
        private bool m_Disposed;

        public X509Certificate2 Certificate { get; } = certificate;

        public void AddLease()
        {
            lock (m_Gate)
            {
                if (m_Disposed)
                {
                    throw new ObjectDisposedException(nameof(CertificateEntry));
                }

                m_LeaseCount++;
            }
        }

        public void ReleaseLease()
        {
            var dispose = false;
            lock (m_Gate)
            {
                if (m_LeaseCount <= 0)
                {
                    throw new InvalidOperationException("Gateway client TLS certificate lease was released more than once.");
                }

                m_LeaseCount--;
                dispose = m_Retired && m_LeaseCount == 0 && !m_Disposed;
                if (dispose)
                {
                    m_Disposed = true;
                }
            }

            if (dispose)
            {
                Certificate.Dispose();
            }
        }

        public void Retire()
        {
            var dispose = false;
            lock (m_Gate)
            {
                m_Retired = true;
                dispose = m_LeaseCount == 0 && !m_Disposed;
                if (dispose)
                {
                    m_Disposed = true;
                }
            }

            if (dispose)
            {
                Certificate.Dispose();
            }
        }
    }
}

internal sealed class GatewayClientCertificateLease : IDisposable
{
    private GatewayClientCertificateProvider.CertificateEntry? m_Entry;

    internal GatewayClientCertificateLease(GatewayClientCertificateProvider.CertificateEntry entry)
    {
        m_Entry = entry;
    }

    public X509Certificate2 Certificate => m_Entry?.Certificate ??
        throw new ObjectDisposedException(nameof(GatewayClientCertificateLease));

    public void Dispose()
    {
        var entry = Interlocked.Exchange(ref m_Entry, null);
        entry?.ReleaseLease();
    }
}

internal sealed record GatewayClientCertificateStatus(
    string? Thumbprint,
    DateTimeOffset? NotBefore,
    DateTimeOffset? NotAfter,
    DateTimeOffset? LastReloadSucceededAt,
    string? LastReloadFailureMessage,
    bool ReloadEnabled)
{
    public static GatewayClientCertificateStatus NotLoaded(
        bool reloadEnabled,
        string? lastReloadFailureMessage)
    {
        return new GatewayClientCertificateStatus(
            null,
            null,
            null,
            null,
            lastReloadFailureMessage,
            reloadEnabled);
    }
}
