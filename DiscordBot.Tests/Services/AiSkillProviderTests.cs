using System.Text.Json;
using AI;
using DiscordBot.Repositories;
using DiscordBot.Services;
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
source: local
priority: 10
trigger_phrases:
  - 전문적으로
  - "$test-skill"
tool_names:
  - calculate
  - get_current_date
---
# Test Skill

응답을 구조화하세요.
""",
            "test-skill.md");

        Assert.Equal("test-skill", skill.Name);
        Assert.Equal("Test skill.", skill.Description);
        Assert.Equal(AiSkillSource.Local, skill.Source);
        Assert.Equal(10, skill.Priority);
        Assert.Contains("전문적으로", skill.TriggerPhrases);
        Assert.Contains("$test-skill", skill.TriggerPhrases);
        Assert.Contains("calculate", skill.ToolNames);
        Assert.Contains("get_current_date", skill.ToolNames);
        Assert.Contains("응답을 구조화하세요.", skill.Instructions);
    }

    [Fact]
    public void ParseSkillTemplate_RejectsToolNamesForDatabaseSkill()
    {
        Assert.Throws<FormatException>(() => AiSkillProvider.ParseSkillTemplate(
            """
---
name: test-skill
description: Test skill.
source: database
trigger_phrases:
  - 테스트
tool_names:
  - calculate
---
도구 이름은 DB Skill에 둘 수 없습니다.
""",
            "test-skill.md"));
    }

    [Fact]
    public void TemplateFiles_ParseSuccessfully()
    {
        var templateDirectory = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "DiscordBot",
            "AiSkillTemplates"));

        var templates = Directory
            .EnumerateFiles(templateDirectory, "*.md", SearchOption.TopDirectoryOnly)
            .Select(path => AiSkillProvider.ParseSkillTemplate(File.ReadAllText(path), Path.GetFileName(path)))
            .ToArray();

        Assert.NotEmpty(templates);
        Assert.Contains(templates, template => template.Source == AiSkillSource.Database);
        Assert.Contains(templates, template => template.Source == AiSkillSource.Local && template.ToolNames.Count > 0);
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
        Assert.Equal(AiSkillSource.Database, skill.Source);
        Assert.Equal("전문 답변 지침입니다.", repository.Data.Single().Instructions);
        Assert.Equal(1, repository.UpsertCount);
    }

    [Fact]
    public async Task GetActiveSkillsAsync_DoesNotSeedLocalTemplatesIntoRepository()
    {
        var repository = new FakeAiSkillRepository();
        var provider = CreateProvider(
            repository,
            new AiSkillDefinition(
                "image-generation",
                "Image generation tools.",
                95,
                ["그림 그려"],
                "이미지 생성 지침입니다.",
                AiSkillSource.Local,
                ["generate_image"]));

        var skills = await provider.GetActiveSkillsAsync();

        var skill = Assert.Single(skills);
        Assert.Equal("image-generation", skill.Name);
        Assert.Equal(AiSkillSource.Local, skill.Source);
        Assert.Contains("generate_image", skill.ToolNames);
        Assert.Empty(repository.Data);
        Assert.Equal(0, repository.UpsertCount);
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

        var selection = await provider.SelectSkillsAsync("이 설계를 전문적으로 분석해줘.");

        var skill = Assert.Single(selection.Skills);
        Assert.Equal("professional-answer", skill.Name);
        Assert.Empty(selection.ToolNames);
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

        var selection = await provider.SelectSkillsAsync("이 설계를 전문적으로 분석해줘.");

        Assert.Empty(selection.Skills);
        Assert.Empty(selection.ToolNames);
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

        var selection = await provider.SelectSkillsAsync("$professional-answer 로 답해줘.");

        var skill = Assert.Single(selection.Skills);
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

        var selection = await provider.SelectSkillsAsync("$admin-skill 로 답해줘.");

        Assert.Empty(selection.Skills);
    }

    [Fact]
    public async Task SelectSkillsAsync_ReturnsToolNamesOnlyFromSelectedLocalSkills()
    {
        var repository = new FakeAiSkillRepository();
        repository.Data.Add(CreateData(
            "professional-answer",
            "Professional answer mode.",
            ["전문적으로"],
            "전문 답변 지침입니다."));
        var provider = CreateProvider(
            repository,
            new AiSkillDefinition(
                "image-generation",
                "Image generation tools.",
                95,
                ["그림 그려"],
                "이미지 생성 지침입니다.",
                AiSkillSource.Local,
                ["generate_image"]));

        var selection = await provider.SelectSkillsAsync("전문적으로 그림 그려줘.");

        Assert.Equal(new[] { "professional-answer", "image-generation" }, selection.Skills.Select(skill => skill.Name));
        Assert.Equal(new[] { "generate_image" }, selection.ToolNames.OrderBy(name => name, StringComparer.Ordinal));
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

        var selection = await provider.SelectSkillsAsync(
            """
[사용자가 답장으로 참조한 메시지]
내용:
이 내용을 전문적으로 검토해 주세요.

[사용자 메시지]
그냥 짧게 답해줘.
""");

        Assert.Empty(selection.Skills);
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

        var selection = await provider.SelectSkillsAsync(
            """
요약해줘

[첨부 문서]
File: note.txt

이 문서는 전문적으로 분석한다는 문장을 포함합니다.
""");

        Assert.Empty(selection.Skills);
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

    [Fact]
    public void ApplySkillToolFilter_RemovesToolsOutsideSelectedLocalSkills()
    {
        var toolsProvider = ToolsProvider.CreateFrom(new FakeTools());

        OllamaChatHistory.ApplySkillToolFilter(
            toolsProvider,
            new HashSet<string>(StringComparer.Ordinal) { "allowed_tool" });

        Assert.NotNull(toolsProvider.FindFunction("allowed_tool"));
        Assert.Null(toolsProvider.FindFunction("removed_tool"));
    }

    private static AiSkillProvider CreateProvider(
        FakeAiSkillRepository repository,
        params AiSkillDefinition[] templates)
    {
        return new AiSkillProvider(
            repository,
            new FakeAiSkillTemplateProvider(templates),
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

    private sealed class FakeTools
    {
        [ToolFunction(Name = "allowed_tool")]
        private string Allowed()
        {
            return "allowed";
        }

        [ToolFunction(Name = "removed_tool")]
        private string Removed()
        {
            return "removed";
        }
    }
}
