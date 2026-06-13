using System.Buffers;
using DiscordBot.Options;

namespace DiscordBot.Services;

internal interface IDiscordAttachmentDownloader
{
    Task<byte[]> DownloadAsync(
        string url,
        long maxBytes,
        CancellationToken cancellationToken = default);
}

internal sealed class DiscordAttachmentDownloader(IHttpClientFactory httpClientFactory) : IDiscordAttachmentDownloader
{
    private const int BufferSize = 81920;

    public async Task<byte[]> DownloadAsync(
        string url,
        long maxBytes,
        CancellationToken cancellationToken = default)
    {
        if (maxBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes), "Maximum download size must be greater than zero.");
        }

        var httpClient = httpClientFactory.CreateClient(AttachmentDownloadOptions.HttpClientName);
        using var response = await httpClient.GetAsync(
            url,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var contentLength = response.Content.Headers.ContentLength;
        if (contentLength.HasValue && contentLength.Value > maxBytes)
        {
            throw new InvalidOperationException(
                $"Attachment response is {contentLength.Value} bytes, exceeding the {maxBytes} byte limit.");
        }

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        long totalBytes = 0;
        try
        {
            int bytesRead;
            while ((bytesRead = await input.ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken)) > 0)
            {
                totalBytes += bytesRead;
                if (totalBytes > maxBytes)
                {
                    throw new InvalidOperationException(
                        $"Attachment download exceeded the {maxBytes} byte limit.");
                }

                output.Write(buffer, 0, bytesRead);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return output.ToArray();
    }
}
