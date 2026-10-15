using FluentValidation;
using NSG.Mobile.Application.Lookups.DTOs;

namespace NSG.Mobile.Application.Lookups.Validators;

public class CreateLookupRequestValidator : AbstractValidator<CreateLookupRequestDto>
{
    public CreateLookupRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(100).WithMessage("Name must not exceed 100 characters.");
    }
}

public class UpdateLookupRequestValidator : AbstractValidator<UpdateLookupRequestDto>
{
    public UpdateLookupRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(100).WithMessage("Name must not exceed 100 characters.");
    }
}
