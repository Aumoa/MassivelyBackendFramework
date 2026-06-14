using DiscordBot.Repositories;
using DiscordBot.Services;

namespace DiscordBot.Tests.Services;

public sealed class DiscordChatImageToolsTests
{
    [Fact]
    public void ExtractMessageIds_ParsesMultipleIdsAndUrls()
    {
        var messageIds = DiscordChatImageTools.ExtractMessageIds(
            "123, https://discord.com/channels/111/222/333\n123 not-a-message https://discordapp.com/channels/@me/222/444");

        Assert.Equal(["123", "333", "444"], messageIds);
    }

    [Fact]
    public void BuildImageBatchMetadata_FormatsLoadedImages()
    {
        var images = new[]
        {
            CreateImage(id: 1, chatLogId: 10, messageId: "111", fileName: "first.png"),
            CreateImage(id: 2, chatLogId: 20, messageId: "222", fileName: "second.jpg", contentType: "image/jpeg")
        };

        var metadata = DiscordChatImageTools.BuildImageBatchMetadata(images, requestedLimit: 2);

        Assert.Contains("과거 채팅에서 이미지 2장을 불러왔습니다.", metadata);
        Assert.Contains("\"count\":2", metadata);
        Assert.Contains("\"message_id\":\"111\"", metadata);
        Assert.Contains("\"file_name\":\"second.jpg\"", metadata);
        Assert.Contains("\"max_count\":4", metadata);
    }

    private static ChatImageData CreateImage(
        long id = 1,
        long chatLogId = 2,
        string? messageId = "333",
        string? guildId = "111",
        string channelId = "222",
        string userId = "user",
        string content = "content",
        string? fileName = "image.png",
        string contentType = "image/png",
        int width = 320,
        int height = 240,
        byte[]? data = null,
        DateTime? createdAt = null)
    {
        return new ChatImageData(
            id,
            chatLogId,
            messageId,
            guildId,
            channelId,
            userId,
            content,
            fileName,
            contentType,
            width,
            height,
            data ?? [1, 2, 3],
            createdAt ?? new DateTime(2026, 6, 14, 1, 2, 3, DateTimeKind.Utc));
    }
}
