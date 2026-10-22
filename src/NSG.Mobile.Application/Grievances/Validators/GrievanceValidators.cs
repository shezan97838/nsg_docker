using FluentValidation;
using NSG.Mobile.Application.Grievances.DTOs;

namespace NSG.Mobile.Application.Grievances.Validators;

public class CreateGrievanceRequestValidator : AbstractValidator<CreateGrievanceRequestDto>
{
    public CreateGrievanceRequestValidator()
    {
        RuleFor(x => x.CategoryId).GreaterThan(0).WithMessage("Category is required.");
        RuleFor(x => x.Subject).NotEmpty().WithMessage("Subject is required.")
            .Must(s => s == null || s.Trim().Length <= GrievanceLimits.SubjectMaxLength)
            .WithMessage($"Subject must not exceed {GrievanceLimits.SubjectMaxLength} characters.");
        RuleFor(x => x.Note).NotEmpty().WithMessage("Description is required.")
            .Must(n => n == null || n.Trim().Length >= GrievanceLimits.NoteMinLength)
            .WithMessage($"The description must be at least {GrievanceLimits.NoteMinLength} characters.")
            .Must(n => n == null || n.Trim().Length <= GrievanceLimits.NoteMaxLength)
            .WithMessage($"The description must not exceed {GrievanceLimits.NoteMaxLength} characters.");
    }
}

public class UpdateGrievanceStatusRequestValidator : AbstractValidator<UpdateGrievanceStatusRequestDto>
{
    public UpdateGrievanceStatusRequestValidator()
    {
        RuleFor(x => x.ToStatus).NotNull().WithMessage("toStatus is required.")
            .InclusiveBetween(2, 4).WithMessage("toStatus must be 2 (Under Review), 3 (Action Taken) or 4 (Closed).");
        RuleFor(x => x.Remarks)
            .Must(r => r == null || r.Trim().Length <= GrievanceLimits.RemarksMaxLength)
            .WithMessage($"Remarks must not exceed {GrievanceLimits.RemarksMaxLength} characters.");
        RuleFor(x => x.Remarks).Must(r => !string.IsNullOrWhiteSpace(r))
            .When(x => x.ToStatus == 3)
            .WithMessage("Remarks are required when action is taken.");
        RuleFor(x => x.Remarks).Must(r => !string.IsNullOrWhiteSpace(r))
            .When(x => x.ToStatus == 4)
            .WithMessage("Remarks are required when resolving a grievance.");
    }
}

public class AddGrievanceCommentRequestValidator : AbstractValidator<AddGrievanceCommentRequestDto>
{
    public AddGrievanceCommentRequestValidator()
    {
        RuleFor(x => x.Comment).NotEmpty().WithMessage("Comment is required.")
            .Must(c => c == null || c.Trim().Length <= GrievanceLimits.RemarksMaxLength)
            .WithMessage($"Comment must not exceed {GrievanceLimits.RemarksMaxLength} characters.");
    }
}

public class ForwardGrievanceRequestValidator : AbstractValidator<ForwardGrievanceRequestDto>
{
    public ForwardGrievanceRequestValidator()
    {
        RuleFor(x => x.ToAdminId).NotNull().WithMessage("toAdminId is required.")
            .GreaterThan(0).WithMessage("toAdminId must be the id of a Grievance Admin account.");
        RuleFor(x => x.Remarks)
            .Must(r => r == null || r.Trim().Length <= GrievanceLimits.RemarksMaxLength)
            .WithMessage($"Remarks must not exceed {GrievanceLimits.RemarksMaxLength} characters.");
    }
}

/// <summary>Structure of a category save. Duplicate names are decided by the unique index.</summary>
public class SaveGrievanceCategoryRequestValidator : AbstractValidator<SaveGrievanceCategoryRequestDto>
{
    public SaveGrievanceCategoryRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Category name is required.")
            .Must(n => n == null || n.Trim().Length <= GrievanceLimits.CategoryNameMaxLength)
            .WithMessage($"Category name must not exceed {GrievanceLimits.CategoryNameMaxLength} characters.");
        RuleFor(x => x.DepartmentId).GreaterThan(0).When(x => x.DepartmentId.HasValue).WithMessage("Department id must be positive.");
    }
}
