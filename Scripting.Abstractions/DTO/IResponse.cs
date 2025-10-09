namespace Scripting.DTO;

public interface IResponse : IMessage
{
    /// <summary>
    /// Gets or sets the response code.
    /// </summary>
    ResponseCode Code { get; set; }
}
