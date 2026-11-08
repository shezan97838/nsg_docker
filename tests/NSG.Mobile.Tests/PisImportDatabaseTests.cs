using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Application.Pis.Import;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;
using NSG.Mobile.Infrastructure.Persistence;
using NSG.Mobile.Infrastructure.Repositories;

namespace NSG.Mobile.Tests;

[CollectionDefinition("mysql-pis-import", DisableParallelization = true)]
public sealed class MySqlPisImportCollection { }

/// <summary>Forwards to the real repository; tests override single members to inject races and failures.</summary>
internal class DelegatingImportRepository : IPisImportRepository
{
    protected readonly IPisImportRepository Inner;
    public DelegatingImportRepository(IPisImportRepository inner) => Inner = inner;

    public virtual Task<long> CreateBatchAsync(PisUploadBatch b, CancellationToken ct) => Inner.CreateBatchAsync(b, ct);
    public virtual Task<IReadOnlyDictionary<string, UserPis>> GetByNsgNumbersAsync(IReadOnlyCollection<string> n, CancellationToken ct) => Inner.GetByNsgNumbersAsync(n, ct);
    public virtual Task<IReadOnlyDictionary<string, string>> GetNsgNumbersByMobileAsync(IReadOnlyCollection<string> m, CancellationToken ct) => Inner.GetNsgNumbersByMobileAsync(m, ct);
    public virtual Task<PisRowSaveResult> SaveRowAsync(long batchId, int row, UserPis v, bool insert, long adminId, DateTime now, CancellationToken ct) => Inner.SaveRowAsync(batchId, row, v, insert, adminId, now, ct);
    public virtual Task CompleteBatchAsync(PisUploadBatch s, CancellationToken ct) => Inner.CompleteBatchAsync(s, ct);
    public virtual Task SaveErrorsAsync(IReadOnlyList<PisUploadBatchError> e, CancellationToken ct) => Inner.SaveErrorsAsync(e, ct);
    public Task<(IReadOnlyList<PisUploadBatch> Items, int TotalCount)> GetBatchesAsync(long a, int p, int s, CancellationToken ct) => Inner.GetBatchesAsync(a, p, s, ct);
    public Task<PisUploadBatch?> GetBatchAsync(long a, long id, CancellationToken ct) => Inner.GetBatchAsync(a, id, ct);
    public Task<(IReadOnlyList<PisUploadBatchRecord> Items, int TotalCount)> GetBatchRecordsAsync(long id, int p, int s, CancellationToken ct) => Inner.GetBatchRecordsAsync(id, p, s, ct);
    public Task<(IReadOnlyList<PisUploadBatchError> Items, int TotalCount)> GetBatchErrorsAsync(long id, int p, int s, CancellationToken ct) => Inner.GetBatchErrorsAsync(id, p, s, ct);
}

/// <summary>
/// DATABASE-BACKED audit tests of the PIS upload traceability on real MySQL 8: per-row transactions, rollback of the PIS row when its batch
/// mapping cannot be saved, persisted row errors, batch summaries, admin scoping and the unique mapping index. They run ONLY when NSG_TEST_MYSQL
/// names a disposable database ending in "_test" (ApiFactory drops and recreates it through the migrations).
/// </summary>
[Collection("mysql-pis-import")]
public sealed class PisImportDatabaseTests : IAsyncLifetime
{
    private const long AdminA = 9101, AdminB = 9102;
    private ApiFactory _factory = null!;
    private int _mobileCounter;

