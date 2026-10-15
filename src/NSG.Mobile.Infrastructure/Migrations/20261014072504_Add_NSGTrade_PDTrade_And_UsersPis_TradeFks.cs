using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSG.Mobile.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Add_NSGTrade_PDTrade_And_UsersPis_TradeFks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "NSGTradeID",
                table: "UsersPIS",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PDTradeID",
                table: "UsersPIS",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "NSGTrade",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    NSGTradeName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NSGTrade", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PDTrade",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    PDTradeName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PDTrade", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_UsersPIS_NSGTradeID",
                table: "UsersPIS",
                column: "NSGTradeID");

            migrationBuilder.CreateIndex(
                name: "IX_UsersPIS_PDTradeID",
                table: "UsersPIS",
                column: "PDTradeID");

            migrationBuilder.CreateIndex(
                name: "IX_NSGTrade_NSGTradeName",
                table: "NSGTrade",
                column: "NSGTradeName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PDTrade_PDTradeName",
                table: "PDTrade",
                column: "PDTradeName",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_UsersPIS_NSGTrade_NSGTradeID",
                table: "UsersPIS",
                column: "NSGTradeID",
                principalTable: "NSGTrade",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UsersPIS_PDTrade_PDTradeID",
                table: "UsersPIS",
                column: "PDTradeID",
                principalTable: "PDTrade",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UsersPIS_NSGTrade_NSGTradeID",
                table: "UsersPIS");

            migrationBuilder.DropForeignKey(
                name: "FK_UsersPIS_PDTrade_PDTradeID",
                table: "UsersPIS");

            migrationBuilder.DropTable(
                name: "NSGTrade");

            migrationBuilder.DropTable(
                name: "PDTrade");

            migrationBuilder.DropIndex(
                name: "IX_UsersPIS_NSGTradeID",
                table: "UsersPIS");

            migrationBuilder.DropIndex(
                name: "IX_UsersPIS_PDTradeID",
                table: "UsersPIS");

            migrationBuilder.DropColumn(
                name: "NSGTradeID",
                table: "UsersPIS");

            migrationBuilder.DropColumn(
                name: "PDTradeID",
                table: "UsersPIS");
        }
    }
}
