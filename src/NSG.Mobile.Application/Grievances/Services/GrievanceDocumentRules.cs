using System.Text;
using System.Text.Json;
using NSG.Mobile.Application.Common;

namespace NSG.Mobile.Application.Grievances.Services;

/// <summary>A file that passed the checks that need no content: type, extension, content type and size. The magic bytes are checked next.</summary>
public sealed record ValidatedDocument(string Type, string ContentType, string SafeName);

/// <summary>One entry of <c>grievance.document_urls</c>. <see cref="Key"/> is an object key in the private bucket, never a URL.</summary>
public sealed record GrievanceDocumentRecord(string Key, string Name, string ContentType, long SizeBytes, DateTime UploadedAtIst);

/// <summary>Upload validation (pdf and jpg only) and file-name sanitising. Pure functions; no I/O except reading the first bytes of the stream.</summary>
public static class GrievanceDocumentRules
{
    private static readonly byte[] PdfMagic = { 0x25, 0x50, 0x44, 0x46 };   // %PDF
    private static readonly byte[] JpegMagic = { 0xFF, 0xD8, 0xFF };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Checks extension, declared content type, size and the category's allowed types, in that order, and returns the sanitised name to store.
    /// The extension and the content type must agree (.pdf with application/pdf; .jpg/.jpeg with image/jpeg).
    /// </summary>
    public static Result<ValidatedDocument> Validate(string? fileName, string? contentType, long length, EffectiveUploadLimits limits)
    {
        if (!limits.AllowDocuments)
            return Failure("This grievance category does not accept documents.");
        if (length <= 0)
            return Failure("The uploaded file is empty.");
        if (length > limits.MaxFileSizeBytes)
            return Failure($"The file is too large. The maximum size is {limits.MaxFileSizeBytes / (1024 * 1024)} MB.");

        string extension = Path.GetExtension(LastSegment(fileName)).ToLowerInvariant();
        string? type = extension switch
        {
            ".pdf" => "pdf",
            ".jpg" or ".jpeg" => "jpg",
            _ => null
        };
        if (type == null || !limits.AllowedTypes.Contains(type))
            return Failure($"Only {string.Join(" and ", limits.AllowedTypes.Select(t => t.ToUpperInvariant()))} files are accepted for this category.");

        string declared = (contentType ?? string.Empty).Split(';')[0].Trim().ToLowerInvariant();
        bool contentTypeMatches = type == "pdf"
            ? declared == "application/pdf"
            : declared is "image/jpeg" or "image/jpg";
        if (!contentTypeMatches)
            return Failure("The file's content type does not match its extension.");

        string canonicalContentType = type == "pdf" ? "application/pdf" : "image/jpeg";
        return Result.Success(new ValidatedDocument(type, canonicalContentType, SanitizeFileName(fileName, type == "pdf" ? ".pdf" : ".jpg")));
    }

    /// <summary>True when the stream starts with the signature of the claimed type (%PDF, or FF D8 FF). Restores the stream position.</summary>
    public static async Task<bool> HasExpectedSignatureAsync(Stream stream, string type, CancellationToken cancellationToken)
    {
        byte[] expected = type == "pdf" ? PdfMagic : JpegMagic;
        long start = stream.Position;
        byte[] head = new byte[expected.Length];
        int read = 0;
        while (read < head.Length)
        {
            int n = await stream.ReadAsync(head.AsMemory(read), cancellationToken);
            if (n == 0) break;
            read += n;
        }
        stream.Position = start;
        return read == expected.Length && head.AsSpan().SequenceEqual(expected);
    }

    /// <summary>
    /// A name that is safe to store and to put in a Content-Disposition header: no path, no control characters, quotes, semicolons or
    /// separators, bounded length, and the validated extension.
    /// </summary>
    public static string SanitizeFileName(string? original, string extension)
    {
        string name = LastSegment(original).Normalize(NormalizationForm.FormC);
        string stem = Path.GetFileNameWithoutExtension(name);

        StringBuilder builder = new(stem.Length);
        foreach (char c in stem)
        {
            bool keep = char.IsLetterOrDigit(c) || IsCombiningMark(c) || c is ' ' or '-' or '_' or '.' or '(' or ')';
            builder.Append(keep ? c : '_');
        }

        string cleaned = builder.ToString().Trim(' ', '.', '_');
        if (cleaned.Length > GrievanceLimits.FileNameStemMaxLength) cleaned = cleaned[..GrievanceLimits.FileNameStemMaxLength].TrimEnd(' ', '.', '_');
        if (cleaned.Length == 0) cleaned = "document";
        return cleaned + extension;
    }

    // ── Stored document list (document_urls) ──

    public static List<GrievanceDocumentRecord> ParseDocuments(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<GrievanceDocumentRecord>();
        try
        {
            return JsonSerializer.Deserialize<List<GrievanceDocumentRecord>>(json, JsonOptions) ?? new List<GrievanceDocumentRecord>();
        }
        catch (JsonException)
        {
            return new List<GrievanceDocumentRecord>();
        }
    }

    public static string SerializeDocuments(IReadOnlyList<GrievanceDocumentRecord> documents) => JsonSerializer.Serialize(documents, JsonOptions);

    // Vowel signs and viram marks (Devanagari and other Indic scripts) are combining marks, not letters; dropping them would mangle ordinary Hindi names.
    private static bool IsCombiningMark(char c)
        => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) is System.Globalization.UnicodeCategory.NonSpacingMark or System.Globalization.UnicodeCategory.SpacingCombiningMark;

    private static string LastSegment(string? path)
    {
        string normalized = (path ?? string.Empty).Replace('\\', '/');
        return normalized[(normalized.LastIndexOf('/') + 1)..];
    }

    private static Result<ValidatedDocument> Failure(string message) => Result.Failure<ValidatedDocument>(ApiErrorCodes.ValidationFailed, message);

}
