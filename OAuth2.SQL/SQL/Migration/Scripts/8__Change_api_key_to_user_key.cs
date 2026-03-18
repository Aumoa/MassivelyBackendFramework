using SQLMigration;

namespace OAuth2.SQL.Migration;

public partial class Scripts
{
    internal class _8__Change_api_key_to_user_key : IScript
    {
        public string Name => "Change_api_key_to_user_key";

        public int InstalledRank => 8;

        public string UpSql => @"
ALTER TABLE `client_api_key`
    ADD COLUMN `account_id` VARCHAR(128) NOT NULL AFTER `id`,
    DROP INDEX `IDX__client_id`,
    ADD INDEX `IDX__account_id` (`account_id`, `removed_at`);
";

        public string DownSql => @"
ALTER TABLE `client_api_key`
    DROP COLUMN `account_id`,
    DROP INDEX `IDX__account_id`,
    ADD INDEX `IDX__client_id` (`client_id`, `removed_at`);
";
    }
}
