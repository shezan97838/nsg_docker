namespace NSG.Mobile.Domain.Enums;

/// <summary>
/// Census of India urban classification of a city/town.
/// A statutory town has a municipality, corporation, cantonment board or notified town area committee;
/// a census town meets the census urban criteria without statutory notification.
/// </summary>
public enum TownClassification
{
    StatutoryTown = 1,
    CensusTown = 2
}
