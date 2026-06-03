using SQLMigration;

namespace NoraeBook.SQL.Migration;

public partial class Scripts
{
    internal sealed class _2__ReplaceMixedTag : IScript
    {
        public string Name => "Replace mixed tag";

        public int InstalledRank => 2;

        public string UpSql => @"
INSERT IGNORE INTO `noraebook_song_tag` (`song_id`, `tag`)
SELECT `song_id`, 'male'
FROM `noraebook_song_tag`
WHERE `tag` = 'mixed';

INSERT IGNORE INTO `noraebook_song_tag` (`song_id`, `tag`)
SELECT `song_id`, 'female'
FROM `noraebook_song_tag`
WHERE `tag` = 'mixed';

DELETE FROM `noraebook_song_tag`
WHERE `tag` = 'mixed';
";

        public string DownSql => @"
INSERT IGNORE INTO `noraebook_song_tag` (`song_id`, `tag`)
SELECT male_tag.`song_id`, 'mixed'
FROM `noraebook_song_tag` male_tag
INNER JOIN `noraebook_song_tag` female_tag
    ON female_tag.`song_id` = male_tag.`song_id`
WHERE male_tag.`tag` = 'male'
  AND female_tag.`tag` = 'female';
";
    }
}
