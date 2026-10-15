using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSG.Mobile.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Add_Mobile_Profile_Approval_And_Grant_Tables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "approve_mobile_users",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    nsg_number = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    mobile_number = table.Column<string>(type: "varchar(15)", maxLength: 15, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    date_of_birth = table.Column<DateOnly>(type: "date", nullable: false),
                    approval_status = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    pending_nsg_number = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: true, computedColumnSql: "IF(`approval_status` = 0, `nsg_number`, NULL)", stored: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    requested_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    approved_by = table.Column<int>(type: "int", nullable: true),
                    approved_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    remarks = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    created_by = table.Column<int>(type: "int", nullable: true),
                    updated_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    updated_by = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_approve_mobile_users", x => x.id);
                    table.CheckConstraint("ck_approve_mobile_users_approval_status", "`approval_status` IN (0, 1, 2)");
                    table.CheckConstraint("ck_approve_mobile_users_mobile_number_format", "`mobile_number` REGEXP '^[6-9][0-9]{9}$'");
                    table.CheckConstraint("ck_approve_mobile_users_review_fields", "(`approval_status` = 0 AND `approved_by` IS NULL AND `approved_at_utc` IS NULL) OR (`approval_status` <> 0 AND `approved_by` IS NOT NULL AND `approved_at_utc` IS NOT NULL)");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "mobile_auth_grants",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    token_hash = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    purpose = table.Column<int>(type: "int", nullable: false),
                    nsg_number = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    mobile_number = table.Column<string>(type: "varchar(15)", maxLength: 15, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    expires_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    consumed_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mobile_auth_grants", x => x.id);
                    table.CheckConstraint("ck_mobile_auth_grants_mobile_number_format", "`mobile_number` REGEXP '^[6-9][0-9]{9}$'");
                    table.CheckConstraint("ck_mobile_auth_grants_purpose", "`purpose` IN (0, 1, 2)");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "mobile_user_profiles",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    nsg_number = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    mobile_number = table.Column<string>(type: "varchar(15)", maxLength: 15, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    date_of_birth = table.Column<DateOnly>(type: "date", nullable: false),
                    role = table.Column<int>(type: "int", nullable: false),
                    mpin_hash = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    password_hash = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    is_active = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    failed_login_attempts = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    locked_until_utc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    last_login_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    created_by = table.Column<int>(type: "int", nullable: true),
                    updated_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    updated_by = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mobile_user_profiles", x => x.id);
                    table.CheckConstraint("ck_mobile_user_profiles_credentials", "(`mpin_hash` IS NULL OR CHAR_LENGTH(`mpin_hash`) > 0) AND (`password_hash` IS NULL OR CHAR_LENGTH(`password_hash`) > 0)");
                    table.CheckConstraint("ck_mobile_user_profiles_failed_login_attempts", "`failed_login_attempts` >= 0");
                    table.CheckConstraint("ck_mobile_user_profiles_mobile_number_format", "`mobile_number` REGEXP '^[6-9][0-9]{9}$'");
                    table.CheckConstraint("ck_mobile_user_profiles_role", "`role` IN (0, 1, 2, 3)");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_approve_mobile_users_approval_status",
                table: "approve_mobile_users",
                column: "approval_status");

            migrationBuilder.CreateIndex(
                name: "ix_approve_mobile_users_nsg_number_approval_status",
                table: "approve_mobile_users",
                columns: new[] { "nsg_number", "approval_status" });

            migrationBuilder.CreateIndex(
                name: "ix_approve_mobile_users_pending_nsg_number",
                table: "approve_mobile_users",
                column: "pending_nsg_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mobile_auth_grants_nsg_number_consumed_at_utc",
                table: "mobile_auth_grants",
                columns: new[] { "nsg_number", "consumed_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_mobile_auth_grants_token_hash",
                table: "mobile_auth_grants",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mobile_user_profiles_mobile_number",
                table: "mobile_user_profiles",
                column: "mobile_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mobile_user_profiles_nsg_number",
                table: "mobile_user_profiles",
                column: "nsg_number",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "approve_mobile_users");

            migrationBuilder.DropTable(
                name: "mobile_auth_grants");

            migrationBuilder.DropTable(
                name: "mobile_user_profiles");
        }
    }
}
