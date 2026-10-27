using Microsoft.Extensions.Logging;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Grievances.DTOs;
using NSG.Mobile.Application.Grievances.Services;
using NSG.Mobile.Application.Interfaces.Infrastructure;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Tests;

// In-memory stand-ins for the Grievance module's dependencies, so every rule can be tested without MySQL or R2. The repository fake follows
// the contract of GrievanceRepository (owner filter, optimistic updated_at_ist token, conditional status change, unique category names).
// The real SQL, real MySQL behaviour and real R2 are NOT covered here; they need the manual smoke tests described in docs/GRIEVANCE-MODULE.md.

internal sealed class GrvFakeRepository : IGrievanceRepository
{
    private long _nextGrievanceId = 100;
    private int _nextCategoryId = 1;

    public List<GrievanceCategory> Categories { get; } = new();
    public List<Grievance> Grievances { get; } = new();
    public List<GrievanceStatusLog> Logs { get; } = new();
    public Dictionary<long, GrievanceEmployeeInfo> Employees { get; } = new();
    public HashSet<long> ActiveGrievanceAdmins { get; } = new();

    // Test controls
    public bool TicketsUnavailable { get; set; }
    public bool ThrowOnCreate { get; set; }
    public bool ThrowOnResolve { get; set; }
    public int ResolveCalls { get; private set; }

    /// <summary>When set, ChangeStatus answers as if another admin had already moved the grievance to this status.</summary>
    public GrievanceStatus? ReportStatusChangedByOthers { get; set; }
    public int ChangeStatusCalls { get; private set; }
    public GrievanceListFilter? LastFilter { get; private set; }
    public bool? LastCategoryIsActiveFilter { get; private set; } = false;
    public bool CategoryPagedCalled { get; private set; }
    public bool ActiveCategoriesCalled { get; private set; }

    // ── Seeding helpers ──

    public GrievanceCategory AddCategory(string name, bool active = true, bool deleted = false)
    {
        GrievanceCategory category = new()
        {
            Id = _nextCategoryId++,
            GrievanceName = name,
            IsActive = active,
            IsDeleted = deleted
        };
        Categories.Add(category);
        return category;
    }

    public Grievance AddGrievance(long owner, GrievanceCategory category, GrievanceStatus status = GrievanceStatus.Submitted,
        IEnumerable<GrievanceDocumentRecord>? documents = null, DateTime? createdAtIst = null)
    {
        Grievance g = new()
        {
            Id = _nextGrievanceId++,
            TicketNumber = $"GRV-2026-{_nextGrievanceId - 100:D6}",
            CategoryId = category.Id,
            Subject = "Subject",
            GrievanceNote = "A note that is long enough.",
            CurrentStatus = status,
            CreatedBy = owner,
            CreatedAtIst = createdAtIst ?? new DateTime(2026, 10, 15, 11, 0, 0),
            DocumentsJson = documents == null ? null : GrievanceDocumentRules.SerializeDocuments(documents.ToList())
        };
        if (status >= GrievanceStatus.ActionTaken)
        {
            g.ActionTakenBy = 7; g.ActionTakenAtIst = g.CreatedAtIst.AddDays(1); g.ActionRemarks = "Done";
        }
        if (status == GrievanceStatus.Closed)
        {
            g.ClosedBy = 7; g.ClosedAtIst = g.CreatedAtIst.AddDays(3);
        }
        Grievances.Add(g);
        return g;
    }

    // ── Categories ──

    public Task<IReadOnlyList<GrievanceCategory>> GetActiveCategoriesAsync(CancellationToken cancellationToken = default)
    {
        ActiveCategoriesCalled = true;
        return Task.FromResult<IReadOnlyList<GrievanceCategory>>(Categories.Where(c => c.IsActive && !c.IsDeleted).OrderBy(c => c.GrievanceName).ToList());
    }

