using FluentValidation;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.MobileAuth.DTOs;
using NSG.Mobile.Domain.Common;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Application.MobileAuth.Validators;

internal static class MobileAuthRules
{
    public static IRuleBuilderOptions<T, string> NsgNumber<T>(this IRuleBuilder<T, string> rule)
        => rule.NotEmpty().WithMessage("NSG number is required.")
            .MaximumLength(30).WithMessage("NSG number must not exceed 30 characters.");

    public static IRuleBuilderOptions<T, string> PersonalMobile<T>(this IRuleBuilder<T, string> rule)
        => rule.NotEmpty().WithMessage("Mobile number is required.")
            .MaximumLength(20).WithMessage("Mobile number is too long.")
            .Must(m => System.Text.RegularExpressions.Regex.IsMatch(MobileAuthInput.Mobile(m), DomainConstants.PisFormats.MobileNumberPattern))
            .WithMessage("Mobile number must be a valid 10-digit Indian mobile number.");

    public static IRuleBuilderOptions<T, DateOnly> BirthDate<T>(this IRuleBuilder<T, DateOnly> rule)
        => rule.Must(d => d != default && d <= DateOnly.FromDateTime(IndianStandardTime.Now) && d.Year >= 1900)
            .WithMessage("Date of birth is not valid.");

    public static IRuleBuilderOptions<T, string> NewMpin<T>(this IRuleBuilder<T, string> rule)
        => rule.NotEmpty().WithMessage("MPIN is required.")
            .Matches(MpinPolicy.Pattern).WithMessage($"MPIN must be {MpinPolicy.MinLength} to {MpinPolicy.MaxLength} digits.")
            .Must(m => !MpinPolicy.IsTrivial(m)).WithMessage("MPIN is too easy to guess. Avoid repeated digits and sequences such as 1234.");

    public static IRuleBuilderOptions<T, string> GrantToken<T>(this IRuleBuilder<T, string> rule)
        => rule.NotEmpty().WithMessage("Grant token is required.")
            .MaximumLength(200).WithMessage("Grant token is not valid.");
}

/// <summary>Input canonical forms shared by validators and the service.</summary>
public static class MobileAuthInput
{
    public static string Nsg(string value) => (value ?? string.Empty).Trim();

    /// <summary>Ten-digit form: drops separators and an optional +91 / 91 / 0 prefix.</summary>
    public static string Mobile(string value)
    {
        string normalized = MobileNumberNormalizer.Normalize(value ?? string.Empty);
        return normalized.StartsWith(DomainConstants.MobileNumber.IndiaCountryCode, StringComparison.Ordinal)
            ? normalized[DomainConstants.MobileNumber.IndiaCountryCode.Length..]
            : normalized;
    }
}

public class MobileLoginCheckRequestValidator : AbstractValidator<MobileLoginCheckRequestDto>
{
    public MobileLoginCheckRequestValidator()
    {
        RuleFor(x => x.NsgNumber).NsgNumber();
        RuleFor(x => x.MobileNumber).PersonalMobile();
        RuleFor(x => x.DateOfBirth).BirthDate();
    }
}

public class MobileLoginRequestValidator : AbstractValidator<MobileLoginRequestDto>
{
    public MobileLoginRequestValidator()
    {
        RuleFor(x => x.NsgNumber).NsgNumber();
        RuleFor(x => x.MobileNumber).PersonalMobile();
        RuleFor(x => x.DateOfBirth).BirthDate();
        // Format only: a wrong-format MPIN is rejected as invalid input, never counted as a failed attempt.
        RuleFor(x => x.Mpin).NotEmpty().WithMessage("MPIN is required.").Matches(MpinPolicy.Pattern).WithMessage("MPIN must be 4 to 6 digits.");
    }
}

