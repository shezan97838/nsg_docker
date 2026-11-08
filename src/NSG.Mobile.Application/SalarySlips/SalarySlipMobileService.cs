using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Interfaces.Infrastructure;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Application.Interfaces.Services;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Application.SalarySlips;

/// <summary>One of the signed-in person's salary slips. No account number, ciphertext or storage path.</summary>
public sealed class MobileSalarySlipDto
{
    public long SalarySlipId { get; set; }

    /// <summary>First day of the salary month (yyyy-MM-dd).</summary>
    public DateOnly SalaryMonth { get; set; }

    public int PageCount { get; set; }
    public DateTimeOffset AvailableSince { get; set; }

    /// <summary>Relative API route that downloads the PDF.</summary>
    public string DownloadUrl { get; set; } = string.Empty;
}

/// <summary>The signed-in mobile user's own salary slips. The person is resolved from the token only; nothing about them is accepted from the request.</summary>
public interface ISalarySlipMobileService
{
    /// <summary>All of the person's slips, newest month first, optionally limited to one month. An empty result is a success with an empty list (the controller turns it into a message).</summary>
    Task<Result<IReadOnlyList<MobileSalarySlipDto>>> GetMySalarySlipsAsync(DateOnly? salaryMonth, CancellationToken cancellationToken);

    /// <summary>Opens one of the person's slips. Unknown, foreign and file-less slips are all a 404.</summary>
    Task<Result<SalarySlipDownload>> OpenMyDownloadAsync(long salarySlipId, CancellationToken cancellationToken);
}

public class SalarySlipMobileService : ISalarySlipMobileService
{
    public const string PisRecordNotFoundMessage = "Your personnel record was not found.";
    public const string AccountNotRegisteredMessage = "Your salary bank account is not registered. Please contact the IT Cell.";
    public const string DownloadRoute = "api/v1/mobile/salary-slips/{0}/download";

    private readonly ICurrentUserService _currentUser;
    private readonly IMobileUserProfileRepository _profiles;
    private readonly IUserPisRepository _pis;
    private readonly ISalarySlipMobileRepository _slips;
    private readonly ISalarySlipStorageService _storage;
    private readonly IEncryptionService _encryption;
    private readonly IAuditService _audit;
    private readonly ILogger<SalarySlipMobileService> _logger;

