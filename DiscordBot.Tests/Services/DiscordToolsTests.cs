using DiscordBot.Repositories;
using DiscordBot.Services;

namespace DiscordBot.Tests.Services;

public sealed class DiscordToolsTests
{
    [Theory]
    [InlineData("1234567890", "1234567890", null, null)]
    [InlineData(" https://discord.com/channels/111/222/333 ", "333", "222", "111")]
    [InlineData("https://discordapp.com/channels/@me/222/444", "444", "222", null)]
    public void ParseMessageReferenceTarget_ParsesIdsAndDiscordUrls(
        string value,
        string expectedMessageId,
        string? expectedChannelId,
        string? expectedGuildId)
    {
        var target = DiscordTools.ParseMessageReferenceTarget(value);

        Assert.NotNull(target);
        Assert.Equal(expectedMessageId, target.MessageId);
        Assert.Equal(expectedChannelId, target.ChannelId);
        Assert.Equal(expectedGuildId, target.GuildId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-message")]
    [InlineData("https://discord.com/channels/not-guild/222/333")]
    [InlineData("https://example.com/channels/111/222/333")]
    [InlineData("https://discord.com/channels/111/not-channel/333")]
    public void ParseMessageReferenceTarget_RejectsInvalidInputs(string value)
    {
        var target = DiscordTools.ParseMessageReferenceTarget(value);

        Assert.Null(target);
    }

    [Fact]
    public void BuildChatLogDetails_IncludesReplyReferenceIdentifiers()
    {
        var log = CreateLog(
            messageId: "333",
            referencedMessageId: "111",
            referencedChannelId: "222",
            referencedGuildId: "999");

        var details = DiscordTools.BuildChatLogDetails(log, TimeZoneInfo.Utc, "bot", "메시지 조회 결과:");

        Assert.Equal(
            """
메시지 조회 결과:
ChatLogId: 10
Time: 2026-06-14 01:02:03
Author: 사용자 user-1의 메시지
Link: https://discord.com/channels/999/222/333
ReplyTo: https://discord.com/channels/999/222/111
Content: 의견입니다.
""",
            details);
    }

    [Fact]
    public void BuildReplyThreadContext_FormatsParentTargetAndReplies()
    {
        var parent = CreateLog(id: 1, messageId: "111", content: "원 의견");
        var target = CreateLog(id: 2, messageId: "222", content: "대상 의견", referencedMessageId: "111");
        var reply = CreateLog(id: 3, messageId: "333", content: "후속 답장", referencedMessageId: "222");

        var context = DiscordTools.BuildReplyThreadContext(
            target,
            parent,
            [reply],
            TimeZoneInfo.Utc,
            "bot");

        Assert.Contains("상위 참조 메시지:", context);
        Assert.Contains("Content: 원 의견", context);
        Assert.Contains("대상 메시지:", context);
        Assert.Contains("Content: 대상 의견", context);
        Assert.Contains("직접 답장 1건:", context);
        Assert.Contains("Content: 후속 답장", context);
    }

    private static ChatLogData CreateLog(
        long id = 10,
        string? messageId = "333",
        string? guildId = "999",
        string channelId = "222",
        string userId = "user-1",
        string content = "의견입니다.",
        string? referencedMessageId = null,
        string? referencedChannelId = null,
        string? referencedGuildId = null)
    {
        return new ChatLogData(
            id,
            messageId,
            guildId,
            channelId,
            userId,
            content,
            new DateTime(2026, 6, 14, 1, 2, 3, DateTimeKind.Utc),
            referencedMessageId,
            referencedChannelId,
            referencedGuildId);
    }
}
