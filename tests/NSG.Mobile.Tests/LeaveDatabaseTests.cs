using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Application.Leave;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;
using NSG.Mobile.Infrastructure.Persistence;
using NSG.Mobile.Infrastructure.Repositories;

namespace NSG.Mobile.Tests;

/// <summary>A fact that is reported as SKIPPED (not as passed) when no dedicated test database is configured.</summary>
public sealed class MySqlFactAttribute : FactAttribute
{
    public MySqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ApiFactory.ConnectionVariable)))
            Skip = $"{ApiFactory.ConnectionVariable} is not set: these tests need a dedicated MySQL database whose name ends in _test.";
    }
}

[CollectionDefinition("mysql-leave", DisableParallelization = true)]
public sealed class MySqlLeaveCollection { }

/// <summary>
/// DATABASE-BACKED tests of the real <see cref="LeaveRepository"/> and the leave migration on real MySQL. They run ONLY when NSG_TEST_MYSQL names a
/// database ending in "_test" (ApiFactory refuses anything else and DROPS and recreates it through the migrations). Never point it at a shared,
/// development or production database. Each repository call below uses its own DbContext, like separate requests would.
/// </summary>
[Collection("mysql-leave")]
public sealed class LeaveDatabaseTests : IAsyncLifetime
{
    private const long AdminA = 9001, AdminB = 9002;

    /// <summary>MySQL 8 ER_CHECK_CONSTRAINT_VIOLATED (MySqlConnector has no named member for it).</summary>
    private const MySqlErrorCode CheckViolated = (MySqlErrorCode)3819;
    private static readonly DateTime Now = new(2026, 10, 15, 11, 30, 0);

    private ApiFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _factory = new ApiFactory();
        await _factory.ResetDatabaseAsync();   // drops and recreates the *_test database by applying every migration
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private AppDbContext Context()
    {
        // Same resilience as production: retry on transient failures (deadlocks included).
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(_factory.ConnectionString, ServerVersion.AutoDetect(_factory.ConnectionString), my => my.EnableRetryOnFailure(3))
            .Options;
        return new AppDbContext(options);
    }

    private async Task<T> WithRepoAsync<T>(Func<LeaveRepository, Task<T>> action)
    {
        await using AppDbContext context = Context();
        return await action(new LeaveRepository(context));
    }

    private async Task<long> SeedPersonAsync(string nsg)
    {
        await using AppDbContext context = Context();
        await context.Database.ExecuteSqlRawAsync("INSERT INTO `users_pis` (`nsg_number`, `service_number`, `is_active`, `created_at_ist`) VALUES ({0}, {1}, 1, {2})", nsg, "S-" + nsg, Now);
        return await context.UsersPis.Where(p => p.NsgNumber == nsg).Select(p => p.Id).SingleAsync();
    }

    // ── Schema ──

    /// <summary>Runs a raw statement and returns the MySQL error it raised, or null when it succeeded.</summary>
    private async Task<MySqlErrorCode?> RawErrorAsync(string sql, params object[] parameters)
    {
        await using AppDbContext context = Context();
        try
        {
            await context.Database.ExecuteSqlRawAsync(sql, parameters);
            return null;
        }
        catch (Exception ex) when (ex is MySqlException direct) { return direct.ErrorCode; }
        catch (Exception ex) when (ex.InnerException is MySqlException inner) { return inner.ErrorCode; }
    }

    [MySqlFact]
    public async Task The_migration_creates_the_tables_the_unique_key_and_the_leave_admin_role()
    {
        await using (AppDbContext context = Context())
        {
            AdminRole role = await context.AdminRoles.AsNoTracking().SingleAsync(r => r.RoleCode == "LEAVE_ADMIN");
            Assert.Equal((7, true), (role.Id, role.IsActive));
        }

        await WithRepoAsync(r => r.SaveManualAsync("NSG1", 2026, 3, true, AdminA, Now));

        const string insert = "INSERT INTO `personnel_leave_records` (`nsg_number`,`leave_year`,`leave_taken`,`row_version`,`created_at_ist`,`created_by`,`updated_at_ist`,`updated_by`) VALUES ('NSG1',2026,1,1,{0},1,{0},1)";
        Assert.Equal(MySqlErrorCode.DuplicateKeyEntry, await RawErrorAsync(insert, Now));
    }

