namespace NoraeBook.Services;

public interface IKaraokeSongRepository
{
    ValueTask<IReadOnlyList<KaraokeSongEntry>> GetSongsAsync(string ownerId, CancellationToken cancellationToken = default);

    ValueTask<KaraokeSongEntry> SaveSongAsync(string ownerId, KaraokeSongEdit edit, CancellationToken cancellationToken = default);

    ValueTask<bool> DeleteSongAsync(string ownerId, Guid songId, CancellationToken cancellationToken = default);
}
