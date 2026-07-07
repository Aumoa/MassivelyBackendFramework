using SQLMigration;

namespace OAuth2.SQL.Migration;

public partial class Scripts
{
    internal sealed class _13__Add_account_picture : IScript
    {
        public string Name => "Add_account_picture";

        public int InstalledRank => 13;

        public string UpSql => @"
CREATE TABLE `account_picture` (
    `account_id` VARCHAR(128) NOT NULL PRIMARY KEY,
    `content_type` VARCHAR(32) NOT NULL,
    `image_bytes` LONGBLOB NOT NULL,
    `width` INT NOT NULL,
    `height` INT NOT NULL,
    `created_at` DATETIME NOT NULL DEFAULT NOW(),
    `updated_at` DATETIME NOT NULL DEFAULT NOW(),
    INDEX `IDX__account_picture__updated_at` (`updated_at`)
);
";

        public string DownSql => @"
DROP TABLE `account_picture`;
";
    }
}
