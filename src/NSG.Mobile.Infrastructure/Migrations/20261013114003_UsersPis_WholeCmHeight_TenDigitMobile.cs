using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSG.Mobile.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UsersPis_WholeCmHeight_TenDigitMobile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Height is now whole centimetres. Round any fractional values explicitly (172.5 -> 173) instead of relying on
            // an implicit conversion. Rows outside 1-255, or MobileNumber values that still carry "+91" or are not
            // 10 digits, make this migration FAIL rather than lose data: fix or review those rows first.
            migrationBuilder.Sql("UPDATE `UsersPIS` SET `HeightCm` = ROUND(`HeightCm`) WHERE `HeightCm` IS NOT NULL;");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UsersPIS_MobileNumber_Format",
                table: "UsersPIS");

            migrationBuilder.AlterColumn<byte>(
                name: "HeightCm",
                table: "UsersPIS",
                type: "tinyint unsigned",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(5,2)",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_UsersPIS_MobileNumber_Format",
                table: "UsersPIS",
                sql: "`MobileNumber` IS NULL OR `MobileNumber` REGEXP '^[6-9][0-9]{9}$'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_UsersPIS_MobileNumber_Format",
                table: "UsersPIS");

            migrationBuilder.AlterColumn<decimal>(
                name: "HeightCm",
                table: "UsersPIS",
                type: "decimal(5,2)",
                nullable: true,
                oldClrType: typeof(byte),
                oldType: "tinyint unsigned",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_UsersPIS_MobileNumber_Format",
                table: "UsersPIS",
                sql: "`MobileNumber` IS NULL OR `MobileNumber` REGEXP '^(\\\\+91)?[6-9][0-9]{9}$'");
        }
    }
}
