using System.ComponentModel.DataAnnotations;

namespace NSG.Mobile.Domain.Enums;

/// <summary>
/// ABO/Rh blood group (e.g. "O+", "B+" in the PIS workbook). Stored as int; values must not change.
/// <see cref="DisplayAttribute.Name"/> carries the conventional label shown to users.
/// </summary>
public enum BloodGroupType
{
    [Display(Name = "A+")]
    APositive = 1,

    [Display(Name = "A-")]
    ANegative = 2,

    [Display(Name = "B+")]
    BPositive = 3,

    [Display(Name = "B-")]
    BNegative = 4,

    [Display(Name = "AB+")]
    ABPositive = 5,

    [Display(Name = "AB-")]
    ABNegative = 6,

    [Display(Name = "O+")]
    OPositive = 7,

    [Display(Name = "O-")]
    ONegative = 8
}
