using SQLMigration;

namespace SecretGate.SQL.Migration;

public partial class Scripts
{
    internal sealed class _2__AddVaultProfile : IScript
    {
        public string Name => "AddVaultProfile";

        public int InstalledRank => 2;

        public string UpSql => @"
CREATE TABLE `secretgate_vault_profile` (
    `owner_subject` VARCHAR(128)  NOT NULL PRIMARY KEY,
    `salt`          VARBINARY(32) NOT NULL,
    `nonce`         VARBINARY(16) NOT NULL,
    `tag`           VARBINARY(16) NOT NULL,
    `cipher_text`   MEDIUMBLOB    NOT NULL,
    `created_at`    DATETIME(6)   NOT NULL,
    `updated_at`    DATETIME(6)   NOT NULL
) DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
";

        public string DownSql => @"
DROP TABLE `secretgate_vault_profile`;
";
    }
}
