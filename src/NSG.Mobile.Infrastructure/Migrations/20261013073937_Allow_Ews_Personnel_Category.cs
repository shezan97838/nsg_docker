using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSG.Mobile.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Allow_Ews_Personnel_Category : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_UsersPIS_Category",
                table: "UsersPIS");

            migrationBuilder.AddCheckConstraint(
                name: "CK_UsersPIS_Category",
                table: "UsersPIS",
                sql: "`Category` IS NULL OR `Category` IN (1, 2, 3, 4, 5)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_UsersPIS_Category",
                table: "UsersPIS");

            migrationBuilder.AddCheckConstraint(
                name: "CK_UsersPIS_Category",
                table: "UsersPIS",
                sql: "`Category` IS NULL OR `Category` IN (1, 2, 3, 4)");
        }
    }
}
