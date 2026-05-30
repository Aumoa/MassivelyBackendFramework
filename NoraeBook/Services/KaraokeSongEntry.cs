namespace NoraeBook.Services;

public sealed record KaraokeSongEntry(
    Guid Id,
    int SongNumber,
    string Artist,
    string Title,
    IReadOnlyList<string> Tags,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
