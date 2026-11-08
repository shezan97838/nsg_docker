using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSG.Mobile.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Add_Personnel_Form16 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "personnel_form16",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    nsg_number = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    assessment_year = table.Column<string>(type: "varchar(7)", maxLength: 7, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    part_a_file_path = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    part_b_file_path = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    part_a_uploaded_at_ist = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    part_b_uploaded_at_ist = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    uploaded_by = table.Column<long>(type: "bigint", nullable: false),
                    created_at_ist = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_at_ist = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_personnel_form16", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_personnel_form16_nsg_number_assessment_year",
                table: "personnel_form16",
                columns: new[] { "nsg_number", "assessment_year" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "personnel_form16");
        }
    }
}
