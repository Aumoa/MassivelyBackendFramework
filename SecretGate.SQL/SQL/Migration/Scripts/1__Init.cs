using SQLMigration;

namespace SecretGate.SQL.Migration;

public partial class Scripts
{
    internal sealed class _1__Init : IScript
    {
        public string Name => "Init";

        public int InstalledRank => 1;

        public string UpSql => @"
CREATE TABLE `secretgate_vault_secret` (
    `id`            CHAR(36)      NOT NULL PRIMARY KEY,
    `owner_subject` VARCHAR(128)  NOT NULL,
    `name`          VARCHAR(160)  NOT NULL,
    `salt`          VARBINARY(32) NOT NULL,
    `nonce`         VARBINARY(16) NOT NULL,
    `tag`           VARBINARY(16) NOT NULL,
    `cipher_text`   MEDIUMBLOB    NOT NULL,
    `created_at`    DATETIME(6)   NOT NULL,
    `updated_at`    DATETIME(6)   NOT NULL,
    INDEX `IDX__secretgate_vault_secret__owner_subject__updated_at` (`owner_subject`, `updated_at`)
) DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `secretgate_share_secret` (
    `id`              CHAR(36)        NOT NULL PRIMARY KEY,
    `url_token_hash`  VARBINARY(32)   NOT NULL,
    `access_key_hash` VARBINARY(32)   NOT NULL,
    `salt`            VARBINARY(32)   NOT NULL,
    `nonce`           VARBINARY(16)   NOT NULL,
    `tag`             VARBINARY(16)   NOT NULL,
    `cipher_text`     MEDIUMBLOB      NOT NULL,
    `expires_at`      DATETIME(6)     NOT NULL,
    `created_at`      DATETIME(6)     NOT NULL,
    UNIQUE INDEX `UX__secretgate_share_secret__url_token_hash` (`url_token_hash`),
    INDEX `IDX__secretgate_share_secret__expires_at` (`expires_at`)
) DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
";

        public string DownSql => @"
DROP TABLE `secretgate_share_secret`;
DROP TABLE `secretgate_vault_secret`;
";
    }
}
