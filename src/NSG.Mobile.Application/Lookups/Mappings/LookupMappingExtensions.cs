using NSG.Mobile.Application.Lookups.DTOs;
using NSG.Mobile.Domain.Entities;

namespace NSG.Mobile.Application.Lookups.Mappings;

/// <summary>Explicit entity/DTO mapping for the PIS lookup modules (NSG units, parent department ranks).</summary>
public static class LookupMappingExtensions
{
    public static LookupResponseDto ToDto(this PisLookup l) => new()
    {
        Id = l.Id,
        Name = l.Name,
        IsActive = l.IsActive,
        CreatedAtIst = l.CreatedAtIst,
        UpdatedAtIst = l.UpdatedAtIst
    };

    public static LookupDropdownDto ToDropdownDto(this PisLookup l) => new() { Id = l.Id, Name = l.Name };

    /// <summary>Populates a new lookup; Id and timestamps are never taken from the request.</summary>
    public static T ToEntity<T>(this CreateLookupRequestDto r, DateTime nowIst) where T : PisLookup, new() => new()
    {
        Name = r.Name.Trim(),
        IsActive = r.IsActive,
        CreatedAtIst = nowIst
    };

    /// <summary>Applies only the permitted fields.</summary>
    public static void ApplyTo(this UpdateLookupRequestDto r, PisLookup l, DateTime nowIst)
    {
        l.Name = r.Name.Trim();
        l.IsActive = r.IsActive;
        l.UpdatedAtIst = nowIst;
    }
}
