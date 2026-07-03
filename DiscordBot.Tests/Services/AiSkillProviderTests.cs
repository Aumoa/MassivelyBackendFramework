using System.Text.Json;
using DiscordBot.Repositories;
using DiscordBot.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiscordBot.Tests.Services;

public sealed class AiSkillProviderTests
{
    [Fact]
    public void ParseSkillTemplate_ReadsFrontmatterAndInstructionBody()
    {
        var skill = AiSkillProvider.ParseSkillTemplate(
            """
---
name: test-skill
description: Test skill.
priority: 10
trigger_phrases:
  - 전문적으로
  - "$test-skill"
---
# Test Skill

응답을 구조화하세요.
""",
            "test-skill.md");

        Assert.Equal("test-skill", skill.Name);
        Assert.Equal("Test skill.", skill.Description);
        Assert.Equal(10, skill.Priority);
        Assert.Contains("전문적으로", skill.TriggerPhrases);
        Assert.Contains("$test-skill", skill.TriggerPhrases);
        Assert.Contains("응답을 구조화하세요.", skill.Instructions);
    }

    [Fact]
    public async Task GetActiveSkillsAsync_SeedsMissingTemplatesIntoRepository()
    {
        var repository = new FakeAiSkillRepository();
        var provider = CreateProvider(
            repository,
            new AiSkillDefinition(
                "professional-answer",
                "Professional answer mode.",
                100,
                ["전문적으로"],
                "전문 답변 지침입니다."));

        var skills = await provider.GetActiveSkillsAsync();

        var skill = Assert.Single(skills);
        Assert.Equal("professional-answer", skill.Name);
        Assert.Equal("전문 답변 지침입니다.", repository.Data.Single().Instructions);
        Assert.Equal(1, repository.UpsertCount);
    }

    [Fact]
    public async Task GetActiveSkillsAsync_DoesNotOverwriteExistingDatabaseSkillWithTemplate()
    {
        var repository = new FakeAiSkillRepository();
        repository.Data.Add(CreateData(
            "professional-answer",
            "DB description.",
            ["전문적으로"],
            "DB에서 수정한 지침입니다."));

        var provider = CreateProvider(
            repository,
            new AiSkillDefinition(
                "professional-answer",
                "Template description.",
                100,
                ["전문적으로"],
                "템플릿 지침입니다."));

        var skills = await provider.GetActiveSkillsAsync();

        var skill = Assert.Single(skills);
        Assert.Equal("DB description.", skill.Description);
        Assert.Equal("DB에서 수정한 지침입니다.", skill.Instructions);
        Assert.Equal(0, repository.UpsertCount);
    }

    [Fact]
    public async Task SelectSkillsAsync_LoadsMatchingTriggerPhraseFromDatabase()
    {
        var repository = new FakeAiSkillRepository();
        repository.Data.Add(CreateData(
            "professional-answer",
            "Professional answer mode.",
            ["전문적으로"],
            "전문 답변 지침입니다."));
        var provider = CreateProvider(repository);

        var skills = await provider.SelectSkillsAsync("이 설계를 전문적으로 분석해줘.");

        var skill = Assert.Single(skills);
        Assert.Equal("professional-answer", skill.Name);
    }

    [Fact]
    public async Task SelectSkillsAsync_IgnoresDisabledDatabaseSkill()
    {
        var repository = new FakeAiSkillRepository();
        repository.Data.Add(CreateData(
            "professional-answer",
            "Professional answer mode.",
            ["전문적으로"],
            "전문 답변 지침입니다.",
            enabled: false));
        var provider = CreateProvider(repository);

        var skills = await provider.SelectSkillsAsync("이 설계를 전문적으로 분석해줘.");

        Assert.Empty(skills);
    }

    [Fact]
    public async Task SelectSkillsAsync_LoadsExplicitExistingSkillName()
    {
        var repository = new FakeAiSkillRepository();
        repository.Data.Add(CreateData(
            "professional-answer",
            "Professional answer mode.",
            ["전문적으로"],
            "전문 답변 지침입니다."));
        var provider = CreateProvider(repository);

        var skills = await provider.SelectSkillsAsync("$professional-answer 로 답해줘.");

        var skill = Assert.Single(skills);
        Assert.Equal("professional-answer", skill.Name);
    }

