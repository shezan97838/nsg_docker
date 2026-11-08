using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSG.Mobile.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Add_PIS_Ownership_Transfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pis_ownership_transfers",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    pis_record_id = table.Column<long>(type: "bigint", nullable: false),
                    nsg_number = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    previous_assigned_to = table.Column<long>(type: "bigint", nullable: true),
                    new_assigned_to = table.Column<long>(type: "bigint", nullable: false),
                    transferred_by = table.Column<long>(type: "bigint", nullable: false),
                    reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    transferred_at_ist = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pis_ownership_transfers", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_pis_ownership_transfers_pis_record_id_transferred_at_ist",
                table: "pis_ownership_transfers",
                columns: new[] { "pis_record_id", "transferred_at_ist" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pis_ownership_transfers");
        }
    }
}
