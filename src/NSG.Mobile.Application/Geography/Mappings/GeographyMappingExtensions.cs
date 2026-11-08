using NSG.Mobile.Application.Geography.DTOs;
using NSG.Mobile.Domain.Entities;

namespace NSG.Mobile.Application.Geography.Mappings;

/// <summary>Explicit entity/DTO mapping for the geography module.</summary>
public static class GeographyMappingExtensions
{
    public static StateResponseDto ToDto(this State s) => new()
    {
        Id = s.Id,
        Name = s.Name,
        Code = s.Code,
        AdministrativeType = s.AdministrativeType,
        IsActive = s.IsActive,
        CreatedAtIst = s.CreatedAtIst,
        UpdatedAtIst = s.UpdatedAtIst
    };

    public static State ToEntity(this CreateStateRequestDto r, DateTime nowIst) => new()
    {
        Name = r.Name.Trim(),
        Code = NormalizeCode(r.Code),
        AdministrativeType = r.AdministrativeType,
        IsActive = r.IsActive,
        CreatedAtIst = nowIst
    };

    /// <summary>Applies only the permitted fields; Id and timestamps are never taken from the request.</summary>
    public static void ApplyTo(this UpdateStateRequestDto r, State s, DateTime nowIst)
    {
        s.Name = r.Name.Trim();
        s.Code = NormalizeCode(r.Code);
        s.AdministrativeType = r.AdministrativeType;
        s.IsActive = r.IsActive;
        s.UpdatedAtIst = nowIst;
    }

    private static string? NormalizeCode(string? code)
        => string.IsNullOrWhiteSpace(code) ? null : code.Trim();
}
