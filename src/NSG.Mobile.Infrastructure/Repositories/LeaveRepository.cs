using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MySqlConnector;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Application.Leave;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;
using NSG.Mobile.Infrastructure.Persistence;

namespace NSG.Mobile.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="ILeaveRepository"/>. Transactions run inside the execution strategy (retry on transient failure is enabled),
/// so each delegate starts from a clean change tracker and is safe to run twice. Concurrency is settled by the database: the unique key
/// (nsg_number, leave_year), the <c>row_version</c> token of a record and the unique <c>processing_lock</c> of a batch.
/// </summary>
public class LeaveRepository : ILeaveRepository
{
    /// <summary>Largest IN (...) list sent to MySQL in one query.</summary>
    private const int ChunkSize = 500;

    private readonly AppDbContext _context;

    public LeaveRepository(AppDbContext context) => _context = context;

    // ── Personnel ──

    public async Task<IReadOnlyDictionary<string, string>> GetActivePersonnelNsgNumbersAsync(IReadOnlyCollection<string> nsgNumbers, CancellationToken cancellationToken = default)
    {
        Dictionary<string, string> found = new();
        foreach (List<string> chunk in Chunks(nsgNumbers.Distinct(StringComparer.OrdinalIgnoreCase)))
        {
            List<string> stored = await _context.UsersPis.AsNoTracking()
                .Where(p => p.IsActive && chunk.Contains(p.NsgNumber))
                .Select(p => p.NsgNumber)
                .ToListAsync(cancellationToken);
            foreach (string nsg in stored) found[nsg.ToUpperInvariant()] = nsg;
        }

        return found;
    }

    public async Task<string?> GetNsgNumberOfMobileProfileAsync(long mobileProfileId, CancellationToken cancellationToken = default)
        => await _context.MobileUserProfiles.AsNoTracking()
            .Where(p => p.Id == mobileProfileId && p.IsActive)
            .Select(p => p.NsgNumber)
            .FirstOrDefaultAsync(cancellationToken);

    // ── Records ──

    public async Task<PersonnelLeaveRecord?> GetRecordAsync(string nsgNumber, int year, CancellationToken cancellationToken = default)
        => await _context.PersonnelLeaveRecords.AsNoTracking()
            .FirstOrDefaultAsync(r => r.NsgNumber == nsgNumber && r.LeaveYear == year, cancellationToken);

    public async Task<LeaveSaveResult> SaveManualAsync(
        string nsgNumber, int year, int leaveTaken, bool createIfMissing, long adminProfileId, DateTime nowIst, CancellationToken cancellationToken = default)
    {
        for (int attempt = 1; attempt <= LeaveLimits.MaxManualAttempts; attempt++)
        {
            LeaveSaveResult? result = await _context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                _context.ChangeTracker.Clear();
                await using IDbContextTransaction transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
                try
                {
                    PersonnelLeaveRecord? record = await _context.PersonnelLeaveRecords
                        .FirstOrDefaultAsync(r => r.NsgNumber == nsgNumber && r.LeaveYear == year, cancellationToken);

                    if (record == null)
                    {
                        if (!createIfMissing) return new LeaveSaveResult(LeaveSaveOutcome.NotFound, null, null);

                        // On creation the update fields equal the creation fields.
                        record = new PersonnelLeaveRecord
                        {
                            NsgNumber = nsgNumber, LeaveYear = year, LeaveTaken = leaveTaken, RowVersion = 1,
                            CreatedAtIst = nowIst, CreatedBy = adminProfileId, UpdatedAtIst = nowIst, UpdatedBy = adminProfileId
                        };
                        _context.PersonnelLeaveRecords.Add(record);
                        await _context.SaveChangesAsync(cancellationToken);   // unique-key violation here = someone created it first

                        _context.PersonnelLeaveHistory.Add(NewHistory(record, null, LeaveChangeSource.Manual, null, adminProfileId, nowIst));
                        await _context.SaveChangesAsync(cancellationToken);
                        await transaction.CommitAsync(cancellationToken);
                        return new LeaveSaveResult(LeaveSaveOutcome.Created, record, null);
                    }

                    if (record.LeaveTaken == leaveTaken)
                        return new LeaveSaveResult(LeaveSaveOutcome.Unchanged, record, record.LeaveTaken);

                    int old = record.LeaveTaken;
                    record.LeaveTaken = leaveTaken;
                    record.UpdatedBy = adminProfileId;        // creation fields are never touched
                    record.UpdatedAtIst = nowIst;
                    record.RowVersion++;
                    _context.PersonnelLeaveHistory.Add(NewHistory(record, old, LeaveChangeSource.Manual, null, adminProfileId, nowIst));
                    await _context.SaveChangesAsync(cancellationToken);   // UPDATE ... WHERE row_version = <read value>
                    await transaction.CommitAsync(cancellationToken);
                    return new LeaveSaveResult(LeaveSaveOutcome.Updated, record, old);
                }
                catch (DbUpdateConcurrencyException)
                {
                    return null;   // another writer changed the row between read and write: retry on fresh data
                }
                catch (DbUpdateException ex) when (IsDuplicateKey(ex))
                {
                    return null;   // another writer created the same (nsg_number, leave_year): retry, which will now update it
                }
            });

