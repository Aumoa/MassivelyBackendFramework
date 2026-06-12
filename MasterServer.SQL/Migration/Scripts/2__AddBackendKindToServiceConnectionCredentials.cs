using SQLMigration;

namespace MasterServer.SQL.Migration;

public partial class Scripts
{
    internal sealed class _2__AddBackendKindToServiceConnectionCredentials : IScript
    {
        public string Name => "AddBackendKindToServiceConnectionCredentials";

        public int InstalledRank => 2;

        public string UpSql => @"
ALTER TABLE `service_connection_credential`
    ADD COLUMN `backend_kind` VARCHAR(128) NULL AFTER `display_name`;
";

        public string DownSql => @"
ALTER TABLE `service_connection_credential`
    DROP COLUMN `backend_kind`;
";
    }
}