    [MySqlFact]
    public async Task The_database_itself_refuses_negative_or_absurd_leave_a_second_running_batch_and_a_bad_status()
    {
        const string record = "INSERT INTO `personnel_leave_records` (`nsg_number`,`leave_year`,`leave_taken`,`row_version`,`created_at_ist`,`created_by`,`updated_at_ist`,`updated_by`) VALUES ('NSG2',2026,{0},1,{1},1,{1},1)";
        Assert.Equal(CheckViolated, await RawErrorAsync(record, -1, Now));
        Assert.Equal(CheckViolated, await RawErrorAsync(record, 367, Now));

        const string runningBatch = "INSERT INTO `personnel_leave_upload_batches` (`file_name`,`leave_year`,`total_rows`,`created_count`,`updated_count`,`unchanged_count`,`failed_count`,`upload_status`,`processing_lock`,`created_at_ist`,`created_by`) VALUES ('a.xlsx',2026,0,0,0,0,0,{0},1,{1},1)";
        Assert.Equal(CheckViolated, await RawErrorAsync(runningBatch, 9, Now));    // status outside 1..4
        Assert.Equal(CheckViolated, await RawErrorAsync(runningBatch, 2, Now));    // a finished batch must not hold the lock
        Assert.Null(await RawErrorAsync(runningBatch, 1, Now));                                              // the first running batch is fine
        Assert.Equal(MySqlErrorCode.DuplicateKeyEntry, await RawErrorAsync(runningBatch, 1, Now));           // a second one violates the unique lock
    }

    // ── Manual save ──

    [MySqlFact]
    public async Task Manual_create_update_unchanged_and_previous_year_behave_on_real_mysql()
    {
        LeaveSaveResult created = await WithRepoAsync(r => r.SaveManualAsync("NSG1", 2026, 30, true, AdminA, Now));
        Assert.Equal(LeaveSaveOutcome.Created, created.Outcome);

        await WithRepoAsync(r => r.SaveManualAsync("NSG1", 2025, 22, true, AdminA, Now.AddYears(-1)));

        LeaveSaveResult updated = await WithRepoAsync(r => r.SaveManualAsync("NSG1", 2026, 35, true, AdminB, Now.AddHours(1)));
        Assert.Equal((LeaveSaveOutcome.Updated, 30), (updated.Outcome, updated.OldLeaveTaken));

        LeaveSaveResult same = await WithRepoAsync(r => r.SaveManualAsync("NSG1", 2026, 35, true, AdminA, Now.AddHours(2)));
        Assert.Equal(LeaveSaveOutcome.Unchanged, same.Outcome);

        LeaveSaveResult missing = await WithRepoAsync(r => r.SaveManualAsync("NSG1", 2027, 1, false, AdminA, Now));
        Assert.Equal(LeaveSaveOutcome.NotFound, missing.Outcome);

        await using AppDbContext context = Context();
        PersonnelLeaveRecord current = await context.PersonnelLeaveRecords.AsNoTracking().SingleAsync(r => r.NsgNumber == "NSG1" && r.LeaveYear == 2026);
        Assert.Equal((35, AdminA, AdminB, 2), (current.LeaveTaken, current.CreatedBy, current.UpdatedBy, current.RowVersion));
        Assert.Equal((Now, Now.AddHours(1)), (current.CreatedAtIst, current.UpdatedAtIst));              // exact IST values round-trip, no conversion
        PersonnelLeaveRecord previous = await context.PersonnelLeaveRecords.AsNoTracking().SingleAsync(r => r.LeaveYear == 2025);
        Assert.Equal((22, 1), (previous.LeaveTaken, previous.RowVersion));

        List<PersonnelLeaveHistory> history = await context.PersonnelLeaveHistory.AsNoTracking().Where(h => h.LeaveYear == 2026).OrderBy(h => h.Id).ToListAsync();
        Assert.Equal(new int?[] { null, 30 }, history.Select(h => h.OldLeaveTaken).ToArray());
        Assert.Equal(new[] { 30, 35 }, history.Select(h => h.NewLeaveTaken).ToArray());
        Assert.All(history, h => Assert.Equal((LeaveChangeSource.Manual, (long?)null), (h.ChangeSource, h.UploadBatchId)));
    }

