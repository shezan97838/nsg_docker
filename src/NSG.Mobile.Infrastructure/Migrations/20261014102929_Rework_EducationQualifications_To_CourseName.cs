using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSG.Mobile.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Rework_EducationQualifications_To_CourseName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "updated_at_utc",
                table: "education_qualifications");

            migrationBuilder.RenameColumn(
                name: "name",
                table: "education_qualifications",
                newName: "course_name");

            migrationBuilder.RenameIndex(
                name: "ix_education_qualifications_name",
                table: "education_qualifications",
                newName: "ix_education_qualifications_course_name");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "course_name",
                table: "education_qualifications",
                newName: "name");

            migrationBuilder.RenameIndex(
                name: "ix_education_qualifications_course_name",
                table: "education_qualifications",
                newName: "ix_education_qualifications_name");

            migrationBuilder.AddColumn<DateTime>(
                name: "updated_at_utc",
                table: "education_qualifications",
                type: "datetime(6)",
                nullable: true);
        }
    }
}
