using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Application.Interfaces.Repositories;

public enum GrievanceCreateOutcome { Created = 0, TicketNumberUnavailable }

/// <summary><see cref="Grievance"/> is the stored row (with its ticket number and id) when <see cref="Outcome"/> is Created.</summary>
public sealed record GrievanceCreateResult(GrievanceCreateOutcome Outcome, Grievance? Grievance = null);

public enum GrievanceStatusChangeOutcome { Changed = 0, NotFound, StatusChanged }

/// <summary><see cref="CurrentStatus"/> is the status found when another request changed the grievance first (<see cref="GrievanceStatusChangeOutcome.StatusChanged"/>).</summary>
public sealed record GrievanceStatusChangeResult(GrievanceStatusChangeOutcome Outcome, GrievanceStatus? CurrentStatus = null);

public enum CategoryWriteOutcome { Saved = 0, DuplicateName, NotFound }

public enum CategoryDeleteOutcome { Deleted = 0, AlreadyDeleted, NotFound }

public enum GrievanceCommentOutcome { Added = 0, NotFound, Closed }

public enum GrievanceForwardOutcome { Forwarded = 0, NotFound, Closed, AlreadyAssigned }

/// <summary>
/// List filter shared by the employee list (<see cref="CreatedBy"/> set from the token) and the admin list.
/// <see cref="FromIst"/> is inclusive and <see cref="ToIstExclusive"/> exclusive; both are IST instants.
/// </summary>
public sealed record GrievanceListFilter(
    GrievanceStatus? Status = null,
    int? CategoryId = null,
    DateTime? FromIst = null,
    DateTime? ToIstExclusive = null,
    string? TicketSearch = null,
    long? CreatedBy = null,
    long? AssignedTo = null);

/// <summary>Minimum identity of the employee who raised a grievance, for admins only. Name and unit are null when the personnel record is missing or has none.</summary>
public sealed record GrievanceEmployeeInfo(long ProfileId, string NsgNumber, string? FullName, string? UnitName);

public sealed record GrievanceStatusCount(GrievanceStatus Status, int Count);
public sealed record GrievanceCategoryCount(int CategoryId, string CategoryName, int Count);
public sealed record GrievanceMonthCount(int Year, int Month, int Count);

/// <summary>Aggregates for the admin dashboard. <see cref="AverageResolutionSeconds"/> is null while nothing is closed.</summary>
public sealed record GrievanceDashboardData(
    int TotalCount,
    IReadOnlyList<GrievanceStatusCount> ByStatus,
    IReadOnlyList<GrievanceCategoryCount> ByCategory,
    IReadOnlyList<GrievanceMonthCount> ByMonth,
    double? AverageResolutionSeconds);

/// <summary>
/// Data access for <c>grievance_category</c>, <c>grievance</c> and <c>grievance_status_log</c>. Every multi-row write is one transaction.
/// Only active grievances (<c>is_active</c>) are ever returned or changed; there is no physical delete.
/// </summary>
public interface IGrievanceRepository
{
    // ── Categories ──

    /// <summary>Categories that are active and not deleted, by name (the employee dropdown).</summary>
    Task<IReadOnlyList<GrievanceCategory>> GetActiveCategoriesAsync(CancellationToken cancellationToken = default);

    /// <summary>Page of non-deleted categories (active or not), by name.</summary>
    Task<(IReadOnlyList<GrievanceCategory> Items, int TotalCount)> GetCategoriesPagedAsync(
        int pageNumber, int pageSize, bool? isActive, string? search, CancellationToken cancellationToken = default);

    /// <summary>The category row, deleted or not; callers decide what a deleted row means.</summary>
    Task<GrievanceCategory?> GetCategoryByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Inserts the category. The unique name index turns a duplicate (including a deactivated category) into <see cref="CategoryWriteOutcome.DuplicateName"/>.</summary>
    Task<CategoryWriteOutcome> AddCategoryAsync(GrievanceCategory category, CancellationToken cancellationToken = default);

