using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSG.Mobile.Infrastructure.Migrations
{
    /// <summary>
    /// A worksheet row is mapped at most once per batch. The index also serves the batch foreign key, and MySQL refuses to drop an index
    /// that a foreign key relies on, so it is replaced inside ONE ALTER TABLE statement (EF's generated drop-then-create would fail).
    /// </summary>
    public partial class Make_Pis_Upload_Batch_Records_Row_Unique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE `pis_upload_batch_records` " +
                "DROP INDEX `ix_pis_upload_batch_records_batch_id_excel_row_number`, " +
                "ADD UNIQUE INDEX `ix_pis_upload_batch_records_batch_id_excel_row_number` (`batch_id`, `excel_row_number`);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE `pis_upload_batch_records` " +
                "DROP INDEX `ix_pis_upload_batch_records_batch_id_excel_row_number`, " +
                "ADD INDEX `ix_pis_upload_batch_records_batch_id_excel_row_number` (`batch_id`, `excel_row_number`);");
        }
    }
}
