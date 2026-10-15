namespace NSG.Mobile.Application.Trades.DTOs;

// ── NSG trade (table nsg_trade) ──

public class NsgTradeResponseDto
{
    public int Id { get; set; }
    public string NsgTradeName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAtIst { get; set; }
}

public class CreateNsgTradeRequestDto
{
    public string NsgTradeName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

/// <summary><see cref="IsActive"/> is required so an omitted value can never silently change the state.</summary>
public class UpdateNsgTradeRequestDto
{
    public string NsgTradeName { get; set; } = string.Empty;
    public bool? IsActive { get; set; }
}

// ── Parent-department trade (table pd_trade) ──

public class PdTradeResponseDto
{
    public int Id { get; set; }
    public string PdTradeName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAtIst { get; set; }
}

public class CreatePdTradeRequestDto
{
    public string PdTradeName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public class UpdatePdTradeRequestDto
{
    public string PdTradeName { get; set; } = string.Empty;
    public bool? IsActive { get; set; }
}
