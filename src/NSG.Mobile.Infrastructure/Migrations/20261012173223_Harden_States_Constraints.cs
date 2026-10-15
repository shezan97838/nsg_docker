using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSG.Mobile.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Harden_States_Constraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_States_AdministrativeType",
                table: "States",
                sql: "`AdministrativeType` IN (1, 2)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_States_Code_Format",
                table: "States",
                sql: "`Code` IS NULL OR `Code` REGEXP '^[0-9]{2}$'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_States_AdministrativeType",
                table: "States");

            migrationBuilder.DropCheckConstraint(
                name: "CK_States_Code_Format",
                table: "States");
        }
    }
}