    public Task<(IReadOnlyList<GrievanceCategory> Items, int TotalCount)> GetCategoriesPagedAsync(
        int pageNumber, int pageSize, bool? isActive, string? search, CancellationToken cancellationToken = default)
    {
        CategoryPagedCalled = true;
        LastCategoryIsActiveFilter = isActive;
        IEnumerable<GrievanceCategory> query = Categories.Where(c => !c.IsDeleted);
        if (isActive.HasValue) query = query.Where(c => c.IsActive == isActive.Value);
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(c => c.GrievanceName.Contains(search, StringComparison.OrdinalIgnoreCase));
        List<GrievanceCategory> all = query.OrderBy(c => c.GrievanceName).ToList();
        return Task.FromResult<(IReadOnlyList<GrievanceCategory>, int)>((all.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList(), all.Count));
    }

    public Task<GrievanceCategory?> GetCategoryByIdAsync(int id, CancellationToken cancellationToken = default)
        => Task.FromResult(Categories.FirstOrDefault(c => c.Id == id));

    public Task<CategoryWriteOutcome> AddCategoryAsync(GrievanceCategory category, CancellationToken cancellationToken = default)
    {
        // Unique name, case-insensitive like the MySQL default collation, deactivated categories included.
        if (Categories.Any(c => string.Equals(c.GrievanceName, category.GrievanceName, StringComparison.OrdinalIgnoreCase)))
            return Task.FromResult(CategoryWriteOutcome.DuplicateName);
        category.Id = _nextCategoryId++;
        Categories.Add(category);
        return Task.FromResult(CategoryWriteOutcome.Saved);
    }

    public Task<CategoryWriteOutcome> UpdateCategoryAsync(int id, string name, int? departmentId, bool isActive, long? updatedBy, DateTime nowIst, CancellationToken cancellationToken = default)
    {
        GrievanceCategory? category = Categories.FirstOrDefault(c => c.Id == id && !c.IsDeleted);
        if (category == null) return Task.FromResult(CategoryWriteOutcome.NotFound);
        if (Categories.Any(c => c.Id != id && string.Equals(c.GrievanceName, name, StringComparison.OrdinalIgnoreCase)))
            return Task.FromResult(CategoryWriteOutcome.DuplicateName);

        category.GrievanceName = name;
        category.DepartmentId = departmentId;
        category.IsActive = isActive;
        category.UpdatedBy = updatedBy;
        category.UpdatedAtIst = nowIst;
        return Task.FromResult(CategoryWriteOutcome.Saved);
    }

    public Task<CategoryDeleteOutcome> SoftDeleteCategoryAsync(int id, long? deletedBy, DateTime nowIst, CancellationToken cancellationToken = default)
    {
        GrievanceCategory? category = Categories.FirstOrDefault(c => c.Id == id);
        if (category == null) return Task.FromResult(CategoryDeleteOutcome.NotFound);
        if (category.IsDeleted) return Task.FromResult(CategoryDeleteOutcome.AlreadyDeleted);
        category.IsDeleted = true;
        category.IsActive = false;
        category.UpdatedBy = deletedBy;
        category.UpdatedAtIst = nowIst;
        return Task.FromResult(CategoryDeleteOutcome.Deleted);
    }

    // ── Grievances ──

    public Task<GrievanceCreateResult> CreateAsync(Grievance grievance, long createdByProfileId, int maxTicketAttempts, CancellationToken cancellationToken = default)
    {
        if (ThrowOnCreate) throw new InvalidOperationException("Simulated database failure.");
        if (TicketsUnavailable) return Task.FromResult(new GrievanceCreateResult(GrievanceCreateOutcome.TicketNumberUnavailable));

        grievance.Id = _nextGrievanceId++;
        // Same rule as GrievanceRepository: the highest ticket of this year (length first, then text) decides the next one; other years and the legacy GRV- tickets never count.
        string prefix = NSG.Mobile.Application.Grievances.GrievanceTicketNumber.PrefixFor(grievance.CreatedAtIst.Year);
        string? last = Grievances.Where(g => g.TicketNumber.StartsWith(prefix, StringComparison.Ordinal))
            .OrderByDescending(g => g.TicketNumber.Length).ThenByDescending(g => g.TicketNumber, StringComparer.Ordinal).Select(g => g.TicketNumber).FirstOrDefault();
        grievance.TicketNumber = NSG.Mobile.Application.Grievances.GrievanceTicketNumber.Next(grievance.CreatedAtIst.Year, last);
        Grievances.Add(grievance);
        Logs.Add(new GrievanceStatusLog
        {
            GrievancePk = grievance.Id, FromStatus = null, ToStatus = GrievanceStatus.Submitted,
            ChangedBy = createdByProfileId, ChangedByType = GrievanceActorType.Employee, ChangedAtIst = grievance.CreatedAtIst
        });
        return Task.FromResult(new GrievanceCreateResult(GrievanceCreateOutcome.Created, grievance));
    }

