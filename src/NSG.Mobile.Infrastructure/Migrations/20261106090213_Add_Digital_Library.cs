using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSG.Mobile.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Add_Digital_Library : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "digital_library",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    description = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    file_path = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    file_type = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    is_important = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: false),
                    is_active = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    created_at_ist = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    updated_at_ist = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_digital_library", x => x.id);
                    table.CheckConstraint("ck_digital_library_description", "CHAR_LENGTH(`description`) > 0");
                    table.CheckConstraint("ck_digital_library_file_type", "`file_type` IN ('PDF', 'JPG', 'JPEG', 'PNG')");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_digital_library_is_active_created_at_ist",
                table: "digital_library",
                columns: new[] { "is_active", "created_at_ist" });

            migrationBuilder.CreateIndex(
                name: "ix_digital_library_is_active_is_important_created_at_ist",
                table: "digital_library",
                columns: new[] { "is_active", "is_important", "created_at_ist" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "digital_library");
        }
    }
}
