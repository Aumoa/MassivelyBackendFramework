using SQLMigration;

namespace NPay.SQL.Migration;

public partial class Scripts
{
    internal class _3__AddSettlementAiSummaryState : IScript
    {
        public string Name => "AddSettlementAiSummaryState";

        public int InstalledRank => 3;

        public string UpSql => @"
ALTER TABLE `npay_settlement`
    ADD COLUMN `ai_summary` TEXT NULL AFTER `allow_guest_expense_edit`,
    ADD COLUMN `ai_summary_dirty` TINYINT NOT NULL DEFAULT 1 AFTER `ai_summary`,
    ADD COLUMN `ai_summary_revision` INT NOT NULL DEFAULT 0 AFTER `ai_summary_dirty`,
    ADD COLUMN `ai_summary_updated_at` DATETIME NULL AFTER `ai_summary_revision`;
";

        public string DownSql => @"
ALTER TABLE `npay_settlement`
    DROP COLUMN `ai_summary_updated_at`,
    DROP COLUMN `ai_summary_revision`,
    DROP COLUMN `ai_summary_dirty`,
    DROP COLUMN `ai_summary`;
";
    }
}
