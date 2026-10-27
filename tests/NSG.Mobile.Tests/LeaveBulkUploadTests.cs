using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Leave;
using NSG.Mobile.Application.Leave.DTOs;
using NSG.Mobile.Application.Leave.Services;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Tests;

/// <summary>
/// The Excel bulk upload workflow over in-memory fakes: validation, the all-or-nothing policy, the durable batch/error report, honest failure
/// messages and the single-upload rule. The fake repository stages writes and only commits them on success, so these tests prove the
/// SERVICE's decisions; the SQL transaction itself is proven only by the MySQL-gated LeaveDatabaseTests.
/// </summary>
public class LeaveBulkUploadTests
{
    private static readonly DateTime NowIst = new(2026, 10, 15, 11, 30, 0);

    private static byte[] Sheet(params (object? Nsg, object? Leave)[] rows) => XlsxBuilder.Template(rows);

    private static async Task<Result<LeaveUploadBatchDto>> UploadAsync(LeaveKit kit, byte[] bytes, string name = "leave.xlsx", long? declared = null)
        => await kit.Uploads.UploadAsync(LeaveKit.File(bytes, name, declared), CancellationToken.None);

    private static async Task<LeaveUploadBatchDto> UploadOkAsync(LeaveKit kit, byte[] bytes)
    {
        Result<LeaveUploadBatchDto> result = await UploadAsync(kit, bytes);
        Assert.True(result.IsSuccess, result.ErrorMessage);
        return result.Data!;
    }

    private static PersonnelLeaveUploadBatch OnlyBatch(LeaveKit kit) => Assert.Single(kit.Repo.Batches);

    // ═══════════════ Success ═══════════════

    [Fact]
    public async Task A_valid_file_creates_the_records_writes_history_and_completes_the_batch()
    {
        LeaveKit kit = new();

        LeaveUploadBatchDto batch = await UploadOkAsync(kit, Sheet(("NSG1001", 12), ("NSG1002", 0), ("NSG1003", 366)));

        Assert.Equal(("Completed", 3, 3, 0, 0, 0), (batch.UploadStatus, batch.TotalRows, batch.CreatedCount, batch.UpdatedCount, batch.UnchangedCount, batch.FailedCount));
        Assert.Equal(2026, batch.LeaveYear);
        Assert.Equal(new[] { 12, 0, 366 }, kit.Repo.Records.OrderBy(r => r.Id).Select(r => r.LeaveTaken).ToArray());
        Assert.All(kit.Repo.Records, r =>
        {
            Assert.Equal(2026, r.LeaveYear);
            Assert.Equal((LeaveKit.Admin, LeaveKit.Admin), (r.CreatedBy, r.UpdatedBy));
            Assert.Equal((NowIst, NowIst), (r.CreatedAtIst, r.UpdatedAtIst));
        });
        Assert.Equal(3, kit.Repo.History.Count);
        Assert.All(kit.Repo.History, h => Assert.Equal((LeaveChangeSource.Bulk, (long?)batch.Id, (int?)null), (h.ChangeSource, h.UploadBatchId, h.OldLeaveTaken)));
        Assert.Equal(("leave.xlsx", NowIst, LeaveKit.Admin), (OnlyBatch(kit).FileName, OnlyBatch(kit).CreatedAtIst, OnlyBatch(kit).CreatedBy));
        Assert.Null(OnlyBatch(kit).ProcessingLock);
        Assert.NotNull(OnlyBatch(kit).CompletedAtIst);
        Assert.Contains(kit.Audit.Entries, e => e.Action == AuditAction.LeaveBulkUploadProcessed && e.Result == "Success");
    }