    [MySqlFact]
    public async Task Concurrent_creates_of_the_same_record_produce_exactly_one_row_and_a_consistent_history_chain()
    {
        LeaveSaveResult[] results = await Task.WhenAll(Enumerable.Range(1, 8).Select(i =>
            WithRepoAsync(r => r.SaveManualAsync("NSG1", 2026, 10 + i, true, i % 2 == 0 ? AdminA : AdminB, Now.AddSeconds(i)))));

        Assert.Equal(1, results.Count(r => r.Outcome == LeaveSaveOutcome.Created));
        Assert.DoesNotContain(results, r => r.Outcome == LeaveSaveOutcome.NotFound);

        await using AppDbContext context = Context();
        PersonnelLeaveRecord record = await context.PersonnelLeaveRecords.AsNoTracking().SingleAsync();   // one row: the unique key held
        List<PersonnelLeaveHistory> history = await context.PersonnelLeaveHistory.AsNoTracking().OrderBy(h => h.Id).ToListAsync();

        // Every change is traceable and the chain is unbroken: each row's old value is the previous row's new value, and the last equals the stored total.
        Assert.Null(history[0].OldLeaveTaken);
        for (int i = 1; i < history.Count; i++) Assert.Equal(history[i - 1].NewLeaveTaken, history[i].OldLeaveTaken);
        Assert.Equal(record.LeaveTaken, history[^1].NewLeaveTaken);
        Assert.Equal(record.RowVersion, history.Count);          // the version counts the changes
    }

    [MySqlFact]
    public async Task Concurrent_updates_never_lose_a_history_row_or_break_the_chain()
    {
        await WithRepoAsync(r => r.SaveManualAsync("NSG1", 2026, 0, true, AdminA, Now));

        LeaveSaveResult[] results = await Task.WhenAll(Enumerable.Range(1, 10).Select(i =>
            WithRepoAsync(r => r.SaveManualAsync("NSG1", 2026, i, true, AdminB, Now.AddSeconds(i)))));

        await using AppDbContext context = Context();
        PersonnelLeaveRecord record = await context.PersonnelLeaveRecords.AsNoTracking().SingleAsync();
        List<PersonnelLeaveHistory> history = await context.PersonnelLeaveHistory.AsNoTracking().OrderBy(h => h.Id).ToListAsync();
        int written = results.Count(r => r.Outcome is LeaveSaveOutcome.Updated or LeaveSaveOutcome.Created);

        Assert.Equal(1 + written, history.Count);
        for (int i = 1; i < history.Count; i++) Assert.Equal(history[i - 1].NewLeaveTaken, history[i].OldLeaveTaken);
        Assert.Equal(record.LeaveTaken, history[^1].NewLeaveTaken);
        Assert.Equal(1 + written, record.RowVersion);
    }

    // ── Bulk ──

    [MySqlFact]
    public async Task A_bulk_apply_commits_records_history_and_the_completed_batch_together()
    {
        await WithRepoAsync(r => r.SaveManualAsync("NSG1", 2026, 30, true, AdminA, Now));
        long? batchId = await WithRepoAsync(r => r.TryStartBatchAsync("leave.xlsx", 2026, AdminB, Now.AddMinutes(1), TimeSpan.FromMinutes(15)));
        Assert.NotNull(batchId);

        LeaveBulkResult result = await WithRepoAsync(r => r.ApplyBulkAsync(batchId!.Value, 2026, 3,
            new[] { new LeaveBulkRow(2, "NSG1", 35), new LeaveBulkRow(3, "NSG2", 4), new LeaveBulkRow(4, "NSG3", 0) }, AdminB, Now.AddMinutes(2)));

        Assert.Equal((LeaveBulkOutcome.Committed, 2, 1, 0), (result.Outcome, result.Created, result.Updated, result.Unchanged));
        await using AppDbContext context = Context();
        PersonnelLeaveRecord changed = await context.PersonnelLeaveRecords.AsNoTracking().SingleAsync(r => r.NsgNumber == "NSG1");
        Assert.Equal((35, AdminA, AdminB), (changed.LeaveTaken, changed.CreatedBy, changed.UpdatedBy));
        Assert.Equal(Now, changed.CreatedAtIst);                                                          // creation fields preserved
        Assert.Equal(3, await context.PersonnelLeaveHistory.CountAsync(h => h.UploadBatchId == batchId));
        PersonnelLeaveUploadBatch batch = await context.PersonnelLeaveUploadBatches.AsNoTracking().SingleAsync();
        Assert.Equal((LeaveUploadStatus.Completed, 3, 2, 1, null), (batch.UploadStatus, batch.TotalRows, batch.CreatedCount, batch.UpdatedCount, batch.ProcessingLock));

        // The lock is free again.
        Assert.NotNull(await WithRepoAsync(r => r.TryStartBatchAsync("next.xlsx", 2026, AdminA, Now.AddMinutes(3), TimeSpan.FromMinutes(15))));
    }

