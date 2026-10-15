using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSG.Mobile.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UsersPis_PanAndSalaryAccount_BlindIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PanNumberHash",
                table: "UsersPIS",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "SalaryAccountNumberHash",
                table: "UsersPIS",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_UsersPIS_PanNumberHash",
                table: "UsersPIS",
                column: "PanNumberHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UsersPIS_SalaryAccountNumberHash",
                table: "UsersPIS",
                column: "SalaryAccountNumberHash",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_UsersPIS_Hashes_Format",
                table: "UsersPIS",
                sql: "(`PanNumberHash` IS NULL OR `PanNumberHash` REGEXP '^[0-9a-f]{64}$') AND (`SalaryAccountNumberHash` IS NULL OR `SalaryAccountNumberHash` REGEXP '^[0-9a-f]{64}$')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UsersPIS_PanNumberHash",
                table: "UsersPIS");

            migrationBuilder.DropIndex(
                name: "IX_UsersPIS_SalaryAccountNumberHash",
                table: "UsersPIS");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UsersPIS_Hashes_Format",
                table: "UsersPIS");

            migrationBuilder.DropColumn(
                name: "PanNumberHash",
                table: "UsersPIS");

            migrationBuilder.DropColumn(
                name: "SalaryAccountNumberHash",
                table: "UsersPIS");
        }
    }
}
