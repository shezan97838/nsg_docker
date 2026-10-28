namespace NSG.Mobile.Domain.Enums;

/// <summary>
/// Gender of a person in the PIS record. Stored as int; values are part of the schema and must not change.
/// Exactly three values are approved.
/// </summary>
public enum Gender
{
    Male = 1,
    Female = 2,
    Other = 3
}
