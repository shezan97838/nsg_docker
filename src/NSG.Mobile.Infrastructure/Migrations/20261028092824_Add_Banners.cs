using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSG.Mobile.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Add_Banners : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "banners",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    title = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    description = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    category = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    image_url = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    image_alt_text = table.Column<string>(type: "varchar(250)", maxLength: 250, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    nsg_unit_id = table.Column<int>(type: "int", nullable: true),
                    priority = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    is_active = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    start_at_ist = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    end_at_ist = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    created_at_ist = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_at_ist = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_banners", x => x.id);
                    table.CheckConstraint("ck_banners_category", "CHAR_LENGTH(`category`) > 0");
                    table.CheckConstraint("ck_banners_period", "`end_at_ist` IS NULL OR `end_at_ist` > `start_at_ist`");
                    table.CheckConstraint("ck_banners_title", "CHAR_LENGTH(`title`) > 0");
                    table.ForeignKey(
                        name: "fk_banners_nsg_units_nsg_unit_id",
                        column: x => x.nsg_unit_id,
                        principalTable: "nsg_units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_banners_is_active_priority_start_at_ist",
                table: "banners",
                columns: new[] { "is_active", "priority", "start_at_ist" });

            migrationBuilder.CreateIndex(
                name: "ix_banners_nsg_unit_id",
                table: "banners",
                column: "nsg_unit_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "banners");
        }
    }
}
