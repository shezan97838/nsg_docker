using NSG.Mobile.Application.Onboarding.DTOs;
using NSG.Mobile.Application.Devices.DTOs;
using NSG.Mobile.Domain.Entities;

namespace NSG.Mobile.Application.Authentication.Mappings;

/// <summary>
/// Explicit mapping extensions for User entity ↔ DTOs.
/// No AutoMapper — explicit, readable, null-safe, predictable.
/// NEVER maps: PasswordHash, security tokens, internal audit fields.
/// </summary>
public static class UserMappingExtensions
{
    public static PendingRegistrationDto ToPendingDto(this User user)
    {
        return new PendingRegistrationDto
        {
            UserId = user.Id,
            Name = user.Name,
            Rank = user.Rank,
            ForceNumber = user.ForceNumber,
            MobileNumber = user.MobileNumber,
            RegistrationDate = user.CreatedAt
        };
    }
}

/// <summary>Mapping extensions for Device entity ↔ DTOs.</summary>
public static class DeviceMappingExtensions
{
    public static DeviceResponseDto ToDto(this Device device)
    {
        return new DeviceResponseDto
        {
            Id = device.Id,
            Platform = device.Platform.ToString(),
            DeviceName = device.DeviceName,
            Status = device.Status.ToString(),
            AppVersion = device.AppVersion,
            OsVersion = device.OsVersion,
            LastSeenAt = device.LastSeenAt,
            RegisteredAt = device.CreatedAt
        };
    }
}
