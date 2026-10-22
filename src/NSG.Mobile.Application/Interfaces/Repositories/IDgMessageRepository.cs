using NSG.Mobile.Domain.Entities;

namespace NSG.Mobile.Application.Interfaces.Repositories;

public enum DgMessageUpdateOutcome { Updated = 0, NotFound }

/// <summary><see cref="PreviousImageKey"/> is the image key the row held before the update, so a replaced object can be removed afterwards.</summary>
public sealed record DgMessageUpdateResult(DgMessageUpdateOutcome Outcome, DgMessage? Message = null, string? PreviousImageKey = null);

public enum DgMessageDeleteOutcome { Deleted = 0, AlreadyDeleted, NotFound }

/// <summary>Data access for <c>dg_message</c>. There is no physical delete.</summary>
public interface IDgMessageRepository
{
    Task AddAsync(DgMessage message, CancellationToken cancellationToken = default);

    /// <summary>Active, non-deleted messages, newest first.</summary>
    Task<(IReadOnlyList<DgMessage> Items, int TotalCount)> GetActivePagedAsync(int pageNumber, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>One active, non-deleted message; null otherwise.</summary>
    Task<DgMessage?> GetActiveByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces name, designation, message and active flag of a non-deleted message and stamps the updater. The image key is replaced only when
    /// <paramref name="newImageKey"/> is not null; null keeps the existing image.
    /// </summary>
    Task<DgMessageUpdateResult> UpdateAsync(int id, string dgName, string designation, string message, bool isActive, string? newImageKey,
        long? updatedBy, DateTime nowIst, CancellationToken cancellationToken = default);

    /// <summary>Soft delete: sets <c>is_deleted</c>. The row and its image stay.</summary>
    Task<DgMessageDeleteOutcome> SoftDeleteAsync(int id, long? deletedBy, DateTime nowIst, CancellationToken cancellationToken = default);
}
