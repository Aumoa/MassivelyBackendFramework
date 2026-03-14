using System.Data;
using Dapper;

namespace OpenAI.DapperUtility;

internal class MessageRoleHandler : SqlMapper.TypeHandler<MessageRole>
{
    public override MessageRole Parse(object value)
    {
        return (MessageRole)Convert.ToSByte(value);
    }

    public override void SetValue(IDbDataParameter parameter, MessageRole value)
    {
        parameter.Value = (sbyte)value;
    }
}
