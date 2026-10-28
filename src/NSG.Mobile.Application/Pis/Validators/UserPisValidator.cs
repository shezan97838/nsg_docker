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
        RuleFor(x => x.ServiceNumber).MustBeRequiredTrimmedIdentifier(30); // deliberately not unique

        RuleFor(x => x.FullName).MaximumLength(200);
        RuleFor(x => x.FatherName).MaximumLength(200);
        RuleFor(x => x.MotherName).MaximumLength(200);
        RuleFor(x => x.SpouseName).MaximumLength(200);

        RuleFor(x => x.MobileNumber).MustBeIndianMobile();
        RuleFor(x => x.NextOfKinMobileNumber).MustBeIndianMobile();

        // HeightCm is a byte, so fractions and values above 255 cannot be represented; only zero needs rejecting.
        RuleFor(x => x.HeightCm).GreaterThan((byte)0).When(x => x.HeightCm.HasValue)
            .WithMessage("Height must be a whole number of centimetres greater than zero.");

        RuleFor(x => x.WeightKg).GreaterThan(0m).When(x => x.WeightKg.HasValue)
            .WithMessage("Weight must be greater than zero.");
        RuleFor(x => x.WeightKg).LessThanOrEqualTo(DomainConstants.PisFormats.MaxWeightKg).When(x => x.WeightKg.HasValue)
            .WithMessage("Weight must not exceed 999.99 kg.");
        RuleFor(x => x.WeightKg).PrecisionScale(5, 2, true).When(x => x.WeightKg.HasValue)
            .WithMessage("Weight may have at most two decimal places.");

        RuleFor(x => x.NumberOfChildren).GreaterThanOrEqualTo(0).When(x => x.NumberOfChildren.HasValue);

        RuleFor(x => x.EarnedLeaveDue).GreaterThanOrEqualTo(0m).LessThanOrEqualTo(DomainConstants.PisFormats.MaxLeaveDue)
            .PrecisionScale(7, 2, true).When(x => x.EarnedLeaveDue.HasValue);
        RuleFor(x => x.HalfPayLeaveDue).GreaterThanOrEqualTo(0m).LessThanOrEqualTo(DomainConstants.PisFormats.MaxLeaveDue)
            .PrecisionScale(7, 2, true).When(x => x.HalfPayLeaveDue.HasValue);
        RuleFor(x => x.PaternityLeaveAvailed).MaximumLength(100);
        RuleFor(x => x.MaternityLeaveAvailed).MaximumLength(100);
        RuleFor(x => x.ChildCareLeaveAvailed).MaximumLength(100);

        RuleFor(x => x.PermanentVillage).MaximumLength(150);
        RuleFor(x => x.PermanentPostOffice).MaximumLength(150);
        RuleFor(x => x.PermanentPoliceStation).MaximumLength(150);
        RuleFor(x => x.PermanentDistrict).MaximumLength(100);
        RuleFor(x => x.PermanentPinCode).MustBePinCode();

        // Ciphertext columns: length only (never validate or echo plaintext here).
        RuleFor(x => x.AadhaarNumberEncrypted).MaximumLength(256);
        RuleFor(x => x.PanNumberEncrypted).MaximumLength(256);
        RuleFor(x => x.PranNumberEncrypted).MaximumLength(256);
        RuleFor(x => x.SalaryAccountNumberEncrypted).MaximumLength(256);

        // Blind indexes: lower-case hex HMAC-SHA256 only (never plaintext).
        RuleFor(x => x.PanNumberHash).Matches(DomainConstants.PisFormats.BlindIndexPattern).When(x => x.PanNumberHash != null);
        RuleFor(x => x.SalaryAccountNumberHash).Matches(DomainConstants.PisFormats.BlindIndexPattern).When(x => x.SalaryAccountNumberHash != null);

        // Undefined numeric enum values (e.g. a cast from 99) are rejected.
        RuleFor(x => x.Gender).IsInEnum().When(x => x.Gender.HasValue);
        RuleFor(x => x.Category).IsInEnum().When(x => x.Category.HasValue);
        RuleFor(x => x.BloodGroup).IsInEnum().When(x => x.BloodGroup.HasValue);
        RuleFor(x => x.MobileAppActivationStatus).IsInEnum();

        // Only logical impossibilities, mirroring the database check constraints; no NSG service rules are invented.
        RuleFor(x => x.DateOfBirth).Must(d => d!.Value <= DateOnly.FromDateTime(IndianStandardTime.Now))
            .When(x => x.DateOfBirth.HasValue).WithMessage("Date of birth cannot be in the future.");
        RuleFor(x => x.DateOfEnrollmentInGovtService).Must((x, d) => d!.Value >= x.DateOfBirth!.Value)
            .When(x => x.DateOfEnrollmentInGovtService.HasValue && x.DateOfBirth.HasValue)
            .WithMessage("Date of enrollment cannot be before the date of birth.");
        RuleFor(x => x.DateOfSuperannuation).Must((x, d) => d!.Value > x.DateOfBirth!.Value)
            .When(x => x.DateOfSuperannuation.HasValue && x.DateOfBirth.HasValue)
            .WithMessage("Date of superannuation must be after the date of birth.");
        RuleFor(x => x.DateOfRepatriation).Must((x, d) => d!.Value >= x.DateOfInductionInNsg!.Value)
            .When(x => x.DateOfRepatriation.HasValue && x.DateOfInductionInNsg.HasValue)
            .WithMessage("Date of repatriation cannot be before the date of induction.");
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
