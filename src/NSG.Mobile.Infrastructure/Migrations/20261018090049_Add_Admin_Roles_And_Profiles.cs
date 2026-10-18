using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace NSG.Mobile.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Add_Admin_Roles_And_Profiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "admin_roles",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    role_code = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    role_name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    is_active = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    created_at_ist = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_at_ist = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_admin_roles", x => x.id);
                    table.CheckConstraint("ck_admin_roles_role_code_format", "`role_code` REGEXP '^[A-Z][A-Z_]*$'");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "admin_profiles",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    nsg_number = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    role_id = table.Column<int>(type: "int", nullable: false),
                    password_hash = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    activation_status = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    is_active = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    failed_login_attempts = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    lockout_end_ist = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    last_login_at_ist = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    created_at_ist = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_at_ist = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_admin_profiles", x => x.id);
                    table.CheckConstraint("ck_admin_profiles_activation_status", "`activation_status` IN (0, 1)");
                    table.CheckConstraint("ck_admin_profiles_active_has_password", "`activation_status` = 0 OR `password_hash` IS NOT NULL");
                    table.CheckConstraint("ck_admin_profiles_failed_login_attempts", "`failed_login_attempts` >= 0");
                    table.CheckConstraint("ck_admin_profiles_password_hash", "`password_hash` IS NULL OR CHAR_LENGTH(`password_hash`) > 0");
                    table.ForeignKey(
                        name: "fk_admin_profiles_admin_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "admin_roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.InsertData(
                table: "admin_roles",
                columns: new[] { "id", "created_at_ist", "description", "is_active", "role_code", "role_name", "updated_at_ist" },
                values: new object[,]
                {
                    { 1, new DateTime(2026, 10, 18, 0, 0, 0, 0, DateTimeKind.Unspecified), "Provisions and manages Admin Portal accounts.", true, "SUPER_ADMIN", "Super Admin", null },
                    { 2, new DateTime(2026, 10, 18, 0, 0, 0, 0, DateTimeKind.Unspecified), "Reviews mobile-app registration requests.", true, "REGISTRATION_ADMIN", "Registration Admin", null },
                    { 3, new DateTime(2026, 10, 18, 0, 0, 0, 0, DateTimeKind.Unspecified), "Maintains personnel information system records.", true, "PIS_ADMIN", "PIS Admin", null },
                    { 4, new DateTime(2026, 10, 18, 0, 0, 0, 0, DateTimeKind.Unspecified), "Handles grievances.", true, "GRIEVANCE_ADMIN", "Grievance Admin", null },
                    { 5, new DateTime(2026, 10, 18, 0, 0, 0, 0, DateTimeKind.Unspecified), "Manages media cell content.", true, "MEDIA_CELL_ADMIN", "Media Cell Admin", null },
                    { 6, new DateTime(2026, 10, 18, 0, 0, 0, 0, DateTimeKind.Unspecified), "Manages salary information.", true, "SALARY_MANAGEMENT_ADMIN", "Salary Management Admin", null }
                });

            migrationBuilder.CreateIndex(
                name: "ix_admin_profiles_nsg_number",
                table: "admin_profiles",
                column: "nsg_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_admin_profiles_role_id",
                table: "admin_profiles",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_admin_roles_role_code",
                table: "admin_roles",
                column: "role_code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "admin_profiles");

            migrationBuilder.DropTable(
                name: "admin_roles");
        }
    }
}
