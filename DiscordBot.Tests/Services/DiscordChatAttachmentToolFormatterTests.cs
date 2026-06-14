using DiscordBot.Repositories;
using DiscordBot.Services;

namespace DiscordBot.Tests.Services;

public sealed class DiscordChatAttachmentToolFormatterTests
{
    [Theory]
    [InlineData("1234567890", "1234567890")]
    [InlineData(" https://discord.com/channels/111/222/333 ", "333")]
    [InlineData("https://discordapp.com/channels/@me/222/444", "444")]
    [InlineData("not-a-message", null)]
    [InlineData(null, null)]
    public void ExtractMessageId_ParsesDirectIdsAndDiscordUrls(string? value, string? expected)
    {
        var messageId = DiscordChatAttachmentToolFormatter.ExtractMessageId(value);

        Assert.Equal(expected, messageId);
    }

    [Fact]
    public void ExtractMessageIds_ParsesMultipleIdsAndUrls()
    {
        var messageIds = DiscordChatAttachmentToolFormatter.ExtractMessageIds(
            "123, https://discord.com/channels/111/222/333\n123 not-a-message https://discordapp.com/channels/@me/222/444");

        Assert.Equal(["123", "333", "444"], messageIds);
    }

    [Fact]
    public void BuildMessageReference_UsesGuildWhenAvailable()
    {
        var attachment = CreateAttachment(guildId: "111", channelId: "222", messageId: "333");

        var reference = DiscordChatAttachmentToolFormatter.BuildMessageReference(attachment);

        Assert.Equal("https://discord.com/channels/111/222/333", reference);
    }

    [Fact]
    public void BuildMessageReference_UsesDirectMessageMarkerWithoutGuild()
    {
        var attachment = CreateAttachment(guildId: null, channelId: "222", messageId: "333");

        var reference = DiscordChatAttachmentToolFormatter.BuildMessageReference(attachment);

        Assert.Equal("https://discord.com/channels/@me/222/333", reference);
    }

    [Fact]
    public void BuildMessageReference_ExplainsMissingMessageId()
    {
        var attachment = CreateAttachment(messageId: null);

        var reference = DiscordChatAttachmentToolFormatter.BuildMessageReference(attachment);

        Assert.Equal("(메시지가 오래되어 참조할 수 없어요)", reference);
    }

    [Fact]
    public void BuildExcerpt_NormalizesWhitespaceAndTruncates()
    {
        var excerpt = DiscordChatAttachmentToolFormatter.BuildExcerpt("  abc\r\ndef\rghi  ", 7);

        Assert.Equal("abc\ndef...", excerpt);
    }

    [Fact]
    public void BuildExcerpt_HandlesMissingText()
    {
        var excerpt = DiscordChatAttachmentToolFormatter.BuildExcerpt(" \r\n ", 10);

        Assert.Equal("(추출된 텍스트가 없습니다.)", excerpt);
    }

    [Fact]
    public void BuildAttachmentDetails_TruncatesAtCharacterBudget()
    {
        var attachments = new[]
        {
            CreateAttachment(id: 1, extractedText: "abcdef"),
            CreateAttachment(id: 2, extractedText: "second")
        };

        var details = DiscordChatAttachmentToolFormatter.BuildAttachmentDetails(attachments, 3);

        Assert.Contains("과거 채팅에서 문서 2개를 불러왔습니다.", details);
        Assert.Contains("ChatAttachmentId: 1", details);
        Assert.Contains("abc\n...(문서 텍스트가 길어 잘렸습니다.)", details);
        Assert.DoesNotContain("ChatAttachmentId: 2", details);
    }

    [Fact]
    public void BuildSearchResults_FormatsMetadataLinksAndLocalTime()
    {
        var timezone = TimeZoneInfo.CreateCustomTimeZone("KST", TimeSpan.FromHours(9), "KST", "KST");
        var attachments = new[]
        {
            CreateAttachment(
                id: 10,
                chatLogId: 20,
                guildId: "111",
                channelId: "222",
                messageId: "333",
                fileName: "patch.txt",
                extractedText: "  line1\r\nline2  ",
                createdAt: new DateTime(2026, 6, 13, 0, 30, 0, DateTimeKind.Utc))
        };

        var results = DiscordChatAttachmentToolFormatter.BuildSearchResults(["patch"], attachments, timezone);

        Assert.Contains("검색 키워드: patch", results);
        Assert.Contains("결과 1건:", results);
        Assert.Contains("ChatAttachmentId: 10", results);
        Assert.Contains("ChatLogId: 20", results);
        Assert.Contains("Time: 2026-06-13 09:30:00", results);
        Assert.Contains("File: patch.txt", results);
        Assert.Contains("Link: https://discord.com/channels/111/222/333", results);
        Assert.Contains("Excerpt: line1\nline2", results);
    }

    [Fact]
    public void ResolveTimeZone_FallsBackToUtcForInvalidValues()
    {
        var timezone = DiscordChatAttachmentToolFormatter.ResolveTimeZone("bad-timezone");

        Assert.Equal(TimeZoneInfo.Utc, timezone);
    }

    [Fact]
    public void TryParseOptionalDateTime_ReturnsErrorForInvalidText()
    {
        var parsed = DiscordChatAttachmentToolFormatter.TryParseOptionalDateTime(
            "not-a-date",
            TimeZoneInfo.Utc,
            out var dateTime,
            out var error);

        Assert.False(parsed);
        Assert.Null(dateTime);
        Assert.Equal("날짜/시간을 해석하지 못했습니다. 예: 2026-06-13T15:00:00", error);
    }

    [Fact]
    public void TryParseOptionalDateTime_AllowsBlankText()
    {
        var parsed = DiscordChatAttachmentToolFormatter.TryParseOptionalDateTime(
            "",
            TimeZoneInfo.Utc,
            out var dateTime,
            out var error);

        Assert.True(parsed);
        Assert.Null(dateTime);
        Assert.Empty(error);
    }

    private static ChatAttachmentData CreateAttachment(
        long id = 1,
        long chatLogId = 2,
        string? messageId = "333",
        string? guildId = "111",
        string channelId = "222",
        string userId = "user",
        string content = "content",
        string? discordAttachmentId = "attachment",
        string? fileName = "notes.txt",
        string contentType = "text/plain",
        long sizeBytes = 100,
        string sha256 = "hash",
        string? extractedText = "text",
        string extractionStatus = "extracted",
        string? extractionError = null,
        DateTime? createdAt = null)
    {
        return new ChatAttachmentData(
            id,
            chatLogId,
            messageId,
            guildId,
            channelId,
            userId,
            content,
            discordAttachmentId,
            fileName,
            contentType,
            sizeBytes,
            sha256,
            extractedText,
            extractionStatus,
            extractionError,
            createdAt ?? new DateTime(2026, 6, 13, 0, 0, 0, DateTimeKind.Utc));
    }
}
