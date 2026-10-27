namespace NSG.Mobile.Application.Grievances.DTOs;

// ── Categories ──

/// <summary>A category as the mobile app sees it in the submission dropdown (active, not deleted).</summary>
public class GrievanceCategoryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class GrievanceCategoryAdminDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? DepartmentId { get; set; }
    public bool IsActive { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public long? UpdatedBy { get; set; }
}

/// <summary>Create and update body of a category. <see cref="IsActive"/> defaults to true on create and is required on update (an update replaces every field).</summary>
public class SaveGrievanceCategoryRequestDto
{
    public string Name { get; set; } = string.Empty;
    public int? DepartmentId { get; set; }
    public bool? IsActive { get; set; }
}

// ── Grievances ──

public class CreateGrievanceRequestDto
{
    public int CategoryId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
}

/// <summary>Body of the admin status change. <see cref="ToStatus"/>: 2 Under Review, 3 Action Taken (remarks required). Resolving (status 4) is a separate request: it needs a resolution comment and a supporting file (see <see cref="ResolveGrievanceRequestDto"/>).</summary>
public class UpdateGrievanceStatusRequestDto
{
    public int? ToStatus { get; set; }
    public string? Remarks { get; set; }
}

/// <summary>Text part of the admin resolve request (multipart form). The supporting file travels beside it as an upload.</summary>
public class ResolveGrievanceRequestDto
{
    /// <summary>The resolution comment: required, at most 1000 characters. Stored as the closing remarks and in the history.</summary>
    public string? Remarks { get; set; }
}

/// <summary>Body of an admin comment on a grievance.</summary>
public class AddGrievanceCommentRequestDto
{
    public string Comment { get; set; } = string.Empty;
}

/// <summary>Body of a forward or reassignment to another active Grievance Admin (<c>admin_profiles.id</c>).</summary>
public class ForwardGrievanceRequestDto
{
    public long? ToAdminId { get; set; }
    public string? Remarks { get; set; }
}

public class GrievanceDocumentDto
{
    /// <summary>Zero-based position, used in <c>documents/{index}</c>.</summary>
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTimeOffset UploadedAt { get; set; }
}

public class GrievanceStatusEntryDto
{
    /// <summary>1 Status change (includes creation), 2 Comment, 3 Forward.</summary>
    public int Action { get; set; }
    public string ActionName { get; set; } = string.Empty;

    /// <summary>Employee or Admin.</summary>
    public string ActorType { get; set; } = string.Empty;

    public int? FromStatus { get; set; }
    public string? FromStatusName { get; set; }
    public int ToStatus { get; set; }
    public string ToStatusName { get; set; } = string.Empty;
    /// <summary>The comment or remarks of this entry.</summary>
    public string? Remarks { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
}

public class GrievanceAdminStatusEntryDto : GrievanceStatusEntryDto
{
    /// <summary>Id in the table named by <see cref="GrievanceStatusEntryDto.ActorType"/> (Employee: mobile_user_profiles.id, Admin: admin_profiles.id).</summary>
    public long ChangedBy { get; set; }