    [Fact]
    public async Task Existing_current_year_records_get_the_new_total_creation_fields_stay_and_old_values_go_to_history()
    {
        LeaveKit kit = new();
        DateTime created = new(2026, 2, 2, 8, 0, 0);
        PersonnelLeaveRecord existing = kit.Repo.AddRecord("NSG1001", 2026, 30, by: 8001, at: created);

        LeaveUploadBatchDto batch = await UploadOkAsync(kit, Sheet(("NSG1001", 35), ("NSG1002", 4)));

        Assert.Equal((1, 1, 0), (batch.CreatedCount, batch.UpdatedCount, batch.UnchangedCount));
        Assert.Equal(35, existing.LeaveTaken);                                          // replaced, not 65
        Assert.Equal((8001L, created), (existing.CreatedBy, existing.CreatedAtIst));    // creation fields preserved
        Assert.Equal((LeaveKit.Admin, NowIst), (existing.UpdatedBy, existing.UpdatedAtIst));
        Assert.Equal(2, existing.RowVersion);
        PersonnelLeaveHistory change = kit.Repo.History.Single(h => h.NsgNumber == "NSG1001");
        Assert.Equal((30, 35, batch.Id), (change.OldLeaveTaken, change.NewLeaveTaken, change.UploadBatchId));
    }

    [Fact]
    public async Task A_row_that_already_has_the_stored_total_is_counted_unchanged_and_not_written()
    {
        LeaveKit kit = new();
        PersonnelLeaveRecord same = kit.Repo.AddRecord("NSG1001", 2026, 8, by: 8001);
        DateTime updatedBefore = same.UpdatedAtIst;

        LeaveUploadBatchDto batch = await UploadOkAsync(kit, Sheet(("NSG1001", 8), ("NSG1002", 1)));

        Assert.Equal((1, 0, 1), (batch.CreatedCount, batch.UpdatedCount, batch.UnchangedCount));
        Assert.Equal((8001L, updatedBefore, 1), (same.UpdatedBy, same.UpdatedAtIst, same.RowVersion));
        Assert.DoesNotContain(kit.Repo.History, h => h.NsgNumber == "NSG1001");
    }

    [Fact]
    public async Task Uploading_the_same_file_again_changes_nothing_the_second_time()
    {
        LeaveKit kit = new();
        byte[] file = Sheet(("NSG1001", 5), ("NSG1002", 6));
        await UploadOkAsync(kit, file);
        string after = kit.Repo.Snapshot();
        kit.Clock.Advance(TimeSpan.FromHours(1));
        kit.User.AdminProfileId = LeaveKit.OtherAdmin;

        LeaveUploadBatchDto second = await UploadOkAsync(kit, file);

        Assert.Equal((0, 0, 2), (second.CreatedCount, second.UpdatedCount, second.UnchangedCount));
        Assert.Equal(after, kit.Repo.Snapshot());
        Assert.Equal(2, kit.Repo.Batches.Count);
        Assert.All(kit.Repo.Batches, b => Assert.Null(b.ProcessingLock));
    }

    [Fact]
    public async Task Previous_year_records_are_left_exactly_as_they_were()
    {
        LeaveKit kit = new();
        PersonnelLeaveRecord old = kit.Repo.AddRecord("NSG1001", 2025, 22, by: 8001);
        string before = $"{old.LeaveTaken}:{old.UpdatedBy}:{old.UpdatedAtIst:O}:{old.RowVersion}";

        await UploadOkAsync(kit, Sheet(("NSG1001", 3)));

        Assert.Equal(before, $"{old.LeaveTaken}:{old.UpdatedBy}:{old.UpdatedAtIst:O}:{old.RowVersion}");
        Assert.Equal(3, kit.Repo.Records.Single(r => r.LeaveYear == 2026).LeaveTaken);
    }

    [Fact]
    public async Task Five_hundred_rows_are_processed_in_one_batch()
    {
        LeaveKit kit = new();
        for (int i = 1; i <= 500; i++) kit.Repo.AddPerson($"BULK{i:0000}");
        for (int i = 1; i <= 100; i++) kit.Repo.AddRecord($"BULK{i:0000}", 2026, 1);      // 100 will be updated, 400 created

        LeaveUploadBatchDto batch = await UploadOkAsync(kit, XlsxBuilder.Template(Enumerable.Range(1, 500).Select(i => ((object?)$"BULK{i:0000}", (object?)(i % 40 + 2)))));

        Assert.Equal((500, 400, 100, 0), (batch.TotalRows, batch.CreatedCount, batch.UpdatedCount, batch.FailedCount));
        Assert.Equal(500, kit.Repo.Records.Count);
        Assert.Equal(500, kit.Repo.History.Count);
        Assert.Equal(1, kit.Repo.ApplyCalls);
    }

