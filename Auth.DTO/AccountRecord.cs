using MessagePack;

namespace Auth.DTO;

[MessagePackObject]
public record AccountRecord
{
    [Key(0)]
    public string Id { get; set; } = string.Empty;

    [Key(1)]
    public string Name { get; set; } = string.Empty;

    [Key(2)]
    public string Email { get; set; } = string.Empty;
}
