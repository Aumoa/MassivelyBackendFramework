using System.Net.Http.Json;
using AI;
using Microsoft.Extensions.Options;
using OpenAI.Ollama;

namespace OpenAI.Services;

internal class OllamaAIMessenger(IOptions<OllamaOptions> options, HttpClient http, ToolsProvider tools) : IAIMessenger
{
    public async ValueTask<string> GenerateConversationTopicsAsync(string message, CancellationToken cancellationToken = default)
    {
        var response = await http.PostAsJsonAsync(options.Value.Uri + "/api/generate", new
        {
            model = options.Value.GenerateTopicsModel,
            prompt = message,
            stream = false,
            system = @"
너는 대화의 핵심 주제를 요약하는 전문가야. 사용자의 입력 메시지를 분석하여 사용자가 무엇을 묻거나 요청하는지 파악하고, 그 의도를 포함한 명사형 구문으로 20자 이내로 요약해.

[출력 형식 예시]
Ollama AI에 대한 질문
Docker 로그 관리 문의
API 키 발급 방법 확인

규칙: 서론 없이 오직 요약된 문구만 출력해.
",
            options = new
            {
                num_predict = 30,
                temperature = 0.1,
                top_p = 0.7
            }
        }, cancellationToken);
        response.EnsureSuccessStatusCode();
        var generateResponse = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Failed to deserialize the response from Ollama API.");

        return generateResponse.Response;
    }

    public ValueTask<IAIChat> CreateChatAsync(string conversationTopics, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult<IAIChat>(new OllamaAIChat(conversationTopics, options.Value, http, tools));
    }
}
