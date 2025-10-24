using Microsoft.Extensions.Hosting;
using OAuth2.Options;
using StackExchange.Redis;

namespace OAuth2.Services;

internal class RedisConnection(RedisOptions options) : IHostedService, IDisposable, IAsyncDisposable
{
    private bool m_Disposed;
    private ConnectionMultiplexer m_Multiplexer = null!;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        m_Multiplexer = await ConnectionMultiplexer.ConnectAsync(options.ConnectionString).WaitAsync(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (m_Multiplexer != null)
        {
            await m_Multiplexer.CloseAsync().WaitAsync(cancellationToken);
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (m_Disposed)
        {
            return;
        }

        m_Multiplexer?.Dispose();
        m_Disposed = true;
    }

    public virtual async ValueTask DisposeAsync()
    {
        if (m_Disposed)
        {
            return;
        }

        if (m_Multiplexer != null)
        {
            await m_Multiplexer.DisposeAsync();
        }

        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public IDatabase GetDatabase()
    {
        ObjectDisposedException.ThrowIf(m_Disposed, this);
        return m_Multiplexer.GetDatabase(options.DbIndex);
    }
}
