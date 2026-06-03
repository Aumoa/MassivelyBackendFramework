using Dapper;
using Microsoft.Extensions.Options;
using NoraeBook.Options;
using NoraeBook.Services;

namespace NoraeBook.Repositories;

internal sealed class MySqlKaraokeSongRepository(IOptions<MySqlOptions> options)
    : MySqlDbContext(options.Value), IKaraokeSongRepository
{
    public async ValueTask<IReadOnlyList<KaraokeSongEntry>> GetSongsAsync(
        string ownerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);

        await using var connection = GetConnection();

        const string songSql = @"
SELECT
    `id` AS Id,
    `song_number` AS SongNumber,
    `artist` AS Artist,
    `title` AS Title,
    `created_at` AS CreatedAt,
    `updated_at` AS UpdatedAt
FROM `noraebook_song`
WHERE `owner_subject` = @ownerId
ORDER BY `song_number` ASC, `artist` ASC, `title` ASC";

        var songCommand = new CommandDefinition(songSql, new { ownerId }, cancellationToken: cancellationToken);
        var songRows = (await connection.QueryAsync<SongRow>(songCommand)).ToArray();
        if (songRows.Length == 0)
        {
            return [];
        }

        const string tagSql = @"
SELECT
    `song_id` AS SongId,
    `tag` AS Tag
FROM `noraebook_song_tag`
WHERE `song_id` IN @songIds
ORDER BY `tag` ASC";

        var songIds = songRows.Select(static song => song.Id).ToArray();
        var tagCommand = new CommandDefinition(tagSql, new { songIds }, cancellationToken: cancellationToken);
        var tagsBySongId = (await connection.QueryAsync<TagRow>(tagCommand))
            .GroupBy(static tag => tag.SongId)
            .ToDictionary(
                static group => group.Key,
                static group => KaraokeSongTags.NormalizeForStorage(group.Select(static tag => tag.Tag)));

        return songRows
            .Select(song => song.ToEntry(tagsBySongId.GetValueOrDefault(song.Id, [])))
            .ToArray();
    }

    public async ValueTask<KaraokeSongEntry> SaveSongAsync(
        string ownerId,
        KaraokeSongEdit edit,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentNullException.ThrowIfNull(edit);
        if (edit.SongNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(edit), edit.SongNumber, "Song number must be greater than zero.");
        }

        var songId = edit.Id ?? Guid.NewGuid();
        var normalizedTags = KaraokeSongTags.NormalizeForStorage(edit.Tags);
        var nowUtc = DateTime.UtcNow;

        await using var connection = GetConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            if (edit.Id.HasValue)
            {
                const string updateSql = @"
UPDATE `noraebook_song`
SET
    `song_number` = @songNumber,
    `artist` = @artist,
    `title` = @title,
    `updated_at` = @nowUtc
WHERE `id` = @songId
  AND `owner_subject` = @ownerId";

                var updateCommand = new CommandDefinition(
                    updateSql,
                    new
                    {
                        songId = songId.ToString(),
                        ownerId,
                        songNumber = edit.SongNumber,
                        artist = edit.Artist.Trim(),
                        title = edit.Title.Trim(),
                        nowUtc
                    },
                    transaction,
                    cancellationToken: cancellationToken);
                var updatedRows = await connection.ExecuteAsync(updateCommand);
                if (updatedRows == 0)
                {
                    throw new KeyNotFoundException("The song does not exist or belongs to another owner.");
                }
            }
            else
            {
                const string insertSql = @"
INSERT INTO `noraebook_song`
    (`id`, `owner_subject`, `song_number`, `artist`, `title`, `created_at`, `updated_at`)
VALUES
    (@songId, @ownerId, @songNumber, @artist, @title, @nowUtc, @nowUtc)";

                var insertCommand = new CommandDefinition(
                    insertSql,
                    new
                    {
                        songId = songId.ToString(),
                        ownerId,
                        songNumber = edit.SongNumber,
                        artist = edit.Artist.Trim(),
                        title = edit.Title.Trim(),
                        nowUtc
                    },
                    transaction,
                    cancellationToken: cancellationToken);
                await connection.ExecuteAsync(insertCommand);
            }

            const string deleteTagsSql = "DELETE FROM `noraebook_song_tag` WHERE `song_id` = @songId";
            var deleteTagsCommand = new CommandDefinition(
                deleteTagsSql,
                new { songId = songId.ToString() },
                transaction,
                cancellationToken: cancellationToken);
            await connection.ExecuteAsync(deleteTagsCommand);

            if (normalizedTags.Count > 0)
            {
                const string insertTagSql = @"
INSERT INTO `noraebook_song_tag`
    (`song_id`, `tag`)
VALUES
    (@songId, @tag)";

                var tagRows = normalizedTags.Select(tag => new
                {
                    songId = songId.ToString(),
                    tag
                });
                var insertTagsCommand = new CommandDefinition(
                    insertTagSql,
                    tagRows,
                    transaction,
                    cancellationToken: cancellationToken);
                await connection.ExecuteAsync(insertTagsCommand);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        return await GetSongAsync(ownerId, songId, cancellationToken)
            ?? throw new InvalidOperationException("Saved song could not be reloaded.");
    }

    public async ValueTask<bool> DeleteSongAsync(
        string ownerId,
        Guid songId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);

        await using var connection = GetConnection();

        const string sql = @"
DELETE FROM `noraebook_song`
WHERE `id` = @songId
  AND `owner_subject` = @ownerId";

        var command = new CommandDefinition(
            sql,
            new { songId = songId.ToString(), ownerId },
            cancellationToken: cancellationToken);
        return await connection.ExecuteAsync(command) > 0;
    }

    private async ValueTask<KaraokeSongEntry?> GetSongAsync(
        string ownerId,
        Guid songId,
        CancellationToken cancellationToken)
    {
        await using var connection = GetConnection();

        const string songSql = @"
SELECT
    `id` AS Id,
    `song_number` AS SongNumber,
    `artist` AS Artist,
    `title` AS Title,
    `created_at` AS CreatedAt,
    `updated_at` AS UpdatedAt
FROM `noraebook_song`
WHERE `id` = @songId
  AND `owner_subject` = @ownerId
LIMIT 1";

        var songCommand = new CommandDefinition(
            songSql,
            new { songId = songId.ToString(), ownerId },
            cancellationToken: cancellationToken);
        var song = await connection.QueryFirstOrDefaultAsync<SongRow>(songCommand);
        if (song == null)
        {
            return null;
        }

        const string tagSql = @"
SELECT `tag`
FROM `noraebook_song_tag`
WHERE `song_id` = @songId
ORDER BY `tag` ASC";

        var tagCommand = new CommandDefinition(tagSql, new { songId = songId.ToString() }, cancellationToken: cancellationToken);
        var tags = KaraokeSongTags.NormalizeForStorage(await connection.QueryAsync<string>(tagCommand));
        return song.ToEntry(tags);
    }

    private sealed class SongRow
    {
        public Guid Id { get; set; }

        public int SongNumber { get; set; }

        public string Artist { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }

        public KaraokeSongEntry ToEntry(IReadOnlyList<string> tags)
        {
            return new KaraokeSongEntry(
                Id,
                SongNumber,
                Artist,
                Title,
                tags,
                new DateTimeOffset(DateTime.SpecifyKind(CreatedAt, DateTimeKind.Utc)),
                new DateTimeOffset(DateTime.SpecifyKind(UpdatedAt, DateTimeKind.Utc)));
        }
    }

    private sealed class TagRow
    {
        public Guid SongId { get; set; }

        public string Tag { get; set; } = string.Empty;
    }
}
