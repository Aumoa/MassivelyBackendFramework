using AI;
using Microsoft.Extensions.Options;

namespace OpenAI.Services;

internal class OllamaAIMessenger(IOptions<AIModelOptions> options, IChatClient chatClient, ToolsProvider tools) : IAIMessenger
{
    public async ValueTask<string> GenerateConversationTopicsAsync(string message, CancellationToken cancellationToken = default)
    {
        var generateOptions = new ChatCompletionOptions
        {
            Model = options.Value.GenerateTopicsModel,
            Temperature = 0.1f,
            TopP = 0.7f,
            MaxTokens = 30
        };

        return await chatClient.GenerateAsync(message, generateOptions, @"
너는 대화의 핵심 주제를 요약하는 전문가야. 사용자의 입력 메시지를 분석하여 사용자가 무엇을 묻거나 요청하는지 파악하고, 그 의도를 포함한 명사형 구문으로 20자 이내로 요약해.

[출력 형식 예시]
Claude AI에 대한 질문
Docker 로그 관리 문의
API 키 발급 방법 확인

규칙: 서론 없이 오직 요약된 문구만 출력해.
", cancellationToken);
    }

    public ValueTask<IAIChat> CreateChatAsync(string conversationTopics, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult<IAIChat>(new OllamaAIChat(conversationTopics, options.Value, chatClient, tools));
    }
}