    [MySqlFact]
    public async Task A_failure_inside_the_bulk_transaction_rolls_back_every_record_and_history_row_and_is_verified_from_the_database()
    {
        await WithRepoAsync(r => r.SaveManualAsync("NSG1", 2026, 30, true, AdminA, Now));
        long? batchId = await WithRepoAsync(r => r.TryStartBatchAsync("leave.xlsx", 2026, AdminB, Now.AddMinutes(1), TimeSpan.FromMinutes(15)));

        // The third row's NSG number is longer than the column: MySQL (strict mode) rejects it after the first two statements were prepared.
        LeaveBulkResult result = await WithRepoAsync(r => r.ApplyBulkAsync(batchId!.Value, 2026, 4,
            new[]
            {
                new LeaveBulkRow(2, "NSG1", 99),                       // would update the existing record
                new LeaveBulkRow(3, "NSG2", 4),                        // would create
                new LeaveBulkRow(4, new string('X', 60), 5)            // fails
            }, AdminB, Now.AddMinutes(2)));

        Assert.Equal(LeaveBulkOutcome.RolledBackFailure, result.Outcome);
        await using AppDbContext context = Context();
        PersonnelLeaveRecord untouched = await context.PersonnelLeaveRecords.AsNoTracking().SingleAsync();   // only the original row exists
        Assert.Equal(("NSG1", 30, 1, AdminA), (untouched.NsgNumber, untouched.LeaveTaken, untouched.RowVersion, untouched.UpdatedBy));
        Assert.Equal(1, await context.PersonnelLeaveHistory.CountAsync());                                    // only the manual create
        PersonnelLeaveUploadBatch batch = await context.PersonnelLeaveUploadBatches.AsNoTracking().SingleAsync();
        Assert.Equal(LeaveUploadStatus.Processing, batch.UploadStatus);                                       // the batch row was rolled back with the rest

        // The caller (service) then records the failure durably, outside the rolled-back transaction.
        await WithRepoAsync(async r =>
        {
            await r.FinishFailedBatchAsync(batchId.Value, LeaveUploadStatus.ProcessingFailed, 4, 4, new[] { new LeaveUploadIssue(null, null, "rolled back") }, Now.AddMinutes(3));
            return 0;
        });
        await using AppDbContext after = Context();
        PersonnelLeaveUploadBatch failed = await after.PersonnelLeaveUploadBatches.AsNoTracking().SingleAsync();
        Assert.Equal((LeaveUploadStatus.ProcessingFailed, null), (failed.UploadStatus, failed.ProcessingLock));
        Assert.Single(await after.PersonnelLeaveUploadErrors.AsNoTracking().ToListAsync());
    }

