using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSG.Mobile.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Add_Grievance_Resolution_Document : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "resolution_document",
                table: "grievance",
                type: "json",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddCheckConstraint(
                name: "ck_grievance_resolution_document",
                table: "grievance",
                sql: "`resolution_document` IS NULL OR JSON_VALID(`resolution_document`)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_grievance_resolution_document",
                table: "grievance");

            migrationBuilder.DropColumn(
                name: "resolution_document",
                table: "grievance");
        }
    }
}
