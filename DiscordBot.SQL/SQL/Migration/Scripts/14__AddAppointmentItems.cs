using SQLMigration;

namespace DiscordBot.SQL.Migration;

public partial class Scripts
{
    internal class _14__AddAppointmentItems : IScript
    {
        public string Name => "AddAppointmentItems";

        public int InstalledRank => 14;

        public string UpSql => @"
ALTER TABLE `appointment`
    MODIFY COLUMN `description` TEXT NULL;

CREATE TABLE `appointment_item` (
    `id` BIGINT AUTO_INCREMENT NOT NULL PRIMARY KEY,
    `appointment_id` BIGINT NOT NULL,
    `item_type` VARCHAR(32) NOT NULL,
    `created_by_user_id` VARCHAR(64) NOT NULL,
    `content` TEXT NOT NULL,
    `status` VARCHAR(32) NOT NULL DEFAULT 'active',
    `sort_order` INT NOT NULL DEFAULT 0,
    `created_at` DATETIME NOT NULL DEFAULT NOW(),
    `updated_at` DATETIME NULL,
    INDEX `IDX__appointment_item__appointment_type_status_order` (`appointment_id`, `item_type`, `status`, `sort_order`, `id`)
) DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
";

        public string DownSql => @"
DROP TABLE `appointment_item`;

ALTER TABLE `appointment`
    MODIFY COLUMN `description` VARCHAR(2048) NULL;
";
    }
}
