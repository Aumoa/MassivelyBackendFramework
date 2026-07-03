using AI.Providers.Claude;
using DiscordBot.Repositories;
using DiscordBot.Services;
using Microsoft.Extensions.Caching.Memory;

namespace DiscordBot.Tests.Services;

public sealed class ClaudeSettingsServiceTests
{
    [Fact]
    public async Task GetAsync_WhenSettingsDoNotExist_PersistsAppsettingsInstructions()
    {
        var repository = new FakeClaudeSettingsRepository();
        var service = CreateService(repository, persona: " 기본 지침 ");

        var settings = await service.GetAsync();

        Assert.Equal("app-model", settings.Model);
        Assert.Equal("summary-model", settings.SummaryModel);
        Assert.Equal(321, settings.DefaultMaxTokens);
        Assert.Equal("기본 지침", settings.Instructions);
        Assert.Equal("기본 지침", repository.Data?.Instructions);
        Assert.Equal(1, repository.UpsertCount);
    }

    [Fact]
    public async Task GetAsync_WhenInstructionsAreMissing_BackfillsAppsettingsInstructions()
    {
        var repository = new FakeClaudeSettingsRepository
        {
            Data = new ClaudeSettingsData(
                "db-model",
                "db-summary",
                123,
                null,
                new DateTime(2026, 7, 1),
                null)
        };
        var service = CreateService(repository, persona: "기본 지침");

        var settings = await service.GetAsync();

        Assert.Equal("db-model", settings.Model);
        Assert.Equal("db-summary", settings.SummaryModel);
        Assert.Equal(123, settings.DefaultMaxTokens);
        Assert.Equal("기본 지침", settings.Instructions);
        Assert.Equal("기본 지침", repository.Data?.Instructions);
        Assert.Equal(1, repository.UpsertCount);
    }

    [Fact]
    public async Task SaveAsync_UpdatesCachedInstructionsImmediately()
    {
        var repository = new FakeClaudeSettingsRepository
        {
            Data = new ClaudeSettingsData(
                "db-model",
                "db-summary",
                123,
                "old",
                new DateTime(2026, 7, 1),
                null)
        };
        var service = CreateService(repository, persona: "기본 지침");

        await service.GetAsync();
        await service.SaveAsync(" new-model ", " new-summary ", 456, " 새 지침 ");

        repository.Data = repository.Data! with
        {
            Model = "repository-model",
            Instructions = "repository 지침"
        };

        var settings = await service.GetAsync();

        Assert.Equal("new-model", settings.Model);
        Assert.Equal("new-summary", settings.SummaryModel);
        Assert.Equal(456, settings.DefaultMaxTokens);
        Assert.Equal("새 지침", settings.Instructions);
        Assert.Equal("새 지침", repository.LastInstructions);
    }

    private static ClaudeSettingsService CreateService(
        FakeClaudeSettingsRepository repository,
        string? persona = null)
    {
        return new ClaudeSettingsService(
            repository,
            Microsoft.Extensions.Options.Options.Create(new OllamaService.Configuration
            {
                Persona = persona,
                MemorySize = 20,
                Model = "app-model",
                SummaryModel = "summary-model"
            }),
            Microsoft.Extensions.Options.Options.Create(new ClaudeChatClientOptions
            {
                DefaultMaxTokens = 321
            }),
            new MemoryCache(new MemoryCacheOptions()));
    }

    private sealed class FakeClaudeSettingsRepository : IClaudeSettingsRepository
    {
        public ClaudeSettingsData? Data { get; set; }

        public int UpsertCount { get; private set; }

        public string? LastInstructions { get; private set; }

        public ValueTask<ClaudeSettingsData?> GetAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(Data);
        }

        public ValueTask UpsertAsync(
            string model,
            string summaryModel,
            int defaultMaxTokens,
            string instructions,
            CancellationToken cancellationToken = default)
        {
            UpsertCount++;
            LastInstructions = instructions;
            Data = new ClaudeSettingsData(
                model,
                summaryModel,
                defaultMaxTokens,
                instructions,
                Data?.CreatedAt ?? DateTime.Now,
                DateTime.Now);
            return ValueTask.CompletedTask;
        }
    }
}
