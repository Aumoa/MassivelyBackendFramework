namespace DiscordBot.Services;

internal static class DiscordAttachmentBatchProcessor
{
    public static async Task<List<TResult>> ProcessAsync<TAttachment, TResult>(
        IReadOnlyList<TAttachment> attachments,
        Func<TAttachment, Task<TResult>> processAsync,
        Action<TAttachment, Exception> handleError)
    {
        List<TResult> results = [];
        foreach (var attachment in attachments)
        {
            try
            {
                results.Add(await processAsync(attachment));
            }
            catch (Exception e)
            {
                handleError(attachment, e);
            }
        }

        return results;
    }
}
