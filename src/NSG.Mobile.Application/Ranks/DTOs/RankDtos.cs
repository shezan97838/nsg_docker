namespace NSG.Mobile.Application.Ranks.DTOs;

/// <summary>Full rank record returned to the admin portal.</summary>
public class RankResponseDto
{
    public int Id { get; set; }
    public int SrNo { get; set; }
    public string RankName { get; set; } = string.Empty;
    public string? ShortCode { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Minimal rank item for dropdown population.</summary>
public class RankDropdownDto
{
    public int Id { get; set; }
    public int SrNo { get; set; }
    public string RankName { get; set; } = string.Empty;
    public string? ShortCode { get; set; }
}

/// <summary>Request to create a rank.</summary>
public class CreateRankRequestDto
{
    public int SrNo { get; set; }
    public string RankName { get; set; } = string.Empty;
    public string? ShortCode { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Request to update a rank.</summary>
public class UpdateRankRequestDto
{
    public int SrNo { get; set; }
    public string RankName { get; set; } = string.Empty;
    public string? ShortCode { get; set; }
    public bool IsActive { get; set; }
}
