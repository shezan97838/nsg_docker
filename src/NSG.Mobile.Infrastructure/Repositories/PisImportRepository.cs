using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;
using NSG.Mobile.Infrastructure.Persistence;

namespace NSG.Mobile.Infrastructure.Repositories;

public class PisImportRepository : IPisImportRepository
{
    private const int ChunkSize = 1000;

    /// <summary>Columns an update must never write.</summary>
    private static readonly HashSet<string> ProtectedProperties = new()
    {
        nameof(UserPis.Id), nameof(UserPis.NsgNumber), nameof(UserPis.CreatedAtIst), nameof(UserPis.CreatedBy),
        nameof(UserPis.MobileAppActivationStatus), nameof(UserPis.IsActive),
        // Ownership is server-managed: an update can never write it, whatever the row carries.
        nameof(UserPis.AssignedTo)
    };

    /// <summary>Marker returned from the unit of work when the ownership guard refused the update (a real record id is always positive).</summary>
    private const long NotOwnerMarker = -1;

    private readonly AppDbContext _context;
    private readonly ILogger<PisImportRepository> _logger;

    public PisImportRepository(AppDbContext context, ILogger<PisImportRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<long> CreateBatchAsync(PisUploadBatch batch, CancellationToken cancellationToken)
    {
        _context.PisUploadBatches.Add(batch);
        await _context.SaveChangesAsync(cancellationToken);
        long id = batch.Id;
        _context.ChangeTracker.Clear();
        return id;
    }

    public async Task<IReadOnlyDictionary<string, UserPis>> GetByNsgNumbersAsync(IReadOnlyCollection<string> nsgNumbers, CancellationToken cancellationToken)
    {
        Dictionary<string, UserPis> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (string[] chunk in nsgNumbers.Distinct(StringComparer.OrdinalIgnoreCase).Chunk(ChunkSize))
        {
            List<UserPis> found = await _context.UsersPis.AsNoTracking().Where(p => chunk.Contains(p.NsgNumber)).ToListAsync(cancellationToken);
            foreach (UserPis p in found) result[p.NsgNumber] = p;
        }

        return result;
    }
    public async Task<IReadOnlyDictionary<string, string>> GetNsgNumbersByMobileAsync(IReadOnlyCollection<string> mobileNumbers, CancellationToken cancellationToken)
    {
        Dictionary<string, string> result = new();
        foreach (string[] chunk in mobileNumbers.Distinct().Chunk(ChunkSize))
        {
            var found = await _context.UsersPis.AsNoTracking().Where(p => chunk.Contains(p.MobileNumber))
                .Select(p => new { p.MobileNumber, p.NsgNumber }).ToListAsync(cancellationToken);
            foreach (var f in found) result[f.MobileNumber] = f.NsgNumber;
        }

        return result;
    }

    public async Task<PisRowSaveResult> SaveRowAsync(long batchId, int rowNumber, UserPis values, bool insert, long adminId, DateTime nowIst, CancellationToken cancellationToken)
    {
        try
        {
            // The retrying execution strategy needs the whole unit of work in one delegate; every attempt starts from a clean tracker.
            IExecutionStrategy strategy = _context.Database.CreateExecutionStrategy();
            long recordId = await strategy.ExecuteAsync(async () =>
            {
                _context.ChangeTracker.Clear();
                await using IDbContextTransaction transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

                if (insert)
                {
                    values.Id = 0;
                    values.AssignedTo = adminId;   // same INSERT as the personnel row
                    _context.UsersPis.Add(values);
                    await _context.SaveChangesAsync(cancellationToken);
                }
                else
                {
                    // Ownership guard inside the row's own transaction. This UPDATE matches only a row assigned to the uploading admin and
                    // writes the value it already has, so it changes nothing but takes the row lock until commit: a concurrent reassignment
                    // either finished before this point (no match) or waits for this transaction. No match = refuse; nothing was written.
                    int owned = await _context.UsersPis.Where(p => p.Id == values.Id && p.AssignedTo == adminId)
                        .ExecuteUpdateAsync(s => s.SetProperty(p => p.AssignedTo, adminId), cancellationToken);
                    if (owned == 0)
                    {
                        // A connection counting changed rows instead of matched rows reports 0 for the no-op above: look once more.
                        bool stillOwned = await _context.UsersPis.AsNoTracking().AnyAsync(p => p.Id == values.Id && p.AssignedTo == adminId, cancellationToken);
                        if (!stillOwned)
                        {
                            bool exists = await _context.UsersPis.AsNoTracking().AnyAsync(p => p.Id == values.Id, cancellationToken);
                            return exists ? NotOwnerMarker : 0L;
                        }
                    }

                    values.AssignedTo = null;   // never part of the UPDATE (also listed in ProtectedProperties)
                    // Write only the personnel columns, without reading the row first: attach by key and mark exactly those columns.
                    // A blank Excel cell arrives as null and must PRESERVE the stored value, so null columns are not marked and never reach the UPDATE.
                    EntityEntry<UserPis> entry = _context.UsersPis.Attach(values);
                    foreach (PropertyEntry property in entry.Properties.Where(p => !ProtectedProperties.Contains(p.Metadata.Name) && p.CurrentValue is not null and not ""))
                        property.IsModified = true;
                    await _context.SaveChangesAsync(cancellationToken);
                }

                _context.PisUploadBatchRecords.Add(new PisUploadBatchRecord
                {
                    BatchId = batchId,
                    RowNumber = rowNumber,
                    PisRecordId = values.Id,
                    NsgNumber = values.NsgNumber,
                    Operation = insert ? PisRowOperation.Inserted : PisRowOperation.Updated,
                    CreatedAtIst = nowIst
                });
                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return values.Id;
            });

            _context.ChangeTracker.Clear();
            if (recordId == NotOwnerMarker) return new PisRowSaveResult(PisRowSaveStatus.NotOwner);
            if (recordId == 0) return new PisRowSaveResult(PisRowSaveStatus.NotFound);
            return new PisRowSaveResult(PisRowSaveStatus.Saved, recordId);
        }
        catch (DbUpdateConcurrencyException) when (!insert)
        {
            _context.ChangeTracker.Clear();
            return new PisRowSaveResult(PisRowSaveStatus.NotFound);
        }
        catch (DbUpdateException ex) when (ex.InnerException is MySqlException { ErrorCode: MySqlErrorCode.DuplicateKeyEntry } mysql)
        {
            _context.ChangeTracker.Clear();
            // MySQL names the violated index; the duplicated value in the message is never copied anywhere.
            if (mysql.Message.Contains("ix_users_pis_nsg_number", StringComparison.OrdinalIgnoreCase))
            {
                // A retried insert whose first attempt actually committed (lost acknowledgement) collides with itself. The batch-record
                // mapping of this very row proves it was saved: report success, not a conflict, and never insert twice.
                long? savedId = await _context.PisUploadBatchRecords.AsNoTracking()
                    .Where(r => r.BatchId == batchId && r.RowNumber == rowNumber).Select(r => (long?)r.PisRecordId).FirstOrDefaultAsync(cancellationToken);
                return savedId.HasValue
                    ? new PisRowSaveResult(PisRowSaveStatus.Saved, savedId)
                    : new PisRowSaveResult(PisRowSaveStatus.DuplicateNsgNumber);
            }

            if (mysql.Message.Contains("ix_users_pis_mobile_number", StringComparison.OrdinalIgnoreCase))
            {
                string? owner = await _context.UsersPis.AsNoTracking().Where(p => p.MobileNumber == values.MobileNumber)
                    .Select(p => p.NsgNumber).FirstOrDefaultAsync(cancellationToken);
                return new PisRowSaveResult(PisRowSaveStatus.DuplicateMobileNumber, null, owner);
            }

            _logger.LogError("PIS upload row {RowNumber} of batch {BatchId} hit an unexpected unique constraint.", rowNumber, batchId);
            return new PisRowSaveResult(PisRowSaveStatus.Failed);
        }
        catch (Exception ex) when (ex is DbUpdateException or MySqlException or InvalidOperationException)
        {
            _context.ChangeTracker.Clear();
            // Type only: database messages can echo row values.
            _logger.LogError("PIS upload row {RowNumber} of batch {BatchId} could not be saved ({ExceptionType}).", rowNumber, batchId, ex.GetType().Name);
            return new PisRowSaveResult(PisRowSaveStatus.Failed);
        }
    }

    public async Task SaveErrorsAsync(IReadOnlyList<PisUploadBatchError> errors, CancellationToken cancellationToken)
    {
        _context.ChangeTracker.Clear();
        try
        {
            // One SaveChanges = one transaction: all error rows of the worksheet row, or none.
            _context.PisUploadBatchErrors.AddRange(errors);
            await _context.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _context.ChangeTracker.Clear();
        }
    }

    public async Task CompleteBatchAsync(PisUploadBatch summary, CancellationToken cancellationToken)
    {
        _context.ChangeTracker.Clear();

        PisUploadBatch stub = new() { Id = summary.Id };
        _context.PisUploadBatches.Attach(stub);
        stub.Status = summary.Status;
        stub.TotalRows = summary.TotalRows;
        stub.InsertedCount = summary.InsertedCount;
        stub.UpdatedCount = summary.UpdatedCount;
        stub.FailedCount = summary.FailedCount;
        stub.ConflictCount = summary.ConflictCount;
        stub.FailureMessage = summary.FailureMessage;
        stub.CompletedAtIst = summary.CompletedAtIst;
        await _context.SaveChangesAsync(cancellationToken);
        _context.ChangeTracker.Clear();
    }

    public async Task<(IReadOnlyList<PisUploadBatch> Items, int TotalCount)> GetBatchesAsync(long adminId, int pageNumber, int pageSize, CancellationToken cancellationToken)
    {
        IQueryable<PisUploadBatch> query = _context.PisUploadBatches.AsNoTracking().Where(b => b.UploadedBy == adminId);
        int total = await query.CountAsync(cancellationToken);
        List<PisUploadBatch> items = await query.OrderByDescending(b => b.CreatedAtIst).ThenByDescending(b => b.Id)
            .Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return (items, total);
    }

    public Task<PisUploadBatch?> GetBatchAsync(long adminId, long batchId, CancellationToken cancellationToken)
        => _context.PisUploadBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == batchId && b.UploadedBy == adminId, cancellationToken);

    public async Task<(IReadOnlyList<PisUploadBatchRecord> Items, int TotalCount)> GetBatchRecordsAsync(long batchId, int pageNumber, int pageSize, CancellationToken cancellationToken)
    {
        IQueryable<PisUploadBatchRecord> query = _context.PisUploadBatchRecords.AsNoTracking().Where(r => r.BatchId == batchId);
        int total = await query.CountAsync(cancellationToken);
        List<PisUploadBatchRecord> items = await query.OrderBy(r => r.RowNumber).ThenBy(r => r.Id)
            .Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return (items, total);
    }

    public async Task<(IReadOnlyList<PisUploadBatchError> Items, int TotalCount)> GetBatchErrorsAsync(long batchId, int pageNumber, int pageSize, CancellationToken cancellationToken)
    {
        IQueryable<PisUploadBatchError> query = _context.PisUploadBatchErrors.AsNoTracking().Where(e => e.BatchId == batchId);
        int total = await query.CountAsync(cancellationToken);
        List<PisUploadBatchError> items = await query.OrderBy(e => e.RowNumber).ThenBy(e => e.Id)
            .Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return (items, total);
    }
}
