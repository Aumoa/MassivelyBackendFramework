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
    public void BuildPromptContentWithReferencedMessage_ReturnsPromptWithoutReference()
    {
        var prompt = DiscordService.BuildPromptContentWithReferencedMessage(
            "그냥 질문입니다.",
            null,
            "bot");

        Assert.Equal("그냥 질문입니다.", prompt);
    }
}
