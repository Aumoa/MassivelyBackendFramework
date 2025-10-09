using MessagePack;
using Scripting.DTO;

namespace Master.DTO;

[MessagePackObject]
public record SessionRegisterRequest : IRequest
{
    [Key(0)]
    public string ClientId { get; set; } = string.Empty;
}
