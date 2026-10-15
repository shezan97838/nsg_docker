using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Application.Devices.DTOs;

/// <summary>Current device information response.</summary>
public class DeviceResponseDto
{
    public int Id { get; set; }
    public string Platform { get; set; } = string.Empty;
    public string? DeviceName { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? AppVersion { get; set; }
    public string? OsVersion { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public DateTime RegisteredAt { get; set; }
}
