using SQLMigration;

namespace NoraeBook.SQL.Migration;

public partial class Scripts
{
    internal sealed class _1__Init : IScript
    {
        public string Name => "Init";

        public int InstalledRank => 1;

        public string UpSql => @"
CREATE TABLE `noraebook_song` (
    `id`            CHAR(36)     NOT NULL PRIMARY KEY,
    `owner_subject` VARCHAR(128) NOT NULL,
    `song_number`   INT          NOT NULL,
    `artist`        VARCHAR(200) NOT NULL,
    `title`         VARCHAR(240) NOT NULL,
    `created_at`    DATETIME     NOT NULL DEFAULT NOW(),
    `updated_at`    DATETIME     NOT NULL DEFAULT NOW(),
    INDEX `IDX__noraebook_song__owner_subject__song_number` (`owner_subject`, `song_number`),
    INDEX `IDX__noraebook_song__owner_subject__updated_at` (`owner_subject`, `updated_at`)
) DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `noraebook_song_tag` (
    `song_id` CHAR(36)    NOT NULL,
    `tag`     VARCHAR(80) NOT NULL,
    PRIMARY KEY (`song_id`, `tag`),
    INDEX `IDX__noraebook_song_tag__tag` (`tag`),
    CONSTRAINT `FK__noraebook_song_tag__song_id`
        FOREIGN KEY (`song_id`) REFERENCES `noraebook_song`(`id`) ON DELETE CASCADE
) DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
";

        public string DownSql => @"
DROP TABLE `noraebook_song_tag`;
DROP TABLE `noraebook_song`;
";
    }
}