    /// <summary>Replaces name, department and active flag of a non-deleted category and stamps the updater. A deleted category is NotFound.</summary>
    Task<CategoryWriteOutcome> UpdateCategoryAsync(
        int id, string name, int? departmentId, bool isActive, long? updatedBy, DateTime nowIst, CancellationToken cancellationToken = default);

    /// <summary>Soft delete: sets <c>is_deleted</c> and clears <c>is_active</c>. The row and every grievance that references it stay.</summary>
    Task<CategoryDeleteOutcome> SoftDeleteCategoryAsync(int id, long? deletedBy, DateTime nowIst, CancellationToken cancellationToken = default);

    // ── Grievances ──

    /// <summary>
    /// Assigns the next ticket number of the grievance's IST year (<c>NSG-YYYY-000001</c>), inserts the grievance and its first status-log
    /// row in one transaction. A ticket-number collision with a concurrent request is retried, at most <paramref name="maxTicketAttempts"/> times.
    /// </summary>
    Task<GrievanceCreateResult> CreateAsync(Grievance grievance, long createdByProfileId, int maxTicketAttempts, CancellationToken cancellationToken = default);

    /// <summary>Page of grievances (newest first) with their category, matching <paramref name="filter"/>.</summary>
    Task<(IReadOnlyList<Grievance> Items, int TotalCount)> GetPagedAsync(
        GrievanceListFilter filter, int pageNumber, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>One grievance with its category. When <paramref name="createdBy"/> is set the grievance must belong to that employee, else null.</summary>
    Task<Grievance?> GetByIdAsync(long id, long? createdBy, CancellationToken cancellationToken = default);

    /// <summary>History of a grievance (creation, status changes, comments, forwards), oldest first.</summary>
    Task<IReadOnlyList<GrievanceStatusLog>> GetTimelineAsync(long grievancePk, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a grievance from <paramref name="from"/> to <paramref name="to"/> and appends the status-log row in one transaction. The move happens only
    /// if the grievance is still in <paramref name="from"/>, so of two simultaneous requests exactly one wins.
    /// </summary>
    Task<GrievanceStatusChangeResult> ChangeStatusAsync(
        long id, GrievanceStatus from, GrievanceStatus to, long adminProfileId, string? remarks, DateTime nowIst, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a grievance: moves it from Action Taken to Closed, records who and when, the closing remarks and the resolution document
    /// (<paramref name="resolutionDocument"/>, the storage key) and appends the status-log row, all in one transaction. The move happens only if the grievance
    /// is still Action Taken, so of two simultaneous requests exactly one wins and nothing is half-written.
    /// </summary>
    Task<GrievanceStatusChangeResult> ResolveAsync(
        long id, long adminProfileId, string remarks, string resolutionDocument, DateTime nowIst, CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends a comment row (status unchanged) while the grievance is not Closed. The check and the insert are one transaction.
    /// </summary>
    Task<GrievanceCommentOutcome> AddCommentAsync(long id, long adminProfileId, string comment, DateTime nowIst, CancellationToken cancellationToken = default);

    /// <summary>
    /// Assigns the grievance to another Grievance Admin and appends a forward row (status unchanged), in one transaction, while it is not Closed.
    /// </summary>
    Task<GrievanceForwardOutcome> ForwardAsync(long id, long adminProfileId, long toAdminId, string? remarks, DateTime nowIst, CancellationToken cancellationToken = default);

    /// <summary>Name, NSG number and unit of the given employee profiles (mobile profile joined to PIS by NSG number). Plain columns: nothing here is encrypted.</summary>
    Task<IReadOnlyDictionary<long, GrievanceEmployeeInfo>> GetEmployeeInfoAsync(IReadOnlyCollection<long> profileIds, CancellationToken cancellationToken = default);

    // ── Dashboard and lookups ──

    Task<GrievanceDashboardData> GetDashboardAsync(DateTime? fromIst, DateTime? toIstExclusive, CancellationToken cancellationToken = default);

    /// <summary>True when the id is an existing, enabled, activated Admin Portal account holding the GRIEVANCE_ADMIN role.</summary>
    Task<bool> IsActiveGrievanceAdminAsync(long adminProfileId, CancellationToken cancellationToken = default);
}
