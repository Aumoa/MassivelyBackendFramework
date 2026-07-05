using DiscordBot.Repositories;

namespace DiscordBot.Services;

internal static class DiscordUserPermissionLevels
{
    public const string User = "user";
    public const string Administrator = "administrator";

    public static IReadOnlyList<string> All { get; } = [User, Administrator];

    public static string Normalize(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        if (!All.Contains(normalized, StringComparer.Ordinal))
        {
            throw new ArgumentException("Unknown Discord user permission.", nameof(value));
        }

        return normalized;
    }
}

internal sealed record DiscordApplicationOwnerSnapshot(
    string? ApplicationId,
    string? ApplicationName,
    string? OwnerUserId,
    string? OwnerUsername,
    string? TeamId,
    string? TeamName,
    string? TeamOwnerUserId,
    string? TeamOwnerUsername,
    string? RequestingUserTeamRole)
{
    public bool IsOwner(string userId)
    {
        return string.Equals(OwnerUserId, userId, StringComparison.Ordinal)
            || string.Equals(TeamOwnerUserId, userId, StringComparison.Ordinal);
    }
}

internal sealed record DiscordUserAuthorizationView(
    string UserId,
    string Username,
    string? RegisteredDisplayName,
    string RegisteredPermission,
    bool RegisteredPermissionEnabled,
    bool IsApplicationOwner,
    string PrimaryRole,
    IReadOnlyList<string> EffectiveRoles,
    IReadOnlyList<string> AuthoritySources,
    DiscordApplicationOwnerSnapshot ApplicationOwner);

internal interface IDiscordUserPermissionService
{
    ValueTask<IReadOnlyList<DiscordUserPermissionData>> GetAllAsync(CancellationToken cancellationToken = default);

    ValueTask<DiscordUserPermissionData?> GetAsync(long id, CancellationToken cancellationToken = default);

    ValueTask<DiscordUserPermissionData?> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default);

    ValueTask AddAsync(
        string userId,
        string? displayName,
        string permission,
        bool enabled,
        string? memo,
        string? createdBy,
        CancellationToken cancellationToken = default);

    ValueTask UpdateAsync(
        long id,
        string userId,
        string? displayName,
        string permission,
        bool enabled,
        string? memo,
        CancellationToken cancellationToken = default);

    ValueTask DeleteAsync(long id, CancellationToken cancellationToken = default);

    ValueTask<DiscordUserAuthorizationView> GetAuthorizationAsync(
        string userId,
        string username,
        DiscordApplicationOwnerSnapshot applicationOwner,
        CancellationToken cancellationToken = default);
}

internal sealed class DiscordUserPermissionService(IDiscordUserPermissionRepository repository)
    : IDiscordUserPermissionService
{
    public ValueTask<IReadOnlyList<DiscordUserPermissionData>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return repository.GetAllAsync(cancellationToken);
    }

    public ValueTask<DiscordUserPermissionData?> GetAsync(long id, CancellationToken cancellationToken = default)
    {
        return repository.GetAsync(id, cancellationToken);
    }

    public ValueTask<DiscordUserPermissionData?> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        return repository.GetByUserIdAsync(NormalizeRequired(userId), cancellationToken);
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
        return repository.AddAsync(
            NormalizeRequired(userId),
            NormalizeOptional(displayName),
            DiscordUserPermissionLevels.Normalize(permission),
            enabled,
            NormalizeOptional(memo),
            NormalizeOptional(createdBy),
            cancellationToken);
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
        return repository.UpdateAsync(
            id,
            NormalizeRequired(userId),
            NormalizeOptional(displayName),
            DiscordUserPermissionLevels.Normalize(permission),
            enabled,
            NormalizeOptional(memo),
            cancellationToken);
    }

    public ValueTask DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        return repository.DeleteAsync(id, cancellationToken);
    }

    public async ValueTask<DiscordUserAuthorizationView> GetAuthorizationAsync(
        string userId,
        string username,
        DiscordApplicationOwnerSnapshot applicationOwner,
        CancellationToken cancellationToken = default)
    {
        var normalizedUserId = NormalizeRequired(userId);
        var registered = await repository.GetByUserIdAsync(normalizedUserId, cancellationToken);
        return BuildAuthorization(normalizedUserId, username, registered, applicationOwner);
    }

    internal static DiscordUserAuthorizationView BuildAuthorization(
        string userId,
        string username,
        DiscordUserPermissionData? registered,
        DiscordApplicationOwnerSnapshot applicationOwner)
    {
        var hasEnabledRegistration = registered?.Enabled == true;
        var registeredPermission = hasEnabledRegistration
            ? DiscordUserPermissionLevels.Normalize(registered!.Permission)
            : DiscordUserPermissionLevels.User;
        var isApplicationOwner = applicationOwner.IsOwner(userId);

        List<string> roles = [];
        if (isApplicationOwner)
        {
            roles.Add("app_owner");
        }

        if (hasEnabledRegistration
            && string.Equals(registeredPermission, DiscordUserPermissionLevels.Administrator, StringComparison.Ordinal))
        {
            roles.Add("administrator");
        }

        roles.Add("general_user");

        List<string> authoritySources = [];
        if (isApplicationOwner)
        {
            authoritySources.Add("discord_application");
        }

        if (hasEnabledRegistration)
        {
            authoritySources.Add("database");
        }

        authoritySources.Add("server_default");

        return new DiscordUserAuthorizationView(
            userId,
            username,
            registered?.DisplayName,
            registeredPermission,
            hasEnabledRegistration,
            isApplicationOwner,
            roles[0],
            roles,
            authoritySources,
            applicationOwner);
    }

    internal static string NormalizeRequired(string value)
    {
        var normalized = value.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("Discord user ID is required.", nameof(value));
        }

        return normalized;
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
