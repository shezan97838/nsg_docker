namespace NSG.Mobile.Application.Onboarding.DTOs;

/// <summary>Pending registration summary for IT Cell review. SOW §3.7 Step 2.</summary>
public class PendingRegistrationDto
{
    public int UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Rank { get; set; } = string.Empty;
    public string ForceNumber { get; set; } = string.Empty;
    public string MobileNumber { get; set; } = string.Empty;
    public DateTime RegistrationDate { get; set; }
}

/// <summary>IT approval/rejection request.</summary>
public class ApprovalRequestDto
{
    public string? Reason { get; set; }
}