    /// <summary><c>admin_profiles.id</c> the grievance was forwarded to; set only for a forward.</summary>
    public long? ForwardedTo { get; set; }
}

public class GrievanceSummaryDto
{
    public long Id { get; set; }
    /// <summary>The ticket number, format <c>NSG-YYYY-000001</c> (database column <c>grievance_id</c>). <see cref="Id"/> is the numeric key used in routes.</summary>
    public string GrievanceId { get; set; } = string.Empty;
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public int Status { get; set; }
    public string StatusName { get; set; } = string.Empty;
    public int DocumentCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class GrievanceDetailDto : GrievanceSummaryDto
{
    public string Note { get; set; } = string.Empty;
    public IReadOnlyList<GrievanceDocumentDto> Documents { get; set; } = Array.Empty<GrievanceDocumentDto>();
    public IReadOnlyList<GrievanceStatusEntryDto> Timeline { get; set; } = Array.Empty<GrievanceStatusEntryDto>();
    public string? ActionRemarks { get; set; }
    public DateTimeOffset? ActionTakenAt { get; set; }
    public string? ClosedRemarks { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }

    /// <summary>Reference (storage key) of the supporting file the admin uploaded at resolution; not a downloadable link. The file is downloaded with <c>GET grievances/{id}/resolution-document</c>. Null while open and for older closed grievances.</summary>
    public string? ResolutionDocument { get; set; }
}

/// <summary>Minimum identity of the employee, shown to Grievance Admins only.</summary>
public class GrievanceEmployeeDto
{
    public string NsgNumber { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Unit { get; set; }
}

public class GrievanceAdminSummaryDto : GrievanceSummaryDto
{
    /// <summary>Who raised the grievance; null only if the profile no longer exists.</summary>
    public GrievanceEmployeeDto? Employee { get; set; }

    /// <summary><c>mobile_user_profiles.id</c> of the employee.</summary>
    public long CreatedBy { get; set; }

    /// <summary><c>admin_profiles.id</c> of the nodal officer; null when unassigned.</summary>
    public long? AssignedTo { get; set; }
}

public class GrievanceAdminDetailDto : GrievanceAdminSummaryDto
{
    public string Note { get; set; } = string.Empty;
    public IReadOnlyList<GrievanceDocumentDto> Documents { get; set; } = Array.Empty<GrievanceDocumentDto>();
    public IReadOnlyList<GrievanceAdminStatusEntryDto> Timeline { get; set; } = Array.Empty<GrievanceAdminStatusEntryDto>();
    public long? ActionTakenBy { get; set; }
    public string? ActionRemarks { get; set; }
    public DateTimeOffset? ActionTakenAt { get; set; }
    public long? ClosedBy { get; set; }
    public string? ClosedRemarks { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }

    /// <summary>Reference (storage key) of the supporting file uploaded at resolution; not a downloadable link. Download: <c>GET admin/grievances/{id}/resolution-document</c>. Null while open and for older closed grievances.</summary>
    public string? ResolutionDocument { get; set; }
}

// ── Dashboard ──

public class GrievanceStatusCountDto
{
    public int Status { get; set; }
    public string StatusName { get; set; } = string.Empty;
    public int Count { get; set; }
    public bool IsPending { get; set; }
}

public class GrievanceCategoryCountDto
{
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class GrievanceMonthCountDto
{
    /// <summary><c>yyyy-MM</c> (IST).</summary>
    public string Month { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class GrievanceDashboardDto
{
    public int TotalCount { get; set; }

    /// <summary>Grievances in statuses 1-3 (Submitted, Under Review, Action Taken).</summary>
    public int PendingCount { get; set; }

    /// <summary>All four statuses, zero counts included.</summary>
    public IReadOnlyList<GrievanceStatusCountDto> ByStatus { get; set; } = Array.Empty<GrievanceStatusCountDto>();
    public IReadOnlyList<GrievanceCategoryCountDto> ByCategory { get; set; } = Array.Empty<GrievanceCategoryCountDto>();
    public IReadOnlyList<GrievanceMonthCountDto> ByMonth { get; set; } = Array.Empty<GrievanceMonthCountDto>();

    /// <summary>Creation to closing over closed grievances; null while none is closed.</summary>
    public double? AverageResolutionDays { get; set; }
}

// ── Service-level types (not serialized) ──

/// <summary>An uploaded file as the Application layer sees it (the controller adapts <c>IFormFile</c> to this).</summary>
public sealed record GrievanceUploadFile(Stream Content, string FileName, string ContentType, long Length);

/// <summary>A document opened for download. The caller owns and disposes <see cref="Stream"/>.</summary>
public sealed record GrievanceDocumentContent(Stream Stream, string ContentType, string FileName);