    [Fact]
    public async Task The_nsg_number_in_the_file_may_differ_in_case_and_is_stored_as_the_personnel_record_spells_it()
    {
        LeaveKit kit = new();

        await UploadOkAsync(kit, Sheet((" nsg1001 ", 5)));

        Assert.Equal("NSG1001", kit.Repo.Records.Single().NsgNumber);
    }

    [Fact]
    public async Task Numeric_cells_and_text_cells_for_the_leave_count_are_both_accepted()
    {
        LeaveKit kit = new();

        await UploadOkAsync(kit, Sheet(("NSG1001", 5), ("NSG1002", "6"), ("NSG1003", "7.0")));

        Assert.Equal(new[] { 5, 6, 7 }, kit.Repo.Records.OrderBy(r => r.Id).Select(r => r.LeaveTaken).ToArray());
    }

    [Fact]
    public async Task The_year_of_the_batch_is_the_ist_year_at_the_start_of_the_upload()
    {
        LeaveKit kit = new();
        kit.Clock.Now = new DateTimeOffset(2026, 12, 31, 19, 0, 0, TimeSpan.Zero);       // 2027-01-01 00:30 IST

        LeaveUploadBatchDto batch = await UploadOkAsync(kit, Sheet(("NSG1001", 5)));

        Assert.Equal(2027, batch.LeaveYear);
        Assert.Equal(2027, kit.Repo.Records.Single().LeaveYear);
    }

    // ═══════════════ Validation failures: zero leave changes, durable report ═══════════════

