using SQLMigration;

namespace GatewayServer.SQL.Migration;

public partial class Scripts
{
    internal class _1__Init : IScript
    {
        public string Name => "Init";

        public int InstalledRank => 1;

        public string UpSql => @"
";
        public string DownSql => @"
";
    }
}
