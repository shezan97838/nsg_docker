using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Leave;
using NSG.Mobile.Application.Leave.DTOs;
using NSG.Mobile.Application.Leave.Services;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Tests;

/// <summary>Manual leave maintenance and the caller's own record, over in-memory fakes (no database, no host).</summary>
public class LeaveServiceTests
{
    // The default test clock is 2026-10-15 06:00 UTC = 11:30 IST.
    private static readonly DateTime NowIst = new(2026, 10, 15, 11, 30, 0);

    private static async Task<LeaveSaveResponse> SaveOkAsync(LeaveKit kit, string nsg, int leave, bool create = true)
    {
        Result<LeaveSaveResponse> result = await kit.Leave.SaveAsync(nsg, leave, create, CancellationToken.None);
        Assert.True(result.IsSuccess, result.ErrorMessage);
        return result.Data!;
    }

    // ── Create / update ──

    [Fact]
    public async Task A_valid_nsg_number_creates_a_current_year_record_with_all_audit_fields_set_from_the_token_admin()
    {
        LeaveKit kit = new();

        LeaveSaveResponse response = await SaveOkAsync(kit, "NSG1001", 12);

        Assert.True(response.Created);
        Assert.Equal("Leave record created successfully.", response.Body.Message);
        PersonnelLeaveRecord record = Assert.Single(kit.Repo.Records);
        Assert.Equal(("NSG1001", 2026, 12), (record.NsgNumber, record.LeaveYear, record.LeaveTaken));
        Assert.Equal(LeaveKit.Admin, record.CreatedBy);
        Assert.Equal(LeaveKit.Admin, record.UpdatedBy);
        Assert.Equal(NowIst, record.CreatedAtIst);
        Assert.Equal(NowIst, record.UpdatedAtIst);          // on creation the update fields equal the creation fields

        PersonnelLeaveHistory history = Assert.Single(kit.Repo.History);
        Assert.Equal((null, 12, LeaveChangeSource.Manual, (long?)null, LeaveKit.Admin), (history.OldLeaveTaken, history.NewLeaveTaken, history.ChangeSource, history.UploadBatchId, history.ChangedBy));

        Assert.Equal(("NSG1001", 2026, 12, 12), (response.Body.Data!.NsgNumber, response.Body.Data.LeaveYear, response.Body.Data.LeaveTaken, record.LeaveTaken));
        Assert.Contains(kit.Audit.Entries, e => e.Action == AuditAction.LeaveRecordChanged && e.Data!.Contains($"\"actorId\":{LeaveKit.Admin}") && e.Data.Contains("\"newLeaveTaken\":12"));
    }

    [Fact]
    public async Task A_second_save_updates_the_same_record_replacing_the_total_and_keeping_the_creation_fields()
    {
        LeaveKit kit = new();
        await SaveOkAsync(kit, "NSG1001", 30);
        PersonnelLeaveRecord created = kit.Repo.Records.Single();
        DateTime createdAt = created.CreatedAtIst;

        kit.Clock.Advance(TimeSpan.FromHours(3));
        kit.User.AdminProfileId = LeaveKit.OtherAdmin;
        LeaveSaveResponse response = await SaveOkAsync(kit, "NSG1001", 35);

        Assert.False(response.Created);
        Assert.Equal("Leave record updated successfully.", response.Body.Message);
        PersonnelLeaveRecord record = Assert.Single(kit.Repo.Records);        // no duplicate row
        Assert.Equal(35, record.LeaveTaken);                                   // 30 -> 35 is 35, not 65
        Assert.Equal((LeaveKit.Admin, createdAt), (record.CreatedBy, record.CreatedAtIst));
        Assert.Equal(LeaveKit.OtherAdmin, record.UpdatedBy);
        Assert.Equal(NowIst.AddHours(3), record.UpdatedAtIst);
        Assert.Equal(2, record.RowVersion);

        PersonnelLeaveHistory last = kit.Repo.History.OrderBy(h => h.Id).Last();
        Assert.Equal((30, 35, LeaveKit.OtherAdmin), (last.OldLeaveTaken, last.NewLeaveTaken, last.ChangedBy));
    }

