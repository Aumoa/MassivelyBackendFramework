namespace DiscordBot.Services;

internal static class DiscordMessageAttachmentPlanner
{
    public static bool IsImageAttachment(string? fileName, string? contentType)
    {
        if (contentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        var extension = Path.GetExtension(fileName) ?? string.Empty;
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".gif", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".webp", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsDocumentAttachment(
        string? fileName,
        string? contentType,
        long sizeBytes,
        IChatLogAttachmentProcessor attachmentProcessor)
    {
        return !IsImageAttachment(fileName, contentType) &&
               attachmentProcessor.IsSupported(fileName, contentType, sizeBytes);
    }

    public static string BuildPromptContent(
        string content,
        IReadOnlyList<ProcessedChatAttachment> attachments)
    {
        var attachmentTexts = attachments
            .Select(static attachment => attachment.PromptText)
            .Where(static text => !string.IsNullOrWhiteSpace(text))
            .ToList();
        if (attachmentTexts.Count == 0)
        {
            return content;
        }

        return $"""
{content}

{string.Join("\n\n", attachmentTexts)}
""";
    }
}
