using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSG.Mobile.Infrastructure.Migrations
{
    /// <summary>
    /// Final PIS schema: drops <c>users_pis</c> and recreates it as the self-contained personnel table.
    ///
    /// DESTRUCTIVE BY DESIGN: every existing personnel row is discarded (approved by the product owner; the rows are disposable).
    /// No other table is touched: nothing references <c>users_pis</c> by foreign key (a preflight check proves it), the other
    /// modules link to it by NSG number text only. After the migration the table is empty and personnel must be re-entered or
    /// imported; until then everything keyed on a PIS row (mobile registration/login, profile, admin sign-in) finds no person.
    ///
    /// This migration supersedes the never-applied draft <c>20261103052042_Refactor_Users_Pis_To_Text_Model</c>, which was
    /// removed (an in-place conversion with backfills is unnecessary when the data is thrown away).
    ///
    /// Before anything is dropped, the preflight below checks the predictable ways the rebuild could fail half-way and stops with
    /// a message that names the problem. If a failure happens after the preflight (for example the server is stopped or the
    /// account lacks the DROP/CREATE privilege) MySQL DDL cannot be rolled back, so restore the pre-migration backup;
    /// see docs/PIS-MIGRATION-RUNBOOK.md.
    /// </summary>
    public partial class Rebuild_Users_Pis_Final_Schema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── 1. Preflight: nothing below changes the schema or data. Each failed check names its reason in the error. ──
            Preflight(migrationBuilder, "pf_server_version", "PIS rebuild blocked: MySQL 8.0.16 or newer is required",
                "SELECT 1 FROM DUAL WHERE VERSION() LIKE '%MariaDB%' " +
                "OR CAST(SUBSTRING_INDEX(VERSION(), '.', 1) AS UNSIGNED) < 8 " +
                "OR (CAST(SUBSTRING_INDEX(VERSION(), '.', 1) AS UNSIGNED) = 8 " +
                "AND CAST(SUBSTRING_INDEX(SUBSTRING_INDEX(VERSION(), '.', 2), '.', -1) AS UNSIGNED) = 0 " +
                "AND CAST(SUBSTRING_INDEX(SUBSTRING_INDEX(SUBSTRING_INDEX(VERSION(), '.', 3), '.', -1), '-', 1) AS UNSIGNED) < 16)");

            Preflight(migrationBuilder, "pf_foreign_key", "PIS rebuild blocked: a foreign key points at users_pis",
                "SELECT 1 FROM information_schema.referential_constraints " +
                "WHERE constraint_schema = DATABASE() AND referenced_table_name = 'users_pis'");

            Preflight(migrationBuilder, "pf_view", "PIS rebuild blocked: a view uses users_pis",
                "SELECT 1 FROM information_schema.views WHERE table_schema = DATABASE() AND LOWER(view_definition) LIKE '%users\\_pis%'");

            Preflight(migrationBuilder, "pf_routine", "PIS rebuild blocked: a stored routine uses users_pis",
                "SELECT 1 FROM information_schema.routines WHERE routine_schema = DATABASE() AND LOWER(routine_definition) LIKE '%users\\_pis%'");

            Preflight(migrationBuilder, "pf_trigger", "PIS rebuild blocked: a trigger exists on users_pis",
                "SELECT 1 FROM information_schema.triggers WHERE event_object_schema = DATABASE() AND event_object_table = 'users_pis'");

            // ── 2. Rebuild. Existing personnel rows are discarded. ──
            migrationBuilder.Sql("DROP TABLE IF EXISTS `users_pis`;");

            migrationBuilder.CreateTable(
                name: "users_pis",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    nsg_number = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    service_number = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    nsg_rank = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    parent_department_rank = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    full_name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    nsg_unit = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    education = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    date_of_induction_in_nsg = table.Column<DateOnly>(type: "date", nullable: true),
                    side_stepped_details = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    date_of_repatriation = table.Column<DateOnly>(type: "date", nullable: true),
                    date_of_birth = table.Column<DateOnly>(type: "date", nullable: true),
                    date_of_enrollment_in_govt_service = table.Column<DateOnly>(type: "date", nullable: true),
                    date_of_superannuation = table.Column<DateOnly>(type: "date", nullable: true),
                    gender = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    category = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    religion = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    father_name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    mother_name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    spouse_name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    next_of_kin_mobile_number = table.Column<string>(type: "varchar(15)", maxLength: 15, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    number_of_children = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    mobile_number = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    blood_group = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    height_cm = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    weight_kg = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    aadhaar_number_encrypted = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    pan_number_encrypted = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    pran_number_encrypted = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    salary_account_number_encrypted = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    permanent_village = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    permanent_post_office = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    permanent_police_station = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    permanent_district = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    permanent_state = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    permanent_pin_code = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    date_of_ame = table.Column<DateOnly>(type: "date", nullable: true),
                    date_of_immovable_property_return = table.Column<DateOnly>(type: "date", nullable: true),
                    earned_leave_due = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    half_pay_leave_due = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    paternity_leave_availed = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    maternity_leave_availed = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    child_care_leave_availed = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    mobile_app_activation_status = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    is_active = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    created_at_ist = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    created_by = table.Column<int>(type: "int", nullable: true),
                    updated_at_ist = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    updated_by = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users_pis", x => x.id);
                    table.CheckConstraint("ck_users_pis_mobile_app_activation_status", "`mobile_app_activation_status` IN (0, 1, 2)");
                    table.CheckConstraint("ck_users_pis_mobile_number_format", "`mobile_number` REGEXP '^[0-9]{10}$'");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_users_pis_is_active",
                table: "users_pis",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_users_pis_mobile_number",
                table: "users_pis",
                column: "mobile_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_pis_nsg_number",
                table: "users_pis",
                column: "nsg_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_pis_service_number",
                table: "users_pis",
                column: "service_number");
        }

        /// <summary>
        /// Adds one preflight check. A connection-scoped temporary table receives a row only when <paramref name="violationQuery"/>
        /// returns one; adding the CHECK then fails with "Check constraint '&lt;blockedMessage&gt;' is violated." and the real schema has
        /// not been touched. The constraint name is the human-readable reason (MySQL limits it to 64 characters).
        /// </summary>
        private static void Preflight(MigrationBuilder migrationBuilder, string tempTable, string blockedMessage, string violationQuery)
        {
            migrationBuilder.Sql($"CREATE TEMPORARY TABLE `{tempTable}` (`violation` int NOT NULL PRIMARY KEY);");
            migrationBuilder.Sql($"INSERT INTO `{tempTable}` (`violation`) SELECT 1 FROM DUAL WHERE EXISTS ({violationQuery});");
            migrationBuilder.Sql($"ALTER TABLE `{tempTable}` ADD CONSTRAINT `{blockedMessage}` CHECK (`violation` = 0);");
            migrationBuilder.Sql($"DROP TEMPORARY TABLE `{tempTable}`;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Not reversible: the previous table held lookup ids, foreign keys and hash columns, and its rows were discarded by Up.
            // To go back, restore the database backup taken before this migration.
            throw new NotSupportedException(
                "Rebuild_Users_Pis_Final_Schema cannot be reverted: restore the pre-migration database backup instead.");
        }
    }
}
