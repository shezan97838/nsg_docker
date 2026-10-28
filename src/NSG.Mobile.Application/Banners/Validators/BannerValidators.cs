using FluentValidation;
using NSG.Mobile.Application.Banners.DTOs;

namespace NSG.Mobile.Application.Banners.Validators;

public class SaveBannerRequestValidator : AbstractValidator<SaveBannerRequestDto>
{
    public SaveBannerRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().WithMessage("Title is required.")
            .Must(v => v == null || v.Trim().Length <= BannerLimits.TitleMaxLength)
            .WithMessage($"Title must not exceed {BannerLimits.TitleMaxLength} characters.");
        RuleFor(x => x.Description).NotEmpty().WithMessage("Description is required.")
            .Must(v => v == null || v.Trim().Length <= BannerLimits.DescriptionMaxLength)
            .WithMessage($"Description must not exceed {BannerLimits.DescriptionMaxLength} characters.");
        RuleFor(x => x.Category).NotEmpty().WithMessage("Category is required.")
            .Must(v => v == null || v.Trim().Length <= BannerLimits.CategoryMaxLength)
            .WithMessage($"Category must not exceed {BannerLimits.CategoryMaxLength} characters.");

        RuleFor(x => x.ImageAltText)
            .Must(v => v == null || v.Trim().Length <= BannerLimits.ImageAltTextMaxLength)
            .WithMessage($"Image alt text must not exceed {BannerLimits.ImageAltTextMaxLength} characters.");

        RuleFor(x => x.NsgUnitId).GreaterThan(0).When(x => x.NsgUnitId.HasValue).WithMessage("NSG unit id must be a positive number.");
        RuleFor(x => x.Priority).InclusiveBetween(0, BannerLimits.PriorityMax).When(x => x.Priority.HasValue)
            .WithMessage($"Priority must be between 0 and {BannerLimits.PriorityMax}.");
        RuleFor(x => x.IsActive).NotNull().WithMessage("isActive is required.");
    }
}
