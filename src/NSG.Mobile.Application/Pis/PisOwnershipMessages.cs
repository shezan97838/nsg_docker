namespace NSG.Mobile.Application.Pis;

/// <summary>Client-safe messages of the PIS ownership rules (a record is visible and changeable only by the PIS Admin it is assigned to).</summary>
public static class PisOwnershipMessages
{
    /// <summary>Refusal of a write on a record assigned to another PIS Admin.</summary>
    public const string NotAssignedToYou = "This personnel record is assigned to another PIS Admin, so you cannot change it.";

    /// <summary>Refusal of a write on a record assigned to nobody.</summary>
    public const string Unassigned = "This personnel record is not assigned to any PIS Admin yet, so it cannot be changed. Ask the Super Admin to assign it.";

    /// <summary>Refusal of a read of a record assigned to another PIS Admin.</summary>
    public const string NotAssignedToYouToView = "This personnel record is assigned to another PIS Admin, so you cannot view it.";

    /// <summary>Answer for a record the caller cannot see: it does not exist, or it is assigned to nobody.</summary>
    public const string NotFound = "Personnel record not found.";
}