    [Fact]
    public async Task Writing_the_stored_value_again_changes_nothing_not_even_the_update_audit_fields()
    {
        LeaveKit kit = new();
        await SaveOkAsync(kit, "NSG1001", 30);
        string before = kit.Repo.Snapshot();
        kit.Clock.Advance(TimeSpan.FromDays(1));
        kit.User.AdminProfileId = LeaveKit.OtherAdmin;
        int auditBefore = kit.Audit.Entries.Count;

        LeaveSaveResponse response = await SaveOkAsync(kit, "NSG1001", 30);

        Assert.False(response.Created);
        Assert.Contains("nothing was changed", response.Body.Message);
        Assert.Equal(before, kit.Repo.Snapshot());                 // same row version, same updated_by, same history count
        Assert.Equal(auditBefore, kit.Audit.Entries.Count);
    }

    [Fact]
    public async Task The_nsg_number_is_matched_ignoring_case_and_stored_exactly_as_the_personnel_record_spells_it()
    {
        LeaveKit kit = new();

        await SaveOkAsync(kit, "  nsg1001 ", 4);

        Assert.Equal("NSG1001", kit.Repo.Records.Single().NsgNumber);
    }

    [Theory]
    [InlineData("NSG9999")]      // not in the personnel records
    public async Task An_nsg_number_that_is_not_active_personnel_is_rejected_and_nothing_is_written(string nsg)
    {
        LeaveKit kit = new();

        Result<LeaveSaveResponse> result = await kit.Leave.SaveAsync(nsg, 3, true, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApiErrorCodes.NotFound, result.ErrorCode);
        Assert.Equal(LeaveMessages.PersonnelNotFound, result.ErrorMessage);
        Assert.Empty(kit.Repo.Records);
        Assert.Empty(kit.Repo.History);
        Assert.Empty(kit.Audit.Entries);
    }

    [Theory]
    [InlineData(null, 3, "NSG number is required")]
    [InlineData("", 3, "NSG number is required")]
    [InlineData("   ", 3, "NSG number is required")]
    [InlineData("NSG 1001", 3, "must not contain spaces")]
    [InlineData("NSG1001", null, "Leave taken is required")]
    [InlineData("NSG1001", -1, "must not be negative")]
    [InlineData("NSG1001", 367, "must not exceed 366")]
    public async Task Invalid_input_is_rejected_with_a_message_and_nothing_is_written(string? nsg, int? leave, string expectedFragment)
    {
        LeaveKit kit = new();

        Result<LeaveSaveResponse> result = await kit.Leave.SaveAsync(nsg, leave, true, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApiErrorCodes.ValidationFailed, result.ErrorCode);
        Assert.Contains(expectedFragment, result.ErrorMessage);
        Assert.Empty(kit.Repo.Records);
    }

    [Fact]
    public async Task A_nsg_number_longer_than_the_column_is_rejected()
    {
        LeaveKit kit = new();

        Result<LeaveSaveResponse> result = await kit.Leave.SaveAsync(new string('N', 31), 3, true, CancellationToken.None);

        Assert.Equal(ApiErrorCodes.ValidationFailed, result.ErrorCode);
        Assert.Contains("30", result.ErrorMessage);
    }

    [Fact]
    public async Task Zero_and_366_are_the_inclusive_bounds()
    {
        LeaveKit kit = new();

        await SaveOkAsync(kit, "NSG1001", 0);
        await SaveOkAsync(kit, "NSG1002", 366);

        Assert.Equal(new[] { 0, 366 }, kit.Repo.Records.OrderBy(r => r.Id).Select(r => r.LeaveTaken).ToArray());
    }

    [Fact]
    public async Task Update_only_mode_refuses_to_create_a_missing_record()
    {
        LeaveKit kit = new();

        Result<LeaveSaveResponse> result = await kit.Leave.SaveAsync("NSG1001", 5, createIfMissing: false, CancellationToken.None);

        Assert.Equal(ApiErrorCodes.NotFound, result.ErrorCode);
        Assert.Contains("Use POST", result.ErrorMessage);
        Assert.Empty(kit.Repo.Records);
    }

    [Fact]
    public async Task Update_only_mode_changes_an_existing_current_year_record()
    {
        LeaveKit kit = new();
        await SaveOkAsync(kit, "NSG1001", 10);

        LeaveSaveResponse response = await SaveOkAsync(kit, "NSG1001", 11, create: false);

        Assert.False(response.Created);
        Assert.Equal(11, kit.Repo.Records.Single().LeaveTaken);
    }

