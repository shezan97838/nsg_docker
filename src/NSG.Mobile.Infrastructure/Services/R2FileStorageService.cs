using System.Net;
using System.Text.RegularExpressions;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Interfaces.Infrastructure;
using NSG.Mobile.Infrastructure.Options;

namespace NSG.Mobile.Infrastructure.Services;

/// <summary>
/// <see cref="IFileStorageService"/> on a private Cloudflare R2 bucket. Keys are server generated
/// (<c>{category}/{yyyy}/{MM}/{guid}{ext}</c>), the client's file name is never part of a key, and no public or presigned URL is ever produced:
/// callers stream objects through the API after their own authorization check. Failures surface as <see cref="FileStorageUnavailableException"/>
/// without endpoint, bucket or credential details.
/// </summary>
public class R2FileStorageService : IFileStorageService
{
    // category/yyyy/MM/guid.ext — the only shape this service writes, and therefore the only shape it will read or delete.
    private static readonly Regex KeyPattern = new(
        @"^[a-z0-9_-]{1,50}/[0-9]{4}/[0-9]{2}/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\.[a-z0-9]{1,10}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex CategoryPattern = new("^[a-z0-9_-]{1,50}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex ExtensionPattern = new(@"^\.[a-z0-9]{1,10}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly IAmazonS3 _s3;
    private readonly R2Options _r2;
    private readonly FileStorageOptions _limits;
    private readonly ILogger<R2FileStorageService> _logger;
    private readonly HashSet<string> _allowedExtensions;

    public R2FileStorageService(
        IAmazonS3 s3, IOptions<R2Options> r2, IOptions<FileStorageOptions> limits, ILogger<R2FileStorageService> logger)
    {
        _s3 = s3;
        _r2 = r2.Value;
        _limits = limits.Value;
        _logger = logger;
        _allowedExtensions = new HashSet<string>(_limits.AllowedExtensions, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<string> StoreAsync(Stream fileStream, string fileName, string contentType, string category, CancellationToken cancellationToken = default)
    {
        string extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!ExtensionPattern.IsMatch(extension) || !_allowedExtensions.Contains(extension))
            throw new InvalidOperationException($"File extension '{extension}' is not allowed.");
        if (!CategoryPattern.IsMatch(category))
            throw new InvalidOperationException("Storage category is not valid.");
        if (fileStream.CanSeek && fileStream.Length - fileStream.Position > _limits.MaxFileSizeBytes)
            throw new InvalidOperationException($"File size exceeds maximum allowed size of {_limits.MaxFileSizeBytes} bytes.");

        // Server-side name only; "/" is a literal in the format because the date format's "/" is culture dependent.
        string key = string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"{category}/{NSG.Mobile.Domain.Common.IndianStandardTime.Now:yyyy'/'MM}/{Guid.NewGuid()}{extension}");

        // A known length is needed for an unsigned payload; the limit above keeps the buffered copy small.
        Stream body = fileStream;
        MemoryStream? buffer = null;
        try
        {
            if (!fileStream.CanSeek)
            {
                buffer = new MemoryStream();
                await fileStream.CopyToAsync(buffer, cancellationToken);
                if (buffer.Length > _limits.MaxFileSizeBytes)
                    throw new InvalidOperationException($"File size exceeds maximum allowed size of {_limits.MaxFileSizeBytes} bytes.");
                buffer.Position = 0;
                body = buffer;
            }

            long size = body.Length - body.Position;
            PutObjectRequest request = new()
            {
                BucketName = _r2.BucketName,
                Key = _r2.NormalizedPrefix + key,
                InputStream = body,
                ContentType = contentType,
                DisablePayloadSigning = true,   // R2 does not support streaming-signed payloads
                UseChunkEncoding = false,
                AutoCloseStream = false
            };
            request.Headers.ContentLength = size;

            await _s3.PutObjectAsync(request, cancellationToken);
            _logger.LogInformation("File stored in R2: {StorageKey}, ContentType: {ContentType}, Size: {Size}", key, contentType, size);
            return key;
        }
        catch (AmazonServiceException ex)
        {
            _logger.LogError(ex, "R2 upload failed for {StorageKey} (status {StatusCode}, code {ErrorCode})", key, (int)ex.StatusCode, ex.ErrorCode);
            throw new FileStorageUnavailableException("File storage is temporarily unavailable.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            _logger.LogError(ex, "R2 upload failed for {StorageKey}", key);
            throw new FileStorageUnavailableException("File storage is temporarily unavailable.", ex);
        }
        finally
        {
            buffer?.Dispose();
        }
    }

    public async Task<FileStorageResult?> GetAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        if (!IsValidKey(storageKey)) return null;

        try
        {
            GetObjectResponse response = await _s3.GetObjectAsync(_r2.BucketName, _r2.NormalizedPrefix + storageKey, cancellationToken);

            string contentType = string.IsNullOrWhiteSpace(response.Headers.ContentType)
                ? ContentTypeFor(storageKey)
                : response.Headers.ContentType;
            return new FileStorageResult(new ResponseOwningStream(response), contentType, Path.GetFileName(storageKey));
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (AmazonServiceException ex)
        {
            _logger.LogError(ex, "R2 download failed for {StorageKey} (status {StatusCode}, code {ErrorCode})", storageKey, (int)ex.StatusCode, ex.ErrorCode);
            throw new FileStorageUnavailableException("File storage is temporarily unavailable.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            _logger.LogError(ex, "R2 download failed for {StorageKey}", storageKey);
            throw new FileStorageUnavailableException("File storage is temporarily unavailable.", ex);
        }
    }

    /// <summary>Deletes the object. Used as a compensating action, so a failure is logged and reported as false instead of thrown. S3 deletes are idempotent: true also means "already absent".</summary>
    public async Task<bool> DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        if (!IsValidKey(storageKey)) return false;

        try
        {
            await _s3.DeleteObjectAsync(_r2.BucketName, _r2.NormalizedPrefix + storageKey, cancellationToken);
            _logger.LogInformation("File deleted from R2: {StorageKey}", storageKey);
            return true;
        }
        catch (Exception ex) when (ex is AmazonServiceException or HttpRequestException or IOException)
        {
            _logger.LogError(ex, "R2 delete failed for {StorageKey}; the object may be orphaned", storageKey);
            return false;
        }
    }

    /// <summary>True only for keys of the exact shape <see cref="StoreAsync"/> generates; anything else (traversal, absolute paths, other prefixes) is refused.</summary>
    public static bool IsValidKey(string? storageKey) => storageKey != null && KeyPattern.IsMatch(storageKey);

    private static string ContentTypeFor(string key) => Path.GetExtension(key).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        _ => "application/octet-stream"
    };

    /// <summary>Forwards reads to the object stream and releases the whole S3 response (and its HTTP connection) when the caller disposes it.</summary>
    private sealed class ResponseOwningStream : Stream
    {
        private readonly GetObjectResponse _response;
        private readonly Stream _inner;

        public ResponseOwningStream(GetObjectResponse response)
        {
            _response = response;
            _inner = response.ResponseStream;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _response.ContentLength;
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => _inner.ReadAsync(buffer, offset, count, cancellationToken);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => _inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) _response.Dispose();
            base.Dispose(disposing);
        }
    }
}
