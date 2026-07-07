namespace OAuth2.DTO;

public sealed record AccountPicture
{
    public string ContentType { get; init; } = string.Empty;
    public byte[] Image { get; init; } = [];
    public int Width { get; init; }
    public int Height { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}
