using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSG.Mobile.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Add_PIS_Personnel_AssignedTo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "assigned_to",
                table: "users_pis",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_pis_assigned_to",
                table: "users_pis",
                column: "assigned_to");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_users_pis_assigned_to",
                table: "users_pis");

            migrationBuilder.DropColumn(
                name: "assigned_to",
                table: "users_pis");
        }
    }
}