    public SalarySlipMobileService(ICurrentUserService currentUser, IMobileUserProfileRepository profiles, IUserPisRepository pis,
        ISalarySlipMobileRepository slips, ISalarySlipStorageService storage, IEncryptionService encryption, IAuditService audit,
        ILogger<SalarySlipMobileService> logger)
    {
        _currentUser = currentUser;
        _profiles = profiles;
        _pis = pis;
        _slips = slips;
        _storage = storage;
        _encryption = encryption;
        _audit = audit;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<MobileSalarySlipDto>>> GetMySalarySlipsAsync(DateOnly? salaryMonth, CancellationToken cancellationToken)
    {
        Result<byte[]> account = await ResolveMyAccountAsync(cancellationToken);
        if (!account.IsSuccess)
            return Result.Failure<IReadOnlyList<MobileSalarySlipDto>>(account.ErrorCode!, account.ErrorMessage!);

        DateOnly? month = salaryMonth == null ? null : new DateOnly(salaryMonth.Value.Year, salaryMonth.Value.Month, 1);
        IReadOnlyList<SalarySlipCandidate> candidates = await _slips.GetCandidatesAsync(month, cancellationToken);

        // The account is ciphertext in the database, so ownership is decided here by decrypting and comparing.
        List<MobileSalarySlipDto> mine = candidates
            .Where(c => BelongsTo(account.Data!, c.AccountNumberEncrypted))
            .Select(c => new MobileSalarySlipDto
            {
                SalarySlipId = c.Id,
                SalaryMonth = c.SalaryMonth,
                PageCount = c.PageCount,
                AvailableSince = IstTime.FromIst(c.CreatedAtIst),
                DownloadUrl = string.Format(System.Globalization.CultureInfo.InvariantCulture, DownloadRoute, c.Id)
            })
            .ToList();

        return Result.Success<IReadOnlyList<MobileSalarySlipDto>>(mine);
    }

    public async Task<Result<SalarySlipDownload>> OpenMyDownloadAsync(long salarySlipId, CancellationToken cancellationToken)
    {
        Result<byte[]> account = await ResolveMyAccountAsync(cancellationToken);
        if (!account.IsSuccess)
            return Result.Failure<SalarySlipDownload>(account.ErrorCode!, account.ErrorMessage!);

        // Ownership is re-verified here on every request, independently of the listing.
        SalarySlip? slip = await _slips.GetByIdAsync(salarySlipId, cancellationToken);
        if (slip == null || !BelongsTo(account.Data!, slip.AccountNumberEncrypted))
            return Result.Failure<SalarySlipDownload>(ApiErrorCodes.NotFound, "Salary slip not found.");

        Stream? content;
        try
        {
            content = _storage.OpenRead(slip.FilePath);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Salary slip {SalarySlipId} could not be opened from storage", slip.Id);
            return Result.Failure<SalarySlipDownload>(ApiErrorCodes.NotFound, "The salary slip file is not available.");
        }

        if (content == null)
        {
            _logger.LogError("Salary slip {SalarySlipId} (batch {BatchId}) has no file in storage", slip.Id, slip.OriginalBatchId);
            return Result.Failure<SalarySlipDownload>(ApiErrorCodes.NotFound, "The salary slip file is not available.");
        }

        await AuditAsync(slip);
        return Result.Success(new SalarySlipDownload(content, $"SalarySlip_{slip.SalaryMonth:yyyy-MM}.pdf"));
    }

    /// <summary>Token → mobile profile → NSG number → PIS record → decrypted salary bank account (normalised, as bytes).</summary>
    private async Task<Result<byte[]>> ResolveMyAccountAsync(CancellationToken cancellationToken)
    {
        long? profileId = _currentUser.ProfileId;
        MobileUserProfile? profile = profileId == null ? null : await _profiles.GetByIdAsync(profileId.Value, cancellationToken);
        if (profile == null || !profile.IsActive)
            return Result.Failure<byte[]>(ApiErrorCodes.Unauthorized, "Please sign in again.");

        UserPis? pis = await _pis.GetByNsgNumberAsync(profile.NsgNumber, cancellationToken);
        if (pis == null)
            return Result.Failure<byte[]>(ApiErrorCodes.NotFound, PisRecordNotFoundMessage);

        string? account = Normalize(Decrypt(pis.SalaryAccountNumberEncrypted));
        if (account == null)
            return Result.Failure<byte[]>(ApiErrorCodes.NotFound, AccountNotRegisteredMessage);

        return Result.Success(Encoding.UTF8.GetBytes(account));
    }

    private bool BelongsTo(byte[] myAccount, string slipCipherText)
    {
        string? plain = Normalize(Decrypt(slipCipherText));
        return plain != null && CryptographicOperations.FixedTimeEquals(myAccount, Encoding.UTF8.GetBytes(plain));
    }

    private string? Decrypt(string? cipherText)
    {
        if (string.IsNullOrEmpty(cipherText)) return null;
        try
        {
            return _encryption.Decrypt(cipherText);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException or OverflowException)
        {
            _logger.LogWarning("A stored account number could not be decrypted while matching salary slips.");
            return null;
        }
    }

    /// <summary>Digits are compared exactly; surrounding and inner whitespace is not significant.</summary>
    private static string? Normalize(string? value)
    {
        if (value == null) return null;
        string compact = string.Concat(value.Where(c => !char.IsWhiteSpace(c)));
        return compact.Length == 0 ? null : compact;
    }

    private async Task AuditAsync(SalarySlip slip)
    {
        try
        {
            await _audit.LogAsync(AuditAction.DocumentDownloaded, DomainConstants.AuditResults.Success,
                entityType: "SalarySlip", entityId: slip.Id.ToString(), correlationId: _currentUser.CorrelationId,
                additionalData: $"{{\"operation\":\"MobileSalarySlipDownload\",\"profileId\":{_currentUser.ProfileId},\"salaryMonth\":\"{slip.SalaryMonth:yyyy-MM}\"}}",
                cancellationToken: CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Audit entry for the download of salary slip {SalarySlipId} could not be written", slip.Id);
        }
    }
}
