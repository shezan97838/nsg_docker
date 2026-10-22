using FluentValidation;
using NSG.Mobile.Application.DgMessages.DTOs;

namespace NSG.Mobile.Application.DgMessages.Validators;

public class SaveDgMessageRequestValidator : AbstractValidator<SaveDgMessageRequestDto>
{
    public SaveDgMessageRequestValidator()
    {
        RuleFor(x => x.DgName).NotEmpty().WithMessage("DG name is required.")
            .Must(v => v == null || v.Trim().Length <= DgMessageLimits.NameMaxLength)
            .WithMessage($"DG name must not exceed {DgMessageLimits.NameMaxLength} characters.");
        RuleFor(x => x.Designation).NotEmpty().WithMessage("Designation is required.")
            .Must(v => v == null || v.Trim().Length <= DgMessageLimits.DesignationMaxLength)
            .WithMessage($"Designation must not exceed {DgMessageLimits.DesignationMaxLength} characters.");
        RuleFor(x => x.Message).NotEmpty().WithMessage("Message is required.")
            .Must(v => v == null || v.Trim().Length <= DgMessageLimits.MessageMaxLength)
            .WithMessage($"Message must not exceed {DgMessageLimits.MessageMaxLength} characters.");
    }
}
