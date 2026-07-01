using DiscordBot.Services;
using DiscordBot.Repositories;

namespace DiscordBot.Tests.Services;

public sealed class DiscordServiceTests
{
    [Fact]
    public void AppendResponseContent_AppendsStreamingChunksWithoutSeparators()
    {
        var shouldSeparateBeforeContent = false;
        var content = string.Empty;

        content = DiscordService.AppendResponseContent(content, "잠깐", ref shouldSeparateBeforeContent);
        content = DiscordService.AppendResponseContent(content, ", 확인해볼게요.", ref shouldSeparateBeforeContent);

        Assert.Equal("잠깐, 확인해볼게요.", content);
        Assert.False(shouldSeparateBeforeContent);
    }

    [Fact]
    public void AppendResponseContent_InsertsParagraphAfterToolBoundary()
    {
        var shouldSeparateBeforeContent = false;
        var content = DiscordService.AppendResponseContent(
            string.Empty,
            "잠깐, 채팅 기록에서 해당 내용을 먼저 찾아볼게요.",
            ref shouldSeparateBeforeContent);

        shouldSeparateBeforeContent = true;
        content = DiscordService.AppendResponseContent(
            content,
            "어제 나온 메시지를 찾았어요.",
            ref shouldSeparateBeforeContent);

        Assert.Equal(
            """
잠깐, 채팅 기록에서 해당 내용을 먼저 찾아볼게요.

어제 나온 메시지를 찾았어요.
""",
            content);
        Assert.False(shouldSeparateBeforeContent);
    }

    [Fact]
    public void AppendToolUseNotice_InsertsSummaryBeforeResumedContent()
    {
        var shouldSeparateBeforeContent = false;
        var content = DiscordService.AppendResponseContent(
            string.Empty,
            "잠깐, 채팅 기록에서 해당 내용을 먼저 찾아볼게요.",
            ref shouldSeparateBeforeContent);

        content = DiscordService.AppendToolUseNotice(
            content,
            1,
            ref shouldSeparateBeforeContent);
        content = DiscordService.AppendResponseContent(
            content,
            "어제 나온 메시지를 찾았어요.",
            ref shouldSeparateBeforeContent);

        Assert.Equal(
            """
잠깐, 채팅 기록에서 해당 내용을 먼저 찾아볼게요.

1개 도구 사용됨

어제 나온 메시지를 찾았어요.
""",
            content);
        Assert.False(shouldSeparateBeforeContent);
    }

