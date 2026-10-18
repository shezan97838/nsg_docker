using NSG.Mobile.Application.Admin.DTOs;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Domain.Entities;

namespace NSG.Mobile.Application.Admin.Mappings;

/// <summary>Explicit entity-to-DTO mapping for the Admin Portal (no AutoMapper).</summary>
public static class AdminMappingExtensions
{
    public static AdminRoleDto ToDto(this AdminRole role) => new()
    {
        Id = role.Id, RoleCode = role.RoleCode, RoleName = role.RoleName, Description = role.Description
    };

    public static AdminAccountDto ToDto(this AdminAccountRow row) => new()
    {
        Id = row.Profile.Id,
        NsgNumber = row.Profile.NsgNumber,
        FullName = row.FullName,
        RoleCode = row.Profile.Role?.RoleCode ?? string.Empty,
        RoleName = row.Profile.Role?.RoleName ?? string.Empty,
        ActivationStatus = row.Profile.ActivationStatus.ToString(),
        IsActive = row.Profile.IsActive,
        LastLoginAt = row.Profile.LastLoginAtIst.HasValue ? IstTime.FromIst(row.Profile.LastLoginAtIst.Value) : null,
        CreatedAt = IstTime.FromIst(row.Profile.CreatedAtIst),
        UpdatedAt = row.Profile.UpdatedAtIst.HasValue ? IstTime.FromIst(row.Profile.UpdatedAtIst.Value) : null
    };

    public static AdminProfileSummaryDto ToSummary(this AdminProfile profile) => new()
    {
        Id = profile.Id,
        NsgNumber = profile.NsgNumber,
        RoleCode = profile.Role?.RoleCode ?? string.Empty,
        RoleName = profile.Role?.RoleName ?? string.Empty
    };
}
