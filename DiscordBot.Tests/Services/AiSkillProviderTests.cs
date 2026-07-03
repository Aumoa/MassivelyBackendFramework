using DiscordBot.Services;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiscordBot.Tests.Services;

public sealed class AiSkillProviderTests
{
    [Fact]
    public void ParseSkill_ReadsFrontmatterAndInstructionBody()
    {
        var skill = FileAiSkillProvider.ParseSkill(
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

        Assert.NotNull(skill);
        Assert.Equal("test-skill", skill.Name);
        Assert.Equal("Test skill.", skill.Description);
        Assert.Equal(10, skill.Priority);
        Assert.Contains("전문적으로", skill.TriggerPhrases);
        Assert.Contains("$test-skill", skill.TriggerPhrases);
        Assert.Contains("응답을 구조화하세요.", skill.Instructions);
    }

    [Fact]
    public void SelectSkills_LoadsMatchingTriggerPhrase()
    {
        using var skillRoot = TemporarySkillRoot.Create();
        skillRoot.WriteSkill(
            "professional-answer.md",
            """
---
name: professional-answer
description: Professional answer mode.
priority: 100
trigger_phrases:
  - 전문적으로
---
전문 답변 지침입니다.
""");

        var provider = CreateProvider(skillRoot.RootPath);

        var skills = provider.SelectSkills("이 설계를 전문적으로 분석해줘.");

        var skill = Assert.Single(skills);
        Assert.Equal("professional-answer", skill.Name);
    }

    [Fact]
    public void SelectSkills_LoadsExplicitExistingSkillName()
    {
        using var skillRoot = TemporarySkillRoot.Create();
        skillRoot.WriteSkill(
            "professional-answer.md",
            """
---
name: professional-answer
description: Professional answer mode.
priority: 100
trigger_phrases:
  - 전문적으로
---
전문 답변 지침입니다.
""");

        var provider = CreateProvider(skillRoot.RootPath);

        var skills = provider.SelectSkills("$professional-answer 로 답해줘.");

        var skill = Assert.Single(skills);
        Assert.Equal("professional-answer", skill.Name);
    }

    [Fact]
    public void SelectSkills_IgnoresUnknownExplicitSkillName()
    {
        using var skillRoot = TemporarySkillRoot.Create();
        skillRoot.WriteSkill(
            "professional-answer.md",
            """
---
name: professional-answer
description: Professional answer mode.
priority: 100
trigger_phrases:
  - 전문적으로
---
전문 답변 지침입니다.
""");

        var provider = CreateProvider(skillRoot.RootPath);

        var skills = provider.SelectSkills("$admin-skill 로 답해줘.");

        Assert.Empty(skills);
    }

    [Fact]
    public void SelectSkills_UsesCurrentUserMessageInsteadOfReferencedContext()
    {
        using var skillRoot = TemporarySkillRoot.Create();
        skillRoot.WriteSkill(
            "professional-answer.md",
            """
---
name: professional-answer
description: Professional answer mode.
priority: 100
trigger_phrases:
  - 전문적으로
---
전문 답변 지침입니다.
""");

        var provider = CreateProvider(skillRoot.RootPath);

        var skills = provider.SelectSkills(
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
    public void SelectSkills_UsesPromptBeforeAttachmentContent()
    {
        using var skillRoot = TemporarySkillRoot.Create();
        skillRoot.WriteSkill(
            "professional-answer.md",
            """
---
name: professional-answer
description: Professional answer mode.
priority: 100
trigger_phrases:
  - 전문적으로
---
전문 답변 지침입니다.
""");

        var provider = CreateProvider(skillRoot.RootPath);

        var skills = provider.SelectSkills(
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
        var instruction = FileAiSkillProvider.BuildSystemInstruction(
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

    private static FileAiSkillProvider CreateProvider(string rootPath)
    {
        return new FileAiSkillProvider(
            new FakeHostEnvironment(rootPath),
            NullLogger<FileAiSkillProvider>.Instance);
    }

    private sealed class FakeHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";

        public string ApplicationName { get; set; } = "DiscordBot.Tests";

        public string ContentRootPath { get; set; } = contentRootPath;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class TemporarySkillRoot : IDisposable
    {
        private TemporarySkillRoot(string rootPath)
        {
            RootPath = rootPath;
            Directory.CreateDirectory(Path.Combine(rootPath, "AiSkills"));
        }

        public string RootPath { get; }

        public static TemporarySkillRoot Create()
        {
            var rootPath = Path.Combine(Path.GetTempPath(), "DiscordBot.Tests", Guid.NewGuid().ToString("N"));
            return new TemporarySkillRoot(rootPath);
        }

        public void WriteSkill(string fileName, string content)
        {
            File.WriteAllText(Path.Combine(RootPath, "AiSkills", fileName), content);
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
