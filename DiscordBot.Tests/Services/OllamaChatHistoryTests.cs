using DiscordBot.Services;

namespace DiscordBot.Tests.Services;

public sealed class OllamaChatHistoryTests
{
    [Fact]
    public void GetDefaultBehaviorInstruction_PrefersChannelChatLookupForNaturalPastChatReferences()
    {
        var instruction = OllamaChatHistory.GetDefaultBehaviorInstruction();

        Assert.Contains("현재 Discord 채널의 과거 채팅", instruction);
        Assert.Contains("기억이나 추측으로 답하지 말고 먼저 현재 채널의 채팅 조회 도구를 사용하세요", instruction);
        Assert.Contains("AI와 나눈 직전 대화 자체", instruction);
        Assert.Contains("search_chat_history", instruction);
        Assert.Contains("get_chat_context", instruction);
        Assert.Contains("get_chat_by_message_id", instruction);
    }
}
