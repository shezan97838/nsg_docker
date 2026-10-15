using FluentValidation;
using NSG.Mobile.Application.Trades.DTOs;

namespace NSG.Mobile.Application.Trades.Validators;

public class CreateNsgTradeRequestValidator : AbstractValidator<CreateNsgTradeRequestDto>
{
    public CreateNsgTradeRequestValidator()
    {
        RuleFor(x => x.NsgTradeName).NotEmpty().WithMessage("Trade name is required.")
            .MaximumLength(100).WithMessage("Trade name must not exceed 100 characters.");
    }
}

public class UpdateNsgTradeRequestValidator : AbstractValidator<UpdateNsgTradeRequestDto>
{
    public UpdateNsgTradeRequestValidator()
    {
        RuleFor(x => x.NsgTradeName).NotEmpty().WithMessage("Trade name is required.")
            .MaximumLength(100).WithMessage("Trade name must not exceed 100 characters.");
        RuleFor(x => x.IsActive).NotNull().WithMessage("IsActive is required.");
    }
}

public class CreatePdTradeRequestValidator : AbstractValidator<CreatePdTradeRequestDto>
{
    public CreatePdTradeRequestValidator()
    {
        RuleFor(x => x.PdTradeName).NotEmpty().WithMessage("Trade name is required.")
            .MaximumLength(100).WithMessage("Trade name must not exceed 100 characters.");
    }
}

public class UpdatePdTradeRequestValidator : AbstractValidator<UpdatePdTradeRequestDto>
{
    public UpdatePdTradeRequestValidator()
    {
        RuleFor(x => x.PdTradeName).NotEmpty().WithMessage("Trade name is required.")
            .MaximumLength(100).WithMessage("Trade name must not exceed 100 characters.");
        RuleFor(x => x.IsActive).NotNull().WithMessage("IsActive is required.");
    }
}
