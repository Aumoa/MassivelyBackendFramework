using MySql.Data.MySqlClient;
using OAuth2.Options;

namespace OAuth2.Services;

internal class MySqlDbContext(MySqlOptions options)
{
    protected MySqlConnection GetConnection()
    {
        return new MySqlConnection(options.ConnectionString);
    }
}
