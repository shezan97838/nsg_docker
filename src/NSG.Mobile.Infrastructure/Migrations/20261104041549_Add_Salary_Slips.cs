using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSG.Mobile.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Add_Salary_Slips : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "salary_slip_upload_batches",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    original_file_name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    salary_month = table.Column<DateOnly>(type: "date", nullable: false),
                    total_pages = table.Column<int>(type: "int", nullable: false),
                    processed_pages = table.Column<int>(type: "int", nullable: false),
                    unmatched_pages = table.Column<int>(type: "int", nullable: false),
                    unmatched_page_numbers = table.Column<string>(type: "text", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    failure_message = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    uploaded_by = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<int>(type: "int", nullable: false),
                    created_at_ist = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_at_ist = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_salary_slip_upload_batches", x => x.id);
                    table.CheckConstraint("ck_salary_slip_upload_batches_pages", "`total_pages` >= 0 AND `processed_pages` >= 0 AND `unmatched_pages` >= 0");
                    table.CheckConstraint("ck_salary_slip_upload_batches_status", "`status` IN (0, 1, 2, 3)");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "salary_slips",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    account_number_encrypted = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    salary_month = table.Column<DateOnly>(type: "date", nullable: false),
                    user_pis_id = table.Column<long>(type: "bigint", nullable: true),
                    file_path = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    page_count = table.Column<int>(type: "int", nullable: false),
                    original_batch_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at_ist = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_at_ist = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_salary_slips", x => x.id);
                    table.CheckConstraint("ck_salary_slips_page_count", "`page_count` > 0");
                    table.ForeignKey(
                        name: "fk_salary_slips_original_batch_id",
                        column: x => x.original_batch_id,
                        principalTable: "salary_slip_upload_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_salary_slip_batches_uploaded_by_salary_month_created_at_ist",
                table: "salary_slip_upload_batches",
                columns: new[] { "uploaded_by", "salary_month", "created_at_ist" });

            migrationBuilder.CreateIndex(
                name: "ix_salary_slips_original_batch_id",
                table: "salary_slips",
                column: "original_batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_salary_slips_salary_month",
                table: "salary_slips",
                column: "salary_month");

            migrationBuilder.CreateIndex(
                name: "ix_salary_slips_user_pis_id_salary_month",
                table: "salary_slips",
                columns: new[] { "user_pis_id", "salary_month" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "salary_slips");

            migrationBuilder.DropTable(
                name: "salary_slip_upload_batches");
        }
    }
}
