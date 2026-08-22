using SQLMigration;

namespace DiscordBot.SQL.Migration;

public partial class Scripts
{
    internal class _22__AddAmbientChatContextSettings : IScript
    {
        public string Name => "AddAmbientChatContextSettings";

        public int InstalledRank => 22;

        public string UpSql => @"
CREATE TABLE `ambient_chat_context_settings` (
    `id` TINYINT NOT NULL PRIMARY KEY,
    `enabled` BOOLEAN NOT NULL DEFAULT FALSE,
    `window_message_count` INT NOT NULL,
    `window_max_chars` INT NOT NULL,
    `lookback_minutes` INT NOT NULL,
    `created_at` DATETIME NOT NULL DEFAULT NOW(),
    `updated_at` DATETIME NULL
) DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
";

        public string DownSql => @"
DROP TABLE `ambient_chat_context_settings`;
";
    }
}
