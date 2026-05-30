namespace NoraeBook.Services;

public sealed record KaraokeSongEdit(
    Guid? Id,
    int SongNumber,
    string Artist,
    string Title,
    IReadOnlyCollection<string> Tags);
