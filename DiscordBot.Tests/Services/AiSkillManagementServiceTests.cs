using System.Text.Json;
using DiscordBot.Repositories;
using DiscordBot.Services;

namespace DiscordBot.Tests.Services;

public sealed class AiSkillManagementServiceTests
{
    [Fact]
    public async Task GetAllAsync_HidesDatabaseSkillShadowedByLocalTemplate()
    {
        var repository = new FakeAiSkillRepository();
        repository.Data.Add(CreateData("image-generation", "Shadow.", ["그림"], "DB shadow."));
        repository.Data.Add(CreateData("professional-answer", "DB.", ["전문"], "DB instruction."));
        var service = CreateService(
            repository,
            new AiSkillDefinition(
                "image-generation",
                "Local image skill.",
                95,
                ["그림"],
                "Local instruction.",
                AiSkillSource.Local,
                ["generate_image"]));

        var skills = await service.GetAllAsync();

        Assert.Equal(["image-generation", "professional-answer"], skills.Select(skill => skill.Name).OrderBy(name => name));
        var imageSkill = Assert.Single(skills, skill => skill.Name == "image-generation");
        Assert.Equal(AiSkillSource.Local, imageSkill.Source);
        Assert.Equal("Local instruction.", imageSkill.Instructions);
    }

    [Fact]
    public async Task SaveDatabaseSkillAsync_RejectsLocalSkillName()
    {
        var service = CreateService(
            new FakeAiSkillRepository(),
            new AiSkillDefinition(
                "image-generation",
                "Local image skill.",
                95,
                ["그림"],
                "Local instruction.",
                AiSkillSource.Local,
                ["generate_image"]));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.SaveDatabaseSkillAsync(new AiSkillSaveRequest(
                "image-generation",
                "DB skill.",
                0,
                ["그림"],
                "DB instruction.",
                true)));
    }

    [Fact]
    public async Task RenameDatabaseSkillAsync_RenamesDatabaseSkill()
    {
        var repository = new FakeAiSkillRepository();
        repository.Data.Add(CreateData("professional-answer", "DB.", ["전문"], "DB instruction."));
        var service = CreateService(repository);

        await service.RenameDatabaseSkillAsync("professional-answer", "expert-answer");

        Assert.Null(await repository.GetAsync("professional-answer"));
        Assert.NotNull(await repository.GetAsync("expert-answer"));
    }

    [Fact]
    public async Task SearchAsync_FindsInstructionsAndSkillContent()
    {
        var repository = new FakeAiSkillRepository();
        repository.Data.Add(CreateData("professional-answer", "DB.", ["전문"], "보안 검토 지침입니다."));
        var service = CreateService(repository);

        var results = await service.SearchAsync("보안");

        Assert.Contains(results, result => result.Kind == "instructions" && result.Name == "global");
        Assert.Contains(results, result => result.Kind == "skill" && result.Name == "professional-answer");
    }

    private static AiSkillManagementService CreateService(
        FakeAiSkillRepository repository,
        params AiSkillDefinition[] templates)
    {
        return new AiSkillManagementService(
            repository,
            new FakeAiSkillTemplateProvider(templates),
            new FakeClaudeSettingsService());
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

        public ValueTask<IReadOnlyList<AiSkillData>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult<IReadOnlyList<AiSkillData>>([.. Data]);
        }

        public ValueTask<AiSkillData?> GetAsync(string name, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(Data.FirstOrDefault(skill => skill.Name == name));
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
            Data.RemoveAll(skill => skill.Name == name);
            Data.Add(CreateData(name, description, triggerPhrases, instructions, priority, enabled));
            return ValueTask.CompletedTask;
        }

        public ValueTask RenameAsync(string name, string newName, CancellationToken cancellationToken = default)
        {
            var index = Data.FindIndex(skill => skill.Name == name);
            if (index >= 0)
            {
                Data[index] = Data[index] with { Name = newName, UpdatedAt = new DateTime(2026, 7, 2) };
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask DeleteAsync(string name, CancellationToken cancellationToken = default)
        {
            Data.RemoveAll(skill => skill.Name == name);
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

    private sealed class FakeClaudeSettingsService : IClaudeSettingsService
    {
        public ValueTask<ClaudeSettingsData> GetAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(new ClaudeSettingsData(
                "model",
                "summary",
                4096,
                "보안 기본 지침입니다.",
                new DateTime(2026, 7, 1),
                null));
        }

        public ValueTask SaveAsync(
            string model,
            string summaryModel,
            int defaultMaxTokens,
            string instructions,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }
    }
}
