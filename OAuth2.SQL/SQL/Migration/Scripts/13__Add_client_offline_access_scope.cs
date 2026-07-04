using SQLMigration;

namespace OAuth2.SQL.Migration;

public partial class Scripts
{
    internal class _13__Add_client_offline_access_scope : IScript
    {
        public string Name => "Add_client_offline_access_scope";

        public int InstalledRank => 13;

        public string UpSql => @"
INSERT INTO `client_claim` (`client_id`, `name`, `value`)
SELECT `c`.`id`, 'scope', 'offline_access'
FROM `client` `c`
LEFT JOIN `client_claim` `cc`
    ON `cc`.`client_id` = `c`.`id`
    AND `cc`.`name` = 'scope'
    AND `cc`.`value` = 'offline_access'
    AND `cc`.`removed_at` IS NULL
WHERE `c`.`removed_at` IS NULL
    AND `cc`.`id` IS NULL;
";

        public string DownSql => @"
UPDATE `client_claim`
SET `removed_at` = NOW()
WHERE `name` = 'scope'
    AND `value` = 'offline_access'
    AND `removed_at` IS NULL;
";
    }
}
