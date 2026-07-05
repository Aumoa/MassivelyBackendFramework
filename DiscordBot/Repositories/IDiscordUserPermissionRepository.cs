namespace DiscordBot.Repositories;

public record DiscordUserPermissionData(
    long Id,
    string UserId,
    string? DisplayName,
    string Permission,
    bool Enabled,
    string? Memo,
    string? CreatedBy,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public interface IDiscordUserPermissionRepository
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
}