    [Fact]
    public async Task Previous_years_are_never_touched_and_a_new_year_gets_its_own_row()
    {
        LeaveKit kit = new();
        PersonnelLeaveRecord lastYear = kit.Repo.AddRecord("NSG1001", 2025, 22, by: 8001);
        string lastYearBefore = $"{lastYear.LeaveTaken}:{lastYear.UpdatedBy}:{lastYear.UpdatedAtIst:O}:{lastYear.RowVersion}";

        await SaveOkAsync(kit, "NSG1001", 5);

        Assert.Equal(2, kit.Repo.Records.Count);
        Assert.Equal(lastYearBefore, $"{lastYear.LeaveTaken}:{lastYear.UpdatedBy}:{lastYear.UpdatedAtIst:O}:{lastYear.RowVersion}");
        Assert.Equal(5, kit.Repo.Records.Single(r => r.LeaveYear == 2026).LeaveTaken);
    }

    [Fact]
    public async Task The_year_is_the_ist_calendar_year_not_the_utc_one()
    {
        LeaveKit kit = new();
        kit.Clock.Now = new DateTimeOffset(2026, 12, 31, 20, 0, 0, TimeSpan.Zero);     // 2027-01-01 01:30 IST

        await SaveOkAsync(kit, "NSG1001", 1);
        kit.Clock.Now = new DateTimeOffset(2026, 12, 31, 18, 29, 0, TimeSpan.Zero);    // 2026-12-31 23:59 IST
        await SaveOkAsync(kit, "NSG1002", 1);

        Assert.Equal(2027, kit.Repo.Records.Single(r => r.NsgNumber == "NSG1001").LeaveYear);
        Assert.Equal(2026, kit.Repo.Records.Single(r => r.NsgNumber == "NSG1002").LeaveYear);
    }

