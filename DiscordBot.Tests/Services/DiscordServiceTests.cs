using DiscordBot.Services;

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
}
