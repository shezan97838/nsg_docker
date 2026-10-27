using System.IO.Compression;
using System.Security;
using System.Text;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Application.Leave;
using NSG.Mobile.Application.Leave.Services;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Tests;

/// <summary>Builds small, valid .xlsx workbooks in memory (a ZIP of XML parts) for the Leave upload tests.</summary>
internal static class XlsxBuilder
{
    /// <summary>
    /// <paramref name="rows"/> are (Excel row number, cells from column A); a null or empty cell is left out of the XML (a sparse row).
    /// A string cell is a shared string unless <paramref name="inlineStrings"/>; numbers become numeric cells.
    /// </summary>
    public static byte[] Build(IEnumerable<(int Row, object?[] Cells)> rows, bool inlineStrings = false, string? extraSheetXmlInside = null)
    {
        List<string> shared = new();
        StringBuilder sheet = new();
        sheet.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
        foreach ((int row, object?[] cells) in rows)
        {
            sheet.Append($"<row r=\"{row}\">");
            for (int i = 0; i < cells.Length; i++)
            {
                object? value = cells[i];
                if (value is null || value is string { Length: 0 }) continue;

                string reference = $"{(char)('A' + i)}{row}";
                switch (value)
                {
                    case string text when inlineStrings:
                        sheet.Append($"<c r=\"{reference}\" t=\"inlineStr\"><is><t>{SecurityElement.Escape(text)}</t></is></c>");
                        break;
                    case string text:
                        int index = shared.IndexOf(text);
                        if (index < 0) { shared.Add(text); index = shared.Count - 1; }
                        sheet.Append($"<c r=\"{reference}\" t=\"s\"><v>{index}</v></c>");
                        break;
                    default:
                        sheet.Append($"<c r=\"{reference}\"><v>{Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)}</v></c>");
                        break;
                }
            }

            sheet.Append("</row>");
        }

        sheet.Append("</sheetData>").Append(extraSheetXmlInside).Append("</worksheet>");

        StringBuilder strings = new();
        strings.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
        foreach (string text in shared) strings.Append($"<si><t>{SecurityElement.Escape(text)}</t></si>");
        strings.Append("</sst>");

        using MemoryStream output = new();
        using (ZipArchive zip = new(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"xml\" ContentType=\"application/xml\"/></Types>");
            Add(zip, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            Add(zip, "xl/workbook.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Leave\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            Add(zip, "xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
            Add(zip, "xl/worksheets/sheet1.xml", sheet.ToString());
            if (!inlineStrings) Add(zip, "xl/sharedStrings.xml", strings.ToString());
        }

        return output.ToArray();
    }

    /// <summary>The template: header row 1, then one data row per (NSG number, leave) pair from row 2.</summary>
    public static byte[] Template(IEnumerable<(object? Nsg, object? Leave)> data, bool inlineStrings = false, string h1 = "NSGNumber", string h2 = "LeaveTaken")
    {
        List<(int, object?[])> rows = new() { (1, new object?[] { h1, h2 }) };
        int row = 2;
        foreach ((object? nsg, object? leave) in data) rows.Add((row++, new[] { nsg, leave }));
        return Build(rows, inlineStrings);
    }

    public static void Add(ZipArchive zip, string name, string content)
    {
        ZipArchiveEntry entry = zip.CreateEntry(name);
        using StreamWriter writer = new(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}

/// <summary>Whether the fake's bulk transaction should misbehave, and how.</summary>
public enum BulkFault
{
    None,
    /// <summary>Database error AFTER the records and history were staged: everything staged must vanish (rollback).</summary>
    DatabaseErrorAfterWrites,
    /// <summary>Someone else created/changed a record meanwhile: rolled back as a conflict.</summary>
    Conflict,
    /// <summary>The commit failed and even the verification read failed: the outcome is unknown.</summary>
    OutcomeUnknown,
    /// <summary>An exception the repository does not handle (escapes to the service).</summary>
    Unhandled
}

/// <summary>
/// In-memory <see cref="ILeaveRepository"/> with the same contract as the EF one: unique (nsg, year), a single running batch, all-or-nothing
/// bulk apply (writes are staged and only copied over on success), history written with every change. It proves the SERVICE logic; the SQL is
/// proven only by the MySQL-gated <c>LeaveDatabaseTests</c>.
/// </summary>
internal sealed class LeaveFakeRepository : ILeaveRepository
{
    private long _nextId = 1;

    public Dictionary<string, string> Personnel { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<long, string> MobileProfiles { get; } = new();
    public List<PersonnelLeaveRecord> Records { get; } = new();
    public List<PersonnelLeaveHistory> History { get; } = new();
    public List<PersonnelLeaveUploadBatch> Batches { get; } = new();
    public List<PersonnelLeaveUploadError> Errors { get; } = new();

    public BulkFault Fault { get; set; }
    public bool FinishFails { get; set; }
    public bool ManualConflict { get; set; }
    public int ApplyCalls { get; private set; }

    public void AddPerson(string nsg) => Personnel[nsg] = nsg;

    public PersonnelLeaveRecord AddRecord(string nsg, int year, int leave, long by = 8001, DateTime? at = null)
    {
        DateTime when = at ?? new DateTime(year, 3, 1, 9, 0, 0);
        PersonnelLeaveRecord record = new()
        {
            Id = _nextId++, NsgNumber = nsg, LeaveYear = year, LeaveTaken = leave, RowVersion = 1,
            CreatedAtIst = when, CreatedBy = by, UpdatedAtIst = when, UpdatedBy = by
        };
        Records.Add(record);
        return record;
    }

    public Task<IReadOnlyDictionary<string, string>> GetActivePersonnelNsgNumbersAsync(IReadOnlyCollection<string> nsgNumbers, CancellationToken cancellationToken = default)
    {
        Dictionary<string, string> found = new();
        foreach (string nsg in nsgNumbers)
            if (Personnel.TryGetValue(nsg, out string? stored)) found[nsg.ToUpperInvariant()] = stored;
        return Task.FromResult<IReadOnlyDictionary<string, string>>(found);
    }

    public Task<string?> GetNsgNumberOfMobileProfileAsync(long mobileProfileId, CancellationToken cancellationToken = default)
        => Task.FromResult(MobileProfiles.GetValueOrDefault(mobileProfileId));

    public Task<PersonnelLeaveRecord?> GetRecordAsync(string nsgNumber, int year, CancellationToken cancellationToken = default)
        => Task.FromResult(Records.FirstOrDefault(r => r.NsgNumber == nsgNumber && r.LeaveYear == year));

    public Task<LeaveSaveResult> SaveManualAsync(string nsgNumber, int year, int leaveTaken, bool createIfMissing, long adminProfileId, DateTime nowIst, CancellationToken cancellationToken = default)
    {
        if (ManualConflict) return Task.FromResult(new LeaveSaveResult(LeaveSaveOutcome.ConcurrencyConflict, null, null));

        PersonnelLeaveRecord? record = Records.FirstOrDefault(r => r.NsgNumber == nsgNumber && r.LeaveYear == year);
        if (record == null)
        {
            if (!createIfMissing) return Task.FromResult(new LeaveSaveResult(LeaveSaveOutcome.NotFound, null, null));
            record = new PersonnelLeaveRecord
            {
                Id = _nextId++, NsgNumber = nsgNumber, LeaveYear = year, LeaveTaken = leaveTaken, RowVersion = 1,
                CreatedAtIst = nowIst, CreatedBy = adminProfileId, UpdatedAtIst = nowIst, UpdatedBy = adminProfileId
            };
            Records.Add(record);
            History.Add(HistoryOf(record, null, LeaveChangeSource.Manual, null, adminProfileId, nowIst));
            return Task.FromResult(new LeaveSaveResult(LeaveSaveOutcome.Created, record, null));
        }

        if (record.LeaveTaken == leaveTaken) return Task.FromResult(new LeaveSaveResult(LeaveSaveOutcome.Unchanged, record, record.LeaveTaken));

        int old = record.LeaveTaken;
        record.LeaveTaken = leaveTaken;
        record.UpdatedBy = adminProfileId;
        record.UpdatedAtIst = nowIst;
        record.RowVersion++;
        History.Add(HistoryOf(record, old, LeaveChangeSource.Manual, null, adminProfileId, nowIst));
        return Task.FromResult(new LeaveSaveResult(LeaveSaveOutcome.Updated, record, old));
    }

    public Task<(IReadOnlyList<PersonnelLeaveHistory> Items, int TotalCount)> GetHistoryPagedAsync(string nsgNumber, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        List<PersonnelLeaveHistory> all = History.Where(h => h.NsgNumber == nsgNumber).OrderByDescending(h => h.ChangedAtIst).ThenByDescending(h => h.Id).ToList();
        return Task.FromResult(((IReadOnlyList<PersonnelLeaveHistory>)all.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList(), all.Count));
    }

    public Task<long?> TryStartBatchAsync(string fileName, int year, long adminProfileId, DateTime nowIst, TimeSpan staleAfter, CancellationToken cancellationToken = default)
    {
        foreach (PersonnelLeaveUploadBatch old in Batches.Where(b => b.UploadStatus == LeaveUploadStatus.Processing && b.CreatedAtIst < nowIst - staleAfter))
        {
            old.UploadStatus = LeaveUploadStatus.ProcessingFailed;
            old.ProcessingLock = null;
            old.CompletedAtIst = nowIst;
        }

        if (Batches.Any(b => b.UploadStatus == LeaveUploadStatus.Processing)) return Task.FromResult<long?>(null);

        PersonnelLeaveUploadBatch batch = new()
        {
            Id = _nextId++, FileName = fileName, LeaveYear = year, UploadStatus = LeaveUploadStatus.Processing, ProcessingLock = true,
            CreatedAtIst = nowIst, CreatedBy = adminProfileId
        };
        Batches.Add(batch);
        return Task.FromResult<long?>(batch.Id);
    }

    public Task<LeaveBulkResult> ApplyBulkAsync(long batchId, int year, int totalRows, IReadOnlyList<LeaveBulkRow> rows, long adminProfileId, DateTime nowIst, CancellationToken cancellationToken = default)
    {
        ApplyCalls++;
        if (Fault == BulkFault.Unhandled) throw new InvalidOperationException("boom (not a database error)");

        // Stage on copies: nothing reaches the "database" unless the whole transaction succeeds.
        List<PersonnelLeaveRecord> stagedRecords = Records.Select(Clone).ToList();
        List<PersonnelLeaveHistory> stagedHistory = History.ToList();
        long idBefore = _nextId;
        int created = 0, updated = 0, unchanged = 0;
        foreach (LeaveBulkRow row in rows)
        {
            PersonnelLeaveRecord? record = stagedRecords.FirstOrDefault(r => r.NsgNumber == row.NsgNumber && r.LeaveYear == year);
            if (record == null)
            {
                record = new PersonnelLeaveRecord
                {
                    Id = _nextId++, NsgNumber = row.NsgNumber, LeaveYear = year, LeaveTaken = row.LeaveTaken, RowVersion = 1,
                    CreatedAtIst = nowIst, CreatedBy = adminProfileId, UpdatedAtIst = nowIst, UpdatedBy = adminProfileId
                };
                stagedRecords.Add(record);
                stagedHistory.Add(HistoryOf(record, null, LeaveChangeSource.Bulk, batchId, adminProfileId, nowIst));
                created++;
            }
            else if (record.LeaveTaken == row.LeaveTaken)
            {
                unchanged++;
            }
            else
            {
                int old = record.LeaveTaken;
                record.LeaveTaken = row.LeaveTaken;
                record.UpdatedBy = adminProfileId;
                record.UpdatedAtIst = nowIst;
                record.RowVersion++;
                stagedHistory.Add(HistoryOf(record, old, LeaveChangeSource.Bulk, batchId, adminProfileId, nowIst));
                updated++;
            }
        }

        switch (Fault)
        {
            case BulkFault.DatabaseErrorAfterWrites:
                _nextId = idBefore;
                return Task.FromResult(new LeaveBulkResult(LeaveBulkOutcome.RolledBackFailure, 0, 0, 0));   // staged copies are simply dropped
            case BulkFault.Conflict:
                _nextId = idBefore;
                return Task.FromResult(new LeaveBulkResult(LeaveBulkOutcome.RolledBackConflict, 0, 0, 0));
            case BulkFault.OutcomeUnknown:
                _nextId = idBefore;
                return Task.FromResult(new LeaveBulkResult(LeaveBulkOutcome.Unknown, 0, 0, 0));
        }

        // Commit: copy the staged values onto the existing objects (callers may hold references) and add the new rows.
        foreach (PersonnelLeaveRecord staged in stagedRecords)
        {
            PersonnelLeaveRecord? original = Records.FirstOrDefault(r => r.Id == staged.Id);
            if (original == null) { Records.Add(staged); continue; }
            original.LeaveTaken = staged.LeaveTaken;
            original.RowVersion = staged.RowVersion;
            original.UpdatedBy = staged.UpdatedBy;
            original.UpdatedAtIst = staged.UpdatedAtIst;
        }

        History.Clear();
        History.AddRange(stagedHistory);

        PersonnelLeaveUploadBatch batch = Batches.Single(b => b.Id == batchId);
        batch.TotalRows = totalRows;
        batch.CreatedCount = created;
        batch.UpdatedCount = updated;
        batch.UnchangedCount = unchanged;
        batch.UploadStatus = LeaveUploadStatus.Completed;
        batch.ProcessingLock = null;
        batch.CompletedAtIst = nowIst;
        return Task.FromResult(new LeaveBulkResult(LeaveBulkOutcome.Committed, created, updated, unchanged));
    }

    public Task FinishFailedBatchAsync(long batchId, LeaveUploadStatus status, int totalRows, int failedCount, IReadOnlyList<LeaveUploadIssue> issues, DateTime nowIst, CancellationToken cancellationToken = default)
    {
        if (FinishFails) throw new InvalidOperationException("database unavailable");

        PersonnelLeaveUploadBatch batch = Batches.Single(b => b.Id == batchId);
        if (batch.UploadStatus != LeaveUploadStatus.Processing) return Task.CompletedTask;

        batch.UploadStatus = status;
        batch.TotalRows = totalRows;
        batch.FailedCount = failedCount;
        batch.ProcessingLock = null;
        batch.CompletedAtIst = nowIst;
        foreach (LeaveUploadIssue issue in issues)
            Errors.Add(new PersonnelLeaveUploadError { Id = _nextId++, BatchId = batchId, RowNumber = issue.RowNumber, NsgNumber = issue.NsgNumber, ErrorMessage = issue.Message, CreatedAtIst = nowIst });
        return Task.CompletedTask;
    }

    public Task<PersonnelLeaveUploadBatch?> GetBatchAsync(long batchId, CancellationToken cancellationToken = default)
        => Task.FromResult(Batches.FirstOrDefault(b => b.Id == batchId));

    public Task<(IReadOnlyList<PersonnelLeaveUploadBatch> Items, int TotalCount)> GetBatchesPagedAsync(int pageNumber, int pageSize, LeaveUploadStatus? status, CancellationToken cancellationToken = default)
    {
        List<PersonnelLeaveUploadBatch> all = Batches.Where(b => status == null || b.UploadStatus == status).OrderByDescending(b => b.CreatedAtIst).ThenByDescending(b => b.Id).ToList();
        return Task.FromResult(((IReadOnlyList<PersonnelLeaveUploadBatch>)all.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList(), all.Count));
    }

    public Task<(IReadOnlyList<PersonnelLeaveUploadError> Items, int TotalCount)> GetBatchErrorsPagedAsync(long batchId, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        List<PersonnelLeaveUploadError> all = Errors.Where(e => e.BatchId == batchId).OrderBy(e => e.RowNumber).ThenBy(e => e.Id).ToList();
        return Task.FromResult(((IReadOnlyList<PersonnelLeaveUploadError>)all.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList(), all.Count));
    }

    private PersonnelLeaveHistory HistoryOf(PersonnelLeaveRecord record, int? old, LeaveChangeSource source, long? batchId, long by, DateTime at) => new()
    {
        Id = _nextId++, LeaveRecordId = record.Id, NsgNumber = record.NsgNumber, LeaveYear = record.LeaveYear, OldLeaveTaken = old,
        NewLeaveTaken = record.LeaveTaken, ChangeSource = source, UploadBatchId = batchId, ChangedBy = by, ChangedAtIst = at
    };

    private static PersonnelLeaveRecord Clone(PersonnelLeaveRecord r) => new()
    {
        Id = r.Id, NsgNumber = r.NsgNumber, LeaveYear = r.LeaveYear, LeaveTaken = r.LeaveTaken, RowVersion = r.RowVersion,
        CreatedAtIst = r.CreatedAtIst, CreatedBy = r.CreatedBy, UpdatedAtIst = r.UpdatedAtIst, UpdatedBy = r.UpdatedBy
    };

    /// <summary>A snapshot of the leave data to assert "nothing changed".</summary>
    public string Snapshot() => string.Join("|", Records.OrderBy(r => r.Id).Select(r => $"{r.Id}:{r.NsgNumber}:{r.LeaveYear}:{r.LeaveTaken}:{r.RowVersion}:{r.CreatedBy}:{r.UpdatedBy}:{r.UpdatedAtIst:O}"))
        + "#" + History.Count;
}

/// <summary>The Leave services wired over the fakes the way the DI container wires them.</summary>
internal sealed class LeaveKit
{
    public const long Admin = 9001, OtherAdmin = 9002, Employee = 501, OtherEmployee = 502;

    public LeaveFakeRepository Repo { get; } = new();
    public FakeAudit Audit { get; } = new();
    public FakeCurrentUser User { get; } = new() { AdminProfileId = Admin, ProfileId = Employee };
    public TestClock Clock { get; } = new();
    public LeaveSettings Settings { get; } = new();
    public GrvLogger<LeaveBulkUploadService> UploadLog { get; } = new();

    public LeaveService Leave { get; }
    public LeaveBulkUploadService Uploads { get; }

    public LeaveKit()
    {
        Repo.AddPerson("NSG1001");
        Repo.AddPerson("NSG1002");
        Repo.AddPerson("NSG1003");
        Repo.MobileProfiles[Employee] = "NSG1001";
        Repo.MobileProfiles[OtherEmployee] = "NSG1002";
        Leave = new LeaveService(Repo, Audit, User, Clock);
        Uploads = new LeaveBulkUploadService(Repo, Settings, Audit, User, Clock, UploadLog);
    }

    /// <summary>The year the (fixed) test clock is in, in IST.</summary>
    public int Year => NSG.Mobile.Domain.Common.IndianStandardTime.ConvertFromUtc(Clock.GetUtcNow().UtcDateTime).Year;

    public static LeaveUploadFile File(byte[] bytes, string name = "leave.xlsx", long? declaredLength = null)
        => new(new MemoryStream(bytes), name, declaredLength ?? bytes.Length);
}
