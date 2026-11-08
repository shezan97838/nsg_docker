using System.Globalization;
using Microsoft.Extensions.Logging;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Interfaces.Infrastructure;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Application.Interfaces.Services;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Application.Form16;

/// <summary>
/// The signed-in mobile user's own Form 16 (read only). The person is resolved from the token (profile → NSG number); nothing about them is
/// accepted from the request, so another person's documents cannot be asked for.
/// </summary>
public interface IForm16MobileService
{
    /// <summary>Assessment years with at least one available part, newest first. An empty result is a success with an empty list (the controller turns it into a message).</summary>
    Task<Result<IReadOnlyList<Form16MobileDto>>> GetMyAsync(CancellationToken cancellationToken);

    /// <summary>Availability and upload timestamps for one assessment year; 404 when the person has nothing for it.</summary>
    Task<Result<Form16MobileDto>> GetMyDetailsAsync(string assessmentYear, CancellationToken cancellationToken);

    /// <summary>Opens one part of the person's own Form 16. Missing record, missing part and missing file are all a 404.</summary>
    Task<Result<Form16Download>> OpenMyPartAsync(string assessmentYear, Form16Part part, CancellationToken cancellationToken);
}

public class Form16MobileService : IForm16MobileService
{
    public const string DetailsRoute = "api/v1/mobile/form16/{0}";
    public const string NotAvailableMessage = "Form 16 is not available for this assessment year.";
    public const string FileNotAvailableMessage = "The Form 16 file is not available.";
    public const string InvalidAssessmentYearMessage = "assessmentYear must be in the form yyyy-yy, for example 2026-27.";

    private readonly ICurrentUserService _currentUser;
    private readonly IMobileUserProfileRepository _profiles;
    private readonly IForm16Repository _repository;
    private readonly IForm16StorageService _storage;
    private readonly IAuditService _audit;
    private readonly ILogger<Form16MobileService> _logger;

