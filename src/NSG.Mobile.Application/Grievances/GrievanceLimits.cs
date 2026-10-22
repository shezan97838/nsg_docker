namespace NSG.Mobile.Application.Grievances;

/// <summary>Upload limits applied to a grievance submission: the same for every category (categories carry no rules).</summary>
public sealed record EffectiveUploadLimits(bool AllowDocuments, int MaxFiles, long MaxFileSizeBytes, IReadOnlyList<string> AllowedTypes);

/// <summary>Server-side limits and messages of the Grievance module.</summary>
public static class GrievanceLimits
{
    /// <summary>At most this many attachments per grievance.</summary>
    public const int MaxFiles = 3;

    /// <summary>At most this many megabytes per attachment.</summary>
    public const int MaxFileSizeMb = 5;

    /// <summary>The note (description) must have at least this many characters.</summary>
    public const int NoteMinLength = 10;

    public const int CategoryNameMaxLength = 150;
    public const int SubjectMaxLength = 200;
    public const int NoteMaxLength = 2000;

    /// <summary>Limit for every remark and comment: the status log column holds 1000 characters.</summary>
    public const int RemarksMaxLength = 1000;

    public const int TicketSearchMaxLength = 30;
    public const int FileNameStemMaxLength = 80;

    /// <summary>How often a ticket-number collision is retried.</summary>
    public const int MaxTicketAttempts = 3;

    /// <summary>Storage category, which makes object keys start with <c>grievances/</c>.</summary>
    public const string StorageCategory = "grievances";

    /// <summary>The only document types the module ever accepts.</summary>
    public static readonly IReadOnlyList<string> AllowedTypes = new[] { "pdf", "jpg" };

    /// <summary>The limits every submission is checked against.</summary>
    public static readonly EffectiveUploadLimits Upload = new(true, MaxFiles, MaxFileSizeMb * 1024L * 1024L, AllowedTypes);
}

/// <summary>Error codes of the Grievance module that have no equivalent in <c>ApiErrorCodes</c>.</summary>
public static class GrievanceErrorCodes
{
    public const string StorageUnavailable = "STORAGE_UNAVAILABLE";
    public const string TicketUnavailable = "TICKET_UNAVAILABLE";
}

/// <summary>Human-readable messages shared by the grievance services and controllers.</summary>
public static class GrievanceMessages
{
    public const string NoCategoriesFound = "No grievance categories found.";
    public const string NoGrievancesFound = "No grievances found.";
    public const string NoDashboardData = "No grievances have been submitted yet.";
    public const string GrievanceNotFound = "Grievance not found.";
    public const string CategoryNotFound = "Grievance category not found.";
    public const string DocumentNotFound = "Document not found.";
    public const string DocumentFileMissing = "The document file is no longer available.";
    public const string StorageUnavailable = "File storage is temporarily unavailable. Please try again later.";
    public const string GrievanceClosed = "A closed grievance can no longer be changed.";
    public const string DuplicateCategory =
        "A grievance category with this name already exists. If it is deactivated or deleted, reuse the existing category instead of creating a new one.";
    public const string IdentityMissing = "Your identity could not be established. Please sign in again.";
}
