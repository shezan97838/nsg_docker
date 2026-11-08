using FluentValidation;
using NSG.Mobile.Application.Geography.DTOs;

namespace NSG.Mobile.Application.Geography.Validators;

internal static class GeographyRules
{
    public const string CodePattern = @"^[A-Za-z0-9._-]+$";

    /// <summary>Official LGD state/UT code: exactly two digits, leading zeros significant.</summary>
    public const string StateCodePattern = @"^[0-9]{2}$";

    public static IRuleBuilderOptions<T, string> ValidName<T>(this IRuleBuilder<T, string> rule, int max)
        => rule.NotEmpty().WithMessage("Name is required.")
               .MaximumLength(max).WithMessage($"Name must not exceed {max} characters.");
}

public class CreateStateRequestValidator : AbstractValidator<CreateStateRequestDto>
{
    public CreateStateRequestValidator()
    {
        RuleFor(x => x.Name).ValidName(100);
        RuleFor(x => x.Code).Matches(GeographyRules.StateCodePattern)
            .When(x => !string.IsNullOrWhiteSpace(x.Code)).WithMessage("Code must be the two-digit official state/UT code (e.g. '07').");
        RuleFor(x => x.AdministrativeType).IsInEnum().WithMessage("AdministrativeType must be State or UnionTerritory.");
    }
}

public class UpdateStateRequestValidator : AbstractValidator<UpdateStateRequestDto>
{
    public UpdateStateRequestValidator()
    {
        RuleFor(x => x.Name).ValidName(100);
        RuleFor(x => x.Code).Matches(GeographyRules.StateCodePattern)
            .When(x => !string.IsNullOrWhiteSpace(x.Code)).WithMessage("Code must be the two-digit official state/UT code (e.g. '07').");
        RuleFor(x => x.AdministrativeType).IsInEnum().WithMessage("AdministrativeType must be State or UnionTerritory.");
    }
}
