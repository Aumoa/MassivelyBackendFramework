using SQLMigration;

namespace MasterServer.SQL.Migration;

public partial class Scripts
{
    internal sealed class _5__BackendPacketManifestVerifierProgram : IScript
    {
        public string Name => "BackendPacketManifestVerifierProgram";

        public int InstalledRank => 5;

        public string UpSql => @"
ALTER TABLE `backend_packet_manifest_entry`
    ADD COLUMN `verifier_program` MEDIUMTEXT AFTER `schema_hash`;
";

        public string DownSql => @"
ALTER TABLE `backend_packet_manifest_entry`
    DROP COLUMN `verifier_program`;
";
    }
}
