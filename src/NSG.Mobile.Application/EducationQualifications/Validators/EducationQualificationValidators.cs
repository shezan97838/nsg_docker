using FluentValidation;
using NSG.Mobile.Application.EducationQualifications.DTOs;

namespace NSG.Mobile.Application.EducationQualifications.Validators;

public class CreateEducationQualificationRequestValidator : AbstractValidator<CreateEducationQualificationRequestDto>
{
    public CreateEducationQualificationRequestValidator()
    {
        RuleFor(x => x.CourseName).NotEmpty().WithMessage("Course name is required.")
            .MaximumLength(100).WithMessage("Course name must not exceed 100 characters.");
    }
}

public class UpdateEducationQualificationRequestValidator : AbstractValidator<UpdateEducationQualificationRequestDto>
{
    public UpdateEducationQualificationRequestValidator()
    {
        RuleFor(x => x.CourseName).NotEmpty().WithMessage("Course name is required.")
            .MaximumLength(100).WithMessage("Course name must not exceed 100 characters.");
        RuleFor(x => x.IsActive).NotNull().WithMessage("IsActive is required.");
    }
}