    [Fact]
    public async Task There_is_no_way_to_pass_a_year_or_an_actor_the_request_has_only_nsg_number_and_leave()
    {
        // The request DTOs are the whole client surface. If someone adds Year/CreatedBy/UpdatedBy here this test must be revisited deliberately.
        Assert.Equal(new[] { "LeaveTaken", "NsgNumber" }, typeof(SaveLeaveRecordRequestDto).GetProperties().Select(p => p.Name).OrderBy(n => n).ToArray());
        Assert.Equal(new[] { "LeaveTaken" }, typeof(UpdateLeaveRecordRequestDto).GetProperties().Select(p => p.Name).ToArray());
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Without_an_admin_identity_nothing_happens()
    {
        LeaveKit kit = new();
        kit.User.AdminProfileId = null;

        Result<LeaveSaveResponse> result = await kit.Leave.SaveAsync("NSG1001", 5, true, CancellationToken.None);

        Assert.Equal(ApiErrorCodes.Unauthorized, result.ErrorCode);
        Assert.Empty(kit.Repo.Records);
    }

    [Fact]
    public async Task A_lost_race_that_exhausts_the_retries_is_a_conflict_not_a_silent_overwrite()
    {
        LeaveKit kit = new();
        kit.Repo.ManualConflict = true;

        Result<LeaveSaveResponse> result = await kit.Leave.SaveAsync("NSG1001", 5, true, CancellationToken.None);

        Assert.Equal(ApiErrorCodes.ConcurrencyConflict, result.ErrorCode);
        Assert.Equal(LeaveMessages.RecordChangedConcurrently, result.ErrorMessage);
        Assert.Empty(kit.Repo.Records);
    }

    // ── Admin reads ──

    [Fact]
    public async Task The_admin_view_returns_the_current_year_record_and_404s_with_a_message_when_there_is_none()
    {
        LeaveKit kit = new();
        kit.Repo.AddRecord("NSG1001", 2025, 9);                      // only last year exists

        Result<LeaveRecordAdminDto> missing = await kit.Leave.GetCurrentYearAsync("NSG1001", CancellationToken.None);
        Assert.Equal(ApiErrorCodes.NotFound, missing.ErrorCode);
        Assert.Equal(LeaveMessages.RecordNotFound, missing.ErrorMessage);

        kit.Repo.AddRecord("NSG1001", 2026, 14);
        Result<LeaveRecordAdminDto> found = await kit.Leave.GetCurrentYearAsync("NSG1001", CancellationToken.None);
        Assert.Equal((2026, 14), (found.Data!.LeaveYear, found.Data.LeaveTaken));

        Assert.Equal(ApiErrorCodes.NotFound, (await kit.Leave.GetCurrentYearAsync("NSG9999", CancellationToken.None)).ErrorCode);
    }

    [Fact]
    public async Task The_history_lists_every_change_newest_first_with_its_source()
    {
        LeaveKit kit = new();
        await SaveOkAsync(kit, "NSG1001", 10);
        kit.Clock.Advance(TimeSpan.FromMinutes(5));
        await SaveOkAsync(kit, "NSG1001", 12);

        Result<PaginatedResponse<LeaveHistoryDto>> result = await kit.Leave.GetHistoryAsync("NSG1001", 1, 20, CancellationToken.None);

        Assert.Equal(2, result.Data!.TotalCount);
        Assert.Equal(new int?[] { 10, null }, result.Data.Items.Select(h => h.OldLeaveTaken).ToArray());
        Assert.All(result.Data.Items, h => Assert.Equal("manual", h.Source));
        Assert.Equal(TimeSpan.FromHours(5.5), result.Data.Items[0].ChangedAt.Offset);
    }

    // ── Mobile read ──

    [Fact]
    public async Task A_mobile_user_gets_only_their_own_current_year_record_in_ist()
    {
        LeaveKit kit = new();
        kit.Repo.AddRecord("NSG1001", 2026, 7);
        kit.Repo.AddRecord("NSG1002", 2026, 99);                    // somebody else's
        kit.Repo.AddRecord("NSG1001", 2025, 3);                     // an older year of the caller

        Result<MyLeaveRecordDto> result = await kit.Leave.GetMyCurrentYearAsync(CancellationToken.None);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(("NSG1001", 2026, 7), (result.Data!.NsgNumber, result.Data.LeaveYear, result.Data.LeaveTaken));
        Assert.Equal(TimeSpan.FromHours(5.5), result.Data.LastUpdatedAt.Offset);
    }

    [Fact]
    public async Task Another_mobile_user_gets_their_own_record_so_the_token_alone_decides_whose_data_it_is()
    {
        LeaveKit kit = new();
        kit.Repo.AddRecord("NSG1001", 2026, 7);
        kit.Repo.AddRecord("NSG1002", 2026, 99);
        kit.User.ProfileId = LeaveKit.OtherEmployee;

        Result<MyLeaveRecordDto> result = await kit.Leave.GetMyCurrentYearAsync(CancellationToken.None);

        Assert.Equal(("NSG1002", 99), (result.Data!.NsgNumber, result.Data.LeaveTaken));
    }

    [Fact]
    public async Task A_missing_current_year_record_is_not_found_with_a_message_and_nothing_is_created()
    {
        LeaveKit kit = new();
        kit.Repo.AddRecord("NSG1001", 2025, 3);                     // last year only

        Result<MyLeaveRecordDto> result = await kit.Leave.GetMyCurrentYearAsync(CancellationToken.None);

        Assert.Equal(ApiErrorCodes.NotFound, result.ErrorCode);
        Assert.Contains("2026", result.ErrorMessage);
        Assert.Single(kit.Repo.Records);                            // the GET did not fabricate a row
        Assert.Empty(kit.Repo.History);
    }

    [Fact]
    public async Task A_mobile_token_whose_profile_has_no_active_person_is_unauthorized_and_an_admin_token_has_no_profile()
    {
        LeaveKit kit = new();
        kit.User.ProfileId = 77777;
        Assert.Equal(ApiErrorCodes.Unauthorized, (await kit.Leave.GetMyCurrentYearAsync(CancellationToken.None)).ErrorCode);

        kit.User.ProfileId = null;
        Assert.Equal(ApiErrorCodes.Unauthorized, (await kit.Leave.GetMyCurrentYearAsync(CancellationToken.None)).ErrorCode);
    }

    [Fact]
    public void The_mobile_dto_exposes_only_the_four_agreed_fields()
        => Assert.Equal(new[] { "LastUpdatedAt", "LeaveTaken", "LeaveYear", "NsgNumber" }, typeof(MyLeaveRecordDto).GetProperties().Select(p => p.Name).OrderBy(n => n).ToArray());
}
