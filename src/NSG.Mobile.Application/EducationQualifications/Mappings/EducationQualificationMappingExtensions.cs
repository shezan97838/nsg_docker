using NSG.Mobile.Application.EducationQualifications.DTOs;
using NSG.Mobile.Domain.Entities;

namespace NSG.Mobile.Application.EducationQualifications.Mappings;

/// <summary>Explicit entity/DTO mapping for the education qualification lookup.</summary>
public static class EducationQualificationMappingExtensions
{
    public static EducationQualificationResponseDto ToDto(this EducationQualification e) => new()
    {
        Id = e.Id,
        CourseName = e.CourseName,
        IsActive = e.IsActive,
        CreatedAtIst = e.CreatedAtIst
    };
}
