using MySql.Data.MySqlClient;
using DiscordBot.Options;

namespace DiscordBot.Repositories;

internal class MySqlDbContext(MySqlOptions options)
{
    protected MySqlConnection GetConnection()
    {
        return new MySqlConnection(options.ConnectionString);
    }
}