public class MobileOtpSendRequestValidator : AbstractValidator<MobileOtpSendRequestDto>
{
    public MobileOtpSendRequestValidator()
    {
        RuleFor(x => x.NsgNumber).NsgNumber();
        RuleFor(x => x.MobileNumber).PersonalMobile();
        RuleFor(x => x.DateOfBirth).BirthDate();
        RuleFor(x => x.Flow).IsInEnum().WithMessage("Flow is not valid.");
    }
}

public class MobileOtpVerifyRequestValidator : AbstractValidator<MobileOtpVerifyRequestDto>
{
    public MobileOtpVerifyRequestValidator()
    {
        RuleFor(x => x.NsgNumber).NsgNumber();
        RuleFor(x => x.MobileNumber).PersonalMobile();
        RuleFor(x => x.DateOfBirth).BirthDate();
        RuleFor(x => x.Flow).IsInEnum().WithMessage("Flow is not valid.");
        RuleFor(x => x.OtpReferenceId).NotEmpty().WithMessage("OTP reference is required.");
        RuleFor(x => x.OtpCode).NotEmpty().WithMessage("OTP is required.").Matches(@"^\d{6}$").WithMessage("OTP must be 6 digits.");
    }
}

public class RegistrationSubmitRequestValidator : AbstractValidator<RegistrationSubmitRequestDto>
{
    public RegistrationSubmitRequestValidator() => RuleFor(x => x.GrantToken).GrantToken();
}

public class MpinSetupRequestValidator : AbstractValidator<MpinSetupRequestDto>
{
    public MpinSetupRequestValidator()
    {
        RuleFor(x => x.GrantToken).GrantToken();
        RuleFor(x => x.Mpin).NewMpin();
        RuleFor(x => x.ConfirmMpin).Equal(x => x.Mpin).WithMessage("MPIN and confirmation do not match.");
    }
}

public class MpinChangeRequestValidator : AbstractValidator<MpinChangeRequestDto>
{
    public MpinChangeRequestValidator()
    {
        RuleFor(x => x.CurrentMpin).NotEmpty().WithMessage("Current MPIN is required.").Matches(MpinPolicy.Pattern).WithMessage("Current MPIN must be 4 to 6 digits.");
        RuleFor(x => x.NewMpin).NewMpin();
        RuleFor(x => x.ConfirmNewMpin).Equal(x => x.NewMpin).WithMessage("New MPIN and confirmation do not match.");
        RuleFor(x => x.NewMpin).NotEqual(x => x.CurrentMpin).WithMessage("The new MPIN must be different from the current MPIN.");
    }
}

public class MpinResetRequestValidator : AbstractValidator<MpinResetRequestDto>
{
    public MpinResetRequestValidator()
    {
        RuleFor(x => x.GrantToken).GrantToken();
        RuleFor(x => x.NewMpin).NewMpin();
        RuleFor(x => x.ConfirmNewMpin).Equal(x => x.NewMpin).WithMessage("New MPIN and confirmation do not match.");
    }
}

public class MobileApprovalDecisionRequestValidator : AbstractValidator<MobileApprovalDecisionRequestDto>
{
    public MobileApprovalDecisionRequestValidator()
    {
        RuleFor(x => x.Decision)
            .Must(d => d is MobileAppActivationStatus.Approved or MobileAppActivationStatus.Rejected)
            .WithMessage("Decision must be Approved or Rejected.");
        RuleFor(x => x.Remarks).MaximumLength(500).WithMessage("Remarks must not exceed 500 characters.");
    }
}

public class MobileRefreshRequestValidator : AbstractValidator<MobileRefreshRequestDto>
{
    public MobileRefreshRequestValidator() =>
        RuleFor(x => x.RefreshToken).NotEmpty().WithMessage("Refresh token is required.").MaximumLength(200).WithMessage("Refresh token is not valid.");
}

public class MobileLogoutRequestValidator : AbstractValidator<MobileLogoutRequestDto>
{
    public MobileLogoutRequestValidator() =>
        RuleFor(x => x.RefreshToken).NotEmpty().WithMessage("Refresh token is required.").MaximumLength(200).WithMessage("Refresh token is not valid.");
}
