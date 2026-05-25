using SQLMigration;

namespace OAuth2.SQL.Migration;

public partial class Scripts
{
    internal class _10__Add_client_default_scopes : IScript
    {
        public string Name => "Add_client_default_scopes";

        public int InstalledRank => 10;

        public string UpSql => @"
INSERT INTO `client_claim` (`client_id`, `name`, `value`)
SELECT `c`.`id`, 'scope', `s`.`scope`
FROM `client` `c`
CROSS JOIN (
    SELECT 'openid' AS `scope`
    UNION ALL SELECT 'profile'
    UNION ALL SELECT 'email'
    UNION ALL SELECT 'address'
    UNION ALL SELECT 'phone'
    UNION ALL SELECT 'groups'
) `s`
LEFT JOIN `client_claim` `cc`
    ON `cc`.`client_id` = `c`.`id`
    AND `cc`.`name` = 'scope'
    AND `cc`.`value` = `s`.`scope`
    AND `cc`.`removed_at` IS NULL
WHERE `c`.`removed_at` IS NULL
    AND `cc`.`id` IS NULL;
";

        public string DownSql => @"
UPDATE `client_claim`
SET `removed_at` = NOW()
WHERE `name` = 'scope'
    AND `value` IN ('openid', 'profile', 'email', 'address', 'phone', 'groups')
    AND `removed_at` IS NULL;
";
    }
}
