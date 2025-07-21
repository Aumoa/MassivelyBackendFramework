using MessagePack;
using Scripting.DTO;

namespace Master.DTO;

[MessagePackObject]
public record SessionRegisterResponse : IResponse
{
    [Key(0)]
    public ResponseCode Code { get; set; } = ResponseCode.Success;
}
