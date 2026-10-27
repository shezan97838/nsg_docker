using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSG.Mobile.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Add_Leave_Management : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "personnel_leave_records",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    nsg_number = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    leave_year = table.Column<int>(type: "int", nullable: false),
                    leave_taken = table.Column<int>(type: "int", nullable: false),
                    row_version = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    created_at_ist = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at_ist = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_by = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_personnel_leave_records", x => x.id);
                    table.CheckConstraint("ck_personnel_leave_records_leave_taken", "`leave_taken` >= 0 AND `leave_taken` <= 366");
                    table.CheckConstraint("ck_personnel_leave_records_leave_year", "`leave_year` >= 2000 AND `leave_year` <= 2999");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "personnel_leave_upload_batches",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    file_name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    leave_year = table.Column<int>(type: "int", nullable: false),
                    total_rows = table.Column<int>(type: "int", nullable: false),
                    created_count = table.Column<int>(type: "int", nullable: false),
                    updated_count = table.Column<int>(type: "int", nullable: false),
                    unchanged_count = table.Column<int>(type: "int", nullable: false),
                    failed_count = table.Column<int>(type: "int", nullable: false),
                    upload_status = table.Column<int>(type: "int", nullable: false),
                    processing_lock = table.Column<bool>(type: "tinyint(1)", nullable: true),
                    created_at_ist = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    completed_at_ist = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_personnel_leave_upload_batches", x => x.id);
                    table.CheckConstraint("ck_personnel_leave_upload_batches_counts", "`total_rows` >= 0 AND `created_count` >= 0 AND `updated_count` >= 0 AND `unchanged_count` >= 0 AND `failed_count` >= 0");
                    table.CheckConstraint("ck_personnel_leave_upload_batches_processing_lock", "(`upload_status` = 1 AND `processing_lock` = 1) OR (`upload_status` <> 1 AND `processing_lock` IS NULL)");
                    table.CheckConstraint("ck_personnel_leave_upload_batches_upload_status", "`upload_status` IN (1, 2, 3, 4)");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "personnel_leave_history",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    leave_record_id = table.Column<long>(type: "bigint", nullable: false),
                    nsg_number = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    leave_year = table.Column<int>(type: "int", nullable: false),
                    old_leave_taken = table.Column<int>(type: "int", nullable: true),
                    new_leave_taken = table.Column<int>(type: "int", nullable: false),
                    change_source = table.Column<int>(type: "int", nullable: false),
                    upload_batch_id = table.Column<long>(type: "bigint", nullable: true),
                    changed_by = table.Column<long>(type: "bigint", nullable: false),
                    changed_at_ist = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_personnel_leave_history", x => x.id);
                    table.CheckConstraint("ck_personnel_leave_history_change_source", "`change_source` IN (1, 2)");
                    table.CheckConstraint("ck_personnel_leave_history_counts", "(`old_leave_taken` IS NULL OR `old_leave_taken` >= 0) AND `new_leave_taken` >= 0");
                    table.ForeignKey(
                        name: "fk_personnel_leave_history_leave_record_id",
                        column: x => x.leave_record_id,
                        principalTable: "personnel_leave_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_personnel_leave_history_upload_batch_id",
                        column: x => x.upload_batch_id,
                        principalTable: "personnel_leave_upload_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "personnel_leave_upload_errors",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    batch_id = table.Column<long>(type: "bigint", nullable: false),
                    excel_row_number = table.Column<int>(type: "int", nullable: true),
                    nsg_number = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    error_message = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at_ist = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_personnel_leave_upload_errors", x => x.id);
                    table.ForeignKey(
                        name: "fk_personnel_leave_upload_errors_batch_id",
                        column: x => x.batch_id,
                        principalTable: "personnel_leave_upload_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.InsertData(
                table: "admin_roles",
                columns: new[] { "id", "created_at_ist", "description", "is_active", "role_code", "role_name", "updated_at_ist" },
                values: new object[] { 7, new DateTime(2026, 10, 18, 0, 0, 0, 0, DateTimeKind.Unspecified), "Maintains personnel leave records (manual entry and Excel bulk upload).", true, "LEAVE_ADMIN", "Leave Admin", null });

            migrationBuilder.CreateIndex(
                name: "ix_personnel_leave_history_leave_record_id",
                table: "personnel_leave_history",
                column: "leave_record_id");

            migrationBuilder.CreateIndex(
                name: "ix_personnel_leave_history_nsg_number_changed_at_ist",
                table: "personnel_leave_history",
                columns: new[] { "nsg_number", "changed_at_ist" });

            migrationBuilder.CreateIndex(
                name: "ix_personnel_leave_history_upload_batch_id",
                table: "personnel_leave_history",
                column: "upload_batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_personnel_leave_records_nsg_number_leave_year",
                table: "personnel_leave_records",
                columns: new[] { "nsg_number", "leave_year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_personnel_leave_upload_batches_created_at_ist",
                table: "personnel_leave_upload_batches",
                column: "created_at_ist");

            migrationBuilder.CreateIndex(
                name: "ix_personnel_leave_upload_batches_processing_lock",
                table: "personnel_leave_upload_batches",
                column: "processing_lock",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_personnel_leave_upload_errors_batch_id_excel_row_number",
                table: "personnel_leave_upload_errors",
                columns: new[] { "batch_id", "excel_row_number" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "personnel_leave_history");

            migrationBuilder.DropTable(
                name: "personnel_leave_upload_errors");

            migrationBuilder.DropTable(
                name: "personnel_leave_records");

            migrationBuilder.DropTable(
                name: "personnel_leave_upload_batches");

            migrationBuilder.DeleteData(
                table: "admin_roles",
                keyColumn: "id",
                keyValue: 7);
        }
    }
}
