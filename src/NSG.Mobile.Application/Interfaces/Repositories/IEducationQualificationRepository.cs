using NSG.Mobile.Domain.Entities;

namespace NSG.Mobile.Application.Interfaces.Repositories;

/// <summary>Data access for education qualifications. Hard deletion is intentionally not offered: qualifications are deactivated.</summary>
public interface IEducationQualificationRepository
{
    /// <summary>Tracked qualification, for update and deactivation.</summary>
    Task<EducationQualification?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Untracked page ordered by course name. <paramref name="isActive"/> null returns all.</summary>
    Task<(IReadOnlyList<EducationQualification> Items, int TotalCount)> GetPagedAsync(
        int pageNumber, int pageSize, bool? isActive, string? search, CancellationToken cancellationToken = default);

    Task<bool> NameExistsAsync(string courseName, int? excludeId, CancellationToken cancellationToken = default);

    /// <summary>True when any UsersPIS record uses this qualification.</summary>
    Task<bool> IsInUseAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Returns false when the database rejected the write as a duplicate name (unique index).</summary>
    Task<bool> AddAsync(EducationQualification entity, CancellationToken cancellationToken = default);

    /// <summary>Returns false when the database rejected the write as a duplicate name (unique index).</summary>
    Task<bool> UpdateAsync(EducationQualification entity, CancellationToken cancellationToken = default);
}
