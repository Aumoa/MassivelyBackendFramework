namespace OAuth2.Services;

public class ScopedSemaphore : IDisposable
{
    private readonly SemaphoreSlim m_Semaphore = new(1);

    public async ValueTask WaitAsync(CancellationToken cancellationToken = default)
    {
        await m_Semaphore.WaitAsync(cancellationToken);
    }

    public void Release()
    {
        m_Semaphore.Release();
    }

    public void Dispose()
    {
        m_Semaphore.Dispose();
        GC.SuppressFinalize(this);
    }
}