    public Task<(IReadOnlyList<Grievance> Items, int TotalCount)> GetPagedAsync(GrievanceListFilter filter, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        LastFilter = filter;
        IEnumerable<Grievance> query = Grievances.Where(g => g.IsActive);
        if (filter.CreatedBy.HasValue) query = query.Where(g => g.CreatedBy == filter.CreatedBy.Value);
        if (filter.AssignedTo.HasValue) query = query.Where(g => g.AssignedTo == filter.AssignedTo.Value);
        if (filter.Status.HasValue) query = query.Where(g => g.CurrentStatus == filter.Status.Value);
        if (filter.CategoryId.HasValue) query = query.Where(g => g.CategoryId == filter.CategoryId.Value);
        if (filter.FromIst.HasValue) query = query.Where(g => g.CreatedAtIst >= filter.FromIst.Value);
        if (filter.ToIstExclusive.HasValue) query = query.Where(g => g.CreatedAtIst < filter.ToIstExclusive.Value);
        if (!string.IsNullOrWhiteSpace(filter.TicketSearch)) query = query.Where(g => g.TicketNumber.Contains(filter.TicketSearch));

        List<Grievance> all = query.OrderByDescending(g => g.CreatedAtIst).ThenByDescending(g => g.Id).ToList();
        IReadOnlyList<Grievance> page = all.Skip((pageNumber - 1) * pageSize).Take(pageSize).Select(Snapshot).ToList();
        return Task.FromResult((page, all.Count));
    }

    public Task<Grievance?> GetByIdAsync(long id, long? createdBy, CancellationToken cancellationToken = default)
    {
        Grievance? g = Grievances.FirstOrDefault(x => x.Id == id && x.IsActive && (createdBy == null || x.CreatedBy == createdBy));
        return Task.FromResult(g == null ? null : Snapshot(g));
    }

