using FluentValidation;
using NSG.Mobile.Application.Pis.DTOs;
using NSG.Mobile.Domain.Common;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;

namespace NSG.Mobile.Application.Pis.Validators;

/// <summary>
/// Validates the non-sensitive fields of a personnel record. The encrypted columns are only length-checked here;
/// their plaintext is validated by <see cref="PisSensitiveIdentifiersValidator"/> before encryption.
/// Only the NSG number and service number are mandatory: the source workbook marks nothing else as required.
/// </summary>
public class UserPisValidator : AbstractValidator<UserPis>
{
    public UserPisValidator()
    {
        RuleFor(x => x.NsgNumber).MustBeRequiredTrimmedIdentifier(30);
        RuleFor(x => x.ServiceNumber!).MustBeRequiredTrimmedIdentifier(30).When(x => x.ServiceNumber != null); // nullable and deliberately not unique; when present it must be trimmed

        // Workbook text columns (lengths mirror the database columns).
        RuleFor(x => x.FullName).MaximumLength(200);
        RuleFor(x => x.FatherName).MaximumLength(200);
        RuleFor(x => x.MotherName).MaximumLength(200);
        RuleFor(x => x.SpouseName).MaximumLength(200);
        RuleFor(x => x.NsgRank).MaximumLength(100);
        RuleFor(x => x.ParentDepartmentRank).MaximumLength(100);
        RuleFor(x => x.NsgUnit).MaximumLength(100);
        RuleFor(x => x.Education).MaximumLength(100);
        RuleFor(x => x.Religion).MaximumLength(100);
        RuleFor(x => x.SideSteppedDetails).MaximumLength(255);
        RuleFor(x => x.Gender).MaximumLength(30);
        RuleFor(x => x.Category).MaximumLength(30);
        RuleFor(x => x.BloodGroup).MaximumLength(20);
        RuleFor(x => x.HeightCm).MaximumLength(50);
        RuleFor(x => x.WeightKg).MaximumLength(50);
        RuleFor(x => x.NumberOfChildren).MaximumLength(50);

        // The individual's mobile number is mandatory and unique.
        RuleFor(x => x.MobileNumber).NotEmpty().WithMessage("{PropertyName} is required.");
        RuleFor(x => x.MobileNumber).MustBeIndianMobile();
        RuleFor(x => x.NextOfKinMobileNumber).MaximumLength(15);

        RuleFor(x => x.EarnedLeaveDue).MaximumLength(100);
        RuleFor(x => x.HalfPayLeaveDue).MaximumLength(100);
        RuleFor(x => x.PaternityLeaveAvailed).MaximumLength(100);
        RuleFor(x => x.MaternityLeaveAvailed).MaximumLength(100);
        RuleFor(x => x.ChildCareLeaveAvailed).MaximumLength(100);

        RuleFor(x => x.PermanentVillage).MaximumLength(150);
        RuleFor(x => x.PermanentPostOffice).MaximumLength(150);
        RuleFor(x => x.PermanentPoliceStation).MaximumLength(150);
        RuleFor(x => x.PermanentDistrict).MaximumLength(100);
        RuleFor(x => x.PermanentState).MaximumLength(100);
        RuleFor(x => x.PermanentPinCode).MaximumLength(10);

        // Ciphertext columns: length only (never validate or echo plaintext here).
        RuleFor(x => x.AadhaarNumberEncrypted).MaximumLength(512);
        RuleFor(x => x.PanNumberEncrypted).MaximumLength(512);
        RuleFor(x => x.PranNumberEncrypted).MaximumLength(512);
        RuleFor(x => x.SalaryAccountNumberEncrypted).MaximumLength(512);

        RuleFor(x => x.MobileAppActivationStatus).IsInEnum();

        // A birth date in the future is the only date rule. The order of the other dates is deliberately NOT checked: the source workbook contains legitimate records whose dates are out of the expected order.
        RuleFor(x => x.DateOfBirth).Must(d => d!.Value <= DateOnly.FromDateTime(IndianStandardTime.Now))
            .When(x => x.DateOfBirth.HasValue).WithMessage("Date of birth cannot be in the future.");
    }
}
/// <summary>Validates the plaintext sensitive identifiers before they are encrypted. All four are optional.</summary>
public class PisSensitiveIdentifiersValidator : AbstractValidator<PisSensitiveIdentifiersInput>
{
    public PisSensitiveIdentifiersValidator()
    {
        RuleFor(x => x.AadhaarNumber).MustBeAadhaar();
        RuleFor(x => x.PanNumber).MustBePan();
        RuleFor(x => x.PranNumber).MustBePran();
        RuleFor(x => x.SalaryAccountNumber).MustBeSalaryAccountNumber();
    }
}
