using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Grievances.DTOs;

namespace NSG.Mobile.Application.Grievances.Services;

/// <summary>A file that passed every check and is ready to be stored. <see cref="Buffer"/> is the in-memory copy made for a non-seekable stream; the owner disposes it.</summary>
internal sealed record PreparedDocument(ValidatedDocument Validated, Stream Content, long Size, MemoryStream? Buffer);

/// <summary>
/// The checks shared by every grievance upload (attachments at submission, the resolution file): extension, declared content type, size and the
/// file signature, all BEFORE anything is sent to storage.
/// </summary>
internal static class GrievanceUploadPreparer
{
    public static async Task<Result<PreparedDocument>> PrepareAsync(GrievanceUploadFile file, EffectiveUploadLimits limits, CancellationToken cancellationToken)
    {
        Result<ValidatedDocument> validated = GrievanceDocumentRules.Validate(file.FileName, file.ContentType, file.Length, limits);
        if (!validated.IsSuccess)
            return Result.Failure<PreparedDocument>(validated.ErrorCode!, validated.ErrorMessage!);

        // The signature check needs a seekable stream; a bounded in-memory copy covers the rare stream that is not.
        Stream content = file.Content;
        MemoryStream? buffer = null;
        try
        {
            if (!content.CanSeek)
            {
                buffer = new MemoryStream();
                await content.CopyToAsync(buffer, cancellationToken);
                buffer.Position = 0;
                content = buffer;
            }

            long size = content.Length - content.Position;
            if (size > limits.MaxFileSizeBytes)
            {
                buffer?.Dispose();
                return Result.Failure<PreparedDocument>(ApiErrorCodes.ValidationFailed, "The file is larger than allowed.");
            }
            if (!await GrievanceDocumentRules.HasExpectedSignatureAsync(content, validated.Data!.Type, cancellationToken))
            {
                buffer?.Dispose();
                return Result.Failure<PreparedDocument>(ApiErrorCodes.ValidationFailed, "The file's content does not match its type.");
            }

            return Result.Success(new PreparedDocument(validated.Data, content, size, buffer));
        }
        catch
        {
            buffer?.Dispose();
            throw;
        }
    }
}
