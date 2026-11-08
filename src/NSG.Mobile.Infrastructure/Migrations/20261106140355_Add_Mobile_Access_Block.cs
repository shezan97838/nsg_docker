using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSG.Mobile.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Add_Mobile_Access_Block : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "access_block_reason",
                table: "mobile_user_profiles",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "access_blocked_at_ist",
                table: "mobile_user_profiles",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "access_blocked_by",
                table: "mobile_user_profiles",
                type: "int",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_mobile_user_profiles_access_block",
                table: "mobile_user_profiles",
                sql: "(`access_blocked_at_ist` IS NULL AND `access_blocked_by` IS NULL AND `access_block_reason` IS NULL) OR (`access_blocked_at_ist` IS NOT NULL AND `is_active` = 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_mobile_user_profiles_access_block",
                table: "mobile_user_profiles");

            migrationBuilder.DropColumn(
                name: "access_block_reason",
                table: "mobile_user_profiles");

            migrationBuilder.DropColumn(
                name: "access_blocked_at_ist",
                table: "mobile_user_profiles");

            migrationBuilder.DropColumn(
                name: "access_blocked_by",
                table: "mobile_user_profiles");
        }
    }
}
