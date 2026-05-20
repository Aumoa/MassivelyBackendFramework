using SQLMigration;

namespace NPay.SQL.Migration;

public partial class Scripts
{
    internal class _2__AddAllowGuestExpenseEdit : IScript
    {
        public string Name => "AddAllowGuestExpenseEdit";

        public int InstalledRank => 2;

        public string UpSql => @"
ALTER TABLE `npay_settlement`
    ADD COLUMN `allow_guest_expense_edit` TINYINT NOT NULL DEFAULT 1
        COMMENT '1=guests with the link can add/remove expenses, 0=owner only'
        AFTER `closed_at`;
";

        public string DownSql => @"
ALTER TABLE `npay_settlement` DROP COLUMN `allow_guest_expense_edit`;
";
    }
}