    public Task<IReadOnlyList<GrievanceStatusLog>> GetTimelineAsync(long grievancePk, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<GrievanceStatusLog>>(Logs.Where(l => l.GrievancePk == grievancePk).OrderBy(l => l.ChangedAtIst).ToList());

    public Task<GrievanceCommentOutcome> AddCommentAsync(long id, long adminProfileId, string comment, DateTime nowIst, CancellationToken cancellationToken = default)
    {
        Grievance? g = Grievances.FirstOrDefault(x => x.Id == id && x.IsActive);
        if (g == null) return Task.FromResult(GrievanceCommentOutcome.NotFound);
        if (g.CurrentStatus == GrievanceStatus.Closed) return Task.FromResult(GrievanceCommentOutcome.Closed);

        Logs.Add(new GrievanceStatusLog
        {
            GrievancePk = id, LogAction = GrievanceLogAction.Comment, FromStatus = g.CurrentStatus, ToStatus = g.CurrentStatus, Remarks = comment,
            ChangedBy = adminProfileId, ChangedByType = GrievanceActorType.Admin, ChangedAtIst = nowIst
        });
        return Task.FromResult(GrievanceCommentOutcome.Added);
    }

    public Task<GrievanceForwardOutcome> ForwardAsync(long id, long adminProfileId, long toAdminId, string? remarks, DateTime nowIst, CancellationToken cancellationToken = default)
    {
        Grievance? g = Grievances.FirstOrDefault(x => x.Id == id && x.IsActive);
        if (g == null) return Task.FromResult(GrievanceForwardOutcome.NotFound);
        if (g.CurrentStatus == GrievanceStatus.Closed) return Task.FromResult(GrievanceForwardOutcome.Closed);
        if (g.AssignedTo == toAdminId) return Task.FromResult(GrievanceForwardOutcome.AlreadyAssigned);

        g.AssignedTo = toAdminId;
        g.UpdatedAtIst = nowIst;
        Logs.Add(new GrievanceStatusLog
        {
            GrievancePk = id, LogAction = GrievanceLogAction.Forward, FromStatus = g.CurrentStatus, ToStatus = g.CurrentStatus, Remarks = remarks, ForwardedTo = toAdminId,
            ChangedBy = adminProfileId, ChangedByType = GrievanceActorType.Admin, ChangedAtIst = nowIst
        });
        return Task.FromResult(GrievanceForwardOutcome.Forwarded);
    }

    public Task<GrievanceStatusChangeResult> ChangeStatusAsync(long id, GrievanceStatus from, GrievanceStatus to, long adminProfileId, string? remarks, DateTime nowIst, CancellationToken cancellationToken = default)
    {
        ChangeStatusCalls++;
        if (ReportStatusChangedByOthers.HasValue) return Task.FromResult(new GrievanceStatusChangeResult(GrievanceStatusChangeOutcome.StatusChanged, ReportStatusChangedByOthers));
        Grievance? g = Grievances.FirstOrDefault(x => x.Id == id && x.IsActive);
        if (g == null) return Task.FromResult(new GrievanceStatusChangeResult(GrievanceStatusChangeOutcome.NotFound));
        if (g.CurrentStatus != from) return Task.FromResult(new GrievanceStatusChangeResult(GrievanceStatusChangeOutcome.StatusChanged, g.CurrentStatus));

        g.CurrentStatus = to;
        g.UpdatedAtIst = nowIst;
        if (to == GrievanceStatus.ActionTaken) { g.ActionTakenBy = adminProfileId; g.ActionTakenAtIst = nowIst; g.ActionRemarks = remarks; }
        if (to == GrievanceStatus.Closed) { g.ClosedBy = adminProfileId; g.ClosedAtIst = nowIst; g.ClosedRemarks = remarks; }
        Logs.Add(new GrievanceStatusLog
        {
            GrievancePk = id, FromStatus = from, ToStatus = to, Remarks = remarks,
            ChangedBy = adminProfileId, ChangedByType = GrievanceActorType.Admin, ChangedAtIst = nowIst
        });
        return Task.FromResult(new GrievanceStatusChangeResult(GrievanceStatusChangeOutcome.Changed));
    }

    public Task<GrievanceStatusChangeResult> ResolveAsync(long id, long adminProfileId, string remarks, string resolutionDocument, DateTime nowIst, CancellationToken cancellationToken = default)
    {
        ResolveCalls++;
        if (ThrowOnResolve) throw new InvalidOperationException("Simulated database failure.");
        if (ReportStatusChangedByOthers.HasValue) return Task.FromResult(new GrievanceStatusChangeResult(GrievanceStatusChangeOutcome.StatusChanged, ReportStatusChangedByOthers));
        Grievance? g = Grievances.FirstOrDefault(x => x.Id == id && x.IsActive);
        if (g == null) return Task.FromResult(new GrievanceStatusChangeResult(GrievanceStatusChangeOutcome.NotFound));
        if (g.CurrentStatus != GrievanceStatus.ActionTaken) return Task.FromResult(new GrievanceStatusChangeResult(GrievanceStatusChangeOutcome.StatusChanged, g.CurrentStatus));

        g.CurrentStatus = GrievanceStatus.Closed;
        g.UpdatedAtIst = nowIst;
        g.ClosedBy = adminProfileId; g.ClosedAtIst = nowIst; g.ClosedRemarks = remarks; g.ResolutionDocument = resolutionDocument;
        Logs.Add(new GrievanceStatusLog
        {
            GrievancePk = id, FromStatus = GrievanceStatus.ActionTaken, ToStatus = GrievanceStatus.Closed, Remarks = remarks,
            ChangedBy = adminProfileId, ChangedByType = GrievanceActorType.Admin, ChangedAtIst = nowIst
        });
        return Task.FromResult(new GrievanceStatusChangeResult(GrievanceStatusChangeOutcome.Changed));
    }

    // ── Dashboard and lookups ──

    public Task<IReadOnlyDictionary<long, GrievanceEmployeeInfo>> GetEmployeeInfoAsync(IReadOnlyCollection<long> profileIds, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyDictionary<long, GrievanceEmployeeInfo>>(Employees.Where(e => profileIds.Contains(e.Key)).ToDictionary(e => e.Key, e => e.Value));

    public Task<GrievanceDashboardData> GetDashboardAsync(DateTime? fromIst, DateTime? toIstExclusive, CancellationToken cancellationToken = default)
    {
        List<Grievance> rows = Grievances.Where(g => g.IsActive
            && (fromIst == null || g.CreatedAtIst >= fromIst) && (toIstExclusive == null || g.CreatedAtIst < toIstExclusive)).ToList();
        double? average = rows.Where(g => g.CurrentStatus == GrievanceStatus.Closed && g.ClosedAtIst.HasValue)
            .Select(g => (g.ClosedAtIst!.Value - g.CreatedAtIst).TotalSeconds).DefaultIfEmpty(double.NaN).Average();
        return Task.FromResult(new GrievanceDashboardData(
            rows.Count,
            rows.GroupBy(g => g.CurrentStatus).Select(x => new GrievanceStatusCount(x.Key, x.Count())).ToList(),
            rows.GroupBy(g => g.CategoryId).Select(x => new GrievanceCategoryCount(x.Key, Categories.First(c => c.Id == x.Key).GrievanceName, x.Count())).ToList(),
            rows.GroupBy(g => (g.CreatedAtIst.Year, g.CreatedAtIst.Month)).Select(x => new GrievanceMonthCount(x.Key.Year, x.Key.Month, x.Count())).ToList(),
            double.IsNaN(average ?? double.NaN) ? null : average));
    }

    public Task<bool> IsActiveGrievanceAdminAsync(long adminProfileId, CancellationToken cancellationToken = default)
        => Task.FromResult(ActiveGrievanceAdmins.Contains(adminProfileId));

    /// <summary>A separate copy with the category attached, like a fresh database read: later changes to the stored row do not alter it.</summary>
    private Grievance Snapshot(Grievance g)
    {
        Grievance copy = new()
        {
            Id = g.Id, TicketNumber = g.TicketNumber, CategoryId = g.CategoryId, Subject = g.Subject, GrievanceNote = g.GrievanceNote,
            DocumentsJson = g.DocumentsJson, CurrentStatus = g.CurrentStatus, CreatedBy = g.CreatedBy, CreatedAtIst = g.CreatedAtIst,
            UpdatedAtIst = g.UpdatedAtIst, AssignedTo = g.AssignedTo, ActionTakenBy = g.ActionTakenBy, ActionTakenAtIst = g.ActionTakenAtIst,
            ActionRemarks = g.ActionRemarks, ClosedBy = g.ClosedBy, ClosedAtIst = g.ClosedAtIst, ClosedRemarks = g.ClosedRemarks, ResolutionDocument = g.ResolutionDocument, IsActive = g.IsActive,
            Category = Categories.FirstOrDefault(c => c.Id == g.CategoryId)
        };
        return copy;
    }
}

/// <summary>Records every call and can simulate an outage. Keys have the shape the real storage generates.</summary>
internal sealed class GrvFakeStorage : IFileStorageService
{
    public Dictionary<string, byte[]> Objects { get; } = new();
    public List<string> StoreCalls { get; } = new();
    public List<string> GetCalls { get; } = new();
    public List<string> Deleted { get; } = new();
    public bool UnavailableOnStore { get; set; }
    public bool UnavailableOnGet { get; set; }
    public bool DeleteFails { get; set; }
    public GrvTrackedStream? LastOpened { get; private set; }
    public string? LastStoredFileName { get; private set; }
    public string? LastStoredContentType { get; private set; }

    public async Task<string> StoreAsync(Stream fileStream, string fileName, string contentType, string category, CancellationToken cancellationToken = default)
    {
        if (UnavailableOnStore) throw new FileStorageUnavailableException("down");
        using MemoryStream copy = new();
        await fileStream.CopyToAsync(copy, cancellationToken);

        string key = $"{category}/2026/10/{Guid.NewGuid()}{Path.GetExtension(fileName)}";
        Objects[key] = copy.ToArray();
        StoreCalls.Add(key);
        LastStoredFileName = fileName;
        LastStoredContentType = contentType;
        return key;
    }

    public Task<FileStorageResult?> GetAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        GetCalls.Add(storageKey);
        if (UnavailableOnGet) throw new FileStorageUnavailableException("down");
        if (!Objects.TryGetValue(storageKey, out byte[]? bytes)) return Task.FromResult<FileStorageResult?>(null);

        LastOpened = new GrvTrackedStream(bytes);
        return Task.FromResult<FileStorageResult?>(new FileStorageResult(LastOpened, "application/octet-stream", Path.GetFileName(storageKey)));
    }

    public Task<bool> DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        Deleted.Add(storageKey);
        if (DeleteFails) return Task.FromResult(false);
        Objects.Remove(storageKey);
        return Task.FromResult(true);
    }
}

