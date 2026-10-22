namespace NSG.Mobile.Application.DgMessages.DTOs;

/// <summary>A DG message as the apps see it. The image is fetched with <c>GET /api/v1/dg-messages/{id}/image</c> (authorized, streamed); no URL or key is exposed.</summary>
public class DgMessageDto
{
    public int Id { get; set; }
    public string DgName { get; set; } = string.Empty;
    public string Designation { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool HasImage { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public long? UpdatedBy { get; set; }
}

/// <summary>Text fields of a create or update. <see cref="IsActive"/> defaults to true on create and is required on update (an update replaces every field).</summary>
public class SaveDgMessageRequestDto
{
    public string DgName { get; set; } = string.Empty;
    public string Designation { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool? IsActive { get; set; }
}

/// <summary>An uploaded image as the Application layer sees it (the controller adapts <c>IFormFile</c> to this).</summary>
public sealed record DgImageUpload(Stream Content, string FileName, string ContentType, long Length);