    public Form16MobileService(ICurrentUserService currentUser, IMobileUserProfileRepository profiles, IForm16Repository repository,
        IForm16StorageService storage, IAuditService audit, ILogger<Form16MobileService> logger)
    {
        _currentUser = currentUser;
        _profiles = profiles;
        _repository = repository;
        _storage = storage;
        _audit = audit;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<Form16MobileDto>>> GetMyAsync(CancellationToken cancellationToken)
    {
        string? nsg = await ResolveMyNsgNumberAsync(cancellationToken);
        if (nsg == null)
            return Result.Failure<IReadOnlyList<Form16MobileDto>>(ApiErrorCodes.Unauthorized, "Please sign in again.");

        IReadOnlyList<PersonnelForm16> rows = await _repository.ListByNsgNumberAsync(nsg, cancellationToken);
        List<Form16MobileDto> mine = rows
            .Where(r => IsMine(r, nsg) && HasAnyPart(r))
            .Select(ToDto)
            .ToList();
        return Result.Success<IReadOnlyList<Form16MobileDto>>(mine);
    }

    public async Task<Result<Form16MobileDto>> GetMyDetailsAsync(string assessmentYear, CancellationToken cancellationToken)
    {
        string? year = Form16Rules.NormalizeAssessmentYear(assessmentYear);
        if (year == null)
            return Result.Failure<Form16MobileDto>(ApiErrorCodes.ValidationFailed, InvalidAssessmentYearMessage);

        string? nsg = await ResolveMyNsgNumberAsync(cancellationToken);
        if (nsg == null)
            return Result.Failure<Form16MobileDto>(ApiErrorCodes.Unauthorized, "Please sign in again.");

        PersonnelForm16? record = await _repository.GetByKeyAsync(nsg, year, cancellationToken);
        return record == null || !IsMine(record, nsg) || !HasAnyPart(record)
            ? Result.Failure<Form16MobileDto>(ApiErrorCodes.NotFound, NotAvailableMessage)
            : Result.Success(ToDto(record));
    }

    public async Task<Result<Form16Download>> OpenMyPartAsync(string assessmentYear, Form16Part part, CancellationToken cancellationToken)
    {
        string? year = Form16Rules.NormalizeAssessmentYear(assessmentYear);
        if (year == null)
            return Result.Failure<Form16Download>(ApiErrorCodes.ValidationFailed, InvalidAssessmentYearMessage);

        string? nsg = await ResolveMyNsgNumberAsync(cancellationToken);
        if (nsg == null)
            return Result.Failure<Form16Download>(ApiErrorCodes.Unauthorized, "Please sign in again.");

        // The lookup key contains the NSG number from the token, and ownership is re-checked on the row that comes back.
        PersonnelForm16? record = await _repository.GetByKeyAsync(nsg, year, cancellationToken);
        string label = part == Form16Part.PartA ? "Part A" : "Part B";
        string? path = record == null || !IsMine(record, nsg) ? null : part == Form16Part.PartA ? record.PartAFilePath : record.PartBFilePath;
        if (string.IsNullOrEmpty(path))
            return Result.Failure<Form16Download>(ApiErrorCodes.NotFound, $"Form 16 {label} is not available for this assessment year.");

        Stream? content;
        try
        {
            content = _storage.OpenRead(path);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Form 16 {Part} of record {Form16Id} could not be opened from storage", label, record!.Id);
            return Result.Failure<Form16Download>(ApiErrorCodes.NotFound, FileNotAvailableMessage);
        }

        if (content == null)
        {
            _logger.LogError("Form 16 {Part} of record {Form16Id} is in the database but has no file in storage", label, record!.Id);
            return Result.Failure<Form16Download>(ApiErrorCodes.NotFound, FileNotAvailableMessage);
        }

        await AuditAsync(record!, label);
        return Result.Success(new Form16Download(content, $"Form16_{year}_{(part == Form16Part.PartA ? "PartA" : "PartB")}.pdf"));
    }

    /// <summary>Token → mobile profile → NSG number. Null when the profile is missing or inactive.</summary>
    private async Task<string?> ResolveMyNsgNumberAsync(CancellationToken cancellationToken)
    {
        long? profileId = _currentUser.ProfileId;
        MobileUserProfile? profile = profileId == null ? null : await _profiles.GetByIdAsync(profileId.Value, cancellationToken);
        return profile is { IsActive: true } && !string.IsNullOrWhiteSpace(profile.NsgNumber) ? profile.NsgNumber : null;
    }

    private static bool IsMine(PersonnelForm16 record, string nsg) => string.Equals(record.NsgNumber, nsg, StringComparison.OrdinalIgnoreCase);

    private static bool HasAnyPart(PersonnelForm16 r) => !string.IsNullOrEmpty(r.PartAFilePath) || !string.IsNullOrEmpty(r.PartBFilePath);

    private static Form16MobileDto ToDto(PersonnelForm16 r)
    {
        string route = string.Format(CultureInfo.InvariantCulture, DetailsRoute, r.AssessmentYear);
        bool a = !string.IsNullOrEmpty(r.PartAFilePath);
        bool b = !string.IsNullOrEmpty(r.PartBFilePath);
        return new Form16MobileDto
        {
            AssessmentYear = r.AssessmentYear,
            PartAAvailable = a,
            PartAUploadedAt = a && r.PartAUploadedAtIst != null ? IstTime.FromIst(r.PartAUploadedAtIst.Value) : null,
            PartBAvailable = b,
            PartBUploadedAt = b && r.PartBUploadedAtIst != null ? IstTime.FromIst(r.PartBUploadedAtIst.Value) : null,
            PartADownloadUrl = a ? route + "/part-a" : null,
            PartBDownloadUrl = b ? route + "/part-b" : null
        };
    }

    private async Task AuditAsync(PersonnelForm16 record, string label)
    {
        try
        {
            await _audit.LogAsync(AuditAction.DocumentDownloaded, DomainConstants.AuditResults.Success,
                entityType: "PersonnelForm16", entityId: record.Id.ToString(), correlationId: _currentUser.CorrelationId,
                additionalData: $"{{\"operation\":\"MobileForm16Download\",\"profileId\":{_currentUser.ProfileId},\"assessmentYear\":\"{record.AssessmentYear}\",\"part\":\"{label}\"}}",
                cancellationToken: CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Audit entry for the download of Form 16 record {Form16Id} could not be written", record.Id);
        }
    }
}
