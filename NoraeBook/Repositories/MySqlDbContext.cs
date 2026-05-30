using MySql.Data.MySqlClient;
using NoraeBook.Options;

namespace NoraeBook.Repositories;

internal abstract class MySqlDbContext(MySqlOptions options)
{
    protected MySqlConnection GetConnection()
    {
        return new MySqlConnection(options.ConnectionString);
    }
}