    [MySqlFact]
    public async Task The_database_allows_one_running_batch_a_failed_batch_frees_the_lock_and_a_completed_batch_is_never_overwritten()
    {
        long? first = await WithRepoAsync(r => r.TryStartBatchAsync("a.xlsx", 2026, AdminA, Now, TimeSpan.FromMinutes(15)));
        long? second = await WithRepoAsync(r => r.TryStartBatchAsync("b.xlsx", 2026, AdminB, Now.AddMinutes(1), TimeSpan.FromMinutes(15)));
        Assert.NotNull(first);
        Assert.Null(second);                                                  // the unique lock refused it

        // Started together: exactly one wins.
        await WithRepoAsync(async r => { await r.FinishFailedBatchAsync(first!.Value, LeaveUploadStatus.ValidationFailed, 1, 1, new[] { new LeaveUploadIssue(2, "NSG1", "bad") }, Now); return 0; });
        long?[] racers = await Task.WhenAll(Enumerable.Range(1, 6).Select(i => WithRepoAsync(r => r.TryStartBatchAsync($"{i}.xlsx", 2026, AdminA, Now.AddMinutes(5), TimeSpan.FromMinutes(15)))));
        Assert.Equal(1, racers.Count(id => id != null));

        long winner = racers.Single(id => id != null)!.Value;
        await WithRepoAsync(r => r.ApplyBulkAsync(winner, 2026, 1, new[] { new LeaveBulkRow(2, "NSG1", 5) }, AdminA, Now.AddMinutes(6)));
        await WithRepoAsync(async r => { await r.FinishFailedBatchAsync(winner, LeaveUploadStatus.ProcessingFailed, 1, 1, new[] { new LeaveUploadIssue(null, null, "late failure") }, Now.AddMinutes(7)); return 0; });

        await using AppDbContext context = Context();
        Assert.Equal(LeaveUploadStatus.Completed, (await context.PersonnelLeaveUploadBatches.AsNoTracking().SingleAsync(b => b.Id == winner)).UploadStatus);
        Assert.Empty(await context.PersonnelLeaveUploadErrors.AsNoTracking().Where(e => e.BatchId == winner).ToListAsync());
    }

    [MySqlFact]
    public async Task An_abandoned_processing_batch_is_closed_after_the_stale_period_and_stops_blocking()
    {
        long? stuck = await WithRepoAsync(r => r.TryStartBatchAsync("crashed.xlsx", 2026, AdminA, Now, TimeSpan.FromMinutes(15)));

        Assert.Null(await WithRepoAsync(r => r.TryStartBatchAsync("b.xlsx", 2026, AdminB, Now.AddMinutes(10), TimeSpan.FromMinutes(15))));
        long? next = await WithRepoAsync(r => r.TryStartBatchAsync("c.xlsx", 2026, AdminB, Now.AddMinutes(16), TimeSpan.FromMinutes(15)));

        Assert.NotNull(next);
        await using AppDbContext context = Context();
        PersonnelLeaveUploadBatch old = await context.PersonnelLeaveUploadBatches.AsNoTracking().SingleAsync(b => b.Id == stuck);
        Assert.Equal((LeaveUploadStatus.ProcessingFailed, null), (old.UploadStatus, old.ProcessingLock));
        Assert.Single(await context.PersonnelLeaveUploadErrors.AsNoTracking().Where(e => e.BatchId == stuck).ToListAsync());
    }

    // ── Reads ──

    [MySqlFact]
    public async Task Personnel_lookup_matches_active_people_ignoring_case_and_returns_the_stored_spelling()
    {
        await SeedPersonAsync("NSG1001");
        await SeedPersonAsync("NSG1002");
        await using (AppDbContext context = Context())
            await context.Database.ExecuteSqlRawAsync("UPDATE `users_pis` SET `is_active` = 0 WHERE `nsg_number` = 'NSG1002'");

        IReadOnlyDictionary<string, string> found = await WithRepoAsync(r => r.GetActivePersonnelNsgNumbersAsync(new[] { "nsg1001", "NSG1002", "NSG9999" }));

        Assert.Equal(new[] { ("NSG1001", "NSG1001") }, found.Select(kv => (kv.Key, kv.Value)).ToArray());
    }

    [MySqlFact]
    public async Task History_and_batch_pages_are_newest_first_with_totals()
    {
        for (int i = 1; i <= 5; i++) await WithRepoAsync(r => r.SaveManualAsync("NSG1", 2026, i, true, AdminA, Now.AddMinutes(i)));

        (IReadOnlyList<PersonnelLeaveHistory> items, int total) = await WithRepoAsync(r => r.GetHistoryPagedAsync("NSG1", 1, 2));

        Assert.Equal((5, new[] { 5, 4 }), (total, items.Select(h => h.NewLeaveTaken).ToArray()));
    }
}
