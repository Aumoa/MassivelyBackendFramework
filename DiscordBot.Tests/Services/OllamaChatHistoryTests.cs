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
        Assert.Contains("과거 지시어를 쓰지 않아도", instruction);
        Assert.Contains("현재 입력만으로 전제가 설명되지 않는 이전 발화", instruction);
        Assert.Contains("먼저 현재 채널의 채팅 조회 도구로 실제 기록을 확인하세요", instruction);
        Assert.Contains("어떤 근거로", instruction);
        Assert.Contains("히스토리 밖 대화 가능성", instruction);
        Assert.Contains("전제가 현재 대화 문맥에 있는 것처럼 보이더라도 기억만으로 근거를 단정하지 마세요", instruction);
        Assert.Contains("AI와 나눈 직전 대화 자체", instruction);
        Assert.Contains("문장 의미나 표현만 명확히 묻고", instruction);
        Assert.Contains("근거/출처/참조/이전 분석의 이유를 묻는 질문", instruction);
        Assert.Contains("search_chat_history", instruction);
        Assert.Contains("get_chat_context", instruction);
        Assert.Contains("get_chat_by_message_id", instruction);
    }
}
