using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.DgMessages.DTOs;
using NSG.Mobile.Domain.Entities;

namespace NSG.Mobile.Application.DgMessages.Mappings;

/// <summary>Explicit entity → DTO mapping. Stored IST instants leave the API with the fixed +05:30 offset; the image key never leaves the server.</summary>
public static class DgMessageMappingExtensions
{
    public static DgMessageDto ToDto(this DgMessage m) => new()
    {
        Id = m.Id,
        DgName = m.DgName,
        Designation = m.Designation,
        Message = m.Message,
        HasImage = !string.IsNullOrEmpty(m.ImageStorageKey),
        IsActive = m.IsActive,
        CreatedAt = IstTime.FromIst(m.CreatedAtIst),
        CreatedBy = m.CreatedBy,
        UpdatedAt = m.UpdatedAtIst.HasValue ? IstTime.FromIst(m.UpdatedAtIst.Value) : null,
        UpdatedBy = m.UpdatedBy
    };
}
