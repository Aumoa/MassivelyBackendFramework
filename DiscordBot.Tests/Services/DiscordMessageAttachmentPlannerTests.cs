using DiscordBot.Repositories;
using DiscordBot.Services;

namespace DiscordBot.Tests.Services;

public sealed class DiscordMessageAttachmentPlannerTests
{
    [Theory]
    [InlineData("image.png", null, true)]
    [InlineData("image.JPG", null, true)]
    [InlineData("image.webp", null, true)]
    [InlineData("payload.bin", "image/png", true)]
    [InlineData("notes.txt", "text/plain", false)]
    [InlineData("archive.zip", "application/zip", false)]
    public void IsImageAttachment_UsesContentTypeAndKnownExtensions(
        string fileName,
        string? contentType,
        bool expected)
    {
        var isImage = DiscordMessageAttachmentPlanner.IsImageAttachment(fileName, contentType);

        Assert.Equal(expected, isImage);
    }

    [Fact]
    public void IsDocumentAttachment_RejectsImagesBeforeProcessorSupportCheck()
    {
        var processor = new FakeAttachmentProcessor((_, _, _) => true);

        var isDocument = DiscordMessageAttachmentPlanner.IsDocumentAttachment(
            "image.png",
            "image/png",
            123,
            processor);

        Assert.False(isDocument);
        Assert.Equal(0, processor.SupportChecks);
    }

    [Fact]
    public void IsDocumentAttachment_AcceptsProcessorSupportedNonImages()
    {
        var processor = new FakeAttachmentProcessor((fileName, contentType, sizeBytes) =>
            fileName == "notes.txt" &&
            contentType == "text/plain" &&
            sizeBytes == 123);

        var isDocument = DiscordMessageAttachmentPlanner.IsDocumentAttachment(
            "notes.txt",
            "text/plain",
            123,
            processor);

        Assert.True(isDocument);
        Assert.Equal(1, processor.SupportChecks);
    }

    [Fact]
    public void BuildPromptContent_ReturnsOriginalContentWhenNoPromptTextExists()
    {
        var attachments = new[]
        {
            CreateProcessedAttachment(null),
            CreateProcessedAttachment(" \r\n ")
        };

        var prompt = DiscordMessageAttachmentPlanner.BuildPromptContent("요약해줘", attachments);

        Assert.Equal("요약해줘", prompt);
    }

    [Fact]
    public void BuildPromptContent_AppendsOnlyUsableDocumentPromptTexts()
    {
        var attachments = new[]
        {
            CreateProcessedAttachment("[첨부 문서]\nFile: a.txt\n\nalpha"),
            CreateProcessedAttachment(null),
            CreateProcessedAttachment("[첨부 문서]\nFile: b.txt\n\nbeta")
        };

        var prompt = DiscordMessageAttachmentPlanner.BuildPromptContent("요약해줘", attachments);

        Assert.Equal(
            """
요약해줘

[첨부 문서]
File: a.txt

alpha

[첨부 문서]
File: b.txt

beta
""",
            prompt);
    }

    private static ProcessedChatAttachment CreateProcessedAttachment(string? promptText)
    {
        return new ProcessedChatAttachment(
            new ChatLogAttachmentInput(
                "attachment",
                "notes.txt",
                "text/plain",
                10,
                "hash",
                [1, 2, 3],
                "text",
                "extracted",
                null),
            promptText);
    }

    private sealed class FakeAttachmentProcessor(
        Func<string?, string?, long, bool> isSupported) : IChatLogAttachmentProcessor
    {
        public int SupportChecks { get; private set; }

        public bool IsSupported(string? fileName, string? contentType, long sizeBytes)
        {
            SupportChecks++;
            return isSupported(fileName, contentType, sizeBytes);
        }

        public ValueTask<ProcessedChatAttachment> ProcessAsync(
            string? discordAttachmentId,
            string? fileName,
            string? contentType,
            long sizeBytes,
            byte[] data,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
