using SQLMigration;

namespace NPay.SQL.Migration;

public partial class Scripts
{
    internal class _1__Init : IScript
    {
        public string Name => "Init";

        public int InstalledRank => 1;

        public string UpSql => @"
CREATE TABLE `npay_settlement` (
    `id`             CHAR(36)     NOT NULL PRIMARY KEY,
    `owner_subject`  VARCHAR(128) NOT NULL,
    `title`          VARCHAR(200) NOT NULL,
    `description`    TEXT,
    `status`         TINYINT      NOT NULL DEFAULT 0 COMMENT '0=Open, 1=Closed',
    `created_at`     DATETIME     NOT NULL DEFAULT NOW(),
    `closed_at`      DATETIME,
    INDEX `IDX__npay_settlement__owner_subject__created_at` (`owner_subject`, `created_at`)
);

CREATE TABLE `npay_participant` (
    `id`             CHAR(36)     NOT NULL PRIMARY KEY,
    `settlement_id`  CHAR(36)     NOT NULL,
    `name`           VARCHAR(100) NOT NULL,
    `subject`        VARCHAR(128),
    INDEX `IDX__npay_participant__settlement_id` (`settlement_id`),
    CONSTRAINT `FK__npay_participant__settlement_id`
        FOREIGN KEY (`settlement_id`) REFERENCES `npay_settlement`(`id`) ON DELETE CASCADE
);

CREATE TABLE `npay_expense` (
    `id`                    CHAR(36)       NOT NULL PRIMARY KEY,
    `settlement_id`         CHAR(36)       NOT NULL,
    `label`                 VARCHAR(200)   NOT NULL,
    `amount`                DECIMAL(18, 2) NOT NULL,
    `paid_by_participant_id` CHAR(36)      NOT NULL,
    `created_at`            DATETIME       NOT NULL DEFAULT NOW(),
    INDEX `IDX__npay_expense__settlement_id` (`settlement_id`),
    CONSTRAINT `FK__npay_expense__settlement_id`
        FOREIGN KEY (`settlement_id`) REFERENCES `npay_settlement`(`id`) ON DELETE CASCADE
);

CREATE TABLE `npay_expense_split` (
    `expense_id`     CHAR(36) NOT NULL,
    `participant_id` CHAR(36) NOT NULL,
    PRIMARY KEY (`expense_id`, `participant_id`),
    CONSTRAINT `FK__npay_expense_split__expense_id`
        FOREIGN KEY (`expense_id`) REFERENCES `npay_expense`(`id`) ON DELETE CASCADE
);
";

        public string DownSql => @"
DROP TABLE `npay_expense_split`;
DROP TABLE `npay_expense`;
DROP TABLE `npay_participant`;
DROP TABLE `npay_settlement`;
";
    }
}
