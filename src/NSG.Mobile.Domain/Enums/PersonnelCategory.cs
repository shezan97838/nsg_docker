namespace NSG.Mobile.Domain.Enums;

/// <summary>
/// Reservation category of a person (PIS workbook column "CATEGORY (SC/ST/GEN/OBC)"). Stored as int; values are part
/// of the schema and must not change. No other categories are approved (EWS was considered and rejected).
/// </summary>
public enum PersonnelCategory
{
    GEN = 1,
    SC = 2,
    ST = 3,
    OBC = 4
}
