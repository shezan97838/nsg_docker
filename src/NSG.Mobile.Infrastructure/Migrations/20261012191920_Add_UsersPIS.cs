using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSG.Mobile.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Add_UsersPIS : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EducationQualifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EducationQualifications", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "NsgUnits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NsgUnits", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ParentDepartmentRanks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParentDepartmentRanks", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Religions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Religions", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "UsersPIS",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    NsgNumber = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ServiceNumber = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NsgRankId = table.Column<int>(type: "int", nullable: true),
                    ParentDepartmentRankId = table.Column<int>(type: "int", nullable: true),
                    FullName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NsgUnitId = table.Column<int>(type: "int", nullable: true),
                    EducationId = table.Column<int>(type: "int", nullable: true),
                    DateOfInductionInNsg = table.Column<DateOnly>(type: "date", nullable: true),
                    SideSteppedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    SideSteppedFromNsgUnitId = table.Column<int>(type: "int", nullable: true),
                    DateOfRepatriation = table.Column<DateOnly>(type: "date", nullable: true),
                    DateOfBirth = table.Column<DateOnly>(type: "date", nullable: true),
                    DateOfEnrollmentInGovtService = table.Column<DateOnly>(type: "date", nullable: true),
                    DateOfSuperannuation = table.Column<DateOnly>(type: "date", nullable: true),
                    Category = table.Column<int>(type: "int", nullable: true),
                    ReligionId = table.Column<int>(type: "int", nullable: true),
                    FatherName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MotherName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SpouseName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NextOfKinMobileNumber = table.Column<string>(type: "varchar(15)", maxLength: 15, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NumberOfChildren = table.Column<int>(type: "int", nullable: true),
                    MobileNumber = table.Column<string>(type: "varchar(15)", maxLength: 15, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BloodGroup = table.Column<int>(type: "int", nullable: true),
                    HeightCm = table.Column<decimal>(type: "decimal(5,1)", nullable: true),
                    WeightKg = table.Column<decimal>(type: "decimal(5,1)", nullable: true),
                    AadhaarNumberEncrypted = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PanNumberEncrypted = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PranNumberEncrypted = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SalaryAccountNumberEncrypted = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PermanentVillage = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PermanentPostOffice = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PermanentPoliceStation = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PermanentDistrict = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PermanentStateId = table.Column<int>(type: "int", nullable: true),
                    PermanentPinCode = table.Column<string>(type: "varchar(6)", maxLength: 6, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DateOfAme = table.Column<DateOnly>(type: "date", nullable: true),
                    DateOfImmovablePropertyReturn = table.Column<DateOnly>(type: "date", nullable: true),
                    EarnedLeaveDue = table.Column<decimal>(type: "decimal(7,2)", nullable: true),
                    HalfPayLeaveDue = table.Column<decimal>(type: "decimal(7,2)", nullable: true),
                    PaternityLeaveAvailed = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MaternityLeaveAvailed = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ChildCareLeaveAvailed = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsersPIS", x => x.Id);
                    table.CheckConstraint("CK_UsersPIS_BloodGroup", "`BloodGroup` IS NULL OR `BloodGroup` BETWEEN 1 AND 8");
                    table.CheckConstraint("CK_UsersPIS_Category", "`Category` IS NULL OR `Category` IN (1, 2, 3, 4)");
                    table.CheckConstraint("CK_UsersPIS_Enrollment_After_Birth", "`DateOfEnrollmentInGovtService` IS NULL OR `DateOfBirth` IS NULL OR `DateOfEnrollmentInGovtService` >= `DateOfBirth`");
                    table.CheckConstraint("CK_UsersPIS_MobileNumber_Format", "`MobileNumber` IS NULL OR `MobileNumber` REGEXP '^(\\\\+91)?[6-9][0-9]{9}$'");
                    table.CheckConstraint("CK_UsersPIS_Numbers", "(`HeightCm` IS NULL OR `HeightCm` > 0) AND (`WeightKg` IS NULL OR `WeightKg` > 0) AND (`NumberOfChildren` IS NULL OR `NumberOfChildren` >= 0) AND (`EarnedLeaveDue` IS NULL OR `EarnedLeaveDue` >= 0) AND (`HalfPayLeaveDue` IS NULL OR `HalfPayLeaveDue` >= 0)");
                    table.CheckConstraint("CK_UsersPIS_PinCode_Format", "`PermanentPinCode` IS NULL OR `PermanentPinCode` REGEXP '^[1-9][0-9]{5}$'");
                    table.CheckConstraint("CK_UsersPIS_Repatriation_After_Induction", "`DateOfRepatriation` IS NULL OR `DateOfInductionInNsg` IS NULL OR `DateOfRepatriation` >= `DateOfInductionInNsg`");
                    table.CheckConstraint("CK_UsersPIS_Superannuation_After_Birth", "`DateOfSuperannuation` IS NULL OR `DateOfBirth` IS NULL OR `DateOfSuperannuation` > `DateOfBirth`");
                    table.ForeignKey(
                        name: "FK_UsersPIS_EducationQualifications_EducationId",
                        column: x => x.EducationId,
                        principalTable: "EducationQualifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UsersPIS_NsgUnits_NsgUnitId",
                        column: x => x.NsgUnitId,
                        principalTable: "NsgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UsersPIS_NsgUnits_SideSteppedFromNsgUnitId",
                        column: x => x.SideSteppedFromNsgUnitId,
                        principalTable: "NsgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UsersPIS_ParentDepartmentRanks_ParentDepartmentRankId",
                        column: x => x.ParentDepartmentRankId,
                        principalTable: "ParentDepartmentRanks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UsersPIS_Religions_ReligionId",
                        column: x => x.ReligionId,
                        principalTable: "Religions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UsersPIS_States_PermanentStateId",
                        column: x => x.PermanentStateId,
                        principalTable: "States",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UsersPIS_rank_hierarchy_NsgRankId",
                        column: x => x.NsgRankId,
                        principalTable: "rank_hierarchy",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_EducationQualifications_Name",
                table: "EducationQualifications",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NsgUnits_Name",
                table: "NsgUnits",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ParentDepartmentRanks_Name",
                table: "ParentDepartmentRanks",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Religions_Name",
                table: "Religions",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UsersPIS_EducationId",
                table: "UsersPIS",
                column: "EducationId");

            migrationBuilder.CreateIndex(
                name: "IX_UsersPIS_IsActive",
                table: "UsersPIS",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_UsersPIS_MobileNumber",
                table: "UsersPIS",
                column: "MobileNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UsersPIS_NsgNumber",
                table: "UsersPIS",
                column: "NsgNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UsersPIS_NsgRankId",
                table: "UsersPIS",
                column: "NsgRankId");

            migrationBuilder.CreateIndex(
                name: "IX_UsersPIS_NsgUnitId",
                table: "UsersPIS",
                column: "NsgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_UsersPIS_ParentDepartmentRankId",
                table: "UsersPIS",
                column: "ParentDepartmentRankId");

            migrationBuilder.CreateIndex(
                name: "IX_UsersPIS_PermanentStateId",
                table: "UsersPIS",
                column: "PermanentStateId");

            migrationBuilder.CreateIndex(
                name: "IX_UsersPIS_ReligionId",
                table: "UsersPIS",
                column: "ReligionId");

            migrationBuilder.CreateIndex(
                name: "IX_UsersPIS_ServiceNumber",
                table: "UsersPIS",
                column: "ServiceNumber");

            migrationBuilder.CreateIndex(
                name: "IX_UsersPIS_SideSteppedFromNsgUnitId",
                table: "UsersPIS",
                column: "SideSteppedFromNsgUnitId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UsersPIS");

            migrationBuilder.DropTable(
                name: "EducationQualifications");

            migrationBuilder.DropTable(
                name: "NsgUnits");

            migrationBuilder.DropTable(
                name: "ParentDepartmentRanks");

            migrationBuilder.DropTable(
                name: "Religions");
        }
    }
}
