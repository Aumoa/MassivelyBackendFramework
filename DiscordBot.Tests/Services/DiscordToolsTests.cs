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

    [Fact]
    public void BuildChatSearchResultDetails_IncludesReferencedContent()
    {
        var parent = CreateLog(id: 1, messageId: "111", content: "원 의견");
        var target = CreateLog(
            id: 2,
            messageId: "222",
            content: "답장 의견",
            referencedMessageId: "111",
            referencedChannelId: "222",
            referencedGuildId: "999");

        var result = DiscordTools.BuildChatSearchResultDetails(
            1,
            target,
            parent,
            TimeZoneInfo.Utc,
            "bot");

        Assert.Contains("--- 결과 #1 ---", result);
        Assert.Contains("ReplyTo: https://discord.com/channels/999/222/111", result);
        Assert.Contains("ReferencedContent: 원 의견", result);
        Assert.Contains("Content: 답장 의견", result);
    }

    [Fact]
    public void NormalizeChatSearchKeywords_ExpandsLightweightSynonyms()
    {
        var keywords = DiscordTools.NormalizeChatSearchKeywords("닭집, 배포");

        Assert.Contains("닭집", keywords);
        Assert.Contains("닭한마리", keywords);
        Assert.Contains("닭 한마리", keywords);
        Assert.Contains("배포", keywords);
        Assert.Contains("릴리즈", keywords);
        Assert.Contains("release", keywords);
    }

    [Theory]
    [InlineData("<@1234567890>", "1234567890")]
    [InlineData("<@!1234567890>", "1234567890")]
    [InlineData("1234567890", "1234567890")]
    [InlineData("user-name", null)]
    public void NormalizeDiscordUserId_ParsesMentionsAndIds(string value, string? expected)
    {
        var userId = DiscordTools.NormalizeDiscordUserId(value);

        Assert.Equal(expected, userId);
    }

    [Fact]
    public void TryApplyChatSearchTimePreset_FillsLastSevenDays()
    {
        DateTimeOffset? from = null;
        DateTimeOffset? to = null;

        var ok = DiscordTools.TryApplyChatSearchTimePreset(
            "last_7_days",
            TimeZoneInfo.Utc,
            new DateTime(2026, 6, 14, 12, 0, 0, DateTimeKind.Utc),
            ref from,
            ref to,
            out var error);

        Assert.True(ok, error);
        Assert.Equal(new DateTimeOffset(2026, 6, 7, 12, 0, 0, TimeSpan.Zero), from);
        Assert.Equal(new DateTimeOffset(2026, 6, 14, 12, 0, 0, TimeSpan.Zero), to);
    }

    [Theory]
    [InlineData("최근 일주일", "last_7_days")]
    [InlineData("last-7-days", "last_7_days")]
    [InlineData("이번 주", "this_week")]
    public void NormalizeChatSearchTimePreset_MapsCommonForms(string value, string expected)
    {
        var preset = DiscordTools.NormalizeChatSearchTimePreset(value);

        Assert.Equal(expected, preset);
    }

    [Fact]
    public void NormalizeDiscussionKeywords_IncludesWholeTopicAndTokens()
    {
        var keywords = DiscordTools.NormalizeDiscussionKeywords(" 디스코드 봇, 배포 ");

        Assert.Equal(["디스코드 봇, 배포", "디스코드", "배포"], keywords);
    }

    [Theory]
    [InlineData("decision", "decisions")]
    [InlineData("actions", "action_items")]
    [InlineData("timeline", "timeline")]
    [InlineData("open_questions", "open_questions")]
    [InlineData("anything-else", "summary")]
    public void NormalizeDiscussionMode_MapsKnownModes(string value, string expected)
    {
        var mode = DiscordTools.NormalizeDiscussionMode(value);

        Assert.Equal(expected, mode);
    }

    [Fact]
    public void BuildDiscussionSummaryInput_FormatsActionItemsWithoutLinks()
    {
        var logs = new[]
        {
            CreateLog(id: 1, messageId: "111", content: "내일까지 초안을 올릴게요."),
            CreateLog(id: 2, messageId: "222", content: "좋아요. 리뷰는 제가 볼게요.")
        };

        var summaryInput = DiscordTools.BuildDiscussionSummaryInput(
            "222",
            "초안",
            "action_items",
            80,
            null,
            null,
            TimeZoneInfo.Utc,
            ["초안"],
            logs,
            includeLinks: false,
            source: "topic_search",
            contextEachSide: 2,
            selfUserId: "bot");

        Assert.Contains("- 조회 방식: 주제 검색 + 주변 맥락", summaryInput);
        Assert.Contains("- 요약 모드: 액션아이템", summaryInput);
        Assert.Contains("Content: 내일까지 초안을 올릴게요.", summaryInput);
        Assert.Contains("- 액션아이템은 담당자, 할 일, 기한이 발췌에서 확인될 때만 적으세요.", summaryInput);
        Assert.DoesNotContain("Link:", summaryInput);
    }

    [Fact]
    public void FilterAppointmentsByQuery_MatchesTitleAndDescriptionTerms()
    {
        var appointments = new[]
        {
            CreateAppointment(id: 1, title: "닭한마리 + 파티룸", description: "7월 저녁 모임"),
            CreateAppointment(id: 2, title: "치과 예약", description: "정기 검진")
        };

        var result = DiscordTools.FilterAppointmentsByQuery(appointments, "파티룸 저녁");

        Assert.Collection(result, appointment => Assert.Equal(1, appointment.Id));
    }

    [Fact]
    public void BuildAppointmentCandidateList_IncludesIdsAndResponseRule()
    {
        var appointments = new[]
        {
            CreateAppointment(id: 1, title: "닭한마리 + 파티룸", description: "저녁 약속")
        };

        var result = DiscordTools.BuildAppointmentCandidateList(appointments);

        Assert.Contains("약속 후보 1건:", result);
        Assert.Contains("ID: 1", result);
        Assert.Contains("제목: 닭한마리 + 파티룸", result);
        Assert.Contains("원본: https://discord.com/channels/999/222/333", result);
        Assert.Contains("후보가 하나이고 사용자 요청과 명확히 일치하면", result);
    }

    [Fact]
    public void BuildAppointmentDetails_SeparatesDetailsPlansAndSuggestions()
    {
        var appointment = CreateAppointment(id: 1, title: "닭한마리 + 파티룸", description: "저녁에 만나서 이동");
        var items = new[]
        {
            CreateAppointmentItem(id: 10, appointmentId: 1, itemType: "plan", content: "닭한마리 먹기", sortOrder: 1),
            CreateAppointmentItem(id: 20, appointmentId: 1, itemType: "suggestion", content: "보드게임 가져가기", sortOrder: 1)
        };

        var result = DiscordTools.BuildAppointmentDetails(appointment, items, TimeZoneInfo.Utc);

        Assert.Contains("약속 상세 조회 결과:", result);
        Assert.Contains("상세:\n저녁에 만나서 이동", result);
        Assert.Contains("플랜:\n- ItemId 10: 닭한마리 먹기", result);
        Assert.Contains("제안:\n- ItemId 20: 보드게임 가져가기", result);
        Assert.Contains("플랜은 확정된 항목, 제안은 미확정 의견입니다.", result);
    }

    [Theory]
    [InlineData("plan", "plan")]
    [InlineData("플랜", "plan")]
    [InlineData("suggestion", "suggestion")]
    [InlineData("제안", "suggestion")]
    [InlineData("all", "all")]
    [InlineData("???", null)]
    public void NormalizeAppointmentItemType_MapsAliases(string value, string? expected)
    {
        var itemType = DiscordTools.NormalizeAppointmentItemType(value);

        Assert.Equal(expected, itemType);
    }

    [Fact]
    public void SplitAppointmentItemContents_TrimsBulletsAndDeduplicates()
    {
        var items = DiscordTools.SplitAppointmentItemContents("""
- 닭한마리 먹기
* 파티룸 이동
닭한마리 먹기
""");

        Assert.Equal(["닭한마리 먹기", "파티룸 이동"], items);
    }

    [Fact]
    public void ParseAppointmentItemIds_ParsesDistinctPositiveIds()
    {
        var itemIds = DiscordTools.ParseAppointmentItemIds("10, 20\n10 nope -1");

        Assert.Equal([10, 20], itemIds);
    }

    [Fact]
    public void BuildChannelNoteList_FormatsSharedChannelNotes()
    {
        var notes = new[]
        {
            CreateChannelNote(id: 5, title: "배포 규칙", content: "배포 전에는 채널에서 한 번 더 확인한다.", tags: "배포,확인")
        };

        var result = DiscordTools.BuildChannelNoteList(notes);

        Assert.Contains("채널 메모 1건:", result);
        Assert.Contains("ID: 5", result);
        Assert.Contains("제목: 배포 규칙", result);
        Assert.Contains("태그: 배포,확인", result);
        Assert.Contains("내용: 배포 전에는 채널에서 한 번 더 확인한다.", result);
        Assert.Contains("원본: https://discord.com/channels/999/222/333", result);
        Assert.Contains("개인 지침이나 개인 기억으로 표현하지 말고 채널 메모라고 표현하세요.", result);
    }

    [Fact]
    public void NormalizeChannelNoteContent_TruncatesLongContent()
    {
        var content = new string('a', 4_001);

        var result = DiscordTools.NormalizeChannelNoteContent(content);

        Assert.Equal(4_000, result.Length);
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

    private static AppointmentData CreateAppointment(
        long id = 1,
        string? guildId = "999",
        string channelId = "222",
        string userId = "user-1",
        string? sourceMessageId = "333",
        string title = "약속",
        string? description = null,
        DateTime? startsAtUtc = null,
        bool hasTime = true,
        string timezone = "UTC",
        string status = "active",
        DateTime? createdAt = null,
        DateTime? updatedAt = null,
        DateTime? expiresAtUtc = null)
    {
        var start = startsAtUtc ?? new DateTime(2026, 7, 18, 10, 0, 0, DateTimeKind.Utc);
        return new AppointmentData(
            id,
            guildId,
            channelId,
            userId,
            sourceMessageId,
            title,
            description,
            start,
            hasTime,
            timezone,
            status,
            createdAt ?? new DateTime(2026, 6, 14, 1, 2, 3, DateTimeKind.Utc),
            updatedAt,
            expiresAtUtc ?? start.AddDays(30));
    }

    private static AppointmentItemData CreateAppointmentItem(
        long id = 1,
        long appointmentId = 1,
        string itemType = "plan",
        string createdByUserId = "user-1",
        string content = "항목",
        string status = "active",
        int sortOrder = 1,
        DateTime? createdAt = null,
        DateTime? updatedAt = null)
    {
        return new AppointmentItemData(
            id,
            appointmentId,
            itemType,
            createdByUserId,
            content,
            status,
            sortOrder,
            createdAt ?? new DateTime(2026, 6, 14, 1, 2, 3, DateTimeKind.Utc),
            updatedAt);
    }

    private static ChannelNoteData CreateChannelNote(
        long id = 1,
        string? guildId = "999",
        string channelId = "222",
        string createdByUserId = "user-1",
        string title = "메모",
        string content = "내용",
        string? tags = null,
        string? sourceMessageId = "333",
        string status = "active",
        DateTime? createdAt = null,
        DateTime? updatedAt = null)
    {
        return new ChannelNoteData(
            id,
            guildId,
            channelId,
            createdByUserId,
            title,
            content,
            tags,
            sourceMessageId,
            status,
            createdAt ?? new DateTime(2026, 6, 14, 1, 2, 3, DateTimeKind.Utc),
            updatedAt);
    }
}
