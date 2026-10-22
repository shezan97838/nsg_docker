using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSG.Mobile.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Add_Grievance_Management : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "grievance_category",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    grievance_name = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    department_id = table.Column<int>(type: "int", nullable: true),
                    is_active = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    is_deleted = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: false),
                    created_at_ist = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_at_ist = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_grievance_category", x => x.id);
                    table.CheckConstraint("ck_grievance_category_name", "CHAR_LENGTH(`grievance_name`) > 0");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "grievance",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    grievance_id = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    category_id = table.Column<int>(type: "int", nullable: false),
                    subject = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    grievance_note = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    document_urls = table.Column<string>(type: "json", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    current_status = table.Column<int>(type: "int", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    created_at_ist = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_at_ist = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    assigned_to = table.Column<long>(type: "bigint", nullable: true),
                    action_taken_by = table.Column<long>(type: "bigint", nullable: true),
                    action_taken_at_ist = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    action_remarks = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    closed_by = table.Column<long>(type: "bigint", nullable: true),
                    closed_at_ist = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    closed_remarks = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    is_active = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_grievance", x => x.id);
                    table.CheckConstraint("ck_grievance_action_fields", "`current_status` < 3 OR (`action_taken_by` IS NOT NULL AND `action_taken_at_ist` IS NOT NULL AND `action_remarks` IS NOT NULL)");
                    table.CheckConstraint("ck_grievance_closed_fields", "`current_status` <> 4 OR (`closed_by` IS NOT NULL AND `closed_at_ist` IS NOT NULL)");
                    table.CheckConstraint("ck_grievance_current_status", "`current_status` IN (1, 2, 3, 4)");
                    table.CheckConstraint("ck_grievance_documents", "`document_urls` IS NULL OR JSON_VALID(`document_urls`)");
                    table.CheckConstraint("ck_grievance_subject", "CHAR_LENGTH(`subject`) > 0");
                    table.ForeignKey(
                        name: "fk_grievance_grievance_category_category_id",
                        column: x => x.category_id,
                        principalTable: "grievance_category",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_grievance_mobile_user_profiles_created_by",
                        column: x => x.created_by,
                        principalTable: "mobile_user_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "grievance_status_log",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    log_action = table.Column<int>(type: "int", nullable: false),
                    forwarded_to = table.Column<long>(type: "bigint", nullable: true),
                    grievance_pk = table.Column<long>(type: "bigint", nullable: false),
                    from_status = table.Column<int>(type: "int", nullable: true),
                    to_status = table.Column<int>(type: "int", nullable: false),
                    remarks = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    changed_by = table.Column<long>(type: "bigint", nullable: false),
                    changed_by_type = table.Column<int>(type: "int", nullable: false),
                    changed_at_ist = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_grievance_status_log", x => x.id);
                    table.CheckConstraint("ck_grievance_status_log_changed_by_type", "`changed_by_type` IN (1, 2)");
                    table.CheckConstraint("ck_grievance_status_log_from_status", "`from_status` IS NULL OR `from_status` IN (1, 2, 3, 4)");
                    table.CheckConstraint("ck_grievance_status_log_log_action", "`log_action` IN (1, 2, 3)");
                    table.CheckConstraint("ck_grievance_status_log_to_status", "`to_status` IN (1, 2, 3, 4)");
                    table.ForeignKey(
                        name: "fk_grievance_status_log_grievance_grievance_pk",
                        column: x => x.grievance_pk,
                        principalTable: "grievance",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_grievance_category_id",
                table: "grievance",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_grievance_created_by_current_status",
                table: "grievance",
                columns: new[] { "created_by", "current_status" });

            migrationBuilder.CreateIndex(
                name: "ix_grievance_current_status_created_at_ist",
                table: "grievance",
                columns: new[] { "current_status", "created_at_ist" });

            migrationBuilder.CreateIndex(
                name: "ix_grievance_grievance_id",
                table: "grievance",
                column: "grievance_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_grievance_category_grievance_name",
                table: "grievance_category",
                column: "grievance_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_grievance_status_log_grievance_pk_changed_at_ist",
                table: "grievance_status_log",
                columns: new[] { "grievance_pk", "changed_at_ist" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "grievance_status_log");

            migrationBuilder.DropTable(
                name: "grievance");

            migrationBuilder.DropTable(
                name: "grievance_category");
        }
    }
}
