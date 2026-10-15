using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NSG.Mobile.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenameTimestampUtcToIst : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_approve_mobile_users_review_fields",
                table: "approve_mobile_users");

            migrationBuilder.RenameColumn(
                name: "updated_at_utc",
                table: "users_pis",
                newName: "updated_at_ist");

            migrationBuilder.RenameColumn(
                name: "created_at_utc",
                table: "users_pis",
                newName: "created_at_ist");

            migrationBuilder.RenameColumn(
                name: "updated_at_utc",
                table: "users",
                newName: "updated_at_ist");

            migrationBuilder.RenameColumn(
                name: "lockout_end_at_utc",
                table: "users",
                newName: "lockout_end_at_ist");

            migrationBuilder.RenameColumn(
                name: "last_login_at_utc",
                table: "users",
                newName: "last_login_at_ist");

            migrationBuilder.RenameColumn(
                name: "created_at_utc",
                table: "users",
                newName: "created_at_ist");

            migrationBuilder.RenameColumn(
                name: "approved_at_utc",
                table: "users",
                newName: "approved_at_ist");

            migrationBuilder.RenameColumn(
                name: "updated_at_utc",
                table: "states",
                newName: "updated_at_ist");

            migrationBuilder.RenameColumn(
                name: "created_at_utc",
                table: "states",
                newName: "created_at_ist");

            migrationBuilder.RenameColumn(
                name: "updated_at_utc",
                table: "religions",
                newName: "updated_at_ist");

            migrationBuilder.RenameColumn(
                name: "created_at_utc",
                table: "religions",
                newName: "created_at_ist");

            migrationBuilder.RenameColumn(
                name: "revoked_at_utc",
                table: "refresh_tokens",
                newName: "revoked_at_ist");

            migrationBuilder.RenameColumn(
                name: "reuse_detected_at_utc",
                table: "refresh_tokens",
                newName: "reuse_detected_at_ist");

            migrationBuilder.RenameColumn(
                name: "expires_at_utc",
                table: "refresh_tokens",
                newName: "expires_at_ist");

            migrationBuilder.RenameColumn(
                name: "created_at_utc",
                table: "refresh_tokens",
                newName: "created_at_ist");

            migrationBuilder.RenameColumn(
                name: "updated_at_utc",
                table: "rank_hierarchy",
                newName: "updated_at_ist");

            migrationBuilder.RenameColumn(
                name: "created_at_utc",
                table: "rank_hierarchy",
                newName: "created_at_ist");

            migrationBuilder.RenameColumn(
                name: "created_at_utc",
                table: "pd_trade",
                newName: "created_at_ist");

            migrationBuilder.RenameColumn(
                name: "updated_at_utc",
                table: "parent_department_ranks",
                newName: "updated_at_ist");

            migrationBuilder.RenameColumn(
                name: "created_at_utc",
                table: "parent_department_ranks",
                newName: "created_at_ist");

            migrationBuilder.RenameColumn(
                name: "verified_at_utc",
                table: "otp_requests",
                newName: "verified_at_ist");

            migrationBuilder.RenameColumn(
                name: "expires_at_utc",
                table: "otp_requests",
                newName: "expires_at_ist");

            migrationBuilder.RenameColumn(
                name: "created_at_utc",
                table: "otp_requests",
                newName: "created_at_ist");

            migrationBuilder.RenameColumn(
                name: "updated_at_utc",
                table: "nsg_units",
                newName: "updated_at_ist");

            migrationBuilder.RenameColumn(
                name: "created_at_utc",
                table: "nsg_units",
                newName: "created_at_ist");

            migrationBuilder.RenameColumn(
                name: "created_at_utc",
                table: "nsg_trade",
                newName: "created_at_ist");

            migrationBuilder.RenameColumn(
                name: "updated_at_utc",
                table: "mobile_user_profiles",
                newName: "updated_at_ist");

            migrationBuilder.RenameColumn(
                name: "locked_until_utc",
                table: "mobile_user_profiles",
                newName: "locked_until_ist");

            migrationBuilder.RenameColumn(
                name: "last_login_at_utc",
                table: "mobile_user_profiles",
                newName: "last_login_at_ist");

            migrationBuilder.RenameColumn(
                name: "created_at_utc",
                table: "mobile_user_profiles",
                newName: "created_at_ist");

            migrationBuilder.RenameColumn(
                name: "revoked_at_utc",
                table: "mobile_refresh_tokens",
                newName: "revoked_at_ist");

            migrationBuilder.RenameColumn(
                name: "reuse_detected_at_utc",
                table: "mobile_refresh_tokens",
                newName: "reuse_detected_at_ist");

            migrationBuilder.RenameColumn(
                name: "expires_at_utc",
                table: "mobile_refresh_tokens",
                newName: "expires_at_ist");

            migrationBuilder.RenameColumn(
                name: "created_at_utc",
                table: "mobile_refresh_tokens",
                newName: "created_at_ist");

            migrationBuilder.RenameIndex(
                name: "ix_mobile_refresh_tokens_profile_id_revoked_at_utc",
                table: "mobile_refresh_tokens",
                newName: "ix_mobile_refresh_tokens_profile_id_revoked_at_ist");

            migrationBuilder.RenameColumn(
                name: "expires_at_utc",
                table: "mobile_auth_grants",
                newName: "expires_at_ist");

            migrationBuilder.RenameColumn(
                name: "created_at_utc",
                table: "mobile_auth_grants",
                newName: "created_at_ist");

            migrationBuilder.RenameColumn(
                name: "consumed_at_utc",
                table: "mobile_auth_grants",
                newName: "consumed_at_ist");

            migrationBuilder.RenameIndex(
                name: "ix_mobile_auth_grants_nsg_number_consumed_at_utc",
                table: "mobile_auth_grants",
                newName: "ix_mobile_auth_grants_nsg_number_consumed_at_ist");

            migrationBuilder.RenameColumn(
                name: "created_at_utc",
                table: "education_qualifications",
                newName: "created_at_ist");

            migrationBuilder.RenameColumn(
                name: "updated_at_utc",
                table: "devices",
                newName: "updated_at_ist");

            migrationBuilder.RenameColumn(
                name: "revoked_at_utc",
                table: "devices",
                newName: "revoked_at_ist");

            migrationBuilder.RenameColumn(
                name: "last_seen_at_utc",
                table: "devices",
                newName: "last_seen_at_ist");

            migrationBuilder.RenameColumn(
                name: "created_at_utc",
                table: "devices",
                newName: "created_at_ist");

            migrationBuilder.RenameColumn(
                name: "updated_at_utc",
                table: "cities",
                newName: "updated_at_ist");

            migrationBuilder.RenameColumn(
                name: "created_at_utc",
                table: "cities",
                newName: "created_at_ist");

            migrationBuilder.RenameColumn(
                name: "created_at_utc",
                table: "audit_logs",
                newName: "created_at_ist");

            migrationBuilder.RenameColumn(
                name: "updated_at_utc",
                table: "approve_mobile_users",
                newName: "updated_at_ist");

            migrationBuilder.RenameColumn(
                name: "requested_at_utc",
                table: "approve_mobile_users",
                newName: "requested_at_ist");

            migrationBuilder.RenameColumn(
                name: "created_at_utc",
                table: "approve_mobile_users",
                newName: "created_at_ist");

            migrationBuilder.RenameColumn(
                name: "approved_at_utc",
                table: "approve_mobile_users",
                newName: "approved_at_ist");

            migrationBuilder.AddCheckConstraint(
                name: "ck_approve_mobile_users_review_fields",
                table: "approve_mobile_users",
                sql: "(`approval_status` = 0 AND `approved_by` IS NULL AND `approved_at_ist` IS NULL) OR (`approval_status` <> 0 AND `approved_by` IS NOT NULL AND `approved_at_ist` IS NOT NULL)");

            // Shift existing historical timestamps by +05:30 into IST
            migrationBuilder.Sql("UPDATE `users_pis` SET `created_at_ist` = DATE_ADD(`created_at_ist`, INTERVAL '5:30' HOUR_MINUTE) WHERE `created_at_ist` IS NOT NULL;");
            migrationBuilder.Sql("UPDATE `users_pis` SET `updated_at_ist` = DATE_ADD(`updated_at_ist`, INTERVAL '5:30' HOUR_MINUTE) WHERE `updated_at_ist` IS NOT NULL;");

            migrationBuilder.Sql("UPDATE `users` SET `approved_at_ist` = DATE_ADD(`approved_at_ist`, INTERVAL '5:30' HOUR_MINUTE) WHERE `approved_at_ist` IS NOT NULL;");
            migrationBuilder.Sql("UPDATE `users` SET `last_login_at_ist` = DATE_ADD(`last_login_at_ist`, INTERVAL '5:30' HOUR_MINUTE) WHERE `last_login_at_ist` IS NOT NULL;");
            migrationBuilder.Sql("UPDATE `users` SET `lockout_end_at_ist` = DATE_ADD(`lockout_end_at_ist`, INTERVAL '5:30' HOUR_MINUTE) WHERE `lockout_end_at_ist` IS NOT NULL;");
            migrationBuilder.Sql("UPDATE `users` SET `created_at_ist` = DATE_ADD(`created_at_ist`, INTERVAL '5:30' HOUR_MINUTE) WHERE `created_at_ist` IS NOT NULL;");
            migrationBuilder.Sql("UPDATE `users` SET `updated_at_ist` = DATE_ADD(`updated_at_ist`, INTERVAL '5:30' HOUR_MINUTE) WHERE `updated_at_ist` IS NOT NULL;");

            migrationBuilder.Sql("UPDATE `states` SET `created_at_ist` = DATE_ADD(`created_at_ist`, INTERVAL '5:30' HOUR_MINUTE) WHERE `created_at_ist` IS NOT NULL;");
            migrationBuilder.Sql("UPDATE `states` SET `updated_at_ist` = DATE_ADD(`updated_at_ist`, INTERVAL '5:30' HOUR_MINUTE) WHERE `updated_at_ist` IS NOT NULL;");

            migrationBuilder.Sql("UPDATE `cities` SET `created_at_ist` = DATE_ADD(`created_at_ist`, INTERVAL '5:30' HOUR_MINUTE) WHERE `created_at_ist` IS NOT NULL;");
            migrationBuilder.Sql("UPDATE `cities` SET `updated_at_ist` = DATE_ADD(`updated_at_ist`, INTERVAL '5:30' HOUR_MINUTE) WHERE `updated_at_ist` IS NOT NULL;");

            migrationBuilder.Sql("UPDATE `religions` SET `created_at_ist` = DATE_ADD(`created_at_ist`, INTERVAL '5:30' HOUR_MINUTE) WHERE `created_at_ist` IS NOT NULL;");
            migrationBuilder.Sql("UPDATE `religions` SET `updated_at_ist` = DATE_ADD(`updated_at_ist`, INTERVAL '5:30' HOUR_MINUTE) WHERE `updated_at_ist` IS NOT NULL;");

            migrationBuilder.Sql("UPDATE `refresh_tokens` SET `expires_at_ist` = DATE_ADD(`expires_at_ist`, INTERVAL '5:30' HOUR_MINUTE) WHERE `expires_at_ist` IS NOT NULL;");
            migrationBuilder.Sql("UPDATE `refresh_tokens` SET `revoked_at_ist` = DATE_ADD(`revoked_at_ist`, INTERVAL '5:30' HOUR_MINUTE) WHERE `revoked_at_ist` IS NOT NULL;");
            migrationBuilder.Sql("UPDATE `refresh_tokens` SET `reuse_detected_at_ist` = DATE_ADD(`reuse_detected_at_ist`, INTERVAL '5:30' HOUR_MINUTE) WHERE `reuse_detected_at_ist` IS NOT NULL;");
            migrationBuilder.Sql("UPDATE `refresh_tokens` SET `created_at_ist` = DATE_ADD(`created_at_ist`, INTERVAL '5:30' HOUR_MINUTE) WHERE `created_at_ist` IS NOT NULL;");

            migrationBuilder.Sql("UPDATE `rank_hierarchy` SET `created_at_ist` = DATE_ADD(`created_at_ist`, INTERVAL '5:30' HOUR_MINUTE) WHERE `created_at_ist` IS NOT NULL;");
            migrationBuilder.Sql("UPDATE `rank_hierarchy` SET `updated_at_ist` = DATE_ADD(`updated_at_ist`, INTERVAL '5:30' HOUR_MINUTE) WHERE `updated_at_ist` IS NOT NULL;");

            migrationBuilder.Sql("UPDATE `pd_trade` SET `created_at_ist` = DATE_ADD(`created_at_ist`, INTERVAL '5:30' HOUR_MINUTE) WHERE `created_at_ist` IS NOT NULL;");

            migrationBuilder.Sql("UPDATE `parent_department_ranks` SET `created_at_ist` = DATE_ADD(`created_at_ist`, INTERVAL '5:30' HOUR_MINUTE) WHERE `created_at_ist` IS NOT NULL;");
            migrationBuilder.Sql("UPDATE `parent_department_ranks` SET `updated_at_ist` = DATE_ADD(`updated_at_ist`, INTERVAL '5:30' HOUR_MINUTE) WHERE `updated_at_ist` IS NOT NULL;");

            migrationBuilder.Sql("UPDATE `otp_requests` SET `expires_at_ist` = DATE_ADD(`expires_at_ist`, INTERVAL '5:30' HOUR_MINUTE) WHERE `expires_at_ist` IS NOT NULL;");
            migrationBuilder.Sql("UPDATE `otp_requests` SET `verified_at_ist` = DATE_ADD(`verified_at_ist`, INTERVAL '5:30' HOUR_MINUTE) WHERE `verified_at_ist` IS NOT NULL;");
            migrationBuilder.Sql("UPDATE `otp_requests` SET `created_at_ist` = DATE_ADD(`created_at_ist`, INTERVAL '5:30' HOUR_MINUTE) WHERE `created_at_ist` IS NOT NULL;");

            migrationBuilder.Sql("UPDATE `nsg_units` SET `created_at_ist` = DATE_ADD(`created_at_ist`, INTERVAL '5:30' HOUR_MINUTE) WHERE `created_at_ist` IS NOT NULL;");
            migrationBuilder.Sql("UPDATE `nsg_units` SET `updated_at_ist` = DATE_ADD(`updated_at_ist`, INTERVAL '5:30' HOUR_MINUTE) WHERE `updated_at_ist` IS NOT NULL;");

            migrationBuilder.Sql("UPDATE `nsg_trade` SET `created_at_ist` = DATE_ADD(`created_at_ist`, INTERVAL '5:30' HOUR_MINUTE) WHERE `created_at_ist` IS NOT NULL;");

            migrationBuilder.Sql("UPDATE `mobile_user_profiles` SET `locked_until_ist` = DATE_ADD(`locked_until_ist`, INTERVAL '5:30' HOUR_MINUTE) WHERE `locked_until_ist` IS NOT NULL;");
            migrationBuilder.Sql("UPDATE `mobile_user_profiles` SET `last_login_at_ist` = DATE_ADD(`last_login_at_ist`, INTERVAL '5:30' HOUR_MINUTE) WHERE `last_login_at_ist` IS NOT NULL;");
            migrationBuilder.Sql("UPDATE `mobile_user_profiles` SET `created_at_ist` = DATE_ADD(`created_at_ist`, INTERVAL '5:30' HOUR_MINUTE) WHERE `created_at_ist` IS NOT NULL;");
            migrationBuilder.Sql("UPDATE `mobile_user_profiles` SET `updated_at_ist` = DATE_ADD(`updated_at_ist`, INTERVAL '5:30' HOUR_MINUTE) WHERE `updated_at_ist` IS NOT NULL;");

            migrationBuilder.Sql("UPDATE `mobile_refresh_tokens` SET `expires_at_ist` = DATE_ADD(`expires_at_ist`, INTERVAL '5:30' HOUR_MINUTE) WHERE `expires_at_ist` IS NOT NULL;");
            migrationBuilder.Sql("UPDATE `mobile_refresh_tokens` SET `created_at_ist` = DATE_ADD(`created_at_ist`, INTERVAL '5:30' HOUR_MINUTE) WHERE `created_at_ist` IS NOT NULL;");
            migrationBuilder.Sql("UPDATE `mobile_refresh_tokens` SET `revoked_at_ist` = DATE_ADD(`revoked_at_ist`, INTERVAL '5:30' HOUR_MINUTE) WHERE `revoked_at_ist` IS NOT NULL;");
            migrationBuilder.Sql("UPDATE `mobile_refresh_tokens` SET `reuse_detected_at_ist` = DATE_ADD(`reuse_detected_at_ist`, INTERVAL '5:30' HOUR_MINUTE) WHERE `reuse_detected_at_ist` IS NOT NULL;");

            migrationBuilder.Sql("UPDATE `mobile_auth_grants` SET `expires_at_ist` = DATE_ADD(`expires_at_ist`, INTERVAL '5:30' HOUR_MINUTE) WHERE `expires_at_ist` IS NOT NULL;");
            migrationBuilder.Sql("UPDATE `mobile_auth_grants` SET `created_at_ist` = DATE_ADD(`created_at_ist`, INTERVAL '5:30' HOUR_MINUTE) WHERE `created_at_ist` IS NOT NULL;");
            migrationBuilder.Sql("UPDATE `mobile_auth_grants` SET `consumed_at_ist` = DATE_ADD(`consumed_at_ist`, INTERVAL '5:30' HOUR_MINUTE) WHERE `consumed_at_ist` IS NOT NULL;");

            migrationBuilder.Sql("UPDATE `education_qualifications` SET `created_at_ist` = DATE_ADD(`created_at_ist`, INTERVAL '5:30' HOUR_MINUTE) WHERE `created_at_ist` IS NOT NULL;");

            migrationBuilder.Sql("UPDATE `devices` SET `last_seen_at_ist` = DATE_ADD(`last_seen_at_ist`, INTERVAL '5:30' HOUR_MINUTE) WHERE `last_seen_at_ist` IS NOT NULL;");
            migrationBuilder.Sql("UPDATE `devices` SET `revoked_at_ist` = DATE_ADD(`revoked_at_ist`, INTERVAL '5:30' HOUR_MINUTE) WHERE `revoked_at_ist` IS NOT NULL;");
            migrationBuilder.Sql("UPDATE `devices` SET `created_at_ist` = DATE_ADD(`created_at_ist`, INTERVAL '5:30' HOUR_MINUTE) WHERE `created_at_ist` IS NOT NULL;");
            migrationBuilder.Sql("UPDATE `devices` SET `updated_at_ist` = DATE_ADD(`updated_at_ist`, INTERVAL '5:30' HOUR_MINUTE) WHERE `updated_at_ist` IS NOT NULL;");

            migrationBuilder.Sql("UPDATE `audit_logs` SET `created_at_ist` = DATE_ADD(`created_at_ist`, INTERVAL '5:30' HOUR_MINUTE) WHERE `created_at_ist` IS NOT NULL;");

            migrationBuilder.Sql("UPDATE `approve_mobile_users` SET `requested_at_ist` = DATE_ADD(`requested_at_ist`, INTERVAL '5:30' HOUR_MINUTE) WHERE `requested_at_ist` IS NOT NULL;");
            migrationBuilder.Sql("UPDATE `approve_mobile_users` SET `approved_at_ist` = DATE_ADD(`approved_at_ist`, INTERVAL '5:30' HOUR_MINUTE) WHERE `approved_at_ist` IS NOT NULL;");
            migrationBuilder.Sql("UPDATE `approve_mobile_users` SET `created_at_ist` = DATE_ADD(`created_at_ist`, INTERVAL '5:30' HOUR_MINUTE) WHERE `created_at_ist` IS NOT NULL;");
            migrationBuilder.Sql("UPDATE `approve_mobile_users` SET `updated_at_ist` = DATE_ADD(`updated_at_ist`, INTERVAL '5:30' HOUR_MINUTE) WHERE `updated_at_ist` IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_approve_mobile_users_review_fields",
                table: "approve_mobile_users");

            migrationBuilder.RenameColumn(
                name: "updated_at_ist",
                table: "users_pis",
                newName: "updated_at_utc");

            migrationBuilder.RenameColumn(
                name: "created_at_ist",
                table: "users_pis",
                newName: "created_at_utc");

            migrationBuilder.RenameColumn(
                name: "updated_at_ist",
                table: "users",
                newName: "updated_at_utc");

            migrationBuilder.RenameColumn(
                name: "lockout_end_at_ist",
                table: "users",
                newName: "lockout_end_at_utc");

            migrationBuilder.RenameColumn(
                name: "last_login_at_ist",
                table: "users",
                newName: "last_login_at_utc");

            migrationBuilder.RenameColumn(
                name: "created_at_ist",
                table: "users",
                newName: "created_at_utc");

            migrationBuilder.RenameColumn(
                name: "approved_at_ist",
                table: "users",
                newName: "approved_at_utc");

            migrationBuilder.RenameColumn(
                name: "updated_at_ist",
                table: "states",
                newName: "updated_at_utc");

            migrationBuilder.RenameColumn(
                name: "created_at_ist",
                table: "states",
                newName: "created_at_utc");

            migrationBuilder.RenameColumn(
                name: "updated_at_ist",
                table: "religions",
                newName: "updated_at_utc");

            migrationBuilder.RenameColumn(
                name: "created_at_ist",
                table: "religions",
                newName: "created_at_utc");

            migrationBuilder.RenameColumn(
                name: "revoked_at_ist",
                table: "refresh_tokens",
                newName: "revoked_at_utc");

            migrationBuilder.RenameColumn(
                name: "reuse_detected_at_ist",
                table: "refresh_tokens",
                newName: "reuse_detected_at_utc");

            migrationBuilder.RenameColumn(
                name: "expires_at_ist",
                table: "refresh_tokens",
                newName: "expires_at_utc");

            migrationBuilder.RenameColumn(
                name: "created_at_ist",
                table: "refresh_tokens",
                newName: "created_at_utc");

            migrationBuilder.RenameColumn(
                name: "updated_at_ist",
                table: "rank_hierarchy",
                newName: "updated_at_utc");

            migrationBuilder.RenameColumn(
                name: "created_at_ist",
                table: "rank_hierarchy",
                newName: "created_at_utc");

            migrationBuilder.RenameColumn(
                name: "created_at_ist",
                table: "pd_trade",
                newName: "created_at_utc");

            migrationBuilder.RenameColumn(
                name: "updated_at_ist",
                table: "parent_department_ranks",
                newName: "updated_at_utc");

            migrationBuilder.RenameColumn(
                name: "created_at_ist",
                table: "parent_department_ranks",
                newName: "created_at_utc");

            migrationBuilder.RenameColumn(
                name: "verified_at_ist",
                table: "otp_requests",
                newName: "verified_at_utc");

            migrationBuilder.RenameColumn(
                name: "expires_at_ist",
                table: "otp_requests",
                newName: "expires_at_utc");

            migrationBuilder.RenameColumn(
                name: "created_at_ist",
                table: "otp_requests",
                newName: "created_at_utc");

            migrationBuilder.RenameColumn(
                name: "updated_at_ist",
                table: "nsg_units",
                newName: "updated_at_utc");

            migrationBuilder.RenameColumn(
                name: "created_at_ist",
                table: "nsg_units",
                newName: "created_at_utc");

            migrationBuilder.RenameColumn(
                name: "created_at_ist",
                table: "nsg_trade",
                newName: "created_at_utc");

            migrationBuilder.RenameColumn(
                name: "updated_at_ist",
                table: "mobile_user_profiles",
                newName: "updated_at_utc");

            migrationBuilder.RenameColumn(
                name: "locked_until_ist",
                table: "mobile_user_profiles",
                newName: "locked_until_utc");

            migrationBuilder.RenameColumn(
                name: "last_login_at_ist",
                table: "mobile_user_profiles",
                newName: "last_login_at_utc");

            migrationBuilder.RenameColumn(
                name: "created_at_ist",
                table: "mobile_user_profiles",
                newName: "created_at_utc");

            migrationBuilder.RenameColumn(
                name: "revoked_at_ist",
                table: "mobile_refresh_tokens",
                newName: "revoked_at_utc");

            migrationBuilder.RenameColumn(
                name: "reuse_detected_at_ist",
                table: "mobile_refresh_tokens",
                newName: "reuse_detected_at_utc");

            migrationBuilder.RenameColumn(
                name: "expires_at_ist",
                table: "mobile_refresh_tokens",
                newName: "expires_at_utc");

            migrationBuilder.RenameColumn(
                name: "created_at_ist",
                table: "mobile_refresh_tokens",
                newName: "created_at_utc");

            migrationBuilder.RenameIndex(
                name: "ix_mobile_refresh_tokens_profile_id_revoked_at_ist",
                table: "mobile_refresh_tokens",
                newName: "ix_mobile_refresh_tokens_profile_id_revoked_at_utc");

            migrationBuilder.RenameColumn(
                name: "expires_at_ist",
                table: "mobile_auth_grants",
                newName: "expires_at_utc");

            migrationBuilder.RenameColumn(
                name: "created_at_ist",
                table: "mobile_auth_grants",
                newName: "created_at_utc");

            migrationBuilder.RenameColumn(
                name: "consumed_at_ist",
                table: "mobile_auth_grants",
                newName: "consumed_at_utc");

            migrationBuilder.RenameIndex(
                name: "ix_mobile_auth_grants_nsg_number_consumed_at_ist",
                table: "mobile_auth_grants",
                newName: "ix_mobile_auth_grants_nsg_number_consumed_at_utc");

            migrationBuilder.RenameColumn(
                name: "created_at_ist",
                table: "education_qualifications",
                newName: "created_at_utc");

            migrationBuilder.RenameColumn(
                name: "updated_at_ist",
                table: "devices",
                newName: "updated_at_utc");

            migrationBuilder.RenameColumn(
                name: "revoked_at_ist",
                table: "devices",
                newName: "revoked_at_utc");

            migrationBuilder.RenameColumn(
                name: "last_seen_at_ist",
                table: "devices",
                newName: "last_seen_at_utc");

            migrationBuilder.RenameColumn(
                name: "created_at_ist",
                table: "devices",
                newName: "created_at_utc");

            migrationBuilder.RenameColumn(
                name: "updated_at_ist",
                table: "cities",
                newName: "updated_at_utc");

            migrationBuilder.RenameColumn(
                name: "created_at_ist",
                table: "cities",
                newName: "created_at_utc");

            migrationBuilder.RenameColumn(
                name: "created_at_ist",
                table: "audit_logs",
                newName: "created_at_utc");

            migrationBuilder.RenameColumn(
                name: "updated_at_ist",
                table: "approve_mobile_users",
                newName: "updated_at_utc");

            migrationBuilder.RenameColumn(
                name: "requested_at_ist",
                table: "approve_mobile_users",
                newName: "requested_at_utc");

            migrationBuilder.RenameColumn(
                name: "created_at_ist",
                table: "approve_mobile_users",
                newName: "created_at_utc");

            migrationBuilder.RenameColumn(
                name: "approved_at_ist",
                table: "approve_mobile_users",
                newName: "approved_at_utc");

            migrationBuilder.AddCheckConstraint(
                name: "ck_approve_mobile_users_review_fields",
                table: "approve_mobile_users",
                sql: "(`approval_status` = 0 AND `approved_by` IS NULL AND `approved_at_utc` IS NULL) OR (`approval_status` <> 0 AND `approved_by` IS NOT NULL AND `approved_at_utc` IS NOT NULL)");
        }
    }
}