    public async Task InitializeAsync()
    {
        _factory = new ApiFactory();
        await _factory.ResetDatabaseAsync();
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private AppDbContext Context()
    {
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(_factory.ConnectionString, ServerVersion.AutoDetect(_factory.ConnectionString), my => my.EnableRetryOnFailure(3))
            .Options;
        return new AppDbContext(options);
    }

    private string Mobile() => (9800000000L + ++_mobileCounter).ToString();

    private static byte[] Sheet(params object?[][] people) => PisXlsx.Build(people);

    /// <summary>A service over the real repository (own DbContext, like one request). <paramref name="wrap"/> may inject faults.</summary>
    private async Task<Result<PisUploadResultDto>> UploadAsync(byte[] file, PisUploadType type, long admin = AdminA,
        Func<IPisImportRepository, IPisImportRepository>? wrap = null)
    {
        await using AppDbContext context = Context();
        IPisImportRepository repository = new PisImportRepository(context, NullLogger<PisImportRepository>.Instance);
        if (wrap != null) repository = wrap(repository);
        PisImportService service = new(repository, new PisFakeEncryption(), new FakeAudit(), new FakeCurrentUser { AdminProfileId = admin }, NullLogger<PisImportService>.Instance);
        return await service.UploadAsync(new PisUploadFile(new MemoryStream(file), "pis.xlsx", file.Length), type, CancellationToken.None);
    }

    private async Task<PisUploadResultDto> OkAsync(byte[] file, PisUploadType type, long admin = AdminA, Func<IPisImportRepository, IPisImportRepository>? wrap = null)
    {
        Result<PisUploadResultDto> result = await UploadAsync(file, type, admin, wrap);
        Assert.True(result.IsSuccess, result.ErrorMessage);
        return result.Data!;
    }

    private async Task SeedAsync(string nsg, string mobile, string name = "Seed", long assignedTo = AdminA)
    {
        await using AppDbContext c = Context();
        c.UsersPis.Add(new UserPis { NsgNumber = nsg, MobileNumber = mobile, FullName = name, IsActive = false, MobileAppActivationStatus = MobileAppActivationStatus.Approved,
            CreatedBy = 5, CreatedAtIst = new DateTime(2020, 1, 1, 8, 0, 0), ServiceNumber = "OLD", AadhaarNumberEncrypted = "enc:old", AssignedTo = assignedTo });
        await c.SaveChangesAsync();
    }

    private sealed class Racer : DelegatingImportRepository
    {
        public Func<UserPis, bool, Task>? Before { get; set; }
        public Racer(IPisImportRepository inner) : base(inner) { }

        public override async Task<PisRowSaveResult> SaveRowAsync(long batchId, int row, UserPis v, bool insert, long adminId, DateTime now, CancellationToken ct)
        {
            if (Before != null) await Before(v, insert);
            return await Inner.SaveRowAsync(batchId, row, v, insert, adminId, now, ct);
        }
    }

    // ═══ 1-2. Success: PIS row and batch mapping together ═══

    [MySqlFact]
    public async Task Insert_saves_the_person_the_mapping_and_the_batch_summary_with_ciphertext_only()
    {
        PisUploadResultDto result = await OkAsync(Sheet(PisXlsx.Person("N1", Mobile())), PisUploadType.Upsert);

        Assert.Equal((1, 1, 0, 0, 0), (result.TotalRows, result.Inserted, result.Updated, result.Failed, result.Conflicts));
        await using AppDbContext c = Context();
        UserPis person = await c.UsersPis.SingleAsync(p => p.NsgNumber == "N1");
        Assert.Equal(((int)AdminA, MobileAppActivationStatus.Pending, true), (person.CreatedBy, person.MobileAppActivationStatus, person.IsActive));
        Assert.StartsWith("enc:", person.AadhaarNumberEncrypted);
        Assert.DoesNotContain("123412341234", person.AadhaarNumberEncrypted);

        PisUploadBatchRecord map = await c.PisUploadBatchRecords.SingleAsync();
        Assert.Equal((result.BatchId, person.Id, "N1", 4, PisRowOperation.Inserted), (map.BatchId, map.PisRecordId, map.NsgNumber, map.RowNumber, map.Operation));
        PisUploadBatch batch = await c.PisUploadBatches.SingleAsync();
        Assert.Equal((PisUploadStatus.Completed, PisUploadType.Upsert, AdminA, "pis.xlsx", 1), (batch.Status, batch.UploadType, batch.UploadedBy, batch.FileName, batch.TotalRows));
        Assert.NotNull(batch.CompletedAtIst);
        Assert.Empty(await c.PisUploadBatchErrors.ToListAsync());
    }

    [MySqlFact]
    public async Task Update_in_both_modes_maps_the_row_as_updated_and_keeps_creation_activation_and_blank_cells()
    {
        string mobile = Mobile();
        await SeedAsync("N1", mobile, assignedTo: AdminB);
        await SeedAsync("N2", Mobile(), assignedTo: AdminB);
        object?[] blankService = PisXlsx.Person("N1", mobile, "Changed"); blankService[2] = null;

        PisUploadResultDto upsert = await OkAsync(Sheet(blankService), PisUploadType.Upsert, AdminB);
        PisUploadResultDto updateOnly = await OkAsync(Sheet(PisXlsx.Person("N2", Mobile(), "Changed2")), PisUploadType.UpdateOnly, AdminB);

        Assert.Equal((0, 1), (upsert.Inserted, upsert.Updated));
        Assert.Equal((0, 1), (updateOnly.Inserted, updateOnly.Updated));
        await using AppDbContext c = Context();
        UserPis n1 = await c.UsersPis.SingleAsync(p => p.NsgNumber == "N1");
        Assert.Equal(("Changed", "OLD", false, MobileAppActivationStatus.Approved, 5, new DateTime(2020, 1, 1, 8, 0, 0), (int)AdminB),
            (n1.FullName, n1.ServiceNumber, n1.IsActive, n1.MobileAppActivationStatus, n1.CreatedBy, n1.CreatedAtIst, n1.UpdatedBy));
        Assert.NotNull(n1.UpdatedAtIst);
        Assert.Equal(2, await c.PisUploadBatchRecords.CountAsync(r => r.Operation == PisRowOperation.Updated));
        Assert.All(await c.PisUploadBatchRecords.ToListAsync(), r => Assert.NotEqual(0, r.PisRecordId));
    }

    // ═══ 3-4, 6-8. A MySQL failure on one row: rollback, separate error write, processing continues ═══

    [MySqlFact]
    public async Task Insert_failure_rolls_back_persists_the_error_and_the_next_row_still_saves()
    {
        string raced = Mobile();
        Racer Wrap(IPisImportRepository inner) => new(inner)
        {
            // Another request takes N2's mobile number between the pre-check and the insert: a genuine MySQL unique violation.
            Before = async (v, insert) => { if (v.NsgNumber == "N2") await SeedAsync("OWNER", raced); }
        };

        PisUploadResultDto result = await OkAsync(Sheet(PisXlsx.Person("N1", Mobile()), PisXlsx.Person("N2", raced), PisXlsx.Person("N3", Mobile())),
            PisUploadType.Upsert, wrap: Wrap);

        Assert.Equal((3, 2, 0, 0, 1), (result.TotalRows, result.Inserted, result.Updated, result.Failed, result.Conflicts));
        PisUploadErrorDto e = Assert.Single(result.Errors);
        Assert.Equal((5, "N2", "conflict", "DUPLICATE_MOBILE_NUMBER", "OWNER"), (e.RowNumber, e.NsgNumber, e.Status, e.ConflictType, e.ConflictingNsgNumber));

        await using AppDbContext c = Context();
        Assert.False(await c.UsersPis.AnyAsync(p => p.NsgNumber == "N2"));                       // rolled back
        Assert.False(await c.PisUploadBatchRecords.AnyAsync(r => r.NsgNumber == "N2"));          // never a successful mapping
        Assert.Equal(new[] { "N1", "N3" }, await c.PisUploadBatchRecords.OrderBy(r => r.RowNumber).Select(r => r.NsgNumber).ToArrayAsync());
        PisUploadBatchError saved = await c.PisUploadBatchErrors.SingleAsync();                  // persisted in its own transaction
        Assert.Equal((result.BatchId, 5, "N2", PisRowStatus.Conflict, "DUPLICATE_MOBILE_NUMBER", "OWNER", "mobileNumber"),
            (saved.BatchId, saved.RowNumber, saved.NsgNumber, saved.Status, saved.ConflictType, saved.ConflictingNsgNumber, saved.Field));
        Assert.NotEqual(default, saved.CreatedAtIst);

        PisUploadBatch batch = await c.PisUploadBatches.SingleAsync();
        Assert.Equal(PisUploadStatus.CompletedWithErrors, batch.Status);
        // Summary equals what the tracking tables contain; every row exactly once.
        Assert.Equal(batch.TotalRows, batch.InsertedCount + batch.UpdatedCount + batch.FailedCount + batch.ConflictCount);
        Assert.Equal(batch.InsertedCount + batch.UpdatedCount, await c.PisUploadBatchRecords.CountAsync());
        Assert.Equal(batch.FailedCount + batch.ConflictCount, await c.PisUploadBatchErrors.Select(x => x.RowNumber).Distinct().CountAsync());
    }

    [MySqlFact]
    public async Task Update_failure_leaves_the_person_unchanged_persists_the_error_and_has_no_mapping()
    {
        string own = Mobile(), raced = Mobile();
        await SeedAsync("N1", own, "Before");
        Racer Wrap(IPisImportRepository inner) => new(inner) { Before = async (v, insert) => await SeedAsync("OWNER", raced) };

        PisUploadResultDto result = await OkAsync(Sheet(PisXlsx.Person("N1", raced, "After")), PisUploadType.UpdateOnly, wrap: Wrap);

        Assert.Equal((0, 0, 0, 1), (result.Inserted, result.Updated, result.Failed, result.Conflicts));
        await using AppDbContext c = Context();
        UserPis n1 = await c.UsersPis.SingleAsync(p => p.NsgNumber == "N1");
        Assert.Equal(("Before", own, null), (n1.FullName, n1.MobileNumber, n1.UpdatedBy?.ToString()));
        Assert.Empty(await c.PisUploadBatchRecords.ToListAsync());
        Assert.Equal("DUPLICATE_MOBILE_NUMBER", (await c.PisUploadBatchErrors.SingleAsync()).ConflictType);
    }

    // ═══ 5. A mapping failure rolls the PIS write back ═══

    [MySqlFact]
    public async Task A_mapping_that_cannot_be_saved_rolls_back_the_person_insert_and_update()
    {
        await SeedAsync("N1", Mobile(), "Before");
        await using AppDbContext context = Context();
        PisImportRepository repository = new(context, NullLogger<PisImportRepository>.Instance);

        // Batch 999999 does not exist: the mapping row violates its foreign key AFTER the PIS write succeeded.
        PisRowSaveResult insert = await repository.SaveRowAsync(999_999, 4, new UserPis { NsgNumber = "NEW", MobileNumber = Mobile(), FullName = "X" }, true, AdminA, DateTime.Now, CancellationToken.None);
        long existingId = await context.UsersPis.AsNoTracking().Where(p => p.NsgNumber == "N1").Select(p => p.Id).SingleAsync();
        PisRowSaveResult update = await repository.SaveRowAsync(999_999, 4, new UserPis { Id = existingId, NsgNumber = "N1", MobileNumber = Mobile(), FullName = "After" }, false, AdminA, DateTime.Now, CancellationToken.None);

        Assert.Equal((PisRowSaveStatus.Failed, PisRowSaveStatus.Failed), (insert.Status, update.Status));
        await using AppDbContext check = Context();
        Assert.False(await check.UsersPis.AnyAsync(p => p.NsgNumber == "NEW"));
        Assert.Equal("Before", (await check.UsersPis.SingleAsync(p => p.NsgNumber == "N1")).FullName);
        Assert.Empty(await check.PisUploadBatchRecords.ToListAsync());
    }

    // ═══ Constraints ═══

    [MySqlFact]
    public async Task A_worksheet_row_can_be_mapped_only_once_per_batch()
    {
        PisUploadResultDto result = await OkAsync(Sheet(PisXlsx.Person("N1", Mobile())), PisUploadType.Upsert);
        await using AppDbContext c = Context();

        Exception ex = await Assert.ThrowsAsync<DbUpdateException>(async () =>
        {
            c.PisUploadBatchRecords.Add(new PisUploadBatchRecord { BatchId = result.BatchId, RowNumber = 4, PisRecordId = 1, NsgNumber = "N1", Operation = PisRowOperation.Updated });
            await c.SaveChangesAsync();
        });
        Assert.Equal(MySqlErrorCode.DuplicateKeyEntry, ((MySqlException)ex.InnerException!).ErrorCode);
    }

    [MySqlFact]
    public async Task Migration_created_the_tracking_tables_with_the_expected_columns_and_indexes()
    {
        await using AppDbContext c = Context();
        var indexes = await c.Database.SqlQueryRaw<string>(
            "SELECT CONCAT(table_name, ':', index_name, ':', non_unique) AS Value FROM information_schema.statistics WHERE table_schema = DATABASE() AND table_name LIKE 'pis_upload%' GROUP BY table_name, index_name, non_unique").ToListAsync();

        Assert.Contains("pis_upload_batch_records:ix_pis_upload_batch_records_batch_id_excel_row_number:0", indexes);   // unique
        Assert.Contains("pis_upload_batch_records:ix_pis_upload_batch_records_pis_record_id:1", indexes);
        Assert.Contains("pis_upload_batches:ix_pis_upload_batches_uploaded_by_created_at_ist:1", indexes);
        Assert.Contains("pis_upload_batch_errors:ix_pis_upload_batch_errors_batch_id_excel_row_number:1", indexes);
        // No foreign key from or to users_pis (ADR-020).
        Assert.Equal(0, await c.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS Value FROM information_schema.referential_constraints WHERE constraint_schema = DATABASE() AND (table_name = 'users_pis' OR referenced_table_name = 'users_pis')").SingleAsync());
    }

    // ═══ 11. Failure handling: finalisation, tracking, interruption ═══

    private sealed class FailingFinalise : DelegatingImportRepository
    {
        public FailingFinalise(IPisImportRepository inner) : base(inner) { }
        public override Task CompleteBatchAsync(PisUploadBatch s, CancellationToken ct)
            => s.Status == PisUploadStatus.Failed ? Inner.CompleteBatchAsync(s, ct) : throw new InvalidOperationException("connection lost");
    }

    [MySqlFact]
    public async Task A_failure_while_finalising_marks_the_batch_failed_keeps_saved_rows_and_reports_the_batch_id()
    {
        Result<PisUploadResultDto> result = await UploadAsync(Sheet(PisXlsx.Person("N1", Mobile())), PisUploadType.Upsert, wrap: r => new FailingFinalise(r));

        Assert.False(result.IsSuccess);
        Assert.Contains("batch 1", result.ErrorMessage);
        await using AppDbContext c = Context();
        PisUploadBatch batch = await c.PisUploadBatches.SingleAsync();
        Assert.Equal(PisUploadStatus.Failed, batch.Status);
        Assert.NotNull(batch.FailureMessage);
        Assert.Equal(1, await c.PisUploadBatchRecords.CountAsync());            // the saved row is still traceable
        Assert.Equal(1, await c.UsersPis.CountAsync(p => p.NsgNumber == "N1"));
    }

    private sealed class FailingErrorWrite : DelegatingImportRepository
    {
        public FailingErrorWrite(IPisImportRepository inner) : base(inner) { }
        public override Task SaveErrorsAsync(IReadOnlyList<PisUploadBatchError> e, CancellationToken ct) => throw new InvalidOperationException("tracking down");
    }

    [MySqlFact]
    public async Task A_failure_to_save_a_row_error_is_surfaced_and_recorded_on_the_batch_not_swallowed()
    {
        object?[] bad = PisXlsx.Person("N2", Mobile()); bad[PisXlsx.Dob] = "garbage";

        PisUploadResultDto result = await OkAsync(Sheet(PisXlsx.Person("N1", Mobile()), bad), PisUploadType.Upsert, wrap: r => new FailingErrorWrite(r));

        Assert.Equal((1, 1), (result.Inserted, result.Failed));
        Assert.Equal(new[] { 5 }, result.ErrorTrackingFailedRows);
        Assert.Single(result.Errors);                                           // still returned to the caller
        await using AppDbContext c = Context();
        PisUploadBatch batch = await c.PisUploadBatches.SingleAsync();
        Assert.Equal(PisUploadStatus.CompletedWithErrors, batch.Status);
        Assert.Contains("5", batch.FailureMessage);
        Assert.Empty(await c.PisUploadBatchErrors.ToListAsync());
    }

    [MySqlFact]
    public async Task Rows_already_processed_and_their_errors_survive_an_interruption()
    {
        object?[] bad = PisXlsx.Person("N1", Mobile()); bad[PisXlsx.Dob] = "garbage";
        // The process dies while saving the third row: rows before it, and the error of row 4, are already durable.
        Racer Wrap(IPisImportRepository inner) => new(inner) { Before = (v, insert) => v.NsgNumber == "N3" ? throw new OperationCanceledException() : Task.CompletedTask };

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            UploadAsync(Sheet(bad, PisXlsx.Person("N2", Mobile()), PisXlsx.Person("N3", Mobile())), PisUploadType.Upsert, wrap: Wrap));

        await using AppDbContext c = Context();
        PisUploadBatch batch = await c.PisUploadBatches.SingleAsync();
        Assert.Equal(PisUploadStatus.Failed, batch.Status);
        Assert.Equal(new[] { "N2" }, await c.PisUploadBatchRecords.Select(r => r.NsgNumber).ToArrayAsync());
        Assert.Equal(1, await c.PisUploadBatchErrors.CountAsync(e => e.RowNumber == 4));
        Assert.False(await c.UsersPis.AnyAsync(p => p.NsgNumber == "N3"));
    }

    // ═══ 12. Sensitive values never reach the error table ═══

    [MySqlFact]
    public async Task Error_rows_never_contain_sensitive_plaintext()
    {
        object?[] bad = PisXlsx.Person("N1", Mobile());
        bad[PisXlsx.Aadhaar] = "999988887777X"; bad[PisXlsx.Pan] = "BADPAN99"; bad[19] = "55556666"; bad[PisXlsx.SalaryAccount] = "12ab";

        PisUploadResultDto result = await OkAsync(Sheet(bad), PisUploadType.Upsert);

        Assert.Equal(1, result.Failed);
        await using AppDbContext c = Context();
        List<PisUploadBatchError> errors = await c.PisUploadBatchErrors.ToListAsync();
        Assert.Equal(4, errors.Count);
        Assert.All(errors, e =>
        {
            Assert.Null(e.Value);
            string all = e.Message + e.Value + e.Field;
            foreach (string secret in new[] { "999988887777", "BADPAN99", "55556666", "12ab" }) Assert.DoesNotContain(secret, all);
        });
        Assert.False(await c.UsersPis.AnyAsync());
    }

    // ═══ 9-10. Admin scoping and batch detail ═══

    [MySqlFact]
    public async Task Batches_are_scoped_to_the_admin_and_the_detail_shows_successful_and_failed_rows()
    {
        object?[] bad = PisXlsx.Person("N2", Mobile()); bad[PisXlsx.Dob] = "garbage";
        PisUploadResultDto a = await OkAsync(Sheet(PisXlsx.Person("N1", Mobile()), bad), PisUploadType.Upsert, AdminA);
        PisUploadResultDto b = await OkAsync(Sheet(PisXlsx.Person("N3", Mobile(), "By B")), PisUploadType.Upsert, AdminB);

        await using AppDbContext context = Context();
        PisImportService Service(long admin) => new(new PisImportRepository(context, NullLogger<PisImportRepository>.Instance), new PisFakeEncryption(),
            new FakeAudit(), new FakeCurrentUser { AdminProfileId = admin }, NullLogger<PisImportService>.Instance);

        Assert.Equal(new[] { a.BatchId }, (await Service(AdminA).GetBatchesAsync(1, 20, CancellationToken.None)).Data!.Items.Select(i => i.BatchId).ToArray());
        Assert.Equal(new[] { b.BatchId }, (await Service(AdminB).GetBatchesAsync(1, 20, CancellationToken.None)).Data!.Items.Select(i => i.BatchId).ToArray());
        Assert.Equal(ApiErrorCodes.NotFound, (await Service(AdminB).GetBatchAsync(a.BatchId, 1, 20, CancellationToken.None)).ErrorCode);   // guessed id

        PisUploadBatchDetailDto detail = (await Service(AdminA).GetBatchAsync(a.BatchId, 1, 20, CancellationToken.None)).Data!;
        Assert.Equal(("UPSERT", 1, 1), (detail.Batch.UploadType, detail.Batch.Inserted, detail.Batch.Failed));
        Assert.Equal(("N1", "INSERTED", 4), (detail.Records.Items[0].NsgNumber, detail.Records.Items[0].Operation, detail.Records.Items[0].RowNumber));
        Assert.Equal(("N2", "INVALID_DATE", 5, "dateOfBirth"), (detail.Errors.Items[0].NsgNumber, detail.Errors.Items[0].ConflictType, detail.Errors.Items[0].RowNumber, detail.Errors.Items[0].Field));

        // Admin B's own record appears in B's batch only; B's upload could not touch A's N1 (created_by and assigned_to stay A, never updated).
        PisUploadBatchDetailDto detailB = (await Service(AdminB).GetBatchAsync(b.BatchId, 1, 20, CancellationToken.None)).Data!;
        Assert.Equal("INSERTED", Assert.Single(detailB.Records.Items).Operation);
        UserPis n1 = await context.UsersPis.AsNoTracking().SingleAsync(p => p.NsgNumber == "N1");
        Assert.Equal(((int)AdminA, (int?)null, (long?)AdminA), (n1.CreatedBy, n1.UpdatedBy, n1.AssignedTo));
    }

    // ═══ 7. Concurrent uploads ═══

    [MySqlFact]
    public async Task Concurrent_uploads_of_the_same_new_person_create_exactly_one_record_and_consistent_summaries()
    {
        byte[] file = Sheet(PisXlsx.Person("N1", Mobile()));

        Result<PisUploadResultDto>[] results = await Task.WhenAll(
            UploadAsync(file, PisUploadType.Upsert, AdminA), UploadAsync(file, PisUploadType.Upsert, AdminB));

        Assert.All(results, r => Assert.True(r.IsSuccess, r.ErrorMessage));
        await using AppDbContext c = Context();
        Assert.Equal(1, await c.UsersPis.CountAsync(p => p.NsgNumber == "N1"));
        Assert.Equal(2, await c.PisUploadBatches.CountAsync());
        Assert.All(results, r => Assert.Equal(1, r.Data!.Inserted + r.Data.Updated + r.Data.Failed + r.Data.Conflicts));
        Assert.Equal(1, results.Sum(r => r.Data!.Inserted));                    // only one of them inserted
        Assert.Equal(await c.PisUploadBatchRecords.CountAsync(), results.Sum(r => r.Data!.Inserted + r.Data.Updated));
    }
}
