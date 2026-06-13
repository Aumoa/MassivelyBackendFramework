using System.Security.Cryptography;
using System.Text;
using DiscordBot.Options;
using DiscordBot.Services;

namespace DiscordBot.Tests.Services;

public sealed class ChatLogAttachmentProcessorTests
{
    [Theory]
    [InlineData("notes.txt", null, 10, true)]
    [InlineData("notes.md", null, 10, true)]
    [InlineData("payload.bin", "text/plain; charset=utf-8", 10, true)]
    [InlineData("payload.bin", "application/json", 10, true)]
    [InlineData("report.pdf", null, 10, true)]
    [InlineData("archive.zip", "application/zip", 10, false)]
    [InlineData("empty.txt", "text/plain", 0, false)]
    [InlineData("large.txt", "text/plain", 11, false)]
    public void IsSupported_UsesAllowedTypesAndSizeLimit(
        string fileName,
        string? contentType,
        long sizeBytes,
        bool expected)
    {
        var processor = CreateProcessor(new AttachmentProcessingOptions
        {
            MaxAttachmentBytes = 10
        });

        var supported = processor.IsSupported(fileName, contentType, sizeBytes);

        Assert.Equal(expected, supported);
    }

    [Fact]
    public async Task ProcessAsync_TextFileNormalizesAndStoresExtractedText()
    {
        var processor = CreateProcessor();
        var data = Encoding.UTF8.GetBytes("  hello\r\nworld\rdone  ");

        var result = await processor.ProcessAsync(
            "1234",
            "notes.txt",
            "text/plain; charset=utf-8",
            data.Length,
            data);

        Assert.Equal("notes.txt", result.StoredAttachment.FileName);
        Assert.Equal("text/plain", result.StoredAttachment.ContentType);
        Assert.Equal(data.Length, result.StoredAttachment.SizeBytes);
        Assert.Equal(ExpectedSha256(data), result.StoredAttachment.Sha256);
        Assert.Same(data, result.StoredAttachment.Data);
        Assert.Equal("hello\nworld\ndone", result.StoredAttachment.ExtractedText);
        Assert.Equal("extracted", result.StoredAttachment.ExtractionStatus);
        Assert.Null(result.StoredAttachment.ExtractionError);
        Assert.Contains("[첨부 문서]", result.PromptText);
        Assert.Contains("hello\nworld\ndone", result.PromptText);
    }

    [Theory]
    [MemberData(nameof(EncodedTextData))]
    public async Task ProcessAsync_DecodesTextEncodings(byte[] data)
    {
        var processor = CreateProcessor();

        var result = await processor.ProcessAsync(
            null,
            "encoded.txt",
            "text/plain",
            data.Length,
            data);

        Assert.Equal("hello", result.StoredAttachment.ExtractedText);
        Assert.Equal("extracted", result.StoredAttachment.ExtractionStatus);
    }

    [Fact]
    public async Task ProcessAsync_RespectsExtractedAndPromptTextLimits()
    {
        var processor = CreateProcessor(new AttachmentProcessingOptions
        {
            MaxExtractedTextCharacters = 5,
            MaxPromptTextCharacters = 3
        });
        var data = Encoding.UTF8.GetBytes("abcdef");

        var result = await processor.ProcessAsync(
            null,
            "notes.txt",
            "text/plain",
            data.Length,
            data);

        Assert.Equal("abcde", result.StoredAttachment.ExtractedText);
        Assert.Contains("abc", result.PromptText);
        Assert.DoesNotContain("abcd", result.PromptText);
    }

    [Fact]
    public async Task ProcessAsync_RejectsActualDataLengthOverLimit()
    {
        var processor = CreateProcessor(new AttachmentProcessingOptions
        {
            MaxAttachmentBytes = 5
        });
        var data = Encoding.UTF8.GetBytes("abcdef");

        var result = await processor.ProcessAsync(
            null,
            "notes.txt",
            "text/plain",
            1,
            data);

        Assert.Equal(data.Length, result.StoredAttachment.SizeBytes);
        Assert.Null(result.StoredAttachment.ExtractedText);
        Assert.Equal("unsupported", result.StoredAttachment.ExtractionStatus);
        Assert.Null(result.PromptText);
    }

    [Fact]
    public async Task ProcessAsync_WhitespaceOnlyTextMarksEmpty()
    {
        var processor = CreateProcessor();
        var data = Encoding.UTF8.GetBytes(" \r\n\t ");

        var result = await processor.ProcessAsync(
            null,
            "blank.txt",
            "text/plain",
            data.Length,
            data);

        Assert.Null(result.StoredAttachment.ExtractedText);
        Assert.Equal("empty", result.StoredAttachment.ExtractionStatus);
        Assert.Null(result.StoredAttachment.ExtractionError);
        Assert.Null(result.PromptText);
    }

    [Fact]
    public async Task ProcessAsync_UnsupportedAttachmentStillPersistsMetadata()
    {
        var processor = CreateProcessor();
        var data = Encoding.UTF8.GetBytes("zip-data");

        var result = await processor.ProcessAsync(
            "5678",
            "archive.zip",
            "application/zip",
            data.Length,
            data);

        Assert.Equal("5678", result.StoredAttachment.DiscordAttachmentId);
        Assert.Equal("archive.zip", result.StoredAttachment.FileName);
        Assert.Equal("application/zip", result.StoredAttachment.ContentType);
        Assert.Equal(ExpectedSha256(data), result.StoredAttachment.Sha256);
        Assert.Same(data, result.StoredAttachment.Data);
        Assert.Null(result.StoredAttachment.ExtractedText);
        Assert.Equal("unsupported", result.StoredAttachment.ExtractionStatus);
        Assert.Equal("Unsupported attachment type or size.", result.StoredAttachment.ExtractionError);
        Assert.Null(result.PromptText);
    }

    [Fact]
    public async Task ProcessAsync_InvalidPdfMarksFailed()
    {
        var processor = CreateProcessor();
        var data = Encoding.UTF8.GetBytes("%PDF-invalid");

        var result = await processor.ProcessAsync(
            null,
            "report.pdf",
            "application/pdf",
            data.Length,
            data);

        Assert.Null(result.StoredAttachment.ExtractedText);
        Assert.Equal("failed", result.StoredAttachment.ExtractionStatus);
        Assert.False(string.IsNullOrWhiteSpace(result.StoredAttachment.ExtractionError));
        Assert.Null(result.PromptText);
    }

    public static TheoryData<byte[]> EncodedTextData()
    {
        var data = new TheoryData<byte[]>();
        data.Add(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("hello")).ToArray());
        data.Add(Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("hello")).ToArray());
        data.Add(Encoding.BigEndianUnicode.GetPreamble().Concat(Encoding.BigEndianUnicode.GetBytes("hello")).ToArray());
        return data;
    }

    private static ChatLogAttachmentProcessor CreateProcessor(AttachmentProcessingOptions? options = null)
    {
        return new ChatLogAttachmentProcessor(
            Microsoft.Extensions.Options.Options.Create(options ?? new AttachmentProcessingOptions()));
    }

    private static string ExpectedSha256(byte[] data)
    {
        return Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    }
}
