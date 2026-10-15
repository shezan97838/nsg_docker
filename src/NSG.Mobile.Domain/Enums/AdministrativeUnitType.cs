namespace NSG.Mobile.Domain.Enums;

/// <summary>
/// Constitutional classification of a first-level administrative division of India.
/// Stored in the database as an int; the numeric values are part of the schema and must not change:
/// 1 = State, 2 = UnionTerritory.
/// </summary>
public enum AdministrativeUnitType
{
    State = 1,
    UnionTerritory = 2
}
