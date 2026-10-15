namespace NSG.Mobile.Application.EducationQualifications.DTOs;

public class EducationQualificationResponseDto
{
    public int Id { get; set; }
    public string CourseName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAtIst { get; set; }
}

public class CreateEducationQualificationRequestDto
{
    public string CourseName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

/// <summary><see cref="IsActive"/> is required so an omitted value can never silently change the state.</summary>
public class UpdateEducationQualificationRequestDto
{
    public string CourseName { get; set; } = string.Empty;
    public bool? IsActive { get; set; }
}