    private static async Task AssertRejectedAsync(LeaveKit kit, Result<LeaveUploadBatchDto> result, string messageFragment, string snapshotBefore)
    {
        Assert.False(result.IsSuccess);
        Assert.Equal(ApiErrorCodes.ValidationFailed, result.ErrorCode);
        Assert.Contains(messageFragment, result.ErrorMessage);
        Assert.Equal(snapshotBefore, kit.Repo.Snapshot());                 // not one leave record or history row changed
        PersonnelLeaveUploadBatch batch = OnlyBatch(kit);
        Assert.Equal(LeaveUploadStatus.ValidationFailed, batch.UploadStatus);
        Assert.Null(batch.ProcessingLock);                                  // the lock is released
        Assert.NotNull(batch.CompletedAtIst);
        Assert.Contains(kit.Repo.Errors, e => e.BatchId == batch.Id);        // the report survives
        Assert.Contains($"batch {batch.Id}", result.ErrorMessage);
        Assert.Equal(0, kit.Repo.ApplyCalls);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task One_invalid_row_rejects_the_whole_file_and_changes_nothing_not_even_the_valid_rows()
    {
        LeaveKit kit = new();
        kit.Repo.AddRecord("NSG1003", 2026, 9);
        string before = kit.Repo.Snapshot();

        Result<LeaveUploadBatchDto> result = await UploadAsync(kit, Sheet(("NSG1001", 5), ("NSG1002", -3), ("NSG1003", 10)));

        await AssertRejectedAsync(kit, result, "1 of 3 rows have errors", before);
        PersonnelLeaveUploadError error = Assert.Single(kit.Repo.Errors);
        Assert.Equal((3, "NSG1002"), (error.RowNumber, error.NsgNumber));      // Excel row 3 = second data row
        Assert.Contains("must not be negative", error.ErrorMessage);
        Assert.Equal((3, 1), (OnlyBatch(kit).TotalRows, OnlyBatch(kit).FailedCount));
    }

    [Fact]
    public async Task Problems_are_reported_by_excel_row_number_for_every_kind_of_bad_value()
    {
        LeaveKit kit = new();
        byte[] file = XlsxBuilder.Build(new (int, object?[])[]
        {
            (1, new object?[] { "NSGNumber", "LeaveTaken" }),
            (2, new object?[] { "NSG1001", 5 }),             // fine
            (3, new object?[] { null, 5 }),                  // NSG missing
            (4, new object?[] { "NSG1002", null }),          // leave missing
            (5, new object?[] { "NSG1002", "abc" }),         // not a number
            (6, new object?[] { "NSG1002", 2.5 }),           // fraction
            (7, new object?[] { "NSG1002", 367 }),           // too many days
            (8, new object?[] { "NSG9999", 4 }),             // unknown person
            (9, new object?[] { "NSG 1002", 4 }),            // space inside
            (11, new object?[] { new string('N', 31), 4 })   // too long
        });

        Result<LeaveUploadBatchDto> result = await UploadAsync(kit, file);

        await AssertRejectedAsync(kit, result, "8 of 9 rows have errors", "#0");
        Dictionary<int, string> byRow = kit.Repo.Errors.ToDictionary(e => e.RowNumber!.Value, e => e.ErrorMessage);
        Assert.Equal(new[] { 3, 4, 5, 6, 7, 8, 9, 11 }, byRow.Keys.OrderBy(k => k).ToArray());
        Assert.Contains("NSGNumber is required", byRow[3]);
        Assert.Contains("LeaveTaken is required", byRow[4]);
        Assert.Contains("whole number between 0 and 366", byRow[5]);
        Assert.Contains("no fractions", byRow[6]);
        Assert.Contains("must not exceed 366", byRow[7]);
        Assert.Contains("not found among active personnel", byRow[8]);
        Assert.Contains("spaces", byRow[9]);
        Assert.Contains("30", byRow[11]);
    }

    [Fact]
    public async Task A_row_with_two_problems_lists_both_but_counts_as_one_failed_row()
    {
        LeaveKit kit = new();

        Result<LeaveUploadBatchDto> result = await UploadAsync(kit, Sheet((null, -1)));

        await AssertRejectedAsync(kit, result, "1 of 1 rows have errors", "#0");
        Assert.Equal(2, kit.Repo.Errors.Count);
        Assert.Equal(1, OnlyBatch(kit).FailedCount);
    }

    [Fact]
    public async Task Duplicate_nsg_numbers_in_one_file_are_an_error_on_every_occurrence_and_nothing_is_chosen_silently()
    {
        LeaveKit kit = new();
        string before = kit.Repo.Snapshot();

        Result<LeaveUploadBatchDto> result = await UploadAsync(kit, Sheet(("NSG1001", 5), ("NSG1002", 6), ("nsg1001", 9)));

        await AssertRejectedAsync(kit, result, "2 of 3 rows have errors", before);
        Assert.Equal(new[] { 2, 4 }, kit.Repo.Errors.Select(e => e.RowNumber!.Value).OrderBy(r => r).ToArray());
        Assert.All(kit.Repo.Errors, e => Assert.Contains("more than once in the file (rows 2, 4)", e.ErrorMessage));
        Assert.Empty(kit.Repo.Records);
    }

    [Theory]
    [InlineData("Employee", "Leave", "Unexpected column")]
    [InlineData("NSGNumber", "Days", "Unexpected column 'Days'")]
    public async Task Wrong_columns_reject_the_file_with_a_file_level_error(string h1, string h2, string expected)
    {
        LeaveKit kit = new();

        Result<LeaveUploadBatchDto> result = await UploadAsync(kit, XlsxBuilder.Template(new (object?, object?)[] { ("NSG1001", 1) }, h1: h1, h2: h2));

        await AssertRejectedAsync(kit, result, expected, "#0");
        PersonnelLeaveUploadError error = Assert.Single(kit.Repo.Errors);
        Assert.Null(error.RowNumber);                                   // a problem with the whole file has no row
    }

    [Theory]
    [InlineData("leave.xls")]
    [InlineData("leave.csv")]
    [InlineData("leave.xlsx.exe")]
    [InlineData("leave")]
    public async Task Only_the_xlsx_extension_is_accepted_whatever_the_content(string fileName)
    {
        LeaveKit kit = new();

        Result<LeaveUploadBatchDto> result = await kit.Uploads.UploadAsync(LeaveKit.File(Sheet(("NSG1001", 1)), fileName), CancellationToken.None);

        await AssertRejectedAsync(kit, result, "Only Excel workbooks in the .xlsx format", "#0");
    }

    [Theory]
    [InlineData("id,leave\nNSG1001,5")]
    [InlineData("<html><script>alert(1)</script></html>")]
    public async Task A_file_named_xlsx_whose_bytes_are_not_a_workbook_is_rejected(string content)
    {
        LeaveKit kit = new();

        Result<LeaveUploadBatchDto> result = await UploadAsync(kit, System.Text.Encoding.UTF8.GetBytes(content));

        await AssertRejectedAsync(kit, result, "not a valid .xlsx", "#0");
    }

    [Fact]
    public async Task An_empty_file_is_rejected()
    {
        LeaveKit kit = new();

        Result<LeaveUploadBatchDto> result = await kit.Uploads.UploadAsync(LeaveKit.File(Array.Empty<byte>()), CancellationToken.None);

        await AssertRejectedAsync(kit, result, "An Excel file (.xlsx) is required", "#0");
    }

    [Fact]
    public async Task A_file_over_the_size_limit_is_rejected_by_its_declared_length_and_by_its_real_length()
    {
        LeaveKit kit = new();
        kit.Settings.MaxFileSizeBytes = 2048;
        byte[] big = Sheet(Enumerable.Range(0, 400).Select(i => ((object?)"NSG1001", (object?)1)).ToArray());
        Assert.True(big.Length > 2048, $"the test file must exceed the limit, was {big.Length}");

        Result<LeaveUploadBatchDto> honest = await UploadAsync(kit, big);
        Assert.Equal(ApiErrorCodes.ValidationFailed, honest.ErrorCode);
        Assert.Contains("larger than", honest.ErrorMessage);

        // A client that lies about the length is caught by the bounded read.
        kit.Repo.Batches.Clear();
        kit.Repo.Errors.Clear();
        Result<LeaveUploadBatchDto> liar = await UploadAsync(kit, big, declared: 100);
        Assert.Equal(ApiErrorCodes.ValidationFailed, liar.ErrorCode);
        Assert.Contains("larger than", liar.ErrorMessage);
        Assert.Empty(kit.Repo.Records);
    }

    [Fact]
    public async Task More_rows_than_the_configured_maximum_is_rejected()
    {
        LeaveKit kit = new();
        kit.Settings.MaxRows = 2;

        Result<LeaveUploadBatchDto> result = await UploadAsync(kit, Sheet(("NSG1001", 1), ("NSG1002", 1), ("NSG1003", 1)));

        await AssertRejectedAsync(kit, result, "more than 2 data rows", "#0");
    }

    [Fact]
    public async Task More_than_the_stored_error_limit_keeps_the_first_ones_and_says_how_many_more_there_were()
    {
        LeaveKit kit = new();
        int rows = LeaveLimits.MaxStoredErrors + 25;

        Result<LeaveUploadBatchDto> result = await UploadAsync(kit, XlsxBuilder.Template(Enumerable.Range(1, rows).Select(i => ((object?)$"NSG{i}X", (object?)1))));

        Assert.False(result.IsSuccess);
        Assert.Equal(LeaveLimits.MaxStoredErrors + 1, kit.Repo.Errors.Count);
        Assert.Contains("25 more problems", kit.Repo.Errors.Last().ErrorMessage);
        Assert.Equal(rows, OnlyBatch(kit).FailedCount);
    }

    // ═══════════════ Processing failures: rollback, honest messages ═══════════════

    [Fact]
    public async Task A_database_failure_after_the_writes_rolls_back_every_record_and_history_row_and_is_reported_as_failed()
    {
        LeaveKit kit = new();
        kit.Repo.AddRecord("NSG1001", 2026, 30);
        string before = kit.Repo.Snapshot();
        kit.Repo.Fault = BulkFault.DatabaseErrorAfterWrites;

        Result<LeaveUploadBatchDto> result = await UploadAsync(kit, Sheet(("NSG1001", 35), ("NSG1002", 4)));

        Assert.False(result.IsSuccess);
        Assert.Equal(LeaveErrorCodes.ProcessingFailed, result.ErrorCode);
        Assert.Contains("rolled back and no leave record was changed", result.ErrorMessage);
        Assert.Equal(before, kit.Repo.Snapshot());                       // record 1 still 30, no new record, no history
        PersonnelLeaveUploadBatch batch = OnlyBatch(kit);
        Assert.Equal(LeaveUploadStatus.ProcessingFailed, batch.UploadStatus);
        Assert.Equal(0, batch.CreatedCount + batch.UpdatedCount);       // nothing is claimed as done
        Assert.Null(batch.ProcessingLock);
        PersonnelLeaveUploadError error = Assert.Single(kit.Repo.Errors);
        Assert.Null(error.RowNumber);
        Assert.Contains($"See batch {batch.Id}", result.ErrorMessage);
        Assert.Contains(kit.Audit.Entries, e => e.Action == AuditAction.LeaveBulkUploadProcessed && e.Result == "Failure");
    }

    [Fact]
    public async Task A_record_changed_by_someone_else_during_the_upload_rolls_back_as_a_conflict()
    {
        LeaveKit kit = new();
        kit.Repo.Fault = BulkFault.Conflict;

        Result<LeaveUploadBatchDto> result = await UploadAsync(kit, Sheet(("NSG1001", 5)));

        Assert.Equal(ApiErrorCodes.Conflict, result.ErrorCode);
        Assert.Contains("Nothing was saved", result.ErrorMessage);
        Assert.Empty(kit.Repo.Records);
        Assert.Equal(LeaveUploadStatus.ProcessingFailed, OnlyBatch(kit).UploadStatus);
    }

    [Fact]
    public async Task When_the_failure_report_itself_cannot_be_saved_the_message_does_not_pretend_it_was()
    {
        LeaveKit kit = new();
        kit.Repo.Fault = BulkFault.DatabaseErrorAfterWrites;
        kit.Repo.FinishFails = true;

        Result<LeaveUploadBatchDto> result = await UploadAsync(kit, Sheet(("NSG1001", 5)));

        Assert.Equal(LeaveErrorCodes.ProcessingFailed, result.ErrorCode);
        Assert.Contains("could not be saved", result.ErrorMessage);
        Assert.DoesNotContain($"See batch", result.ErrorMessage);
        Assert.Empty(kit.Repo.Records);
        Assert.Empty(kit.Repo.Errors);
        Assert.Equal(LeaveUploadStatus.Processing, OnlyBatch(kit).UploadStatus);       // still honest: it was never closed
        Assert.Contains(kit.UploadLog.Entries, e => e.Level == Microsoft.Extensions.Logging.LogLevel.Error);
    }

    [Fact]
    public async Task A_validation_report_that_cannot_be_saved_says_so_and_still_tells_the_admin_the_first_problem()
    {
        LeaveKit kit = new();
        kit.Repo.FinishFails = true;

        Result<LeaveUploadBatchDto> result = await UploadAsync(kit, Sheet(("NSG1001", -5)));

        Assert.Equal(ApiErrorCodes.ValidationFailed, result.ErrorCode);
        Assert.Contains("could not be saved", result.ErrorMessage);
        Assert.Contains("row 2", result.ErrorMessage);
        Assert.Contains("must not be negative", result.ErrorMessage);
        Assert.Empty(kit.Repo.Records);
    }

    [Fact]
    public async Task An_unknown_transaction_outcome_is_never_reported_as_rolled_back_or_as_success_and_the_batch_stays_open()
    {
        LeaveKit kit = new();
        kit.Repo.Fault = BulkFault.OutcomeUnknown;

        Result<LeaveUploadBatchDto> result = await UploadAsync(kit, Sheet(("NSG1001", 5)));

        Assert.False(result.IsSuccess);
        Assert.Equal(LeaveErrorCodes.OutcomeUnknown, result.ErrorCode);
        Assert.Contains("could not be confirmed", result.ErrorMessage);
        Assert.Contains($"batch {OnlyBatch(kit).Id}", result.ErrorMessage);
        Assert.DoesNotContain("rolled back", result.ErrorMessage);
        Assert.Equal(LeaveUploadStatus.Processing, OnlyBatch(kit).UploadStatus);       // not marked failed: we do not know
        Assert.Contains(kit.Audit.Entries, e => e.Action == AuditAction.LeaveBulkUploadProcessed && e.Data!.Contains("OutcomeUnknown"));
    }

    [Fact]
    public async Task An_unexpected_exception_closes_the_batch_as_failed_and_is_rethrown_for_the_global_handler()
    {
        LeaveKit kit = new();
        kit.Repo.Fault = BulkFault.Unhandled;

        await Assert.ThrowsAsync<InvalidOperationException>(() => UploadAsync(kit, Sheet(("NSG1001", 5))));

        PersonnelLeaveUploadBatch batch = OnlyBatch(kit);
        Assert.Equal(LeaveUploadStatus.ProcessingFailed, batch.UploadStatus);
        Assert.Null(batch.ProcessingLock);
        Assert.Empty(kit.Repo.Records);
        Assert.DoesNotContain("boom", string.Join(" ", kit.Repo.Errors.Select(e => e.ErrorMessage)));   // exception text is never stored
    }

    // ═══════════════ Concurrency ═══════════════

    [Fact]
    public async Task A_second_upload_while_one_is_processing_is_refused_and_does_not_touch_anything()
    {
        LeaveKit kit = new();
        await kit.Repo.TryStartBatchAsync("running.xlsx", 2026, LeaveKit.OtherAdmin, NowIst, TimeSpan.FromMinutes(15));
        string before = kit.Repo.Snapshot();

        Result<LeaveUploadBatchDto> result = await UploadAsync(kit, Sheet(("NSG1001", 5)));

        Assert.Equal(ApiErrorCodes.Conflict, result.ErrorCode);
        Assert.Equal(LeaveMessages.UploadInProgress, result.ErrorMessage);
        Assert.Equal(before, kit.Repo.Snapshot());
        Assert.Single(kit.Repo.Batches);
        Assert.Equal(0, kit.Repo.ApplyCalls);
    }

    [Fact]
    public async Task A_batch_abandoned_by_a_crashed_host_stops_blocking_after_the_stale_period()
    {
        LeaveKit kit = new();
        await kit.Repo.TryStartBatchAsync("crashed.xlsx", 2026, LeaveKit.OtherAdmin, NowIst, TimeSpan.FromMinutes(15));
        kit.Clock.Advance(TimeSpan.FromMinutes(16));

        LeaveUploadBatchDto batch = await UploadOkAsync(kit, Sheet(("NSG1001", 5)));

        Assert.Equal("Completed", batch.UploadStatus);
        Assert.Equal(LeaveUploadStatus.ProcessingFailed, kit.Repo.Batches.First().UploadStatus);
        Assert.Null(kit.Repo.Batches.First().ProcessingLock);
    }

    [Fact]
    public async Task Two_uploads_started_together_only_one_gets_the_lock()
    {
        LeaveKit kit = new();
        byte[] file = Sheet(("NSG1001", 5));

        // The fake's lock is taken synchronously by TryStartBatchAsync, as the database's unique index does.
        Task<Result<LeaveUploadBatchDto>> first = UploadAsync(kit, file);
        Result<LeaveUploadBatchDto> firstResult = await first;
        Assert.True(firstResult.IsSuccess);
        Assert.Null(kit.Repo.Batches.Single().ProcessingLock);

        long? a = await kit.Repo.TryStartBatchAsync("a.xlsx", 2026, 1, NowIst, TimeSpan.FromMinutes(15));
        long? b = await kit.Repo.TryStartBatchAsync("b.xlsx", 2026, 2, NowIst, TimeSpan.FromMinutes(15));
        Assert.NotNull(a);
        Assert.Null(b);
    }

    // ═══════════════ Identity ═══════════════

    [Fact]
    public async Task Without_an_admin_identity_no_batch_is_even_started()
    {
        LeaveKit kit = new();
        kit.User.AdminProfileId = null;

        Result<LeaveUploadBatchDto> result = await UploadAsync(kit, Sheet(("NSG1001", 5)));

        Assert.Equal(ApiErrorCodes.Unauthorized, result.ErrorCode);
        Assert.Empty(kit.Repo.Batches);
    }

    [Fact]
    public async Task The_acting_admin_is_the_token_admin_on_every_row_and_on_the_batch()
    {
        LeaveKit kit = new();
        kit.User.AdminProfileId = LeaveKit.OtherAdmin;

        await UploadOkAsync(kit, Sheet(("NSG1001", 5), ("NSG1002", 6)));

        Assert.All(kit.Repo.Records, r => Assert.Equal((LeaveKit.OtherAdmin, LeaveKit.OtherAdmin), (r.CreatedBy, r.UpdatedBy)));
        Assert.All(kit.Repo.History, h => Assert.Equal(LeaveKit.OtherAdmin, h.ChangedBy));
        Assert.Equal(LeaveKit.OtherAdmin, OnlyBatch(kit).CreatedBy);
    }

    // ═══════════════ Batch queries ═══════════════

    [Fact]
    public async Task Batches_and_their_errors_can_be_read_back_after_a_failure()
    {
        LeaveKit kit = new();
        await UploadAsync(kit, Sheet(("NSG1001", -1), ("NSG1002", 5)));
        await UploadOkAsync(kit, Sheet(("NSG1001", 1)));

        Result<PaginatedResponse<LeaveUploadBatchDto>> all = await kit.Uploads.GetBatchesAsync(1, 20, null, CancellationToken.None);
        Assert.Equal(new[] { "Completed", "ValidationFailed" }, all.Data!.Items.Select(b => b.UploadStatus).ToArray());   // newest first

        Result<PaginatedResponse<LeaveUploadBatchDto>> failed = await kit.Uploads.GetBatchesAsync(1, 20, LeaveUploadStatus.ValidationFailed, CancellationToken.None);
        LeaveUploadBatchDto batch = Assert.Single(failed.Data!.Items);
        Assert.Equal((2, 1), (batch.TotalRows, batch.FailedCount));

        Result<LeaveUploadBatchDto> one = await kit.Uploads.GetBatchAsync(batch.Id, CancellationToken.None);
        Assert.Equal("ValidationFailed", one.Data!.UploadStatus);

        Result<PaginatedResponse<LeaveUploadErrorDto>> errors = await kit.Uploads.GetErrorsAsync(batch.Id, 1, 20, CancellationToken.None);
        LeaveUploadErrorDto error = Assert.Single(errors.Data!.Items);
        Assert.Equal((2, "NSG1001"), (error.RowNumber, error.NsgNumber));
        Assert.Equal(TimeSpan.FromHours(5.5), one.Data.CreatedAt.Offset);
    }

    [Fact]
    public async Task An_unknown_batch_is_not_found_with_a_message()
    {
        LeaveKit kit = new();

        Assert.Equal(LeaveMessages.BatchNotFound, (await kit.Uploads.GetBatchAsync(404, CancellationToken.None)).ErrorMessage);
        Assert.Equal(ApiErrorCodes.NotFound, (await kit.Uploads.GetErrorsAsync(404, 1, 20, CancellationToken.None)).ErrorCode);
    }

    [Theory]
    [InlineData("C:\\Users\\admin\\Desktop\\leave.xlsx", "leave.xlsx")]
    [InlineData("../../etc/passwd.xlsx", "passwd.xlsx")]
    [InlineData("", "upload.xlsx")]
    public void The_stored_file_name_is_a_safe_display_name_never_a_path(string supplied, string expected)
        => Assert.Equal(expected, LeaveBulkUploadService.SafeFileName(supplied));

    [Fact]
    public void A_file_name_is_cut_to_the_column_length_and_stripped_of_control_characters()
    {
        string name = LeaveBulkUploadService.SafeFileName("a\r\nb" + new string('x', 400) + ".xlsx");

        Assert.Equal(LeaveLimits.FileNameMaxLength, name.Length);
        Assert.DoesNotContain('\n', name);
    }
}
