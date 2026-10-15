using NSG.Mobile.Application.Trades.DTOs;
using NSG.Mobile.Domain.Entities;

namespace NSG.Mobile.Application.Trades.Mappings;

/// <summary>Explicit entity/DTO mapping for the two trade lookups.</summary>
public static class TradeMappingExtensions
{
    public static NsgTradeResponseDto ToDto(this NsgTrade t) => new()
    {
        Id = t.Id,
        NsgTradeName = t.NsgTradeName,
        IsActive = t.IsActive,
        CreatedAtIst = t.CreatedAtIst
    };

    public static PdTradeResponseDto ToDto(this PdTrade t) => new()
    {
        Id = t.Id,
        PdTradeName = t.PdTradeName,
        IsActive = t.IsActive,
        CreatedAtIst = t.CreatedAtIst
    };
}
