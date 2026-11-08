using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSG.Mobile.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Add_Pis_Upload_Batches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pis_upload_batches",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    file_name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    upload_type = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<int>(type: "int", nullable: false),
                    total_rows = table.Column<int>(type: "int", nullable: false),
                    inserted_count = table.Column<int>(type: "int", nullable: false),
                    updated_count = table.Column<int>(type: "int", nullable: false),
                    failed_count = table.Column<int>(type: "int", nullable: false),
                    conflict_count = table.Column<int>(type: "int", nullable: false),
                    failure_message = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    uploaded_by = table.Column<long>(type: "bigint", nullable: false),
                    created_at_ist = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    completed_at_ist = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pis_upload_batches", x => x.id);
                    table.CheckConstraint("ck_pis_upload_batches_counts", "`total_rows` >= 0 AND `inserted_count` >= 0 AND `updated_count` >= 0 AND `failed_count` >= 0 AND `conflict_count` >= 0");
                    table.CheckConstraint("ck_pis_upload_batches_status", "`status` IN (1, 2, 3, 4, 5)");
                    table.CheckConstraint("ck_pis_upload_batches_upload_type", "`upload_type` IN (1, 2)");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "pis_upload_batch_errors",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    batch_id = table.Column<long>(type: "bigint", nullable: false),
                    excel_row_number = table.Column<int>(type: "int", nullable: false),
                    nsg_number = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status = table.Column<int>(type: "int", nullable: false),
                    conflict_type = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    field = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    value = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    message = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    conflicting_nsg_number = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at_ist = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pis_upload_batch_errors", x => x.id);
                    table.CheckConstraint("ck_pis_upload_batch_errors_status", "`status` IN (1, 2)");
                    table.ForeignKey(
                        name: "fk_pis_upload_batch_errors_batch_id",
                        column: x => x.batch_id,
                        principalTable: "pis_upload_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "pis_upload_batch_records",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    batch_id = table.Column<long>(type: "bigint", nullable: false),
                    excel_row_number = table.Column<int>(type: "int", nullable: false),
                    pis_record_id = table.Column<long>(type: "bigint", nullable: false),
                    nsg_number = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    operation = table.Column<int>(type: "int", nullable: false),
                    created_at_ist = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pis_upload_batch_records", x => x.id);
                    table.CheckConstraint("ck_pis_upload_batch_records_operation", "`operation` IN (1, 2)");
                    table.ForeignKey(
                        name: "fk_pis_upload_batch_records_batch_id",
                        column: x => x.batch_id,
                        principalTable: "pis_upload_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_pis_upload_batch_errors_batch_id_excel_row_number",
                table: "pis_upload_batch_errors",
                columns: new[] { "batch_id", "excel_row_number" });

            migrationBuilder.CreateIndex(
                name: "ix_pis_upload_batch_records_batch_id_excel_row_number",
                table: "pis_upload_batch_records",
                columns: new[] { "batch_id", "excel_row_number" });

            migrationBuilder.CreateIndex(
                name: "ix_pis_upload_batch_records_pis_record_id",
                table: "pis_upload_batch_records",
                column: "pis_record_id");

            migrationBuilder.CreateIndex(
                name: "ix_pis_upload_batches_uploaded_by_created_at_ist",
                table: "pis_upload_batches",
                columns: new[] { "uploaded_by", "created_at_ist" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pis_upload_batch_errors");

            migrationBuilder.DropTable(
                name: "pis_upload_batch_records");

            migrationBuilder.DropTable(
                name: "pis_upload_batches");
        }
    }
}
