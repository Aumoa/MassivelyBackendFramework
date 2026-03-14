using MySql.Data.MySqlClient;
using OpenAI.Options;

namespace OpenAI.Services;

internal class MySqlDbContext(MySqlOptions options)
{
    protected MySqlConnection GetConnection()
    {
        return new MySqlConnection(options.ConnectionString);
    }
}
