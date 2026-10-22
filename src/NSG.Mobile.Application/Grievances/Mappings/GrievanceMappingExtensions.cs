using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Grievances.DTOs;
using NSG.Mobile.Application.Grievances.Services;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Application.Grievances.Mappings;

/// <summary>Explicit entity → DTO mapping for the Grievance module. Stored IST instants leave the API with the fixed +05:30 offset.</summary>
public static class GrievanceMappingExtensions
{
    public static string DisplayName(this GrievanceStatus status) => status switch
    {
        GrievanceStatus.Submitted => "Submitted",
        GrievanceStatus.UnderReview => "Under Review",
        GrievanceStatus.ActionTaken => "Action Taken",
        GrievanceStatus.Closed => "Closed",
        _ => status.ToString()
    };

    /// <summary>Pending means not yet closed: statuses 1-3.</summary>
    public static bool IsPending(this GrievanceStatus status) => status != GrievanceStatus.Closed;

    public static string DisplayName(this GrievanceLogAction action) => action switch
    {
        GrievanceLogAction.StatusChange => "Status change",
        GrievanceLogAction.Comment => "Comment",
        GrievanceLogAction.Forward => "Forwarded",
        _ => action.ToString()
    };

    public static GrievanceCategoryDto ToDto(this GrievanceCategory category) => new() { Id = category.Id, Name = category.GrievanceName };

    public static GrievanceCategoryAdminDto ToAdminDto(this GrievanceCategory category) => new()
    {
        Id = category.Id,
        Name = category.GrievanceName,
        DepartmentId = category.DepartmentId,
        IsActive = category.IsActive,
        IsDeleted = category.IsDeleted,
        CreatedAt = IstTime.FromIst(category.CreatedAtIst),
        CreatedBy = category.CreatedBy,
        UpdatedAt = category.UpdatedAtIst.HasValue ? IstTime.FromIst(category.UpdatedAtIst.Value) : null,
        UpdatedBy = category.UpdatedBy
    };

    public static IReadOnlyList<GrievanceDocumentDto> ToDocumentDtos(this Grievance grievance)
        => GrievanceDocumentRules.ParseDocuments(grievance.DocumentsJson)
            .Select((d, index) => d.ToDto(index))
            .ToList();

    public static GrievanceDocumentDto ToDto(this GrievanceDocumentRecord record, int index) => new()
    {
        Index = index,
        Name = record.Name,
        ContentType = record.ContentType,
        SizeBytes = record.SizeBytes,
        UploadedAt = IstTime.FromIst(record.UploadedAtIst)
    };

    public static GrievanceSummaryDto ToSummaryDto(this Grievance g, string categoryName)
    {
        GrievanceSummaryDto dto = new();
        FillSummary(dto, g, categoryName);
        return dto;
    }

    public static GrievanceAdminSummaryDto ToAdminSummaryDto(this Grievance g, string categoryName, GrievanceEmployeeInfo? employee = null)
    {
        GrievanceAdminSummaryDto dto = new() { CreatedBy = g.CreatedBy, AssignedTo = g.AssignedTo, Employee = employee?.ToDto() };
        FillSummary(dto, g, categoryName);
        return dto;
    }

    public static GrievanceDetailDto ToDetailDto(this Grievance g, string categoryName, IReadOnlyList<GrievanceStatusLog> timeline)
    {
        GrievanceDetailDto dto = new()
        {
            Note = g.GrievanceNote,
            Documents = g.ToDocumentDtos(),
            Timeline = timeline.Select(l => l.ToDto<GrievanceStatusEntryDto>()).ToList(),
            ActionRemarks = g.ActionRemarks,
            ActionTakenAt = g.ActionTakenAtIst.HasValue ? IstTime.FromIst(g.ActionTakenAtIst.Value) : null,
            ClosedRemarks = g.ClosedRemarks,
            ClosedAt = g.ClosedAtIst.HasValue ? IstTime.FromIst(g.ClosedAtIst.Value) : null
        };
        FillSummary(dto, g, categoryName);
        return dto;
    }

    public static GrievanceAdminDetailDto ToAdminDetailDto(this Grievance g, string categoryName, IReadOnlyList<GrievanceStatusLog> timeline, GrievanceEmployeeInfo? employee = null)
    {
        GrievanceAdminDetailDto dto = new()
        {
            CreatedBy = g.CreatedBy,
            AssignedTo = g.AssignedTo,
            Employee = employee?.ToDto(),
            Note = g.GrievanceNote,
            Documents = g.ToDocumentDtos(),
            Timeline = timeline.Select(l =>
            {
                GrievanceAdminStatusEntryDto entry = l.ToDto<GrievanceAdminStatusEntryDto>();
                entry.ChangedBy = l.ChangedBy;
                entry.ForwardedTo = l.ForwardedTo;
                return entry;
            }).ToList(),
            ActionTakenBy = g.ActionTakenBy,
            ActionRemarks = g.ActionRemarks,
            ActionTakenAt = g.ActionTakenAtIst.HasValue ? IstTime.FromIst(g.ActionTakenAtIst.Value) : null,
            ClosedBy = g.ClosedBy,
            ClosedRemarks = g.ClosedRemarks,
            ClosedAt = g.ClosedAtIst.HasValue ? IstTime.FromIst(g.ClosedAtIst.Value) : null
        };
        FillSummary(dto, g, categoryName);
        return dto;
    }

    private static GrievanceEmployeeDto ToDto(this GrievanceEmployeeInfo info) => new() { NsgNumber = info.NsgNumber, Name = info.FullName, Unit = info.UnitName };

    private static T ToDto<T>(this GrievanceStatusLog log) where T : GrievanceStatusEntryDto, new() => new()
    {
        Action = (int)log.LogAction,
        ActionName = log.LogAction.DisplayName(),
        ActorType = log.ChangedByType.ToString(),
        FromStatus = log.FromStatus.HasValue ? (int)log.FromStatus.Value : null,
        FromStatusName = log.FromStatus?.DisplayName(),
        ToStatus = (int)log.ToStatus,
        ToStatusName = log.ToStatus.DisplayName(),
        Remarks = log.Remarks,
        ChangedAt = IstTime.FromIst(log.ChangedAtIst)
    };

    private static void FillSummary(GrievanceSummaryDto dto, Grievance g, string categoryName)
    {
        dto.Id = g.Id;
        dto.GrievanceId = g.TicketNumber;
        dto.CategoryId = g.CategoryId;
        dto.CategoryName = categoryName;
        dto.Subject = g.Subject;
        dto.Status = (int)g.CurrentStatus;
        dto.StatusName = g.CurrentStatus.DisplayName();
        dto.DocumentCount = GrievanceDocumentRules.ParseDocuments(g.DocumentsJson).Count;
        dto.CreatedAt = IstTime.FromIst(g.CreatedAtIst);
    }
}
