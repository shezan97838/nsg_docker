using System.Globalization;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Grievances.DTOs;
using NSG.Mobile.Application.Grievances.Mappings;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Application.Grievances.Services;

/// <summary>Counts for the Grievance Admin dashboard.</summary>
public interface IGrievanceDashboardService
{
    /// <summary>Summary over all grievances, or those created from <paramref name="fromDate"/> to <paramref name="toDate"/> (IST calendar days, inclusive).</summary>
    Task<Result<GrievanceDashboardDto>> GetSummaryAsync(DateOnly? fromDate, DateOnly? toDate, CancellationToken cancellationToken);
}

public class GrievanceDashboardService : IGrievanceDashboardService
{
    private readonly IGrievanceRepository _repository;
    public GrievanceDashboardService(IGrievanceRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<GrievanceDashboardDto>> GetSummaryAsync(DateOnly? fromDate, DateOnly? toDate, CancellationToken cancellationToken)
    {
        if (fromDate.HasValue && toDate.HasValue && fromDate.Value > toDate.Value)
            return Result.Failure<GrievanceDashboardDto>(ApiErrorCodes.ValidationFailed, "fromDate must not be after toDate.");

        DateTime? from = fromDate?.ToDateTime(TimeOnly.MinValue);
        DateTime? toExclusive = toDate?.AddDays(1).ToDateTime(TimeOnly.MinValue);
        GrievanceDashboardData data = await _repository.GetDashboardAsync(from, toExclusive, cancellationToken);

        // All four statuses always appear, zero included.
        Dictionary<GrievanceStatus, int> counts = data.ByStatus.ToDictionary(s => s.Status, s => s.Count);
        List<GrievanceStatusCountDto> byStatus = Enum.GetValues<GrievanceStatus>()
            .OrderBy(s => (int)s)
            .Select(s => new GrievanceStatusCountDto
            {
                Status = (int)s,
                StatusName = s.DisplayName(),
                Count = counts.GetValueOrDefault(s),
                IsPending = s.IsPending()
            }).ToList();

        return Result.Success(new GrievanceDashboardDto
        {
            TotalCount = data.TotalCount,
            PendingCount = byStatus.Where(s => s.IsPending).Sum(s => s.Count),
            ByStatus = byStatus,
            ByCategory = data.ByCategory.Select(c => new GrievanceCategoryCountDto { CategoryId = c.CategoryId, CategoryName = c.CategoryName, Count = c.Count }).ToList(),
            ByMonth = data.ByMonth.Select(m => new GrievanceMonthCountDto
            {
                Month = string.Create(CultureInfo.InvariantCulture, $"{m.Year:D4}-{m.Month:D2}"),
                Count = m.Count
            }).ToList(),
            AverageResolutionDays = data.AverageResolutionSeconds.HasValue ? Math.Round(data.AverageResolutionSeconds.Value / 86400d, 2) : null
        });
    }
}