            if (result != null)
            {
                if (result.Record != null) _context.Entry(result.Record).State = EntityState.Detached;
                return result;
            }
        }

        return new LeaveSaveResult(LeaveSaveOutcome.ConcurrencyConflict, null, null);
    }

    public async Task<(IReadOnlyList<PersonnelLeaveHistory> Items, int TotalCount)> GetHistoryPagedAsync(
        string nsgNumber, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        IQueryable<PersonnelLeaveHistory> query = _context.PersonnelLeaveHistory.AsNoTracking().Where(h => h.NsgNumber == nsgNumber);
        int total = await query.CountAsync(cancellationToken);
        List<PersonnelLeaveHistory> items = await query
            .OrderByDescending(h => h.ChangedAtIst).ThenByDescending(h => h.Id)
            .Skip((pageNumber - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    // ── Bulk upload ──

    public async Task<long?> TryStartBatchAsync(
        string fileName, int year, long adminProfileId, DateTime nowIst, TimeSpan staleAfter, CancellationToken cancellationToken = default)
    {
        _context.ChangeTracker.Clear();

        // A batch left in Processing by a crashed host would hold the lock for ever: close it once it is clearly abandoned.
        DateTime cutoff = nowIst - staleAfter;
        List<PersonnelLeaveUploadBatch> abandoned = await _context.PersonnelLeaveUploadBatches
            .Where(b => b.UploadStatus == LeaveUploadStatus.Processing && b.CreatedAtIst < cutoff)
            .ToListAsync(cancellationToken);
        if (abandoned.Count > 0)
        {
            foreach (PersonnelLeaveUploadBatch old in abandoned)
            {
                old.UploadStatus = LeaveUploadStatus.ProcessingFailed;
                old.ProcessingLock = null;
                old.CompletedAtIst = nowIst;
                _context.PersonnelLeaveUploadErrors.Add(new PersonnelLeaveUploadError
                {
                    BatchId = old.Id, RowNumber = null, CreatedAtIst = nowIst,
                    ErrorMessage = "The upload did not finish (the server stopped or lost its database connection) and was closed automatically. Check the leave records before uploading again."
                });
            }

            await _context.SaveChangesAsync(cancellationToken);
            _context.ChangeTracker.Clear();
        }

        PersonnelLeaveUploadBatch batch = new()
        {
            FileName = fileName, LeaveYear = year, UploadStatus = LeaveUploadStatus.Processing, ProcessingLock = true,
            CreatedAtIst = nowIst, CreatedBy = adminProfileId
        };
        _context.PersonnelLeaveUploadBatches.Add(batch);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsDuplicateKey(ex))
        {
            _context.ChangeTracker.Clear();
            return null;   // another upload holds the lock
        }

        long id = batch.Id;
        _context.ChangeTracker.Clear();
        return id;
    }

    public async Task<LeaveBulkResult> ApplyBulkAsync(
        long batchId, int year, int totalRows, IReadOnlyList<LeaveBulkRow> rows, long adminProfileId, DateTime nowIst, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                _context.ChangeTracker.Clear();
                await using IDbContextTransaction transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

                PersonnelLeaveUploadBatch batch = await _context.PersonnelLeaveUploadBatches.FirstAsync(b => b.Id == batchId, cancellationToken);
                if (batch.UploadStatus == LeaveUploadStatus.Completed)
                    return new LeaveBulkResult(LeaveBulkOutcome.Committed, batch.CreatedCount, batch.UpdatedCount, batch.UnchangedCount);   // an earlier, ambiguous attempt did commit
                if (batch.UploadStatus != LeaveUploadStatus.Processing)
                    throw new InvalidOperationException("The upload batch is no longer being processed.");

                Dictionary<string, PersonnelLeaveRecord> existing = new(StringComparer.OrdinalIgnoreCase);
                foreach (List<string> chunk in Chunks(rows.Select(r => r.NsgNumber)))
                {
                    List<PersonnelLeaveRecord> found = await _context.PersonnelLeaveRecords
                        .Where(r => r.LeaveYear == year && chunk.Contains(r.NsgNumber))
                        .ToListAsync(cancellationToken);
                    foreach (PersonnelLeaveRecord record in found) existing[record.NsgNumber] = record;
                }

                int created = 0, updated = 0, unchanged = 0;
                List<(PersonnelLeaveRecord Record, int? Old)> changes = new();
                foreach (LeaveBulkRow row in rows)
                {
                    if (existing.TryGetValue(row.NsgNumber, out PersonnelLeaveRecord? record))
                    {
                        if (record.LeaveTaken == row.LeaveTaken) { unchanged++; continue; }

                        changes.Add((record, record.LeaveTaken));
                        record.LeaveTaken = row.LeaveTaken;
                        record.UpdatedBy = adminProfileId;       // creation fields are never touched
                        record.UpdatedAtIst = nowIst;
                        record.RowVersion++;
                        updated++;
                    }
                    else
                    {
                        PersonnelLeaveRecord added = new()
                        {
                            NsgNumber = row.NsgNumber, LeaveYear = year, LeaveTaken = row.LeaveTaken, RowVersion = 1,
                            CreatedAtIst = nowIst, CreatedBy = adminProfileId, UpdatedAtIst = nowIst, UpdatedBy = adminProfileId
                        };
                        _context.PersonnelLeaveRecords.Add(added);
                        changes.Add((added, null));
                        created++;
                    }
                }

                // Records first (the history rows need the generated ids), then history and the batch result, all in the same transaction.
                await _context.SaveChangesAsync(cancellationToken);
                foreach ((PersonnelLeaveRecord record, int? old) in changes)
                    _context.PersonnelLeaveHistory.Add(NewHistory(record, old, LeaveChangeSource.Bulk, batchId, adminProfileId, nowIst));

                batch.TotalRows = totalRows;
                batch.CreatedCount = created;
                batch.UpdatedCount = updated;
                batch.UnchangedCount = unchanged;
                batch.FailedCount = 0;
                batch.UploadStatus = LeaveUploadStatus.Completed;
                batch.ProcessingLock = null;
                batch.CompletedAtIst = nowIst;
                await _context.SaveChangesAsync(cancellationToken);

                await transaction.CommitAsync(cancellationToken);
                _context.ChangeTracker.Clear();
                return new LeaveBulkResult(LeaveBulkOutcome.Committed, created, updated, unchanged);
            });
        }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException || ex is DbUpdateException dbEx && IsDuplicateKey(dbEx))
        {
            // The delegate's transaction was disposed without commit: everything it wrote is rolled back.
            _context.ChangeTracker.Clear();
            return new LeaveBulkResult(LeaveBulkOutcome.RolledBackConflict, 0, 0, 0);
        }
        catch (Exception)
        {
            return await VerifyOutcomeAsync(batchId);
        }
    }

    /// <summary>
    /// After a failure inside the transaction: the batch's status is written by that same transaction, so reading it tells what really happened.
    /// Completed = it did commit; Processing = it did not. If even that read fails, nothing is known.
    /// </summary>
    private async Task<LeaveBulkResult> VerifyOutcomeAsync(long batchId)
    {
        try
        {
            _context.ChangeTracker.Clear();
            var state = await _context.PersonnelLeaveUploadBatches.AsNoTracking()
                .Where(b => b.Id == batchId)
                .Select(b => new { b.UploadStatus, b.CreatedCount, b.UpdatedCount, b.UnchangedCount })
                .FirstOrDefaultAsync(CancellationToken.None);

            if (state == null) return new LeaveBulkResult(LeaveBulkOutcome.Unknown, 0, 0, 0);
            return state.UploadStatus == LeaveUploadStatus.Completed
                ? new LeaveBulkResult(LeaveBulkOutcome.Committed, state.CreatedCount, state.UpdatedCount, state.UnchangedCount)
                : new LeaveBulkResult(LeaveBulkOutcome.RolledBackFailure, 0, 0, 0);
        }
        catch (Exception)
        {
            return new LeaveBulkResult(LeaveBulkOutcome.Unknown, 0, 0, 0);
        }
    }

    public async Task FinishFailedBatchAsync(
        long batchId, LeaveUploadStatus status, int totalRows, int failedCount, IReadOnlyList<LeaveUploadIssue> issues, DateTime nowIst, CancellationToken cancellationToken = default)
    {
        await _context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            _context.ChangeTracker.Clear();
            await using IDbContextTransaction transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

            // Only a batch that is still Processing can be failed: a Completed batch is never overwritten.
            PersonnelLeaveUploadBatch? batch = await _context.PersonnelLeaveUploadBatches
                .FirstOrDefaultAsync(b => b.Id == batchId && b.UploadStatus == LeaveUploadStatus.Processing, cancellationToken);
            if (batch == null) return;

            batch.UploadStatus = status;
            batch.TotalRows = totalRows;
            batch.FailedCount = failedCount;
            batch.ProcessingLock = null;
            batch.CompletedAtIst = nowIst;
            foreach (LeaveUploadIssue issue in issues)
            {
                _context.PersonnelLeaveUploadErrors.Add(new PersonnelLeaveUploadError
                {
                    BatchId = batchId, RowNumber = issue.RowNumber, NsgNumber = issue.NsgNumber, ErrorMessage = issue.Message, CreatedAtIst = nowIst
                });
            }

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            _context.ChangeTracker.Clear();
        });
    }

    public async Task<PersonnelLeaveUploadBatch?> GetBatchAsync(long batchId, CancellationToken cancellationToken = default)
        => await _context.PersonnelLeaveUploadBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == batchId, cancellationToken);

    public async Task<(IReadOnlyList<PersonnelLeaveUploadBatch> Items, int TotalCount)> GetBatchesPagedAsync(
        int pageNumber, int pageSize, LeaveUploadStatus? status, CancellationToken cancellationToken = default)
    {
        IQueryable<PersonnelLeaveUploadBatch> query = _context.PersonnelLeaveUploadBatches.AsNoTracking();
        if (status.HasValue) query = query.Where(b => b.UploadStatus == status.Value);

        int total = await query.CountAsync(cancellationToken);
        List<PersonnelLeaveUploadBatch> items = await query
            .OrderByDescending(b => b.CreatedAtIst).ThenByDescending(b => b.Id)
            .Skip((pageNumber - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public async Task<(IReadOnlyList<PersonnelLeaveUploadError> Items, int TotalCount)> GetBatchErrorsPagedAsync(
        long batchId, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        IQueryable<PersonnelLeaveUploadError> query = _context.PersonnelLeaveUploadErrors.AsNoTracking().Where(e => e.BatchId == batchId);
        int total = await query.CountAsync(cancellationToken);
        List<PersonnelLeaveUploadError> items = await query
            .OrderBy(e => e.RowNumber).ThenBy(e => e.Id)
            .Skip((pageNumber - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    // ── Helpers ──

    private static PersonnelLeaveHistory NewHistory(
        PersonnelLeaveRecord record, int? old, LeaveChangeSource source, long? batchId, long adminProfileId, DateTime nowIst) => new()
    {
        LeaveRecordId = record.Id,
        NsgNumber = record.NsgNumber,
        LeaveYear = record.LeaveYear,
        OldLeaveTaken = old,
        NewLeaveTaken = record.LeaveTaken,
        ChangeSource = source,
        UploadBatchId = batchId,
        ChangedBy = adminProfileId,
        ChangedAtIst = nowIst
    };

    private static IEnumerable<List<string>> Chunks(IEnumerable<string> values)
    {
        List<string> chunk = new(ChunkSize);
        foreach (string value in values)
        {
            chunk.Add(value);
            if (chunk.Count < ChunkSize) continue;
            yield return chunk;
            chunk = new List<string>(ChunkSize);
        }

        if (chunk.Count > 0) yield return chunk;
    }

    private static bool IsDuplicateKey(DbUpdateException ex)
        => ex.InnerException is MySqlException { ErrorCode: MySqlErrorCode.DuplicateKeyEntry };
}