internal sealed class GrvTrackedStream : MemoryStream
{
    public GrvTrackedStream(byte[] buffer) : base(buffer) { }
    public bool WasDisposed { get; private set; }

    protected override void Dispose(bool disposing)
    {
        WasDisposed = true;
        base.Dispose(disposing);
    }
}

internal sealed class GrvAvailability : IStorageAvailability
{
    public bool IsAvailable { get; set; } = true;
}

/// <summary>Captures log entries so a test can assert that a warning or error was written (and what it left out).</summary>
internal sealed class GrvLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message)> Entries { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => Entries.Add((logLevel, formatter(state, exception)));
}

/// <summary>Everything a Grievance test needs, wired the way the DI container wires it.</summary>
internal sealed class GrvKit
{
    public const long Employee = 501, OtherEmployee = 502, Admin = 9001;

    public GrvFakeRepository Repo { get; } = new();
    public GrvFakeStorage Storage { get; } = new();
    public GrvAvailability Availability { get; } = new();
    public FakeAudit Audit { get; } = new();
    public FakeCurrentUser User { get; } = new() { ProfileId = Employee, AdminProfileId = Admin };
    public TestClock Clock { get; } = new();
    public GrvLogger<GrievanceService> EmployeeLog { get; } = new();
    public GrvLogger<GrievanceAdminService> AdminLog { get; } = new();

