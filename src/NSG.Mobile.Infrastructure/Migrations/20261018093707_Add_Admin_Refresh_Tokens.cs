using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSG.Mobile.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Add_Admin_Refresh_Tokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "admin_refresh_tokens",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    admin_profile_id = table.Column<long>(type: "bigint", nullable: false),
                    token_hash = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    family_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    expires_at_ist = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    created_at_ist = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    created_by_ip = table.Column<string>(type: "varchar(45)", maxLength: 45, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    revoked_at_ist = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    revoke_reason = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    reuse_detected_at_ist = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_admin_refresh_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_admin_refresh_tokens_admin_profiles_admin_profile_id",
                        column: x => x.admin_profile_id,
                        principalTable: "admin_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_admin_refresh_tokens_admin_profile_id_revoked_at_ist",
                table: "admin_refresh_tokens",
                columns: new[] { "admin_profile_id", "revoked_at_ist" });

            migrationBuilder.CreateIndex(
                name: "ix_admin_refresh_tokens_family_id",
                table: "admin_refresh_tokens",
                column: "family_id");

            migrationBuilder.CreateIndex(
                name: "ix_admin_refresh_tokens_token_hash",
                table: "admin_refresh_tokens",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "admin_refresh_tokens");
        }
    }
}
