using FluentValidation;
using NSG.Mobile.Application.Admin.DTOs;
using NSG.Mobile.Application.MobileAuth.Validators;
using NSG.Mobile.Domain.Common;
using NSG.Mobile.Domain.Constants;

namespace NSG.Mobile.Application.Admin.Validators;

internal static class AdminRules
{
    public static IRuleBuilderOptions<T, string> NsgNumber<T>(this IRuleBuilder<T, string> rule)
        => rule.NotEmpty().WithMessage("NSG number is required.").MaximumLength(30).WithMessage("NSG number must not exceed 30 characters.");

    public static IRuleBuilderOptions<T, string> PisMobile<T>(this IRuleBuilder<T, string> rule)
        => rule.NotEmpty().WithMessage("Mobile number is required.").MaximumLength(20).WithMessage("Mobile number is too long.")
            .Must(m => System.Text.RegularExpressions.Regex.IsMatch(MobileAuthInput.Mobile(m), DomainConstants.PisFormats.MobileNumberPattern))
            .WithMessage("Mobile number must be a valid 10-digit Indian mobile number.");

    public static IRuleBuilderOptions<T, DateOnly> BirthDate<T>(this IRuleBuilder<T, DateOnly> rule)
        => rule.Must(d => d != default && d <= DateOnly.FromDateTime(IndianStandardTime.Now) && d.Year >= 1900).WithMessage("Date of birth is not valid.");

    public static IRuleBuilderOptions<T, string> OtpCode<T>(this IRuleBuilder<T, string> rule)
        => rule.NotEmpty().WithMessage("OTP is required.").Matches("^[0-9]{4,8}$").WithMessage("OTP must be 4 to 8 digits.");

    /// <summary>Sign-in checks the shape only: a malformed password is invalid input, not a counted attempt. Complexity is enforced when one is chosen.</summary>
    public static IRuleBuilderOptions<T, string> EnteredPassword<T>(this IRuleBuilder<T, string> rule)
        => rule.NotEmpty().WithMessage("Password is required.").MaximumLength(AdminPasswordPolicy.MaxLength).WithMessage("Password is too long.");
}

public class CreateAdminAccountRequestValidator : AbstractValidator<CreateAdminAccountRequestDto>
{
    public CreateAdminAccountRequestValidator()
    {
        RuleFor(x => x.NsgNumber).NsgNumber();
        RuleFor(x => x.MobileNumber).PisMobile();
        RuleFor(x => x.DateOfBirth).BirthDate();
        RuleFor(x => x.RoleCode).NotEmpty().WithMessage("Role is required.").MaximumLength(50).WithMessage("Role is not valid.");
    }
}

public class AdminActivationOtpRequestValidator : AbstractValidator<AdminActivationOtpRequestDto>
{
    public AdminActivationOtpRequestValidator()
    {
        RuleFor(x => x.NsgNumber).NsgNumber();
        RuleFor(x => x.MobileNumber).PisMobile();
        RuleFor(x => x.DateOfBirth).BirthDate();
    }
}

public class AdminActivationCompleteRequestValidator : AbstractValidator<AdminActivationCompleteRequestDto>
{
    public AdminActivationCompleteRequestValidator()
    {
        RuleFor(x => x.NsgNumber).NsgNumber();
        RuleFor(x => x.MobileNumber).PisMobile();
        RuleFor(x => x.DateOfBirth).BirthDate();
        RuleFor(x => x.OtpReferenceId).NotEmpty().WithMessage("OTP reference is required.");
        RuleFor(x => x.OtpCode).OtpCode();
        RuleFor(x => x.NewPassword).Custom((password, context) =>
        {
            string? violation = AdminPasswordPolicy.Violation(password, context.InstanceToValidate.NsgNumber);
            if (violation != null) context.AddFailure(violation);
        });
        RuleFor(x => x.ConfirmPassword).Equal(x => x.NewPassword).WithMessage("Passwords do not match.");
    }
}

public class AdminLoginRequestValidator : AbstractValidator<AdminLoginRequestDto>
{
    public AdminLoginRequestValidator()
    {
        RuleFor(x => x.NsgNumber).NsgNumber();
        RuleFor(x => x.Password).EnteredPassword();
    }
}

public class AdminLoginVerifyRequestValidator : AbstractValidator<AdminLoginVerifyRequestDto>
{
    public AdminLoginVerifyRequestValidator()
    {
        RuleFor(x => x.NsgNumber).NsgNumber();
        RuleFor(x => x.Password).EnteredPassword();
        RuleFor(x => x.OtpReferenceId).NotEmpty().WithMessage("OTP reference is required.");
        RuleFor(x => x.OtpCode).OtpCode();
    }
}

public class AdminRefreshRequestValidator : AbstractValidator<AdminRefreshRequestDto>
{
    public AdminRefreshRequestValidator() => RuleFor(x => x.RefreshToken).NotEmpty().WithMessage("Refresh token is required.").MaximumLength(200).WithMessage("Refresh token is not valid.");
}

public class AdminLogoutRequestValidator : AbstractValidator<AdminLogoutRequestDto>
{
    public AdminLogoutRequestValidator() => RuleFor(x => x.RefreshToken).NotEmpty().WithMessage("Refresh token is required.").MaximumLength(200).WithMessage("Refresh token is not valid.");
}
