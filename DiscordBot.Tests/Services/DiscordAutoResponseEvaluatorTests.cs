using System.Net;
using DiscordBot.Options;
using DiscordBot.Services;

namespace DiscordBot.Tests.Services;

public sealed class DiscordAutoResponseEvaluatorTests
{
    [Fact]
    public void PassesStaticFilter_RejectsDirectMessages()
    {
        var message = CreateMessage("봇이 이거 할 수 있나?", isDirectMessage: true);

        var result = DiscordAutoResponseEvaluator.PassesStaticFilter(message, new AutoResponseOptions());

        Assert.False(result);
    }

    [Fact]
    public void PassesStaticFilter_RejectsAlreadyMentionedMessages()
    {
        var message = CreateMessage("봇아 이거 봐줘", mentionsBot: true);

        var result = DiscordAutoResponseEvaluator.PassesStaticFilter(message, new AutoResponseOptions());

        Assert.False(result);
    }

    [Fact]
    public void PassesStaticFilter_AcceptsBotAliasWithoutMention()
    {
        var message = CreateMessage("봇이 이런 것도 처리할 수 있나?");

        var result = DiscordAutoResponseEvaluator.PassesStaticFilter(message, new AutoResponseOptions());

        Assert.True(result);
    }

    [Fact]
    public void PassesStaticFilter_AcceptsQuestionLikeMessage()
    {
        var message = CreateMessage("이거 배포 순서 아는 사람?");

        var result = DiscordAutoResponseEvaluator.PassesStaticFilter(message, new AutoResponseOptions());

        Assert.True(result);
    }

    [Fact]
    public void PassesStaticFilter_RejectsBareUrlWithVaguePrompt()
    {
        var message = CreateMessage("https://youtube.com/watch?v=abc 이거 어때?");

        var result = DiscordAutoResponseEvaluator.PassesStaticFilter(message, new AutoResponseOptions());

        Assert.False(result);
    }

    [Fact]
    public void PassesStaticFilter_AcceptsReplyToBotMessage()
    {
        var message = CreateMessage("그럼 다시 해줘", referencesBotMessage: true);

        var result = DiscordAutoResponseEvaluator.PassesStaticFilter(message, new AutoResponseOptions());

        Assert.True(result);
    }

    [Fact]
    public void ParseDecision_ReadsSnakeCaseJson()
    {
        var decision = DiscordAutoResponseEvaluator.ParseDecision(
            """{"should_respond":true,"reason":"봇을 지칭함","focus":"설정 설명"}""");

        Assert.True(decision.ShouldRespond);
        Assert.Equal("봇을 지칭함", decision.Reason);
        Assert.Equal("설정 설명", decision.Focus);
    }

    [Fact]
    public void ParseDecision_DefaultsToDeclineForInvalidJson()
    {
        var decision = DiscordAutoResponseEvaluator.ParseDecision("응답해야 함");

        Assert.False(decision.ShouldRespond);
        Assert.Equal("invalid_classifier_response", decision.Reason);
    }

    [Fact]
    public void BuildAutomaticResponsePrompt_InstructsToolUseForNearbyContext()
    {
        var prompt = DiscordAutoResponseEvaluator.BuildAutomaticResponsePrompt(
            [CreateMessage("아까 봇이 말한 거 다시 설명 가능?")],
            new DiscordAutoResponseDecision(true, "봇 후속 반응", "이전 설명 보충"));

        Assert.Contains("[자동 응답 모드]", prompt);
        Assert.Contains("현재 채널의 채팅 조회 도구", prompt);
        Assert.Contains("이전 설명 보충", prompt);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(HttpStatusCode.RequestTimeout, true)]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    public void IsTransientClassifierFailure_ClassifiesStatusCodes(
        HttpStatusCode? statusCode,
        bool expected)
    {
        var exception = new HttpRequestException("request failed", null, statusCode);

        var result = DiscordAutoResponseEvaluator.IsTransientClassifierFailure(exception);

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task ExecuteClassifierRequestAsync_RetriesTransientFailuresWithBackoff()
    {
        var attempts = 0;
        var delays = new List<TimeSpan>();

        var result = await DiscordAutoResponseEvaluator.ExecuteClassifierRequestAsync(
            operation: _ =>
            {
                attempts++;
                return attempts < 3
                    ? Task.FromException<string>(new HttpRequestException(
                        "service unavailable",
                        null,
                        HttpStatusCode.ServiceUnavailable))
                    : Task.FromResult("success");
            },
            onRetry: null,
            cancellationToken: CancellationToken.None,
            delayAsync: (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            });

        Assert.Equal("success", result);
        Assert.Equal(3, attempts);
        Assert.Equal(
            [TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(500)],
            delays);
    }

    [Fact]
    public async Task ExecuteClassifierRequestAsync_DoesNotRetryPermanentFailure()
    {
        var attempts = 0;

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            DiscordAutoResponseEvaluator.ExecuteClassifierRequestAsync(
                operation: _ =>
                {
                    attempts++;
                    return Task.FromException<string>(new HttpRequestException(
                        "bad request",
                        null,
                        HttpStatusCode.BadRequest));
                },
                onRetry: null,
                cancellationToken: CancellationToken.None,
                delayAsync: (_, _) => Task.CompletedTask));

        Assert.Equal(1, attempts);
    }

    private static DiscordAutoResponseMessage CreateMessage(
        string content,
        bool isDirectMessage = false,
        bool mentionsBot = false,
        bool referencesBotMessage = false,
        bool hasAttachments = false)
    {
        return new DiscordAutoResponseMessage(
            "message-1",
            "guild-1",
            "channel-1",
            "user-1",
            "tester",
            content,
            new DateTimeOffset(2026, 7, 5, 12, 0, 0, TimeSpan.Zero),
            isDirectMessage,
            mentionsBot,
            referencesBotMessage,
            hasAttachments);
    }
}
