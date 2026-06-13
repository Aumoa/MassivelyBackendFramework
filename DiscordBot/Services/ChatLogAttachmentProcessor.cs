using System.Security.Cryptography;
using System.Text;
using DiscordBot.Options;
using DiscordBot.Repositories;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig;

namespace DiscordBot.Services;

internal sealed record ProcessedChatAttachment(
    ChatLogAttachmentInput StoredAttachment,
    string? PromptText);

internal interface IChatLogAttachmentProcessor
{
    bool IsSupported(string? fileName, string? contentType, long sizeBytes);

    ValueTask<ProcessedChatAttachment> ProcessAsync(
        string? discordAttachmentId,
        string? fileName,
        string? contentType,
        long sizeBytes,
        byte[] data,
        CancellationToken cancellationToken = default);
}

internal sealed class ChatLogAttachmentProcessor(IOptions<AttachmentProcessingOptions> options) : IChatLogAttachmentProcessor
{
    private const string StatusExtracted = "extracted";
    private const string StatusEmpty = "empty";
    private const string StatusUnsupported = "unsupported";
    private const string StatusFailed = "failed";
    private const string DefaultContentType = "application/octet-stream";

    private readonly AttachmentProcessingOptions m_Options = options.Value;

    public bool IsSupported(string? fileName, string? contentType, long sizeBytes)
    {
        if (sizeBytes <= 0 || sizeBytes > m_Options.MaxAttachmentBytes)
        {
            return false;
        }

        return IsAllowedContentType(contentType) || IsAllowedExtension(fileName);
    }

    public ValueTask<ProcessedChatAttachment> ProcessAsync(
        string? discordAttachmentId,
        string? fileName,
        string? contentType,
        long sizeBytes,
        byte[] data,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedContentType = NormalizeContentType(contentType);
        var actualSizeBytes = data.LongLength;
        var sha256 = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

        if (!IsSupported(fileName, normalizedContentType, actualSizeBytes))
        {
            return ValueTask.FromResult(CreateResult(
                discordAttachmentId,
                fileName,
                normalizedContentType,
                actualSizeBytes,
                sha256,
                data,
                null,
                StatusUnsupported,
                "Unsupported attachment type or size."));
        }

        try
        {
            var extractedText = ExtractText(fileName, normalizedContentType, data);
            extractedText = NormalizeExtractedText(extractedText);
            if (string.IsNullOrWhiteSpace(extractedText))
            {
                return ValueTask.FromResult(CreateResult(
                    discordAttachmentId,
                    fileName,
                    normalizedContentType,
                    actualSizeBytes,
                    sha256,
                    data,
                    null,
                    StatusEmpty,
                    null));
            }

            var storedText = Truncate(extractedText, m_Options.MaxExtractedTextCharacters);
            return ValueTask.FromResult(CreateResult(
                discordAttachmentId,
                fileName,
                normalizedContentType,
                actualSizeBytes,
                sha256,
                data,
                storedText,
                StatusExtracted,
                null));
        }
        catch (Exception e)
        {
            return ValueTask.FromResult(CreateResult(
                discordAttachmentId,
                fileName,
                normalizedContentType,
                actualSizeBytes,
                sha256,
                data,
                null,
                StatusFailed,
                e.Message));
        }
    }

    private ProcessedChatAttachment CreateResult(
        string? discordAttachmentId,
        string? fileName,
        string contentType,
        long sizeBytes,
        string sha256,
        byte[] data,
        string? extractedText,
        string extractionStatus,
        string? extractionError)
    {
        var storedAttachment = new ChatLogAttachmentInput(
            discordAttachmentId,
            fileName,
            contentType,
            sizeBytes,
            sha256,
            data,
            extractedText,
            extractionStatus,
            Truncate(extractionError, 1024));

        return new ProcessedChatAttachment(
            storedAttachment,
            BuildPromptText(fileName, contentType, extractedText, extractionStatus));
    }

    private string? BuildPromptText(
        string? fileName,
        string contentType,
        string? extractedText,
        string extractionStatus)
    {
        if (string.IsNullOrWhiteSpace(extractedText))
        {
            return null;
        }

        var promptText = Truncate(extractedText, m_Options.MaxPromptTextCharacters);
        return $"""
[첨부 문서]
File: {fileName ?? "(unknown)"}
Content-Type: {contentType}
Extraction-Status: {extractionStatus}

{promptText}
""";
    }

    private string ExtractText(string? fileName, string contentType, byte[] data)
    {
        if (IsPdf(fileName, contentType))
        {
            return ExtractPdfText(data);
        }

        if (IsTextLike(fileName, contentType))
        {
            return DecodeText(data);
        }

        return string.Empty;
    }

    private static string ExtractPdfText(byte[] data)
    {
        using var stream = new MemoryStream(data, writable: false);
        using var document = PdfDocument.Open(stream);
        var builder = new StringBuilder();
        foreach (var page in document.GetPages())
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
                builder.AppendLine();
            }

            builder.AppendLine(page.Text);
        }

        return builder.ToString();
    }

    private static string DecodeText(byte[] data)
    {
        if (data.Length >= 3 &&
            data[0] == 0xEF &&
            data[1] == 0xBB &&
            data[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(data, 3, data.Length - 3);
        }

        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(data, 2, data.Length - 2);
        }

        if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(data, 2, data.Length - 2);
        }

        try
        {
            return new UTF8Encoding(false, true).GetString(data);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(data);
        }
    }

    private static string NormalizeExtractedText(string value)
    {
        return value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
    }

    private bool IsAllowedExtension(string? fileName)
    {
        var extension = Path.GetExtension(fileName);
        return !string.IsNullOrWhiteSpace(extension) &&
               m_Options.AllowedExtensions.Any(allowed => extension.Equals(allowed, StringComparison.OrdinalIgnoreCase));
    }

    private bool IsAllowedContentType(string? contentType)
    {
        var normalized = NormalizeContentType(contentType);
        return m_Options.AllowedContentTypes.Any(allowed => normalized.Equals(allowed, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPdf(string? fileName, string contentType)
    {
        var extension = Path.GetExtension(fileName) ?? string.Empty;
        return contentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTextLike(string? fileName, string contentType)
    {
        if (contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
            contentType.Equals("application/json", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var extension = Path.GetExtension(fileName) ?? string.Empty;
        return extension.Equals(".txt", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".md", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".markdown", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".csv", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".json", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".log", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return DefaultContentType;
        }

        var semicolonIndex = contentType.IndexOf(';', StringComparison.Ordinal);
        return semicolonIndex >= 0
            ? contentType[..semicolonIndex].Trim().ToLowerInvariant()
            : contentType.Trim().ToLowerInvariant();
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }
}
