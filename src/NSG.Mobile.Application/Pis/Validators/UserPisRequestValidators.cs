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
        RuleFor(x => x.SideSteppedDetails).MaximumLength(255);
        RuleFor(x => x.FatherName).MaximumLength(200);
        RuleFor(x => x.MotherName).MaximumLength(200);
        RuleFor(x => x.SpouseName).MaximumLength(200);

        RuleFor(x => x.MobileNumber).MustBeIndianMobile();
        RuleFor(x => x.NextOfKinMobileNumber).MaximumLength(15);   // free text: may carry a country code or spaces

        // Lookup values and the numeric-looking columns are plain text (stored exactly as sent): only the column lengths are checked.
        RuleFor(x => x.NsgRank).MaximumLength(100);
        RuleFor(x => x.ParentDepartmentRank).MaximumLength(100);
        RuleFor(x => x.NsgUnit).MaximumLength(100);
        RuleFor(x => x.Education).MaximumLength(100);
        RuleFor(x => x.Religion).MaximumLength(100);
        RuleFor(x => x.PermanentState).MaximumLength(100);
        RuleFor(x => x.HeightCm).MaximumLength(50);
        RuleFor(x => x.WeightKg).MaximumLength(50);
        RuleFor(x => x.NumberOfChildren).MaximumLength(50);
        RuleFor(x => x.EarnedLeaveDue).MaximumLength(100);
        RuleFor(x => x.HalfPayLeaveDue).MaximumLength(100);        RuleFor(x => x.PaternityLeaveAvailed).MaximumLength(100);
        RuleFor(x => x.MaternityLeaveAvailed).MaximumLength(100);
        RuleFor(x => x.ChildCareLeaveAvailed).MaximumLength(100);

        RuleFor(x => x.PermanentVillage).MaximumLength(150);
        RuleFor(x => x.PermanentPostOffice).MaximumLength(150);
        RuleFor(x => x.PermanentPoliceStation).MaximumLength(150);
        RuleFor(x => x.PermanentDistrict).MaximumLength(100);
        RuleFor(x => x.PermanentPinCode).MaximumLength(10);

        // On update a mask-shaped value is let through here only so the service can compare it with the mask of the
        // stored value (it rejects any mask that does not match). Create never accepts masks.
        RuleFor(x => x.AadhaarNumber).MustBeAadhaar().When(x => !(allowMaskedSensitive && PisMasking.LooksMasked(x.AadhaarNumber?.Trim())));
        RuleFor(x => x.PanNumber).MustBePan().When(x => !(allowMaskedSensitive && PisMasking.LooksMasked(PisNormalizer.Pan(x.PanNumber))));
        RuleFor(x => x.PranNumber).MustBePran().When(x => !(allowMaskedSensitive && PisMasking.LooksMasked(x.PranNumber?.Trim())));
        RuleFor(x => x.SalaryAccountNumber).MustBeSalaryAccountNumber().When(x => !(allowMaskedSensitive && PisMasking.LooksMasked(PisNormalizer.SalaryAccountNumber(x.SalaryAccountNumber))));

        // Gender, category and blood group are free text (no predefined list); only the column lengths are checked.
        RuleFor(x => x.Gender).MaximumLength(30);
        RuleFor(x => x.Category).MaximumLength(30);
        RuleFor(x => x.BloodGroup).MaximumLength(20);
        // A birth date in the future is the only date rule. The order of the other dates is deliberately NOT checked: the source workbook contains legitimate records whose dates are out of the expected order.
        RuleFor(x => x.DateOfBirth).Must(d => d!.Value <= DateOnly.FromDateTime(IndianStandardTime.Now))
            .When(x => x.DateOfBirth.HasValue).WithMessage("Date of birth cannot be in the future.");
    }
}

public class CreateUserPisRequestValidator : UserPisWriteDtoValidator<CreateUserPisRequestDto>
{
    public CreateUserPisRequestValidator() : base(allowMaskedSensitive: false)
    {
        RuleFor(x => x.NsgNumber).MustBeRequiredTrimmedIdentifier(30);

        // The individual's mobile number is mandatory and unique (users_pis.mobile_number is NOT NULL). Update keeps the stored
        // number when it is omitted, like gender and the sensitive identifiers.
        RuleFor(x => x.MobileNumber).NotEmpty().WithMessage("{PropertyName} is required.");

        // Gender (any text) is mandatory for new records only; update keeps the stored value when it is omitted.
        RuleFor(x => x.Gender).NotEmpty().WithMessage("Gender is required.");
    }
}

public class UpdateUserPisRequestValidator : UserPisWriteDtoValidator<UpdateUserPisRequestDto>
{
    public UpdateUserPisRequestValidator() : base(allowMaskedSensitive: true)
    {
    }
}
