using FluentValidation;
using NSG.Mobile.Application.Ranks.DTOs;

namespace NSG.Mobile.Application.Ranks.Validators;

public class CreateRankRequestValidator : AbstractValidator<CreateRankRequestDto>
{
    public CreateRankRequestValidator()
    {
        RuleFor(x => x.SrNo).GreaterThan(0).WithMessage("SrNo must be greater than 0.");
        RuleFor(x => x.RankName)
            .NotEmpty().WithMessage("Rank name is required.")
            .MaximumLength(100).WithMessage("Rank name must not exceed 100 characters.");
        RuleFor(x => x.ShortCode)
            .MaximumLength(20).WithMessage("Short code must not exceed 20 characters.");
    }
}

public class UpdateRankRequestValidator : AbstractValidator<UpdateRankRequestDto>
{
    public UpdateRankRequestValidator()
    {
        RuleFor(x => x.SrNo).GreaterThan(0).WithMessage("SrNo must be greater than 0.");
        RuleFor(x => x.RankName)
            .NotEmpty().WithMessage("Rank name is required.")
            .MaximumLength(100).WithMessage("Rank name must not exceed 100 characters.");
        RuleFor(x => x.ShortCode)
            .MaximumLength(20).WithMessage("Short code must not exceed 20 characters.");
    }
}
