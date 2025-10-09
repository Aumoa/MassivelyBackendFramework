using MessagePack;
using Scripting.DTO;

namespace Gateway.DTO;

[MessagePackObject]
public record LoginResponseNotify : INotify
{
    [Key(0)]
    public ResponseCode Code { get; set; } = ResponseCode.Success;

    [Key(1)]
    public string ClientId { get; set; } = string.Empty;

    [Key(2)]
    public string AccessJwt { get; set; } = string.Empty;
}
