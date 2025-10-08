using System.Data;

namespace SQLMigration;

public interface IScript
{
    string Name { get; }
    int InstalledRank { get; }

    string UpSql { get; }
    string DownSql { get; }
}
