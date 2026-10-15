namespace NSG.Mobile.Domain.Entities;

/// <summary>
/// NSG trade lookup (for example "GD"). Table <c>nsg_trade</c>. Completely independent: no rank, no code, and no link to
/// <see cref="PdTrade"/>; the only reference to it is the optional <see cref="UserPis.NsgTradeId"/>.
/// </summary>
public class NsgTrade
{
    public int Id { get; set; }

    /// <summary>Trade name. Required and unique within this table (case-insensitive under the default MySQL collation).</summary>
    public string NsgTradeName { get; set; } = string.Empty;

    public DateTime CreatedAtIst { get; set; } = NSG.Mobile.Domain.Common.IndianStandardTime.Now;
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Parent-department trade lookup. Table <c>pd_trade</c>. Completely independent of <see cref="NsgTrade"/> and of every rank
/// table; the only reference to it is the optional <see cref="UserPis.PdTradeId"/>.
/// </summary>
public class PdTrade
{
    public int Id { get; set; }

    /// <summary>Trade name. Required and unique within this table (case-insensitive under the default MySQL collation).</summary>
    public string PdTradeName { get; set; } = string.Empty;

    public DateTime CreatedAtIst { get; set; } = NSG.Mobile.Domain.Common.IndianStandardTime.Now;
    public bool IsActive { get; set; } = true;
}
