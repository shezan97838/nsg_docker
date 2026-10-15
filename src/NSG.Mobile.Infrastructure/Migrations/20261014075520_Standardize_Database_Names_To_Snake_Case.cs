using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSG.Mobile.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Standardize_Database_Names_To_Snake_Case : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // EF's differ treats CK_States_Code_Format -> ck_states_code_format as "unchanged" (name and SQL differ only by letter case),
            // so it is dropped and re-created here explicitly. Found by migrating a copy of the development data.
            migrationBuilder.DropCheckConstraint(
                name: "CK_States_Code_Format",
                table: "States");

            migrationBuilder.DropForeignKey(
                name: "FK_Cities_States_StateId",
                table: "Cities");

            migrationBuilder.DropForeignKey(
                name: "FK_Devices_Users_UserId",
                table: "Devices");

            migrationBuilder.DropForeignKey(
                name: "FK_RefreshTokens_Devices_DeviceId",
                table: "RefreshTokens");

            migrationBuilder.DropForeignKey(
                name: "FK_RefreshTokens_RefreshTokens_ReplacedByTokenId",
                table: "RefreshTokens");

            migrationBuilder.DropForeignKey(
                name: "FK_RefreshTokens_Users_UserId",
                table: "RefreshTokens");

            migrationBuilder.DropForeignKey(
                name: "FK_UsersPIS_EducationQualifications_EducationId",
                table: "UsersPIS");

            migrationBuilder.DropForeignKey(
                name: "FK_UsersPIS_NsgUnits_NsgUnitId",
                table: "UsersPIS");

            migrationBuilder.DropForeignKey(
                name: "FK_UsersPIS_NsgUnits_SideSteppedFromNsgUnitId",
                table: "UsersPIS");

            migrationBuilder.DropForeignKey(
                name: "FK_UsersPIS_ParentDepartmentRanks_ParentDepartmentRankId",
                table: "UsersPIS");

            migrationBuilder.DropForeignKey(
                name: "FK_UsersPIS_Religions_ReligionId",
                table: "UsersPIS");

            migrationBuilder.DropForeignKey(
                name: "FK_UsersPIS_States_PermanentStateId",
                table: "UsersPIS");

            migrationBuilder.DropForeignKey(
                name: "FK_UsersPIS_rank_hierarchy_NsgRankId",
                table: "UsersPIS");

            migrationBuilder.DropForeignKey(
                name: "fk_users_pis_nsg_trades",
                table: "UsersPIS");

            migrationBuilder.DropForeignKey(
                name: "fk_users_pis_pd_trades",
                table: "UsersPIS");

            migrationBuilder.DropCheckConstraint(
                name: "CK_States_AdministrativeType",
                table: "States");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UsersPIS_BloodGroup",
                table: "UsersPIS");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UsersPIS_Category",
                table: "UsersPIS");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UsersPIS_Enrollment_After_Birth",
                table: "UsersPIS");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UsersPIS_Hashes_Format",
                table: "UsersPIS");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UsersPIS_MobileAppActivationStatus",
                table: "UsersPIS");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UsersPIS_MobileNumber_Format",
                table: "UsersPIS");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UsersPIS_Numbers",
                table: "UsersPIS");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UsersPIS_PinCode_Format",
                table: "UsersPIS");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UsersPIS_Repatriation_After_Induction",
                table: "UsersPIS");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UsersPIS_Superannuation_After_Birth",
                table: "UsersPIS");

            migrationBuilder.RenameTable(
                name: "Users",
                newName: "users");

            migrationBuilder.RenameTable(
                name: "States",
                newName: "states");

            migrationBuilder.RenameTable(
                name: "Religions",
                newName: "religions");

            migrationBuilder.RenameTable(
                name: "Devices",
                newName: "devices");

            migrationBuilder.RenameTable(
                name: "Cities",
                newName: "cities");

            migrationBuilder.RenameTable(
                name: "UsersPIS",
                newName: "users_pis");

            migrationBuilder.RenameTable(
                name: "RefreshTokens",
                newName: "refresh_tokens");

            migrationBuilder.RenameTable(
                name: "pd_trades",
                newName: "pd_trade");

            migrationBuilder.RenameTable(
                name: "ParentDepartmentRanks",
                newName: "parent_department_ranks");

            migrationBuilder.RenameTable(
                name: "OtpRequests",
                newName: "otp_requests");

            migrationBuilder.RenameTable(
                name: "NsgUnits",
                newName: "nsg_units");

            migrationBuilder.RenameTable(
                name: "nsg_trades",
                newName: "nsg_trade");

            migrationBuilder.RenameTable(
                name: "EducationQualifications",
                newName: "education_qualifications");

            migrationBuilder.RenameTable(
                name: "AuditLogs",
                newName: "audit_logs");

            migrationBuilder.RenameColumn(
                name: "Status",
                table: "users",
                newName: "status");

            migrationBuilder.RenameColumn(
                name: "Role",
                table: "users",
                newName: "role");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "users",
                newName: "name");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "users",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "users",
                newName: "updated_at_utc");

            migrationBuilder.RenameColumn(
                name: "RowVersion",
                table: "users",
                newName: "row_version");

            migrationBuilder.RenameColumn(
                name: "Rank",
                table: "users",
                newName: "rank_name");

            migrationBuilder.RenameColumn(
                name: "PasswordHash",
                table: "users",
                newName: "password_hash");

            migrationBuilder.RenameColumn(
                name: "MobileNumber",
                table: "users",
                newName: "mobile_number");

            migrationBuilder.RenameColumn(
                name: "LockoutEndAt",
                table: "users",
                newName: "lockout_end_at_utc");

            migrationBuilder.RenameColumn(
                name: "LastLoginAt",
                table: "users",
                newName: "last_login_at_utc");

            migrationBuilder.RenameColumn(
                name: "IsActive",
                table: "users",
                newName: "is_active");

            migrationBuilder.RenameColumn(
                name: "ForceNumber",
                table: "users",
                newName: "force_number");

            migrationBuilder.RenameColumn(
                name: "FailedLoginAttempts",
                table: "users",
                newName: "failed_login_attempts");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "users",
                newName: "created_at_utc");

            migrationBuilder.RenameColumn(
                name: "ApprovedBy",
                table: "users",
                newName: "approved_by");

            migrationBuilder.RenameColumn(
                name: "ApprovedAt",
                table: "users",
                newName: "approved_at_utc");

            migrationBuilder.RenameIndex(
                name: "IX_Users_Status",
                table: "users",
                newName: "ix_users_status");

            migrationBuilder.RenameIndex(
                name: "IX_Users_MobileNumber",
                table: "users",
                newName: "ix_users_mobile_number");

            migrationBuilder.RenameIndex(
                name: "IX_Users_ForceNumber",
                table: "users",
                newName: "ix_users_force_number");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "states",
                newName: "name");

            migrationBuilder.RenameColumn(
                name: "Code",
                table: "states",
                newName: "code");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "states",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UpdatedAtUtc",
                table: "states",
                newName: "updated_at_utc");

            migrationBuilder.RenameColumn(
                name: "IsActive",
                table: "states",
                newName: "is_active");

            migrationBuilder.RenameColumn(
                name: "CreatedAtUtc",
                table: "states",
                newName: "created_at_utc");

            migrationBuilder.RenameColumn(
                name: "AdministrativeType",
                table: "states",
                newName: "administrative_type");

            migrationBuilder.RenameIndex(
                name: "IX_States_Name",
                table: "states",
                newName: "ix_states_name");

            migrationBuilder.RenameIndex(
                name: "IX_States_Code",
                table: "states",
                newName: "ix_states_code");

            migrationBuilder.RenameIndex(
                name: "IX_States_AdministrativeType_IsActive",
                table: "states",
                newName: "ix_states_administrative_type_is_active");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "religions",
                newName: "name");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "religions",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UpdatedAtUtc",
                table: "religions",
                newName: "updated_at_utc");

            migrationBuilder.RenameColumn(
                name: "IsActive",
                table: "religions",
                newName: "is_active");

            migrationBuilder.RenameColumn(
                name: "CreatedAtUtc",
                table: "religions",
                newName: "created_at_utc");

            migrationBuilder.RenameIndex(
                name: "IX_Religions_Name",
                table: "religions",
                newName: "ix_religions_name");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "rank_hierarchy",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "rank_hierarchy",
                newName: "updated_at_utc");

            migrationBuilder.RenameColumn(
                name: "SrNo",
                table: "rank_hierarchy",
                newName: "sr_no");

            migrationBuilder.RenameColumn(
                name: "ShortCode",
                table: "rank_hierarchy",
                newName: "short_code");

            migrationBuilder.RenameColumn(
                name: "RankName",
                table: "rank_hierarchy",
                newName: "rank_name");

            migrationBuilder.RenameColumn(
                name: "IsActive",
                table: "rank_hierarchy",
                newName: "is_active");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "rank_hierarchy",
                newName: "created_at_utc");

            migrationBuilder.RenameIndex(
                name: "IX_rank_hierarchy_SrNo_RankName",
                table: "rank_hierarchy",
                newName: "ix_rank_hierarchy_sr_no_rank_name");

            migrationBuilder.RenameIndex(
                name: "IX_rank_hierarchy_SrNo",
                table: "rank_hierarchy",
                newName: "ix_rank_hierarchy_sr_no");

            migrationBuilder.RenameIndex(
                name: "IX_rank_hierarchy_RankName",
                table: "rank_hierarchy",
                newName: "ix_rank_hierarchy_rank_name");

            migrationBuilder.RenameIndex(
                name: "IX_rank_hierarchy_IsActive",
                table: "rank_hierarchy",
                newName: "ix_rank_hierarchy_is_active");

            migrationBuilder.RenameColumn(
                name: "Status",
                table: "devices",
                newName: "status");

            migrationBuilder.RenameColumn(
                name: "Platform",
                table: "devices",
                newName: "platform");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "devices",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "devices",
                newName: "user_id");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "devices",
                newName: "updated_at_utc");

            migrationBuilder.RenameColumn(
                name: "RevokedAt",
                table: "devices",
                newName: "revoked_at_utc");

            migrationBuilder.RenameColumn(
                name: "PushToken",
                table: "devices",
                newName: "push_token");

            migrationBuilder.RenameColumn(
                name: "PublicKey",
                table: "devices",
                newName: "public_key");

            migrationBuilder.RenameColumn(
                name: "OsVersion",
                table: "devices",
                newName: "os_version");

            migrationBuilder.RenameColumn(
                name: "LastSeenAt",
                table: "devices",
                newName: "last_seen_at_utc");

            migrationBuilder.RenameColumn(
                name: "LastIpAddress",
                table: "devices",
                newName: "last_ip_address");

            migrationBuilder.RenameColumn(
                name: "DeviceName",
                table: "devices",
                newName: "device_name");

            migrationBuilder.RenameColumn(
                name: "DeviceIdentifierHash",
                table: "devices",
                newName: "device_identifier_hash");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "devices",
                newName: "created_at_utc");

            migrationBuilder.RenameColumn(
                name: "AppVersion",
                table: "devices",
                newName: "app_version");

            migrationBuilder.RenameIndex(
                name: "IX_Devices_UserId_Status",
                table: "devices",
                newName: "ix_devices_user_id_status");

            migrationBuilder.RenameIndex(
                name: "IX_Devices_UserId_DeviceHash",
                table: "devices",
                newName: "ix_devices_user_id_device_hash");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "cities",
                newName: "name");

            migrationBuilder.RenameColumn(
                name: "Code",
                table: "cities",
                newName: "code");

            migrationBuilder.RenameColumn(
                name: "Classification",
                table: "cities",
                newName: "classification");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "cities",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UpdatedAtUtc",
                table: "cities",
                newName: "updated_at_utc");

            migrationBuilder.RenameColumn(
                name: "StateId",
                table: "cities",
                newName: "state_id");

            migrationBuilder.RenameColumn(
                name: "SourceSystem",
                table: "cities",
                newName: "source_system");

            migrationBuilder.RenameColumn(
                name: "SourceKey",
                table: "cities",
                newName: "source_key");

            migrationBuilder.RenameColumn(
                name: "IsActive",
                table: "cities",
                newName: "is_active");

            migrationBuilder.RenameColumn(
                name: "CreatedAtUtc",
                table: "cities",
                newName: "created_at_utc");

            migrationBuilder.RenameIndex(
                name: "IX_Cities_StateId_Name",
                table: "cities",
                newName: "ix_cities_state_id_name");

            migrationBuilder.RenameIndex(
                name: "IX_Cities_StateId_IsActive",
                table: "cities",
                newName: "ix_cities_state_id_is_active");

            migrationBuilder.RenameIndex(
                name: "IX_Cities_SourceSystem_SourceKey",
                table: "cities",
                newName: "ix_cities_source_system_source_key");

            migrationBuilder.RenameColumn(
                name: "Category",
                table: "users_pis",
                newName: "category");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "users_pis",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "WeightKg",
                table: "users_pis",
                newName: "weight_kg");

            migrationBuilder.RenameColumn(
                name: "UpdatedBy",
                table: "users_pis",
                newName: "updated_by");

            migrationBuilder.RenameColumn(
                name: "UpdatedAtUtc",
                table: "users_pis",
                newName: "updated_at_utc");

            migrationBuilder.RenameColumn(
                name: "SpouseName",
                table: "users_pis",
                newName: "spouse_name");

            migrationBuilder.RenameColumn(
                name: "SideSteppedOn",
                table: "users_pis",
                newName: "side_stepped_on");

            migrationBuilder.RenameColumn(
                name: "SideSteppedFromNsgUnitId",
                table: "users_pis",
                newName: "side_stepped_from_nsg_unit_id");

            migrationBuilder.RenameColumn(
                name: "ServiceNumber",
                table: "users_pis",
                newName: "service_number");

            migrationBuilder.RenameColumn(
                name: "SalaryAccountNumberHash",
                table: "users_pis",
                newName: "salary_account_number_hash");

            migrationBuilder.RenameColumn(
                name: "SalaryAccountNumberEncrypted",
                table: "users_pis",
                newName: "salary_account_number_encrypted");

            migrationBuilder.RenameColumn(
                name: "ReligionId",
                table: "users_pis",
                newName: "religion_id");

            migrationBuilder.RenameColumn(
                name: "PranNumberEncrypted",
                table: "users_pis",
                newName: "pran_number_encrypted");

            migrationBuilder.RenameColumn(
                name: "PermanentVillage",
                table: "users_pis",
                newName: "permanent_village");

            migrationBuilder.RenameColumn(
                name: "PermanentStateId",
                table: "users_pis",
                newName: "permanent_state_id");

            migrationBuilder.RenameColumn(
                name: "PermanentPostOffice",
                table: "users_pis",
                newName: "permanent_post_office");

            migrationBuilder.RenameColumn(
                name: "PermanentPoliceStation",
                table: "users_pis",
                newName: "permanent_police_station");

            migrationBuilder.RenameColumn(
                name: "PermanentPinCode",
                table: "users_pis",
                newName: "permanent_pin_code");

            migrationBuilder.RenameColumn(
                name: "PermanentDistrict",
                table: "users_pis",
                newName: "permanent_district");

            migrationBuilder.RenameColumn(
                name: "PaternityLeaveAvailed",
                table: "users_pis",
                newName: "paternity_leave_availed");

            migrationBuilder.RenameColumn(
                name: "ParentDepartmentRankId",
                table: "users_pis",
                newName: "parent_department_rank_id");

            migrationBuilder.RenameColumn(
                name: "PanNumberHash",
                table: "users_pis",
                newName: "pan_number_hash");

            migrationBuilder.RenameColumn(
                name: "PanNumberEncrypted",
                table: "users_pis",
                newName: "pan_number_encrypted");

            migrationBuilder.RenameColumn(
                name: "NumberOfChildren",
                table: "users_pis",
                newName: "number_of_children");

            migrationBuilder.RenameColumn(
                name: "NsgUnitId",
                table: "users_pis",
                newName: "nsg_unit_id");

            migrationBuilder.RenameColumn(
                name: "NsgRankId",
                table: "users_pis",
                newName: "nsg_rank_id");

            migrationBuilder.RenameColumn(
                name: "NsgNumber",
                table: "users_pis",
                newName: "nsg_number");

            migrationBuilder.RenameColumn(
                name: "NextOfKinMobileNumber",
                table: "users_pis",
                newName: "next_of_kin_mobile_number");

            migrationBuilder.RenameColumn(
                name: "MotherName",
                table: "users_pis",
                newName: "mother_name");

            migrationBuilder.RenameColumn(
                name: "MobileNumber",
                table: "users_pis",
                newName: "mobile_number");

            migrationBuilder.RenameColumn(
                name: "MobileAppActivationStatus",
                table: "users_pis",
                newName: "mobile_app_activation_status");

            migrationBuilder.RenameColumn(
                name: "MaternityLeaveAvailed",
                table: "users_pis",
                newName: "maternity_leave_availed");

            migrationBuilder.RenameColumn(
                name: "IsActive",
                table: "users_pis",
                newName: "is_active");

            migrationBuilder.RenameColumn(
                name: "HeightCm",
                table: "users_pis",
                newName: "height_cm");

            migrationBuilder.RenameColumn(
                name: "HalfPayLeaveDue",
                table: "users_pis",
                newName: "half_pay_leave_due");

            migrationBuilder.RenameColumn(
                name: "FullName",
                table: "users_pis",
                newName: "full_name");

            migrationBuilder.RenameColumn(
                name: "FatherName",
                table: "users_pis",
                newName: "father_name");

            migrationBuilder.RenameColumn(
                name: "EducationId",
                table: "users_pis",
                newName: "education_id");

            migrationBuilder.RenameColumn(
                name: "EarnedLeaveDue",
                table: "users_pis",
                newName: "earned_leave_due");

            migrationBuilder.RenameColumn(
                name: "DateOfSuperannuation",
                table: "users_pis",
                newName: "date_of_superannuation");

            migrationBuilder.RenameColumn(
                name: "DateOfRepatriation",
                table: "users_pis",
                newName: "date_of_repatriation");

            migrationBuilder.RenameColumn(
                name: "DateOfInductionInNsg",
                table: "users_pis",
                newName: "date_of_induction_in_nsg");

            migrationBuilder.RenameColumn(
                name: "DateOfImmovablePropertyReturn",
                table: "users_pis",
                newName: "date_of_immovable_property_return");

            migrationBuilder.RenameColumn(
                name: "DateOfEnrollmentInGovtService",
                table: "users_pis",
                newName: "date_of_enrollment_in_govt_service");

            migrationBuilder.RenameColumn(
                name: "DateOfBirth",
                table: "users_pis",
                newName: "date_of_birth");

            migrationBuilder.RenameColumn(
                name: "DateOfAme",
                table: "users_pis",
                newName: "date_of_ame");

            migrationBuilder.RenameColumn(
                name: "CreatedBy",
                table: "users_pis",
                newName: "created_by");

            migrationBuilder.RenameColumn(
                name: "CreatedAtUtc",
                table: "users_pis",
                newName: "created_at_utc");

            migrationBuilder.RenameColumn(
                name: "ChildCareLeaveAvailed",
                table: "users_pis",
                newName: "child_care_leave_availed");

            migrationBuilder.RenameColumn(
                name: "BloodGroup",
                table: "users_pis",
                newName: "blood_group");

            migrationBuilder.RenameColumn(
                name: "AadhaarNumberEncrypted",
                table: "users_pis",
                newName: "aadhaar_number_encrypted");

            migrationBuilder.RenameIndex(
                name: "IX_UsersPIS_SideSteppedFromNsgUnitId",
                table: "users_pis",
                newName: "ix_users_pis_side_stepped_from_nsg_unit_id");

            migrationBuilder.RenameIndex(
                name: "IX_UsersPIS_ServiceNumber",
                table: "users_pis",
                newName: "ix_users_pis_service_number");

            migrationBuilder.RenameIndex(
                name: "IX_UsersPIS_SalaryAccountNumberHash",
                table: "users_pis",
                newName: "ix_users_pis_salary_account_number_hash");

            migrationBuilder.RenameIndex(
                name: "IX_UsersPIS_ReligionId",
                table: "users_pis",
                newName: "ix_users_pis_religion_id");

            migrationBuilder.RenameIndex(
                name: "IX_UsersPIS_PermanentStateId",
                table: "users_pis",
                newName: "ix_users_pis_permanent_state_id");

            migrationBuilder.RenameIndex(
                name: "IX_UsersPIS_ParentDepartmentRankId",
                table: "users_pis",
                newName: "ix_users_pis_parent_department_rank_id");

            migrationBuilder.RenameIndex(
                name: "IX_UsersPIS_PanNumberHash",
                table: "users_pis",
                newName: "ix_users_pis_pan_number_hash");

            migrationBuilder.RenameIndex(
                name: "IX_UsersPIS_NsgUnitId",
                table: "users_pis",
                newName: "ix_users_pis_nsg_unit_id");

            migrationBuilder.RenameIndex(
                name: "IX_UsersPIS_NsgRankId",
                table: "users_pis",
                newName: "ix_users_pis_nsg_rank_id");

            migrationBuilder.RenameIndex(
                name: "IX_UsersPIS_NsgNumber",
                table: "users_pis",
                newName: "ix_users_pis_nsg_number");

            migrationBuilder.RenameIndex(
                name: "IX_UsersPIS_MobileNumber",
                table: "users_pis",
                newName: "ix_users_pis_mobile_number");

            migrationBuilder.RenameIndex(
                name: "IX_UsersPIS_IsActive",
                table: "users_pis",
                newName: "ix_users_pis_is_active");

            migrationBuilder.RenameIndex(
                name: "IX_UsersPIS_EducationId",
                table: "users_pis",
                newName: "ix_users_pis_education_id");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "refresh_tokens",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "refresh_tokens",
                newName: "user_id");

            migrationBuilder.RenameColumn(
                name: "TokenHash",
                table: "refresh_tokens",
                newName: "token_hash");

            migrationBuilder.RenameColumn(
                name: "RevokedByIp",
                table: "refresh_tokens",
                newName: "revoked_by_ip");

            migrationBuilder.RenameColumn(
                name: "RevokedAt",
                table: "refresh_tokens",
                newName: "revoked_at_utc");

            migrationBuilder.RenameColumn(
                name: "RevokeReason",
                table: "refresh_tokens",
                newName: "revoke_reason");

            migrationBuilder.RenameColumn(
                name: "ReuseDetectedAt",
                table: "refresh_tokens",
                newName: "reuse_detected_at_utc");

            migrationBuilder.RenameColumn(
                name: "ReplacedByTokenId",
                table: "refresh_tokens",
                newName: "replaced_by_token_id");

            migrationBuilder.RenameColumn(
                name: "FamilyId",
                table: "refresh_tokens",
                newName: "family_id");

            migrationBuilder.RenameColumn(
                name: "ExpiresAt",
                table: "refresh_tokens",
                newName: "expires_at_utc");

            migrationBuilder.RenameColumn(
                name: "DeviceId",
                table: "refresh_tokens",
                newName: "device_id");

            migrationBuilder.RenameColumn(
                name: "CreatedByIp",
                table: "refresh_tokens",
                newName: "created_by_ip");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "refresh_tokens",
                newName: "created_at_utc");

            migrationBuilder.RenameIndex(
                name: "IX_RefreshTokens_UserId_RevokedAt",
                table: "refresh_tokens",
                newName: "ix_refresh_tokens_user_id_revoked_at");

            migrationBuilder.RenameIndex(
                name: "IX_RefreshTokens_TokenHash",
                table: "refresh_tokens",
                newName: "ix_refresh_tokens_token_hash");

            migrationBuilder.RenameIndex(
                name: "IX_RefreshTokens_ReplacedByTokenId",
                table: "refresh_tokens",
                newName: "ix_refresh_tokens_replaced_by_token_id");

            migrationBuilder.RenameIndex(
                name: "IX_RefreshTokens_FamilyId",
                table: "refresh_tokens",
                newName: "ix_refresh_tokens_family_id");

            migrationBuilder.RenameIndex(
                name: "IX_RefreshTokens_DeviceId",
                table: "refresh_tokens",
                newName: "ix_refresh_tokens_device_id");

            migrationBuilder.RenameIndex(
                name: "ix_pd_trades_pd_trade_name",
                table: "pd_trade",
                newName: "ix_pd_trade_pd_trade_name");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "parent_department_ranks",
                newName: "name");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "parent_department_ranks",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UpdatedAtUtc",
                table: "parent_department_ranks",
                newName: "updated_at_utc");

            migrationBuilder.RenameColumn(
                name: "IsActive",
                table: "parent_department_ranks",
                newName: "is_active");

            migrationBuilder.RenameColumn(
                name: "CreatedAtUtc",
                table: "parent_department_ranks",
                newName: "created_at_utc");

            migrationBuilder.RenameIndex(
                name: "IX_ParentDepartmentRanks_Name",
                table: "parent_department_ranks",
                newName: "ix_parent_department_ranks_name");

            migrationBuilder.RenameColumn(
                name: "Purpose",
                table: "otp_requests",
                newName: "purpose");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "otp_requests",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "VerifiedAt",
                table: "otp_requests",
                newName: "verified_at_utc");

            migrationBuilder.RenameColumn(
                name: "ReferenceId",
                table: "otp_requests",
                newName: "reference_id");

            migrationBuilder.RenameColumn(
                name: "OtpHash",
                table: "otp_requests",
                newName: "otp_hash");

            migrationBuilder.RenameColumn(
                name: "MobileNumber",
                table: "otp_requests",
                newName: "mobile_number");

            migrationBuilder.RenameColumn(
                name: "IsVerified",
                table: "otp_requests",
                newName: "is_verified");

            migrationBuilder.RenameColumn(
                name: "ExpiresAt",
                table: "otp_requests",
                newName: "expires_at_utc");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "otp_requests",
                newName: "created_at_utc");

            migrationBuilder.RenameColumn(
                name: "AttemptCount",
                table: "otp_requests",
                newName: "attempt_count");

            migrationBuilder.RenameIndex(
                name: "IX_OtpRequests_ReferenceId",
                table: "otp_requests",
                newName: "ix_otp_requests_reference_id");

            migrationBuilder.RenameIndex(
                name: "IX_OtpRequests_Mobile_Purpose_Created",
                table: "otp_requests",
                newName: "ix_otp_requests_mobile_purpose_created");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "nsg_units",
                newName: "name");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "nsg_units",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UpdatedAtUtc",
                table: "nsg_units",
                newName: "updated_at_utc");

            migrationBuilder.RenameColumn(
                name: "IsActive",
                table: "nsg_units",
                newName: "is_active");

            migrationBuilder.RenameColumn(
                name: "CreatedAtUtc",
                table: "nsg_units",
                newName: "created_at_utc");

            migrationBuilder.RenameIndex(
                name: "IX_NsgUnits_Name",
                table: "nsg_units",
                newName: "ix_nsg_units_name");

            migrationBuilder.RenameIndex(
                name: "ix_nsg_trades_nsg_trade_name",
                table: "nsg_trade",
                newName: "ix_nsg_trade_nsg_trade_name");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "education_qualifications",
                newName: "name");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "education_qualifications",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UpdatedAtUtc",
                table: "education_qualifications",
                newName: "updated_at_utc");

            migrationBuilder.RenameColumn(
                name: "IsActive",
                table: "education_qualifications",
                newName: "is_active");

            migrationBuilder.RenameColumn(
                name: "CreatedAtUtc",
                table: "education_qualifications",
                newName: "created_at_utc");

            migrationBuilder.RenameIndex(
                name: "IX_EducationQualifications_Name",
                table: "education_qualifications",
                newName: "ix_education_qualifications_name");

            migrationBuilder.RenameColumn(
                name: "Result",
                table: "audit_logs",
                newName: "result");

            migrationBuilder.RenameColumn(
                name: "Action",
                table: "audit_logs",
                newName: "action");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "audit_logs",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "audit_logs",
                newName: "user_id");

            migrationBuilder.RenameColumn(
                name: "IpAddress",
                table: "audit_logs",
                newName: "ip_address");

            migrationBuilder.RenameColumn(
                name: "EntityType",
                table: "audit_logs",
                newName: "entity_type");

            migrationBuilder.RenameColumn(
                name: "EntityId",
                table: "audit_logs",
                newName: "entity_id");

            migrationBuilder.RenameColumn(
                name: "DeviceId",
                table: "audit_logs",
                newName: "device_id");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "audit_logs",
                newName: "created_at_utc");

            migrationBuilder.RenameColumn(
                name: "CorrelationId",
                table: "audit_logs",
                newName: "correlation_id");

            migrationBuilder.RenameColumn(
                name: "AdditionalData",
                table: "audit_logs",
                newName: "additional_data");

            migrationBuilder.RenameIndex(
                name: "IX_AuditLogs_UserId_CreatedAt",
                table: "audit_logs",
                newName: "ix_audit_logs_user_id_created_at");

            migrationBuilder.RenameIndex(
                name: "IX_AuditLogs_CreatedAt",
                table: "audit_logs",
                newName: "ix_audit_logs_created_at");

            migrationBuilder.RenameIndex(
                name: "IX_AuditLogs_Action",
                table: "audit_logs",
                newName: "ix_audit_logs_action");

            migrationBuilder.AddCheckConstraint(
                name: "ck_states_administrative_type",
                table: "states",
                sql: "`administrative_type` IN (1, 2)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_pis_blood_group",
                table: "users_pis",
                sql: "`blood_group` IS NULL OR `blood_group` BETWEEN 1 AND 8");

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_pis_category",
                table: "users_pis",
                sql: "`category` IS NULL OR `category` IN (1, 2, 3, 4)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_pis_enrollment_after_birth",
                table: "users_pis",
                sql: "`date_of_enrollment_in_govt_service` IS NULL OR `date_of_birth` IS NULL OR `date_of_enrollment_in_govt_service` >= `date_of_birth`");

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_pis_hashes_format",
                table: "users_pis",
                sql: "(`pan_number_hash` IS NULL OR `pan_number_hash` REGEXP '^[0-9a-f]{64}$') AND (`salary_account_number_hash` IS NULL OR `salary_account_number_hash` REGEXP '^[0-9a-f]{64}$')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_pis_mobile_app_activation_status",
                table: "users_pis",
                sql: "`mobile_app_activation_status` IN (0, 1, 2)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_pis_mobile_number_format",
                table: "users_pis",
                sql: "`mobile_number` IS NULL OR `mobile_number` REGEXP '^[6-9][0-9]{9}$'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_pis_numbers",
                table: "users_pis",
                sql: "(`height_cm` IS NULL OR `height_cm` > 0) AND (`weight_kg` IS NULL OR `weight_kg` > 0) AND (`number_of_children` IS NULL OR `number_of_children` >= 0) AND (`earned_leave_due` IS NULL OR `earned_leave_due` >= 0) AND (`half_pay_leave_due` IS NULL OR `half_pay_leave_due` >= 0)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_pis_pin_code_format",
                table: "users_pis",
                sql: "`permanent_pin_code` IS NULL OR `permanent_pin_code` REGEXP '^[1-9][0-9]{5}$'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_pis_repatriation_after_induction",
                table: "users_pis",
                sql: "`date_of_repatriation` IS NULL OR `date_of_induction_in_nsg` IS NULL OR `date_of_repatriation` >= `date_of_induction_in_nsg`");

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_pis_superannuation_after_birth",
                table: "users_pis",
                sql: "`date_of_superannuation` IS NULL OR `date_of_birth` IS NULL OR `date_of_superannuation` > `date_of_birth`");

            migrationBuilder.AddForeignKey(
                name: "fk_cities_states_state_id",
                table: "cities",
                column: "state_id",
                principalTable: "states",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_devices_users_user_id",
                table: "devices",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_refresh_tokens_devices_device_id",
                table: "refresh_tokens",
                column: "device_id",
                principalTable: "devices",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_refresh_tokens_refresh_tokens_replaced_by_token_id",
                table: "refresh_tokens",
                column: "replaced_by_token_id",
                principalTable: "refresh_tokens",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_refresh_tokens_users_user_id",
                table: "refresh_tokens",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_users_pis_education_qualifications_education_id",
                table: "users_pis",
                column: "education_id",
                principalTable: "education_qualifications",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_users_pis_nsg_trade_nsg_trade_id",
                table: "users_pis",
                column: "nsg_trade_id",
                principalTable: "nsg_trade",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_users_pis_nsg_units_nsg_unit_id",
                table: "users_pis",
                column: "nsg_unit_id",
                principalTable: "nsg_units",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_users_pis_nsg_units_side_stepped_from_nsg_unit_id",
                table: "users_pis",
                column: "side_stepped_from_nsg_unit_id",
                principalTable: "nsg_units",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_users_pis_parent_department_ranks_parent_department_rank_id",
                table: "users_pis",
                column: "parent_department_rank_id",
                principalTable: "parent_department_ranks",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_users_pis_pd_trade_pd_trade_id",
                table: "users_pis",
                column: "pd_trade_id",
                principalTable: "pd_trade",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_users_pis_rank_hierarchy_nsg_rank_id",
                table: "users_pis",
                column: "nsg_rank_id",
                principalTable: "rank_hierarchy",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_users_pis_religions_religion_id",
                table: "users_pis",
                column: "religion_id",
                principalTable: "religions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_users_pis_states_permanent_state_id",
                table: "users_pis",
                column: "permanent_state_id",
                principalTable: "states",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddCheckConstraint(
                name: "ck_states_code_format",
                table: "states",
                sql: "`code` IS NULL OR `code` REGEXP '^[0-9]{2}$'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_states_code_format",
                table: "states");

            migrationBuilder.DropForeignKey(
                name: "fk_cities_states_state_id",
                table: "cities");

            migrationBuilder.DropForeignKey(
                name: "fk_devices_users_user_id",
                table: "devices");

            migrationBuilder.DropForeignKey(
                name: "fk_refresh_tokens_devices_device_id",
                table: "refresh_tokens");

            migrationBuilder.DropForeignKey(
                name: "fk_refresh_tokens_refresh_tokens_replaced_by_token_id",
                table: "refresh_tokens");

            migrationBuilder.DropForeignKey(
                name: "fk_refresh_tokens_users_user_id",
                table: "refresh_tokens");

            migrationBuilder.DropForeignKey(
                name: "fk_users_pis_education_qualifications_education_id",
                table: "users_pis");

            migrationBuilder.DropForeignKey(
                name: "fk_users_pis_nsg_trade_nsg_trade_id",
                table: "users_pis");

            migrationBuilder.DropForeignKey(
                name: "fk_users_pis_nsg_units_nsg_unit_id",
                table: "users_pis");

            migrationBuilder.DropForeignKey(
                name: "fk_users_pis_nsg_units_side_stepped_from_nsg_unit_id",
                table: "users_pis");

            migrationBuilder.DropForeignKey(
                name: "fk_users_pis_parent_department_ranks_parent_department_rank_id",
                table: "users_pis");

            migrationBuilder.DropForeignKey(
                name: "fk_users_pis_pd_trade_pd_trade_id",
                table: "users_pis");

            migrationBuilder.DropForeignKey(
                name: "fk_users_pis_rank_hierarchy_nsg_rank_id",
                table: "users_pis");

            migrationBuilder.DropForeignKey(
                name: "fk_users_pis_religions_religion_id",
                table: "users_pis");

            migrationBuilder.DropForeignKey(
                name: "fk_users_pis_states_permanent_state_id",
                table: "users_pis");

            migrationBuilder.DropCheckConstraint(
                name: "ck_states_administrative_type",
                table: "states");

            migrationBuilder.DropCheckConstraint(
                name: "ck_users_pis_blood_group",
                table: "users_pis");

            migrationBuilder.DropCheckConstraint(
                name: "ck_users_pis_category",
                table: "users_pis");

            migrationBuilder.DropCheckConstraint(
                name: "ck_users_pis_enrollment_after_birth",
                table: "users_pis");

            migrationBuilder.DropCheckConstraint(
                name: "ck_users_pis_hashes_format",
                table: "users_pis");

            migrationBuilder.DropCheckConstraint(
                name: "ck_users_pis_mobile_app_activation_status",
                table: "users_pis");

            migrationBuilder.DropCheckConstraint(
                name: "ck_users_pis_mobile_number_format",
                table: "users_pis");

            migrationBuilder.DropCheckConstraint(
                name: "ck_users_pis_numbers",
                table: "users_pis");

            migrationBuilder.DropCheckConstraint(
                name: "ck_users_pis_pin_code_format",
                table: "users_pis");

            migrationBuilder.DropCheckConstraint(
                name: "ck_users_pis_repatriation_after_induction",
                table: "users_pis");

            migrationBuilder.DropCheckConstraint(
                name: "ck_users_pis_superannuation_after_birth",
                table: "users_pis");

            migrationBuilder.RenameTable(
                name: "users",
                newName: "Users");

            migrationBuilder.RenameTable(
                name: "states",
                newName: "States");

            migrationBuilder.RenameTable(
                name: "religions",
                newName: "Religions");

            migrationBuilder.RenameTable(
                name: "devices",
                newName: "Devices");

            migrationBuilder.RenameTable(
                name: "cities",
                newName: "Cities");

            migrationBuilder.RenameTable(
                name: "users_pis",
                newName: "UsersPIS");

            migrationBuilder.RenameTable(
                name: "refresh_tokens",
                newName: "RefreshTokens");

            migrationBuilder.RenameTable(
                name: "pd_trade",
                newName: "pd_trades");

            migrationBuilder.RenameTable(
                name: "parent_department_ranks",
                newName: "ParentDepartmentRanks");

            migrationBuilder.RenameTable(
                name: "otp_requests",
                newName: "OtpRequests");

            migrationBuilder.RenameTable(
                name: "nsg_units",
                newName: "NsgUnits");

            migrationBuilder.RenameTable(
                name: "nsg_trade",
                newName: "nsg_trades");

            migrationBuilder.RenameTable(
                name: "education_qualifications",
                newName: "EducationQualifications");

            migrationBuilder.RenameTable(
                name: "audit_logs",
                newName: "AuditLogs");

            migrationBuilder.RenameColumn(
                name: "status",
                table: "Users",
                newName: "Status");

            migrationBuilder.RenameColumn(
                name: "role",
                table: "Users",
                newName: "Role");

            migrationBuilder.RenameColumn(
                name: "name",
                table: "Users",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "Users",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "updated_at_utc",
                table: "Users",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "row_version",
                table: "Users",
                newName: "RowVersion");

            migrationBuilder.RenameColumn(
                name: "rank_name",
                table: "Users",
                newName: "Rank");

            migrationBuilder.RenameColumn(
                name: "password_hash",
                table: "Users",
                newName: "PasswordHash");

            migrationBuilder.RenameColumn(
                name: "mobile_number",
                table: "Users",
                newName: "MobileNumber");

            migrationBuilder.RenameColumn(
                name: "lockout_end_at_utc",
                table: "Users",
                newName: "LockoutEndAt");

            migrationBuilder.RenameColumn(
                name: "last_login_at_utc",
                table: "Users",
                newName: "LastLoginAt");

            migrationBuilder.RenameColumn(
                name: "is_active",
                table: "Users",
                newName: "IsActive");

            migrationBuilder.RenameColumn(
                name: "force_number",
                table: "Users",
                newName: "ForceNumber");

            migrationBuilder.RenameColumn(
                name: "failed_login_attempts",
                table: "Users",
                newName: "FailedLoginAttempts");

            migrationBuilder.RenameColumn(
                name: "created_at_utc",
                table: "Users",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "approved_by",
                table: "Users",
                newName: "ApprovedBy");

            migrationBuilder.RenameColumn(
                name: "approved_at_utc",
                table: "Users",
                newName: "ApprovedAt");

            migrationBuilder.RenameIndex(
                name: "ix_users_status",
                table: "Users",
                newName: "IX_Users_Status");

            migrationBuilder.RenameIndex(
                name: "ix_users_mobile_number",
                table: "Users",
                newName: "IX_Users_MobileNumber");

            migrationBuilder.RenameIndex(
                name: "ix_users_force_number",
                table: "Users",
                newName: "IX_Users_ForceNumber");

            migrationBuilder.RenameColumn(
                name: "name",
                table: "States",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "code",
                table: "States",
                newName: "Code");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "States",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "updated_at_utc",
                table: "States",
                newName: "UpdatedAtUtc");

            migrationBuilder.RenameColumn(
                name: "is_active",
                table: "States",
                newName: "IsActive");

            migrationBuilder.RenameColumn(
                name: "created_at_utc",
                table: "States",
                newName: "CreatedAtUtc");

            migrationBuilder.RenameColumn(
                name: "administrative_type",
                table: "States",
                newName: "AdministrativeType");

            migrationBuilder.RenameIndex(
                name: "ix_states_name",
                table: "States",
                newName: "IX_States_Name");

            migrationBuilder.RenameIndex(
                name: "ix_states_code",
                table: "States",
                newName: "IX_States_Code");

            migrationBuilder.RenameIndex(
                name: "ix_states_administrative_type_is_active",
                table: "States",
                newName: "IX_States_AdministrativeType_IsActive");

            migrationBuilder.RenameColumn(
                name: "name",
                table: "Religions",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "Religions",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "updated_at_utc",
                table: "Religions",
                newName: "UpdatedAtUtc");

            migrationBuilder.RenameColumn(
                name: "is_active",
                table: "Religions",
                newName: "IsActive");

            migrationBuilder.RenameColumn(
                name: "created_at_utc",
                table: "Religions",
                newName: "CreatedAtUtc");

            migrationBuilder.RenameIndex(
                name: "ix_religions_name",
                table: "Religions",
                newName: "IX_Religions_Name");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "rank_hierarchy",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "updated_at_utc",
                table: "rank_hierarchy",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "sr_no",
                table: "rank_hierarchy",
                newName: "SrNo");

            migrationBuilder.RenameColumn(
                name: "short_code",
                table: "rank_hierarchy",
                newName: "ShortCode");

            migrationBuilder.RenameColumn(
                name: "rank_name",
                table: "rank_hierarchy",
                newName: "RankName");

            migrationBuilder.RenameColumn(
                name: "is_active",
                table: "rank_hierarchy",
                newName: "IsActive");

            migrationBuilder.RenameColumn(
                name: "created_at_utc",
                table: "rank_hierarchy",
                newName: "CreatedAt");

            migrationBuilder.RenameIndex(
                name: "ix_rank_hierarchy_sr_no_rank_name",
                table: "rank_hierarchy",
                newName: "IX_rank_hierarchy_SrNo_RankName");

            migrationBuilder.RenameIndex(
                name: "ix_rank_hierarchy_sr_no",
                table: "rank_hierarchy",
                newName: "IX_rank_hierarchy_SrNo");

            migrationBuilder.RenameIndex(
                name: "ix_rank_hierarchy_rank_name",
                table: "rank_hierarchy",
                newName: "IX_rank_hierarchy_RankName");

            migrationBuilder.RenameIndex(
                name: "ix_rank_hierarchy_is_active",
                table: "rank_hierarchy",
                newName: "IX_rank_hierarchy_IsActive");

            migrationBuilder.RenameColumn(
                name: "status",
                table: "Devices",
                newName: "Status");

            migrationBuilder.RenameColumn(
                name: "platform",
                table: "Devices",
                newName: "Platform");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "Devices",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "user_id",
                table: "Devices",
                newName: "UserId");

            migrationBuilder.RenameColumn(
                name: "updated_at_utc",
                table: "Devices",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "revoked_at_utc",
                table: "Devices",
                newName: "RevokedAt");

            migrationBuilder.RenameColumn(
                name: "push_token",
                table: "Devices",
                newName: "PushToken");

            migrationBuilder.RenameColumn(
                name: "public_key",
                table: "Devices",
                newName: "PublicKey");

            migrationBuilder.RenameColumn(
                name: "os_version",
                table: "Devices",
                newName: "OsVersion");

            migrationBuilder.RenameColumn(
                name: "last_seen_at_utc",
                table: "Devices",
                newName: "LastSeenAt");

            migrationBuilder.RenameColumn(
                name: "last_ip_address",
                table: "Devices",
                newName: "LastIpAddress");

            migrationBuilder.RenameColumn(
                name: "device_name",
                table: "Devices",
                newName: "DeviceName");

            migrationBuilder.RenameColumn(
                name: "device_identifier_hash",
                table: "Devices",
                newName: "DeviceIdentifierHash");

            migrationBuilder.RenameColumn(
                name: "created_at_utc",
                table: "Devices",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "app_version",
                table: "Devices",
                newName: "AppVersion");

            migrationBuilder.RenameIndex(
                name: "ix_devices_user_id_status",
                table: "Devices",
                newName: "IX_Devices_UserId_Status");

            migrationBuilder.RenameIndex(
                name: "ix_devices_user_id_device_hash",
                table: "Devices",
                newName: "IX_Devices_UserId_DeviceHash");

            migrationBuilder.RenameColumn(
                name: "name",
                table: "Cities",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "code",
                table: "Cities",
                newName: "Code");

            migrationBuilder.RenameColumn(
                name: "classification",
                table: "Cities",
                newName: "Classification");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "Cities",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "updated_at_utc",
                table: "Cities",
                newName: "UpdatedAtUtc");

            migrationBuilder.RenameColumn(
                name: "state_id",
                table: "Cities",
                newName: "StateId");

            migrationBuilder.RenameColumn(
                name: "source_system",
                table: "Cities",
                newName: "SourceSystem");

            migrationBuilder.RenameColumn(
                name: "source_key",
                table: "Cities",
                newName: "SourceKey");

            migrationBuilder.RenameColumn(
                name: "is_active",
                table: "Cities",
                newName: "IsActive");

            migrationBuilder.RenameColumn(
                name: "created_at_utc",
                table: "Cities",
                newName: "CreatedAtUtc");

            migrationBuilder.RenameIndex(
                name: "ix_cities_state_id_name",
                table: "Cities",
                newName: "IX_Cities_StateId_Name");

            migrationBuilder.RenameIndex(
                name: "ix_cities_state_id_is_active",
                table: "Cities",
                newName: "IX_Cities_StateId_IsActive");

            migrationBuilder.RenameIndex(
                name: "ix_cities_source_system_source_key",
                table: "Cities",
                newName: "IX_Cities_SourceSystem_SourceKey");

            migrationBuilder.RenameColumn(
                name: "category",
                table: "UsersPIS",
                newName: "Category");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "UsersPIS",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "weight_kg",
                table: "UsersPIS",
                newName: "WeightKg");

            migrationBuilder.RenameColumn(
                name: "updated_by",
                table: "UsersPIS",
                newName: "UpdatedBy");

            migrationBuilder.RenameColumn(
                name: "updated_at_utc",
                table: "UsersPIS",
                newName: "UpdatedAtUtc");

            migrationBuilder.RenameColumn(
                name: "spouse_name",
                table: "UsersPIS",
                newName: "SpouseName");

            migrationBuilder.RenameColumn(
                name: "side_stepped_on",
                table: "UsersPIS",
                newName: "SideSteppedOn");

            migrationBuilder.RenameColumn(
                name: "side_stepped_from_nsg_unit_id",
                table: "UsersPIS",
                newName: "SideSteppedFromNsgUnitId");

            migrationBuilder.RenameColumn(
                name: "service_number",
                table: "UsersPIS",
                newName: "ServiceNumber");

            migrationBuilder.RenameColumn(
                name: "salary_account_number_hash",
                table: "UsersPIS",
                newName: "SalaryAccountNumberHash");

            migrationBuilder.RenameColumn(
                name: "salary_account_number_encrypted",
                table: "UsersPIS",
                newName: "SalaryAccountNumberEncrypted");

            migrationBuilder.RenameColumn(
                name: "religion_id",
                table: "UsersPIS",
                newName: "ReligionId");

            migrationBuilder.RenameColumn(
                name: "pran_number_encrypted",
                table: "UsersPIS",
                newName: "PranNumberEncrypted");

            migrationBuilder.RenameColumn(
                name: "permanent_village",
                table: "UsersPIS",
                newName: "PermanentVillage");

            migrationBuilder.RenameColumn(
                name: "permanent_state_id",
                table: "UsersPIS",
                newName: "PermanentStateId");

            migrationBuilder.RenameColumn(
                name: "permanent_post_office",
                table: "UsersPIS",
                newName: "PermanentPostOffice");

            migrationBuilder.RenameColumn(
                name: "permanent_police_station",
                table: "UsersPIS",
                newName: "PermanentPoliceStation");

            migrationBuilder.RenameColumn(
                name: "permanent_pin_code",
                table: "UsersPIS",
                newName: "PermanentPinCode");

            migrationBuilder.RenameColumn(
                name: "permanent_district",
                table: "UsersPIS",
                newName: "PermanentDistrict");

            migrationBuilder.RenameColumn(
                name: "paternity_leave_availed",
                table: "UsersPIS",
                newName: "PaternityLeaveAvailed");

            migrationBuilder.RenameColumn(
                name: "parent_department_rank_id",
                table: "UsersPIS",
                newName: "ParentDepartmentRankId");

            migrationBuilder.RenameColumn(
                name: "pan_number_hash",
                table: "UsersPIS",
                newName: "PanNumberHash");

            migrationBuilder.RenameColumn(
                name: "pan_number_encrypted",
                table: "UsersPIS",
                newName: "PanNumberEncrypted");

            migrationBuilder.RenameColumn(
                name: "number_of_children",
                table: "UsersPIS",
                newName: "NumberOfChildren");

            migrationBuilder.RenameColumn(
                name: "nsg_unit_id",
                table: "UsersPIS",
                newName: "NsgUnitId");

            migrationBuilder.RenameColumn(
                name: "nsg_rank_id",
                table: "UsersPIS",
                newName: "NsgRankId");

            migrationBuilder.RenameColumn(
                name: "nsg_number",
                table: "UsersPIS",
                newName: "NsgNumber");

            migrationBuilder.RenameColumn(
                name: "next_of_kin_mobile_number",
                table: "UsersPIS",
                newName: "NextOfKinMobileNumber");

            migrationBuilder.RenameColumn(
                name: "mother_name",
                table: "UsersPIS",
                newName: "MotherName");

            migrationBuilder.RenameColumn(
                name: "mobile_number",
                table: "UsersPIS",
                newName: "MobileNumber");

            migrationBuilder.RenameColumn(
                name: "mobile_app_activation_status",
                table: "UsersPIS",
                newName: "MobileAppActivationStatus");

            migrationBuilder.RenameColumn(
                name: "maternity_leave_availed",
                table: "UsersPIS",
                newName: "MaternityLeaveAvailed");

            migrationBuilder.RenameColumn(
                name: "is_active",
                table: "UsersPIS",
                newName: "IsActive");

            migrationBuilder.RenameColumn(
                name: "height_cm",
                table: "UsersPIS",
                newName: "HeightCm");

            migrationBuilder.RenameColumn(
                name: "half_pay_leave_due",
                table: "UsersPIS",
                newName: "HalfPayLeaveDue");

            migrationBuilder.RenameColumn(
                name: "full_name",
                table: "UsersPIS",
                newName: "FullName");

            migrationBuilder.RenameColumn(
                name: "father_name",
                table: "UsersPIS",
                newName: "FatherName");

            migrationBuilder.RenameColumn(
                name: "education_id",
                table: "UsersPIS",
                newName: "EducationId");

            migrationBuilder.RenameColumn(
                name: "earned_leave_due",
                table: "UsersPIS",
                newName: "EarnedLeaveDue");

            migrationBuilder.RenameColumn(
                name: "date_of_superannuation",
                table: "UsersPIS",
                newName: "DateOfSuperannuation");

            migrationBuilder.RenameColumn(
                name: "date_of_repatriation",
                table: "UsersPIS",
                newName: "DateOfRepatriation");

            migrationBuilder.RenameColumn(
                name: "date_of_induction_in_nsg",
                table: "UsersPIS",
                newName: "DateOfInductionInNsg");

            migrationBuilder.RenameColumn(
                name: "date_of_immovable_property_return",
                table: "UsersPIS",
                newName: "DateOfImmovablePropertyReturn");

            migrationBuilder.RenameColumn(
                name: "date_of_enrollment_in_govt_service",
                table: "UsersPIS",
                newName: "DateOfEnrollmentInGovtService");

            migrationBuilder.RenameColumn(
                name: "date_of_birth",
                table: "UsersPIS",
                newName: "DateOfBirth");

            migrationBuilder.RenameColumn(
                name: "date_of_ame",
                table: "UsersPIS",
                newName: "DateOfAme");

            migrationBuilder.RenameColumn(
                name: "created_by",
                table: "UsersPIS",
                newName: "CreatedBy");

            migrationBuilder.RenameColumn(
                name: "created_at_utc",
                table: "UsersPIS",
                newName: "CreatedAtUtc");

            migrationBuilder.RenameColumn(
                name: "child_care_leave_availed",
                table: "UsersPIS",
                newName: "ChildCareLeaveAvailed");

            migrationBuilder.RenameColumn(
                name: "blood_group",
                table: "UsersPIS",
                newName: "BloodGroup");

            migrationBuilder.RenameColumn(
                name: "aadhaar_number_encrypted",
                table: "UsersPIS",
                newName: "AadhaarNumberEncrypted");

            migrationBuilder.RenameIndex(
                name: "ix_users_pis_side_stepped_from_nsg_unit_id",
                table: "UsersPIS",
                newName: "IX_UsersPIS_SideSteppedFromNsgUnitId");

            migrationBuilder.RenameIndex(
                name: "ix_users_pis_service_number",
                table: "UsersPIS",
                newName: "IX_UsersPIS_ServiceNumber");

            migrationBuilder.RenameIndex(
                name: "ix_users_pis_salary_account_number_hash",
                table: "UsersPIS",
                newName: "IX_UsersPIS_SalaryAccountNumberHash");

            migrationBuilder.RenameIndex(
                name: "ix_users_pis_religion_id",
                table: "UsersPIS",
                newName: "IX_UsersPIS_ReligionId");

            migrationBuilder.RenameIndex(
                name: "ix_users_pis_permanent_state_id",
                table: "UsersPIS",
                newName: "IX_UsersPIS_PermanentStateId");

            migrationBuilder.RenameIndex(
                name: "ix_users_pis_parent_department_rank_id",
                table: "UsersPIS",
                newName: "IX_UsersPIS_ParentDepartmentRankId");

            migrationBuilder.RenameIndex(
                name: "ix_users_pis_pan_number_hash",
                table: "UsersPIS",
                newName: "IX_UsersPIS_PanNumberHash");

            migrationBuilder.RenameIndex(
                name: "ix_users_pis_nsg_unit_id",
                table: "UsersPIS",
                newName: "IX_UsersPIS_NsgUnitId");

            migrationBuilder.RenameIndex(
                name: "ix_users_pis_nsg_rank_id",
                table: "UsersPIS",
                newName: "IX_UsersPIS_NsgRankId");

            migrationBuilder.RenameIndex(
                name: "ix_users_pis_nsg_number",
                table: "UsersPIS",
                newName: "IX_UsersPIS_NsgNumber");

            migrationBuilder.RenameIndex(
                name: "ix_users_pis_mobile_number",
                table: "UsersPIS",
                newName: "IX_UsersPIS_MobileNumber");

            migrationBuilder.RenameIndex(
                name: "ix_users_pis_is_active",
                table: "UsersPIS",
                newName: "IX_UsersPIS_IsActive");

            migrationBuilder.RenameIndex(
                name: "ix_users_pis_education_id",
                table: "UsersPIS",
                newName: "IX_UsersPIS_EducationId");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "RefreshTokens",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "user_id",
                table: "RefreshTokens",
                newName: "UserId");

            migrationBuilder.RenameColumn(
                name: "token_hash",
                table: "RefreshTokens",
                newName: "TokenHash");

            migrationBuilder.RenameColumn(
                name: "revoked_by_ip",
                table: "RefreshTokens",
                newName: "RevokedByIp");

            migrationBuilder.RenameColumn(
                name: "revoked_at_utc",
                table: "RefreshTokens",
                newName: "RevokedAt");

            migrationBuilder.RenameColumn(
                name: "revoke_reason",
                table: "RefreshTokens",
                newName: "RevokeReason");

            migrationBuilder.RenameColumn(
                name: "reuse_detected_at_utc",
                table: "RefreshTokens",
                newName: "ReuseDetectedAt");

            migrationBuilder.RenameColumn(
                name: "replaced_by_token_id",
                table: "RefreshTokens",
                newName: "ReplacedByTokenId");

            migrationBuilder.RenameColumn(
                name: "family_id",
                table: "RefreshTokens",
                newName: "FamilyId");

            migrationBuilder.RenameColumn(
                name: "expires_at_utc",
                table: "RefreshTokens",
                newName: "ExpiresAt");

            migrationBuilder.RenameColumn(
                name: "device_id",
                table: "RefreshTokens",
                newName: "DeviceId");

            migrationBuilder.RenameColumn(
                name: "created_by_ip",
                table: "RefreshTokens",
                newName: "CreatedByIp");

            migrationBuilder.RenameColumn(
                name: "created_at_utc",
                table: "RefreshTokens",
                newName: "CreatedAt");

            migrationBuilder.RenameIndex(
                name: "ix_refresh_tokens_user_id_revoked_at",
                table: "RefreshTokens",
                newName: "IX_RefreshTokens_UserId_RevokedAt");

            migrationBuilder.RenameIndex(
                name: "ix_refresh_tokens_token_hash",
                table: "RefreshTokens",
                newName: "IX_RefreshTokens_TokenHash");

            migrationBuilder.RenameIndex(
                name: "ix_refresh_tokens_replaced_by_token_id",
                table: "RefreshTokens",
                newName: "IX_RefreshTokens_ReplacedByTokenId");

            migrationBuilder.RenameIndex(
                name: "ix_refresh_tokens_family_id",
                table: "RefreshTokens",
                newName: "IX_RefreshTokens_FamilyId");

            migrationBuilder.RenameIndex(
                name: "ix_refresh_tokens_device_id",
                table: "RefreshTokens",
                newName: "IX_RefreshTokens_DeviceId");

            migrationBuilder.RenameIndex(
                name: "ix_pd_trade_pd_trade_name",
                table: "pd_trades",
                newName: "ix_pd_trades_pd_trade_name");

            migrationBuilder.RenameColumn(
                name: "name",
                table: "ParentDepartmentRanks",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "ParentDepartmentRanks",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "updated_at_utc",
                table: "ParentDepartmentRanks",
                newName: "UpdatedAtUtc");

            migrationBuilder.RenameColumn(
                name: "is_active",
                table: "ParentDepartmentRanks",
                newName: "IsActive");

            migrationBuilder.RenameColumn(
                name: "created_at_utc",
                table: "ParentDepartmentRanks",
                newName: "CreatedAtUtc");

            migrationBuilder.RenameIndex(
                name: "ix_parent_department_ranks_name",
                table: "ParentDepartmentRanks",
                newName: "IX_ParentDepartmentRanks_Name");

            migrationBuilder.RenameColumn(
                name: "purpose",
                table: "OtpRequests",
                newName: "Purpose");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "OtpRequests",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "verified_at_utc",
                table: "OtpRequests",
                newName: "VerifiedAt");

            migrationBuilder.RenameColumn(
                name: "reference_id",
                table: "OtpRequests",
                newName: "ReferenceId");

            migrationBuilder.RenameColumn(
                name: "otp_hash",
                table: "OtpRequests",
                newName: "OtpHash");

            migrationBuilder.RenameColumn(
                name: "mobile_number",
                table: "OtpRequests",
                newName: "MobileNumber");

            migrationBuilder.RenameColumn(
                name: "is_verified",
                table: "OtpRequests",
                newName: "IsVerified");

            migrationBuilder.RenameColumn(
                name: "expires_at_utc",
                table: "OtpRequests",
                newName: "ExpiresAt");

            migrationBuilder.RenameColumn(
                name: "created_at_utc",
                table: "OtpRequests",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "attempt_count",
                table: "OtpRequests",
                newName: "AttemptCount");

            migrationBuilder.RenameIndex(
                name: "ix_otp_requests_reference_id",
                table: "OtpRequests",
                newName: "IX_OtpRequests_ReferenceId");

            migrationBuilder.RenameIndex(
                name: "ix_otp_requests_mobile_purpose_created",
                table: "OtpRequests",
                newName: "IX_OtpRequests_Mobile_Purpose_Created");

            migrationBuilder.RenameColumn(
                name: "name",
                table: "NsgUnits",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "NsgUnits",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "updated_at_utc",
                table: "NsgUnits",
                newName: "UpdatedAtUtc");

            migrationBuilder.RenameColumn(
                name: "is_active",
                table: "NsgUnits",
                newName: "IsActive");

            migrationBuilder.RenameColumn(
                name: "created_at_utc",
                table: "NsgUnits",
                newName: "CreatedAtUtc");

            migrationBuilder.RenameIndex(
                name: "ix_nsg_units_name",
                table: "NsgUnits",
                newName: "IX_NsgUnits_Name");

            migrationBuilder.RenameIndex(
                name: "ix_nsg_trade_nsg_trade_name",
                table: "nsg_trades",
                newName: "ix_nsg_trades_nsg_trade_name");

            migrationBuilder.RenameColumn(
                name: "name",
                table: "EducationQualifications",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "EducationQualifications",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "updated_at_utc",
                table: "EducationQualifications",
                newName: "UpdatedAtUtc");

            migrationBuilder.RenameColumn(
                name: "is_active",
                table: "EducationQualifications",
                newName: "IsActive");

            migrationBuilder.RenameColumn(
                name: "created_at_utc",
                table: "EducationQualifications",
                newName: "CreatedAtUtc");

            migrationBuilder.RenameIndex(
                name: "ix_education_qualifications_name",
                table: "EducationQualifications",
                newName: "IX_EducationQualifications_Name");

            migrationBuilder.RenameColumn(
                name: "result",
                table: "AuditLogs",
                newName: "Result");

            migrationBuilder.RenameColumn(
                name: "action",
                table: "AuditLogs",
                newName: "Action");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "AuditLogs",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "user_id",
                table: "AuditLogs",
                newName: "UserId");

            migrationBuilder.RenameColumn(
                name: "ip_address",
                table: "AuditLogs",
                newName: "IpAddress");

            migrationBuilder.RenameColumn(
                name: "entity_type",
                table: "AuditLogs",
                newName: "EntityType");

            migrationBuilder.RenameColumn(
                name: "entity_id",
                table: "AuditLogs",
                newName: "EntityId");

            migrationBuilder.RenameColumn(
                name: "device_id",
                table: "AuditLogs",
                newName: "DeviceId");

            migrationBuilder.RenameColumn(
                name: "created_at_utc",
                table: "AuditLogs",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "correlation_id",
                table: "AuditLogs",
                newName: "CorrelationId");

            migrationBuilder.RenameColumn(
                name: "additional_data",
                table: "AuditLogs",
                newName: "AdditionalData");

            migrationBuilder.RenameIndex(
                name: "ix_audit_logs_user_id_created_at",
                table: "AuditLogs",
                newName: "IX_AuditLogs_UserId_CreatedAt");

            migrationBuilder.RenameIndex(
                name: "ix_audit_logs_created_at",
                table: "AuditLogs",
                newName: "IX_AuditLogs_CreatedAt");

            migrationBuilder.RenameIndex(
                name: "ix_audit_logs_action",
                table: "AuditLogs",
                newName: "IX_AuditLogs_Action");

            migrationBuilder.AddCheckConstraint(
                name: "CK_States_AdministrativeType",
                table: "States",
                sql: "`AdministrativeType` IN (1, 2)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_UsersPIS_BloodGroup",
                table: "UsersPIS",
                sql: "`BloodGroup` IS NULL OR `BloodGroup` BETWEEN 1 AND 8");

            migrationBuilder.AddCheckConstraint(
                name: "CK_UsersPIS_Category",
                table: "UsersPIS",
                sql: "`Category` IS NULL OR `Category` IN (1, 2, 3, 4)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_UsersPIS_Enrollment_After_Birth",
                table: "UsersPIS",
                sql: "`DateOfEnrollmentInGovtService` IS NULL OR `DateOfBirth` IS NULL OR `DateOfEnrollmentInGovtService` >= `DateOfBirth`");

            migrationBuilder.AddCheckConstraint(
                name: "CK_UsersPIS_Hashes_Format",
                table: "UsersPIS",
                sql: "(`PanNumberHash` IS NULL OR `PanNumberHash` REGEXP '^[0-9a-f]{64}$') AND (`SalaryAccountNumberHash` IS NULL OR `SalaryAccountNumberHash` REGEXP '^[0-9a-f]{64}$')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_UsersPIS_MobileAppActivationStatus",
                table: "UsersPIS",
                sql: "`MobileAppActivationStatus` IN (0, 1, 2)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_UsersPIS_MobileNumber_Format",
                table: "UsersPIS",
                sql: "`MobileNumber` IS NULL OR `MobileNumber` REGEXP '^[6-9][0-9]{9}$'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_UsersPIS_Numbers",
                table: "UsersPIS",
                sql: "(`HeightCm` IS NULL OR `HeightCm` > 0) AND (`WeightKg` IS NULL OR `WeightKg` > 0) AND (`NumberOfChildren` IS NULL OR `NumberOfChildren` >= 0) AND (`EarnedLeaveDue` IS NULL OR `EarnedLeaveDue` >= 0) AND (`HalfPayLeaveDue` IS NULL OR `HalfPayLeaveDue` >= 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_UsersPIS_PinCode_Format",
                table: "UsersPIS",
                sql: "`PermanentPinCode` IS NULL OR `PermanentPinCode` REGEXP '^[1-9][0-9]{5}$'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_UsersPIS_Repatriation_After_Induction",
                table: "UsersPIS",
                sql: "`DateOfRepatriation` IS NULL OR `DateOfInductionInNsg` IS NULL OR `DateOfRepatriation` >= `DateOfInductionInNsg`");

            migrationBuilder.AddCheckConstraint(
                name: "CK_UsersPIS_Superannuation_After_Birth",
                table: "UsersPIS",
                sql: "`DateOfSuperannuation` IS NULL OR `DateOfBirth` IS NULL OR `DateOfSuperannuation` > `DateOfBirth`");

            migrationBuilder.AddForeignKey(
                name: "FK_Cities_States_StateId",
                table: "Cities",
                column: "StateId",
                principalTable: "States",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Devices_Users_UserId",
                table: "Devices",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RefreshTokens_Devices_DeviceId",
                table: "RefreshTokens",
                column: "DeviceId",
                principalTable: "Devices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RefreshTokens_RefreshTokens_ReplacedByTokenId",
                table: "RefreshTokens",
                column: "ReplacedByTokenId",
                principalTable: "RefreshTokens",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RefreshTokens_Users_UserId",
                table: "RefreshTokens",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UsersPIS_EducationQualifications_EducationId",
                table: "UsersPIS",
                column: "EducationId",
                principalTable: "EducationQualifications",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UsersPIS_NsgUnits_NsgUnitId",
                table: "UsersPIS",
                column: "NsgUnitId",
                principalTable: "NsgUnits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UsersPIS_NsgUnits_SideSteppedFromNsgUnitId",
                table: "UsersPIS",
                column: "SideSteppedFromNsgUnitId",
                principalTable: "NsgUnits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UsersPIS_ParentDepartmentRanks_ParentDepartmentRankId",
                table: "UsersPIS",
                column: "ParentDepartmentRankId",
                principalTable: "ParentDepartmentRanks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UsersPIS_Religions_ReligionId",
                table: "UsersPIS",
                column: "ReligionId",
                principalTable: "Religions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UsersPIS_States_PermanentStateId",
                table: "UsersPIS",
                column: "PermanentStateId",
                principalTable: "States",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UsersPIS_rank_hierarchy_NsgRankId",
                table: "UsersPIS",
                column: "NsgRankId",
                principalTable: "rank_hierarchy",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

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

            migrationBuilder.AddCheckConstraint(
                name: "CK_States_Code_Format",
                table: "States",
                sql: "`Code` IS NULL OR `Code` REGEXP '^[0-9]{2}$'");
        }
    }
}
