using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSG.Mobile.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Rename_Trade_Objects_To_Snake_Case : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UsersPIS_NSGTrade_NSGTradeID",
                table: "UsersPIS");

            migrationBuilder.DropForeignKey(
                name: "FK_UsersPIS_PDTrade_PDTradeID",
                table: "UsersPIS");

            migrationBuilder.RenameTable(
                name: "PDTrade",
                newName: "pd_trades");

            migrationBuilder.RenameTable(
                name: "NSGTrade",
                newName: "nsg_trades");

            migrationBuilder.RenameColumn(
                name: "PDTradeID",
                table: "UsersPIS",
                newName: "pd_trade_id");

            migrationBuilder.RenameColumn(
                name: "NSGTradeID",
                table: "UsersPIS",
                newName: "nsg_trade_id");

            migrationBuilder.RenameIndex(
                name: "IX_UsersPIS_PDTradeID",
                table: "UsersPIS",
                newName: "ix_users_pis_pd_trade_id");

            migrationBuilder.RenameIndex(
                name: "IX_UsersPIS_NSGTradeID",
                table: "UsersPIS",
                newName: "ix_users_pis_nsg_trade_id");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "pd_trades",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "PDTradeName",
                table: "pd_trades",
                newName: "pd_trade_name");

            migrationBuilder.RenameColumn(
                name: "IsActive",
                table: "pd_trades",
                newName: "is_active");

            migrationBuilder.RenameColumn(
                name: "CreatedAtUtc",
                table: "pd_trades",
                newName: "created_at_utc");

            migrationBuilder.RenameIndex(
                name: "IX_PDTrade_PDTradeName",
                table: "pd_trades",
                newName: "ix_pd_trades_pd_trade_name");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "nsg_trades",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "NSGTradeName",
                table: "nsg_trades",
                newName: "nsg_trade_name");

            migrationBuilder.RenameColumn(
                name: "IsActive",
                table: "nsg_trades",
                newName: "is_active");

            migrationBuilder.RenameColumn(
                name: "CreatedAtUtc",
                table: "nsg_trades",
                newName: "created_at_utc");

            migrationBuilder.RenameIndex(
                name: "IX_NSGTrade_NSGTradeName",
                table: "nsg_trades",
                newName: "ix_nsg_trades_nsg_trade_name");

            migrationBuilder.AddForeignKey(
                name: "fk_users_pis_nsg_trades",
                table: "UsersPIS",
                column: "nsg_trade_id",
                principalTable: "nsg_trades",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_users_pis_pd_trades",
                table: "UsersPIS",
                column: "pd_trade_id",
                principalTable: "pd_trades",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_users_pis_nsg_trades",
                table: "UsersPIS");

            migrationBuilder.DropForeignKey(
                name: "fk_users_pis_pd_trades",
                table: "UsersPIS");

            migrationBuilder.RenameTable(
                name: "pd_trades",
                newName: "PDTrade");

            migrationBuilder.RenameTable(
                name: "nsg_trades",
                newName: "NSGTrade");

            migrationBuilder.RenameColumn(
                name: "pd_trade_id",
                table: "UsersPIS",
                newName: "PDTradeID");

            migrationBuilder.RenameColumn(
                name: "nsg_trade_id",
                table: "UsersPIS",
                newName: "NSGTradeID");

            migrationBuilder.RenameIndex(
                name: "ix_users_pis_pd_trade_id",
                table: "UsersPIS",
                newName: "IX_UsersPIS_PDTradeID");

            migrationBuilder.RenameIndex(
                name: "ix_users_pis_nsg_trade_id",
                table: "UsersPIS",
                newName: "IX_UsersPIS_NSGTradeID");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "PDTrade",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "pd_trade_name",
                table: "PDTrade",
                newName: "PDTradeName");

            migrationBuilder.RenameColumn(
                name: "is_active",
                table: "PDTrade",
                newName: "IsActive");

            migrationBuilder.RenameColumn(
                name: "created_at_utc",
                table: "PDTrade",
                newName: "CreatedAtUtc");

            migrationBuilder.RenameIndex(
                name: "ix_pd_trades_pd_trade_name",
                table: "PDTrade",
                newName: "IX_PDTrade_PDTradeName");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "NSGTrade",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "nsg_trade_name",
                table: "NSGTrade",
                newName: "NSGTradeName");

            migrationBuilder.RenameColumn(
                name: "is_active",
                table: "NSGTrade",
                newName: "IsActive");

            migrationBuilder.RenameColumn(
                name: "created_at_utc",
                table: "NSGTrade",
                newName: "CreatedAtUtc");

            migrationBuilder.RenameIndex(
                name: "ix_nsg_trades_nsg_trade_name",
                table: "NSGTrade",
                newName: "IX_NSGTrade_NSGTradeName");

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
    }
}