    [Fact]
    public void AppendToolUseNotice_SummarizesMultipleTools()
    {
        var shouldSeparateBeforeContent = false;

        var content = DiscordService.AppendToolUseNotice(
            string.Empty,
            2,
            ref shouldSeparateBeforeContent);

        Assert.Equal("2개 도구 사용됨", content);
        Assert.True(shouldSeparateBeforeContent);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void ShouldRespondToMessage_RespondsToMentionsOrDirectMessages(
        bool isMentioned,
        bool isDirectMessage,
        bool expected)
    {
        var result = DiscordService.ShouldRespondToMessage(isMentioned, isDirectMessage);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void AppendResponseContent_DoesNotAddLeadingSeparatorWithoutPriorContent()
    {
        var shouldSeparateBeforeContent = true;

        var content = DiscordService.AppendResponseContent(
            string.Empty,
            "저장 완료했어요!",
            ref shouldSeparateBeforeContent);

        Assert.Equal("저장 완료했어요!", content);
        Assert.False(shouldSeparateBeforeContent);
    }

    [Fact]
    public void AppendResponseContent_KeepsPendingSeparatorForEmptyContent()
    {
        var shouldSeparateBeforeContent = true;

        var content = DiscordService.AppendResponseContent(
            "먼저 찾아볼게요.",
            string.Empty,
            ref shouldSeparateBeforeContent);

        Assert.Equal("먼저 찾아볼게요.", content);
        Assert.True(shouldSeparateBeforeContent);
    }

    [Fact]
    public void AppendResponseContent_NormalizesBoundaryNewlines()
    {
        var shouldSeparateBeforeContent = true;

        var content = DiscordService.AppendResponseContent(
            "먼저 찾아볼게요.\n",
            "\n내용을 파악했어요.",
            ref shouldSeparateBeforeContent);

        Assert.Equal("먼저 찾아볼게요.\n\n내용을 파악했어요.", content);
        Assert.False(shouldSeparateBeforeContent);
    }

    [Fact]
    public void BuildPromptContentWithReferencedMessage_AddsReferencedMessageContext()
    {
        var referenced = new ChatLogData(
            1,
            "111",
            "guild",
            "channel",
            "user-1",
            "나는 A보다 B가 맞는 것 같아",
            new DateTime(2026, 6, 14, 1, 2, 3, DateTimeKind.Utc));

        var prompt = DiscordService.BuildPromptContentWithReferencedMessage(
            "어떻게 생각해?",
            referenced,
            "bot");

        Assert.Equal(
            """
[사용자가 답장으로 참조한 메시지]
작성자: 사용자 user-1의 메시지
MessageId: 111
내용:
나는 A보다 B가 맞는 것 같아

[사용자 메시지]
어떻게 생각해?
""",
            prompt);
    }

    [Fact]
    public void BuildPromptContentWithReferencedMessage_LabelsBotResponses()
    {
        var referenced = new ChatLogData(
            1,
            "111",
            "guild",
            "channel",
            "bot",
            "이전 답변입니다.",
            new DateTime(2026, 6, 14, 1, 2, 3, DateTimeKind.Utc));

        var prompt = DiscordService.BuildPromptContentWithReferencedMessage(
            "이거 다시 설명해줘.",
            referenced,
            "bot");

        Assert.Contains("작성자: 봇의 이전 응답", prompt);
        Assert.Contains("이전 답변입니다.", prompt);
    }

    [Fact]
    public void BuildPromptContentWithReferencedMessage_AddsReferencedImageNotice()
    {
        var referenced = new ChatLogData(
            1,
            "111",
            "guild",
            "channel",
            "user-1",
            "왔냐",
            new DateTime(2026, 6, 14, 1, 2, 3, DateTimeKind.Utc));

        var prompt = DiscordService.BuildPromptContentWithReferencedMessage(
            "이 캐릭터는 어떤 성격일 것 같아?",
            referenced,
            "bot",
            referencedImageCount: 1);

        Assert.Contains("첨부 이미지: 1장이 현재 사용자 입력 이미지로 함께 포함되었습니다.", prompt);
        Assert.Contains("내용:\n왔냐", prompt);
    }

    [Fact]
    public void BuildPromptContentWithReferencedMessage_ReturnsPromptWithoutReference()
    {
        var prompt = DiscordService.BuildPromptContentWithReferencedMessage(
            "그냥 질문입니다.",
            null,
            "bot");

        Assert.Equal("그냥 질문입니다.", prompt);
    }

    [Fact]
    public void BuildPromptImages_ReturnsNullWithoutImages()
    {
        var images = DiscordService.BuildPromptImages(
            Array.Empty<AI.ChatImage>(),
            Array.Empty<ChatImageData>());

        Assert.Null(images);
    }

    [Fact]
    public void BuildPromptImages_AppendsReferencedImagesAfterCurrentImages()
    {
        var currentImage = new AI.ChatImage
        {
            Base64 = "current",
            MediaType = "image/png"
        };
        var referencedImage = CreateImage(
            contentType: "image/jpeg",
            data: [1, 2, 3]);

        var images = DiscordService.BuildPromptImages(
            [currentImage],
            [referencedImage]);

        Assert.NotNull(images);
        Assert.Equal(2, images.Count);
        Assert.Same(currentImage, images[0]);
        Assert.Equal(Convert.ToBase64String([1, 2, 3]), images[1].Base64);
        Assert.Equal("image/jpeg", images[1].MediaType);
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