    public GrievanceDocumentAccess Access { get; }
    public GrievanceService Employees { get; }
    public GrievanceAdminService Admins { get; }
    public GrievanceCategoryService Categories { get; }
    public GrievanceDashboardService Dashboard { get; }

    public GrvKit()
    {
        Access = new GrievanceDocumentAccess(Storage, Availability, Audit, User, new GrvLogger<GrievanceDocumentAccess>());
        Employees = new GrievanceService(Repo, Storage, Availability, Access, Audit, User, Clock, EmployeeLog);
        Admins = new GrievanceAdminService(Repo, Storage, Availability, Access, Audit, User, Clock, AdminLog);
        Categories = new GrievanceCategoryService(Repo, Audit, User, Clock);
        Dashboard = new GrievanceDashboardService(Repo);
    }

    public static readonly byte[] PdfBytes = System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj\n<<>>\nendobj\n%%EOF");
    public static readonly byte[] JpegBytes = { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00 };
    public static readonly byte[] PngBytes = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    public static GrievanceUploadFile Upload(byte[] bytes, string name, string contentType) => new(new MemoryStream(bytes), name, contentType, bytes.Length);
    public static GrievanceUploadFile Pdf(string name = "report.pdf") => Upload(PdfBytes, name, "application/pdf");
    public static GrievanceUploadFile Jpeg(string name = "photo.jpg") => Upload(JpegBytes, name, "image/jpeg");
    public static GrievanceUploadFile Png(string name = "scan.png") => Upload(PngBytes, name, "image/png");

    /// <summary>A valid PDF of the given size (the signature plus padding), without allocating more than needed.</summary>
    public static GrievanceUploadFile PdfOfSize(long size)
    {
        byte[] bytes = new byte[size];
        PdfBytes.CopyTo(bytes, 0);
        return new GrievanceUploadFile(new MemoryStream(bytes), "big.pdf", "application/pdf", size);
    }
}
