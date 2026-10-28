using FluentValidation;
using NSG.Mobile.Application.Pis.DTOs;
using NSG.Mobile.Application.Pis.Mappings;
using NSG.Mobile.Domain.Common;
using NSG.Mobile.Domain.Constants;

namespace NSG.Mobile.Application.Pis.Validators;

/// <summary>
/// Rules shared by the create and update requests. Formats come from <see cref="PisFieldRules"/> /
/// <see cref="DomainConstants.PisFormats"/>. Messages never repeat submitted values (several are sensitive).
/// Only the service number is mandatory here (the NSG number is added for create): the source workbook marks
/// nothing else as required.
/// </summary>
public abstract class UserPisWriteDtoValidator<T> : AbstractValidator<T> where T : UserPisWriteDto
{
    protected UserPisWriteDtoValidator(bool allowMaskedSensitive)
    {
        RuleFor(x => x.ServiceNumber).MustBeRequiredTrimmedIdentifier(30);

        RuleFor(x => x.FullName).MaximumLength(200);
        RuleFor(x => x.FatherName).MaximumLength(200);
        RuleFor(x => x.MotherName).MaximumLength(200);
        RuleFor(x => x.SpouseName).MaximumLength(200);

        RuleFor(x => x.MobileNumber).MustBeIndianMobile();
        RuleFor(x => x.NextOfKinMobileNumber).MustBeIndianMobile();

        RuleFor(x => x.HeightCm).InclusiveBetween(1, 255).When(x => x.HeightCm.HasValue)
            .WithMessage("Height must be a whole number of centimetres between 1 and 255.");

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

        // On update a mask-shaped value is let through here only so the service can compare it with the mask of the
        // stored value (it rejects any mask that does not match). Create never accepts masks.
        RuleFor(x => x.AadhaarNumber).MustBeAadhaar().When(x => !(allowMaskedSensitive && PisMasking.LooksMasked(x.AadhaarNumber?.Trim())));
        RuleFor(x => x.PanNumber).MustBePan().When(x => !(allowMaskedSensitive && PisMasking.LooksMasked(PisNormalizer.Pan(x.PanNumber))));
        RuleFor(x => x.PranNumber).MustBePran().When(x => !(allowMaskedSensitive && PisMasking.LooksMasked(x.PranNumber?.Trim())));
        RuleFor(x => x.SalaryAccountNumber).MustBeSalaryAccountNumber().When(x => !(allowMaskedSensitive && PisMasking.LooksMasked(PisNormalizer.SalaryAccountNumber(x.SalaryAccountNumber))));

        // Foreign-key ids must be positive; existence is checked by the service.
        RuleFor(x => x.NsgRankId).GreaterThan(0).When(x => x.NsgRankId.HasValue).WithMessage("Reference id must be a positive number.");
        RuleFor(x => x.ParentDepartmentRankId).GreaterThan(0).When(x => x.ParentDepartmentRankId.HasValue).WithMessage("Reference id must be a positive number.");
        RuleFor(x => x.NsgTradeId).GreaterThan(0).When(x => x.NsgTradeId.HasValue).WithMessage("Reference id must be a positive number.");
        RuleFor(x => x.PdTradeId).GreaterThan(0).When(x => x.PdTradeId.HasValue).WithMessage("Reference id must be a positive number.");
        RuleFor(x => x.NsgUnitId).GreaterThan(0).When(x => x.NsgUnitId.HasValue).WithMessage("Reference id must be a positive number.");
        RuleFor(x => x.SideSteppedFromNsgUnitId).GreaterThan(0).When(x => x.SideSteppedFromNsgUnitId.HasValue).WithMessage("Reference id must be a positive number.");
        RuleFor(x => x.EducationId).GreaterThan(0).When(x => x.EducationId.HasValue).WithMessage("Reference id must be a positive number.");
        RuleFor(x => x.ReligionId).GreaterThan(0).When(x => x.ReligionId.HasValue).WithMessage("Reference id must be a positive number.");
        RuleFor(x => x.PermanentStateId).GreaterThan(0).When(x => x.PermanentStateId.HasValue).WithMessage("Reference id must be a positive number.");

        // Undefined numeric enum values (e.g. 99) are rejected.
        RuleFor(x => x.Category).IsInEnum().When(x => x.Category.HasValue);
        RuleFor(x => x.BloodGroup).IsInEnum().When(x => x.BloodGroup.HasValue);

        // Only logical impossibilities, mirroring the database check constraints.
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

public class CreateUserPisRequestValidator : UserPisWriteDtoValidator<CreateUserPisRequestDto>
{
    public CreateUserPisRequestValidator() : base(allowMaskedSensitive: false)
    {
        RuleFor(x => x.NsgNumber).MustBeRequiredTrimmedIdentifier(30);
    }
}

public class UpdateUserPisRequestValidator : UserPisWriteDtoValidator<UpdateUserPisRequestDto>
{
    public UpdateUserPisRequestValidator() : base(allowMaskedSensitive: true)
    {
    }
}
