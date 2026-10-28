using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSG.Mobile.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Remove_Banner_Schedule_And_Category : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_banners_is_active_priority_start_at_ist",
                table: "banners");

            migrationBuilder.DropCheckConstraint(
                name: "ck_banners_category",
                table: "banners");

            migrationBuilder.DropCheckConstraint(
                name: "ck_banners_period",
                table: "banners");

            migrationBuilder.DropColumn(
                name: "category",
                table: "banners");

            migrationBuilder.DropColumn(
                name: "end_at_ist",
                table: "banners");

            migrationBuilder.DropColumn(
                name: "start_at_ist",
                table: "banners");

            migrationBuilder.AlterColumn<int>(
                name: "priority",
                table: "banners",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 0);

            migrationBuilder.AlterColumn<bool>(
                name: "is_active",
                table: "banners",
                type: "tinyint(1)",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "tinyint(1)",
                oldDefaultValue: true);

            migrationBuilder.CreateIndex(
                name: "ix_banners_is_active_priority",
                table: "banners",
                columns: new[] { "is_active", "priority" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_banners_is_active_priority",
                table: "banners");

            migrationBuilder.AlterColumn<int>(
                name: "priority",
                table: "banners",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<bool>(
                name: "is_active",
                table: "banners",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "tinyint(1)");

            migrationBuilder.AddColumn<string>(
                name: "category",
                table: "banners",
                type: "varchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "end_at_ist",
                table: "banners",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "start_at_ist",
                table: "banners",
                type: "datetime(6)",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.CreateIndex(
                name: "ix_banners_is_active_priority_start_at_ist",
                table: "banners",
                columns: new[] { "is_active", "priority", "start_at_ist" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_banners_category",
                table: "banners",
                sql: "CHAR_LENGTH(`category`) > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_banners_period",
                table: "banners",
                sql: "`end_at_ist` IS NULL OR `end_at_ist` > `start_at_ist`");
        }
    }
}
