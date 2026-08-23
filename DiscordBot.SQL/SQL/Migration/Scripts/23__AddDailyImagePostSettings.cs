using SQLMigration;

namespace DiscordBot.SQL.Migration;

public partial class Scripts
{
    internal class _23__AddDailyImagePostSettings : IScript
    {
        public string Name => "AddDailyImagePostSettings";

        public int InstalledRank => 23;

        public string UpSql => @"
CREATE TABLE `daily_image_post_settings` (
    `id` TINYINT NOT NULL PRIMARY KEY,
    `enabled` BOOLEAN NOT NULL DEFAULT FALSE,
    `channel_id` VARCHAR(32) NOT NULL DEFAULT '',
    `post_time_of_day` VARCHAR(5) NOT NULL DEFAULT '09:00',
    `theme_prompt` VARCHAR(1000) NOT NULL DEFAULT '',
    `created_at` DATETIME NOT NULL DEFAULT NOW(),
    `updated_at` DATETIME NULL
) DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
";

        public string DownSql => @"
DROP TABLE `daily_image_post_settings`;
";
    }
}
