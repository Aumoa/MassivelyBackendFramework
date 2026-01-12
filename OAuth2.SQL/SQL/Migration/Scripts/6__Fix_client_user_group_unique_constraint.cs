using SQLMigration;

namespace OAuth2.SQL.Migration;

public partial class Scripts
{
    internal class _6__Fix_client_user_group_unique_constraint : IScript
    {
        public string Name => "Fix_client_user_group_unique_constraint";

        public int InstalledRank => 6;

        public string UpSql => @"
ALTER TABLE `client_user_group`
    DROP INDEX `UNQ__client_id__account_id__group`;

ALTER TABLE `client_user_group`
    ADD UNIQUE INDEX `UNQ__client_id__account_id__group__removed_at` (`client_id`, `account_id`, `group`, `removed_at`);
";

        public string DownSql => @"
ALTER TABLE `client_user_group`
    DROP INDEX `UNQ__client_id__account_id__group__removed_at`;

ALTER TABLE `client_user_group`
    ADD UNIQUE INDEX `UNQ__client_id__account_id__group` (`client_id`, `account_id`, `group`);
";
    }
}