    [Fact]
    public async Task SelectSkillsAsync_IgnoresUnknownExplicitSkillName()
    {
        var repository = new FakeAiSkillRepository();
        repository.Data.Add(CreateData(
            "professional-answer",
            "Professional answer mode.",
            ["전문적으로"],
            "전문 답변 지침입니다."));
        var provider = CreateProvider(repository);

        var skills = await provider.SelectSkillsAsync("$admin-skill 로 답해줘.");

        Assert.Empty(skills);
    }

    [Fact]
    public async Task SelectSkillsAsync_UsesCurrentUserMessageInsteadOfReferencedContext()
    {
        var repository = new FakeAiSkillRepository();
        repository.Data.Add(CreateData(
            "professional-answer",
            "Professional answer mode.",
            ["전문적으로"],
            "전문 답변 지침입니다."));
        var provider = CreateProvider(repository);

        var skills = await provider.SelectSkillsAsync(
            """
[사용자가 답장으로 참조한 메시지]
내용:
이 내용을 전문적으로 검토해 주세요.

[사용자 메시지]
그냥 짧게 답해줘.
""");

        Assert.Empty(skills);
    }

    [Fact]
    public async Task SelectSkillsAsync_UsesPromptBeforeAttachmentContent()
    {
        var repository = new FakeAiSkillRepository();
        repository.Data.Add(CreateData(
            "professional-answer",
            "Professional answer mode.",
            ["전문적으로"],
            "전문 답변 지침입니다."));
        var provider = CreateProvider(repository);

        var skills = await provider.SelectSkillsAsync(
            """
요약해줘

[첨부 문서]
File: note.txt

이 문서는 전문적으로 분석한다는 문장을 포함합니다.
""");

        Assert.Empty(skills);
    }

    [Fact]
    public void BuildSystemInstruction_AddsPriorityGuardAndSkillBody()
    {
        var instruction = AiSkillProvider.BuildSystemInstruction(
            [
                new AiSkillDefinition(
                    "professional-answer",
                    "Professional answer mode.",
                    100,
                    ["전문적으로"],
                    "결론을 먼저 제시하세요.")
            ]);

        Assert.Contains("기본 응답 방침", instruction);
        Assert.Contains("낮은 우선순위", instruction);
        Assert.Contains("[Skill: professional-answer]", instruction);
        Assert.Contains("결론을 먼저 제시하세요.", instruction);
    }

    private static AiSkillProvider CreateProvider(
        FakeAiSkillRepository repository,
        params AiSkillDefinition[] templates)
    {
        return new AiSkillProvider(
            repository,
            new FakeAiSkillTemplateProvider(templates),
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<AiSkillProvider>.Instance);
    }

    private static AiSkillData CreateData(
        string name,
        string description,
        IReadOnlyList<string> triggerPhrases,
        string instructions,
        int priority = 100,
        bool enabled = true)
    {
        return new AiSkillData(
            name,
            description,
            priority,
            JsonSerializer.Serialize(triggerPhrases),
            instructions,
            enabled,
            new DateTime(2026, 7, 1),
            null);
    }

    private sealed class FakeAiSkillRepository : IAiSkillRepository
    {
        public List<AiSkillData> Data { get; } = [];

        public int UpsertCount { get; private set; }

        public ValueTask<IReadOnlyList<AiSkillData>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult<IReadOnlyList<AiSkillData>>([.. Data]);
        }

        public ValueTask UpsertAsync(
            string name,
            string description,
            int priority,
            IReadOnlyList<string> triggerPhrases,
            string instructions,
            bool enabled,
            CancellationToken cancellationToken = default)
        {
            UpsertCount++;
            Data.RemoveAll(skill => skill.Name == name);
            Data.Add(CreateData(name, description, triggerPhrases, instructions, priority, enabled));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeAiSkillTemplateProvider(
        IReadOnlyList<AiSkillDefinition> templates) : IAiSkillTemplateProvider
    {
        public IReadOnlyList<AiSkillDefinition> LoadTemplates()
        {
            return templates;
        }
    }
}
