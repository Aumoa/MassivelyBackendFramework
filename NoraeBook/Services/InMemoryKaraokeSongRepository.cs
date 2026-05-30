namespace NoraeBook.Services;

public sealed class InMemoryKaraokeSongRepository : IKaraokeSongRepository
{
    private readonly Lock m_Sync = new();
    private readonly Dictionary<string, List<KaraokeSongEntry>> m_SongsByOwner = new(StringComparer.Ordinal);

    public ValueTask<IReadOnlyList<KaraokeSongEntry>> GetSongsAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);

        lock (m_Sync)
        {
            if (!m_SongsByOwner.TryGetValue(ownerId, out var songs))
            {
                return ValueTask.FromResult<IReadOnlyList<KaraokeSongEntry>>([]);
            }

            var snapshot = songs
                .OrderBy(static song => song.SongNumber)
                .ThenBy(static song => song.Artist, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(static song => song.Title, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
            return ValueTask.FromResult<IReadOnlyList<KaraokeSongEntry>>(snapshot);
        }
    }

    public ValueTask<KaraokeSongEntry> SaveSongAsync(string ownerId, KaraokeSongEdit edit, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentNullException.ThrowIfNull(edit);
        if (edit.SongNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(edit), edit.SongNumber, "Song number must be greater than zero.");
        }

        var now = DateTimeOffset.UtcNow;
        var normalizedTags = NormalizeTags(edit.Tags);

        lock (m_Sync)
        {
            if (!m_SongsByOwner.TryGetValue(ownerId, out var songs))
            {
                songs = [];
                m_SongsByOwner[ownerId] = songs;
            }

            var existingIndex = edit.Id.HasValue
                ? songs.FindIndex(song => song.Id == edit.Id.Value)
                : -1;
            var createdAt = existingIndex >= 0 ? songs[existingIndex].CreatedAt : now;
            var entry = new KaraokeSongEntry(
                edit.Id ?? Guid.NewGuid(),
                edit.SongNumber,
                edit.Artist.Trim(),
                edit.Title.Trim(),
                normalizedTags,
                createdAt,
                now);

            if (existingIndex >= 0)
            {
                songs[existingIndex] = entry;
            }
            else
            {
                songs.Add(entry);
            }

            return ValueTask.FromResult(entry);
        }
    }

    public ValueTask<bool> DeleteSongAsync(string ownerId, Guid songId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);

        lock (m_Sync)
        {
            if (!m_SongsByOwner.TryGetValue(ownerId, out var songs))
            {
                return ValueTask.FromResult(false);
            }

            return ValueTask.FromResult(songs.RemoveAll(song => song.Id == songId) > 0);
        }
    }

    private static IReadOnlyList<string> NormalizeTags(IReadOnlyCollection<string> tags)
    {
        return tags
            .Select(static tag => tag.Trim())
            .Where(static tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
