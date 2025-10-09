using MessagePack;
using Scripting.DTO;

namespace Master.DTO;

[MessagePackObject]
public record SessionUnregisterResponse : IResponse
{
    [Key(0)]
    public ResponseCode Code { get; set; } = ResponseCode.Success;
}
