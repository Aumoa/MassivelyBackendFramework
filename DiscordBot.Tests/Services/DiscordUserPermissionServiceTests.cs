using DiscordBot.Repositories;
using DiscordBot.Services;

namespace DiscordBot.Tests.Services;

public sealed class DiscordUserPermissionServiceTests
{
    [Fact]
    public void BuildAuthorization_PromotesDiscordApplicationOwner()
    {
        var owner = new DiscordApplicationOwnerSnapshot(
            "app-1",
            "Test Bot",
            "123",
            "owner",
            null,
            null,
            null,
            null,
            null);

        var authorization = DiscordUserPermissionService.BuildAuthorization(
            "123",
            "owner",
            null,
            owner);

        Assert.True(authorization.IsApplicationOwner);
        Assert.Equal("app_owner", authorization.PrimaryRole);
        Assert.Contains("app_owner", authorization.EffectiveRoles);
        Assert.Contains("general_user", authorization.EffectiveRoles);
        Assert.Contains("discord_application", authorization.AuthoritySources);
        Assert.Equal(DiscordUserPermissionLevels.User, authorization.RegisteredPermission);
    }

    [Fact]
    public void BuildAuthorization_UsesEnabledRegisteredAdministratorPermission()
    {
        var registered = CreateData(
            "456",
            DiscordUserPermissionLevels.Administrator,
            enabled: true);
        var owner = new DiscordApplicationOwnerSnapshot(
            "app-1",
            "Test Bot",
            "123",
            "owner",
            null,
            null,
            null,
            null,
            null);

        var authorization = DiscordUserPermissionService.BuildAuthorization(
            "456",
            "admin",
            registered,
            owner);

        Assert.False(authorization.IsApplicationOwner);
        Assert.Equal("administrator", authorization.PrimaryRole);
        Assert.Contains("administrator", authorization.EffectiveRoles);
        Assert.Contains("database", authorization.AuthoritySources);
        Assert.Equal(DiscordUserPermissionLevels.Administrator, authorization.RegisteredPermission);
        Assert.True(authorization.RegisteredPermissionEnabled);
    }

    [Fact]
    public void BuildAuthorization_IgnoresDisabledRegisteredPermission()
    {
        var registered = CreateData(
            "456",
            DiscordUserPermissionLevels.Administrator,
            enabled: false);
        var owner = new DiscordApplicationOwnerSnapshot(
            "app-1",
            "Test Bot",
            "123",
            "owner",
            null,
            null,
            null,
            null,
            null);

        var authorization = DiscordUserPermissionService.BuildAuthorization(
            "456",
            "disabled-admin",
            registered,
            owner);

        Assert.Equal("general_user", authorization.PrimaryRole);
        Assert.DoesNotContain("administrator", authorization.EffectiveRoles);
        Assert.DoesNotContain("database", authorization.AuthoritySources);
        Assert.Equal(DiscordUserPermissionLevels.User, authorization.RegisteredPermission);
        Assert.False(authorization.RegisteredPermissionEnabled);
    }

    [Fact]
    public async Task AddAsync_NormalizesPermissionAndUserId()
    {
        var repository = new FakeDiscordUserPermissionRepository();
        var service = new DiscordUserPermissionService(repository);

        await service.AddAsync(
            " 789 ",
            " Test User ",
            "Administrator",
            enabled: true,
            " Memo ",
            " creator ");

        var data = Assert.Single(repository.Data);
        Assert.Equal("789", data.UserId);
        Assert.Equal("Test User", data.DisplayName);
        Assert.Equal(DiscordUserPermissionLevels.Administrator, data.Permission);
        Assert.Equal("Memo", data.Memo);
        Assert.Equal("creator", data.CreatedBy);
    }

    [Fact]
    public void FormatAuthorization_IncludesTrustedAuthoritySources()
    {
        var owner = new DiscordApplicationOwnerSnapshot(
            "app-1",
            "Test Bot",
            "123",
            "owner",
            null,
            null,
            null,
            null,
            null);
        var authorization = DiscordUserPermissionService.BuildAuthorization(
            "123",
            "owner",
            null,
            owner);

        var text = DiscordUserAuthorizationTools.FormatAuthorization(authorization);

        Assert.Contains("Trusted Discord user authorization result", text);
        Assert.Contains("PrimaryRole: app_owner", text);
        Assert.Contains("AuthoritySources: discord_application", text);
    }

    private static DiscordUserPermissionData CreateData(
        string userId,
        string permission,
        bool enabled)
    {
        return new DiscordUserPermissionData(
            1,
            userId,
            "User",
            permission,
            enabled,
            null,
            "tester",
            new DateTime(2026, 7, 1),
            null);
    }

    private sealed class FakeDiscordUserPermissionRepository : IDiscordUserPermissionRepository
    {
        public List<DiscordUserPermissionData> Data { get; } = [];

        public ValueTask<IReadOnlyList<DiscordUserPermissionData>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult<IReadOnlyList<DiscordUserPermissionData>>([.. Data]);
        }

        public ValueTask<DiscordUserPermissionData?> GetAsync(long id, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(Data.FirstOrDefault(data => data.Id == id));
        }

        public ValueTask<DiscordUserPermissionData?> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(Data.FirstOrDefault(data => data.UserId == userId));
        }

        public ValueTask AddAsync(
            string userId,
            string? displayName,
            string permission,
            bool enabled,
            string? memo,
            string? createdBy,
            CancellationToken cancellationToken = default)
        {
            Data.Add(new DiscordUserPermissionData(
                Data.Count + 1,
                userId,
                displayName,
                permission,
                enabled,
                memo,
                createdBy,
                new DateTime(2026, 7, 1),
                null));
            return ValueTask.CompletedTask;
        }

        public ValueTask UpdateAsync(
            long id,
            string userId,
            string? displayName,
            string permission,
            bool enabled,
            string? memo,
            CancellationToken cancellationToken = default)
        {
            var index = Data.FindIndex(data => data.Id == id);
            if (index >= 0)
            {
                var data = Data[index];
                Data[index] = data with
                {
                    UserId = userId,
                    DisplayName = displayName,
                    Permission = permission,
                    Enabled = enabled,
                    Memo = memo,
                    UpdatedAt = new DateTime(2026, 7, 2)
                };
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask DeleteAsync(long id, CancellationToken cancellationToken = default)
        {
            Data.RemoveAll(data => data.Id == id);
            return ValueTask.CompletedTask;
        }
    }
}
