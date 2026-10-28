using System.Security.Cryptography;
using FluentValidation.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Application.Pis.DTOs;
using NSG.Mobile.Application.Pis.Mappings;
using NSG.Mobile.Application.Pis.Services;
using NSG.Mobile.Application.Pis.Validators;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Infrastructure.Options;
using NSG.Mobile.Infrastructure.Security;

namespace NSG.Mobile.Tests;

/// <summary>
/// No-database tests for round-tripping masked Aadhaar / PAN / PRAN / salary account values through PUT. They use the
/// real AES and HMAC services with in-memory random keys and an in-memory repository. The MySQL-backed
/// <c>UsersPisApiTests</c> are not exercised by these tests.
/// </summary>
public class PisSensitiveRoundTripTests
{
    private const string Aadhaar = "123546789532", Pan = "AGIPR3625F", Pran = "321654987321", Account = "30927989329";

    private readonly AesEncryptionService _aes = new(Options.Create(new EncryptionOptions { Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }));
    private readonly HmacBlindIndexService _hmac = new(Options.Create(new BlindIndexOptions { Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }));
    private readonly UserPis _stored;
    private readonly UserPisService _service;

    public PisSensitiveRoundTripTests()
    {
        _stored = new UserPis
        {
            Id = 1, NsgNumber = "N-1", ServiceNumber = "S-1",
            AadhaarNumberEncrypted = _aes.Encrypt(Aadhaar),
            PanNumberEncrypted = _aes.Encrypt(Pan), PanNumberHash = _hmac.ForPan(Pan),
            PranNumberEncrypted = _aes.Encrypt(Pran),
            SalaryAccountNumberEncrypted = _aes.Encrypt(Account), SalaryAccountNumberHash = _hmac.ForSalaryAccountNumber(Account)
        };
        _service = new UserPisService(new Repo(_stored), _aes, _hmac, new FakeAudit(), new FakeCurrentUser { UserId = 9001 },
            NullLogger<UserPisService>.Instance);
    }

    private sealed class Repo : IUserPisRepository
    {
        private readonly UserPis _row;
        public Repo(UserPis row) => _row = row;
        public Task<UserPis?> GetByIdAsync(long id, CancellationToken ct = default) => Task.FromResult<UserPis?>(_row);
        public Task<UserPis?> GetDetailAsync(long id, CancellationToken ct = default) => Task.FromResult<UserPis?>(_row);
        public Task<UserPis?> GetByNsgNumberAsync(string nsgNumber, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<(IReadOnlyList<UserPis> Items, int TotalCount)> GetPagedAsync(int pageNumber, int pageSize, UserPisFilter filter, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> GetMissingReferencesAsync(UserPis candidate, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        public Task<PisSaveOutcome> AddAsync(UserPis entity, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PisSaveOutcome> UpdateAsync(UserPis entity, CancellationToken ct = default) => Task.FromResult(PisSaveOutcome.Saved);
    }

    private static UpdateUserPisRequestDto Request(string? aadhaar = null, string? pan = null, string? pran = null, string? account = null)
        => new() { ServiceNumber = "S-1", IsActive = true, AadhaarNumber = aadhaar, PanNumber = pan, PranNumber = pran, SalaryAccountNumber = account };

    private static ValidationResult Validate(UpdateUserPisRequestDto r) => new UpdateUserPisRequestValidator().Validate(r);

    /// <summary>What GET returns for the stored record, so the tests use the real masks rather than hand-written ones.</summary>
    private async Task<UserPisResponseDto> GetAsync() => (await _service.GetByIdAsync(1, default)).Data!;

    private (string Aadhaar, string Pan, string Pran, string Account, string PanHash, string AccountHash) Snapshot()
        => (_stored.AadhaarNumberEncrypted!, _stored.PanNumberEncrypted!, _stored.PranNumberEncrypted!, _stored.SalaryAccountNumberEncrypted!,
            _stored.PanNumberHash!, _stored.SalaryAccountNumberHash!);

    // ── Exact GET mask round-trips ──

    [Fact]
    public async Task Exact_masks_from_GET_pass_validation_and_leave_ciphertext_and_blind_indexes_untouched()
    {
        UserPisResponseDto get = await GetAsync();
        var before = Snapshot();
        UpdateUserPisRequestDto request = Request(get.AadhaarNumberMasked, get.PanNumberMasked, get.PranNumberMasked, get.SalaryAccountNumberMasked);

        Assert.True(Validate(request).IsValid);
        Result<UserPisResponseDto> result = await _service.UpdateAsync(1, request, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(before, Snapshot());
        Assert.Equal(Aadhaar, _aes.Decrypt(_stored.AadhaarNumberEncrypted!));
        Assert.Equal(Pran, _aes.Decrypt(_stored.PranNumberEncrypted!));
        Assert.Equal(get.PranNumberMasked, result.Data!.PranNumberMasked);
    }

    [Fact]
    public async Task Each_field_round_trips_on_its_own()
    {
        UserPisResponseDto get = await GetAsync();
        var before = Snapshot();

        foreach (UpdateUserPisRequestDto r in new[]
                 {
                     Request(aadhaar: get.AadhaarNumberMasked), Request(pan: get.PanNumberMasked),
                     Request(pran: get.PranNumberMasked), Request(account: get.SalaryAccountNumberMasked)
                 })
        {
            Assert.True(Validate(r).IsValid);
            Assert.True((await _service.UpdateAsync(1, r, default)).IsSuccess);
            Assert.Equal(before, Snapshot());
        }
    }

    // ── New values ──

    [Fact]
    public async Task New_unmasked_values_replace_the_stored_ones_and_recompute_the_blind_indexes()
    {
        var before = Snapshot();
        UpdateUserPisRequestDto request = Request("999988887777", "abcde1234f", "111122223333", "123456789012");

        Assert.True(Validate(request).IsValid);
        Assert.True((await _service.UpdateAsync(1, request, default)).IsSuccess);

        Assert.Equal("999988887777", _aes.Decrypt(_stored.AadhaarNumberEncrypted!));
        Assert.Equal("ABCDE1234F", _aes.Decrypt(_stored.PanNumberEncrypted!));
        Assert.Equal("111122223333", _aes.Decrypt(_stored.PranNumberEncrypted!));
        Assert.Equal("123456789012", _aes.Decrypt(_stored.SalaryAccountNumberEncrypted!));
        Assert.NotEqual(before.PanHash, _stored.PanNumberHash);
        Assert.Equal(_hmac.ForPan("ABCDE1234F"), _stored.PanNumberHash);
        Assert.NotEqual(before.AccountHash, _stored.SalaryAccountNumberHash);
        Assert.Equal(_hmac.ForSalaryAccountNumber("123456789012"), _stored.SalaryAccountNumberHash);
    }

    // ── Invalid values ──

    [Theory]
    [InlineData("12345", "AGIP3625FF", "32165498732", "12345678")]
    [InlineData("1235 4678 9532", "AGI-R3625F", "32165498732X", "3092-7989-329")]
    [InlineData("", "", "", "")]
    public void Invalid_non_masked_values_are_rejected_by_the_update_validator(string aadhaar, string pan, string pran, string account)
    {
        ValidationResult result = Validate(Request(aadhaar, pan, pran, account));

        // Blank PAN/account normalise to null (existing contract) and are accepted; Aadhaar and PRAN must still be rejected.
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateUserPisRequestDto.AadhaarNumber));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateUserPisRequestDto.PranNumber));
        if (aadhaar.Length > 3)
            Assert.All(result.Errors, e => Assert.DoesNotContain(aadhaar, e.ErrorMessage));
    }

    [Fact]
    public void Masks_are_never_accepted_on_create()
    {
        CreateUserPisRequestDto create = new()
        {
            NsgNumber = "N-2", ServiceNumber = "S-2",
            AadhaarNumber = "XXXXXXXX9532", PanNumber = "XXXXXX625F", PranNumber = "XXXXXXXX7321", SalaryAccountNumber = "XXXXXXX9329"
        };

        ValidationResult result = new CreateUserPisRequestValidator().Validate(create);

        Assert.Equal(4, result.Errors.Count);
    }

    // ── Forged / mismatched masks ──

    [Theory]
    [InlineData("XXXXXXXX0000")]   // right shape, wrong last four
    [InlineData("XXXXXXXXXX9532")] // wrong length for this record
    [InlineData("XXXXXXXXX9532")]
    [InlineData("XXXXXX9532")]
    public async Task A_forged_aadhaar_mask_is_rejected_and_nothing_changes(string forged)
        => await AssertRejectedAsync(Request(aadhaar: forged, pan: "ZZZZZ9999Z"));

    [Theory]
    [InlineData("XXXXXX625G")]
    [InlineData("XXXXXXX625F")]
    public async Task A_forged_pan_mask_is_rejected_and_nothing_changes(string forged)
        => await AssertRejectedAsync(Request(pan: forged, account: "123456789012"));

    [Theory]
    [InlineData("XXXXXXXX0001")]
    [InlineData("XXXXXXXXX7321")]
    public async Task A_forged_pran_mask_is_rejected_and_nothing_changes(string forged)
        => await AssertRejectedAsync(Request(pran: forged, aadhaar: "999988887777"));

    [Theory]
    [InlineData("XXXXXXX9330")]
    [InlineData("XXXXXXXX9329")]
    public async Task A_forged_salary_account_mask_is_rejected_and_nothing_changes(string forged)
        => await AssertRejectedAsync(Request(account: forged, pan: "ZZZZZ9999Z"));

    [Fact]
    public async Task A_mask_is_rejected_when_nothing_is_stored_for_the_field()
    {
        _stored.PranNumberEncrypted = null;
        await AssertRejectedAsync(Request(pran: "XXXXXXXX7321"));
        Assert.Null(_stored.PranNumberEncrypted);
    }

    [Fact]
    public async Task A_mask_is_rejected_when_the_stored_value_cannot_be_decrypted()
    {
        _stored.AadhaarNumberEncrypted = "not-valid-ciphertext";
        await AssertRejectedAsync(Request(aadhaar: "XXXXXXXX9532"));
        Assert.Equal("not-valid-ciphertext", _stored.AadhaarNumberEncrypted);
    }

    [Fact]
    public void A_pran_mask_is_not_a_valid_real_pran_and_is_never_encrypted_as_one()
    {
        Assert.DoesNotMatch(NSG.Mobile.Domain.Constants.DomainConstants.PisFormats.PranPattern, "XXXXXXXX7321");
        Assert.False(new PisSensitiveIdentifiersValidator().Validate(new PisSensitiveIdentifiersInput { PranNumber = "XXXXXXXX7321" }).IsValid);
    }

    private async Task AssertRejectedAsync(UpdateUserPisRequestDto request)
    {
        var before = Snapshot();

        Result<UserPisResponseDto> result = await _service.UpdateAsync(1, request, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApiErrorCodes.ValidationFailed, result.ErrorCode);
        Assert.DoesNotContain("XXXX", result.ErrorMessage);
        Assert.Equal(before, Snapshot());   // other fields in the same request were not applied either
    }

    // ── Null / empty contract is unchanged ──

    [Fact]
    public async Task Null_values_keep_the_stored_data()
    {
        var before = Snapshot();
        UpdateUserPisRequestDto request = Request();

        Assert.True(Validate(request).IsValid);
        Assert.True((await _service.UpdateAsync(1, request, default)).IsSuccess);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public void Empty_and_blank_strings_are_still_rejected_by_validation_so_nothing_can_be_cleared()
    {
        // Existing contract, unchanged: only null means "omitted"; there is no clearing semantics.
        ValidationResult result = Validate(Request(aadhaar: "", pan: "  ", pran: "", account: ""));

        Assert.Equal(4, result.Errors.Count);
    }

    // ── Mask helper ──

    [Theory]
    [InlineData("XXXXXXXX9532", true)]
    [InlineData("XXXXXX625F", true)]
    [InlineData("XXXXXXX9329", true)]
    [InlineData("123546789532", false)]
    [InlineData("AGIPR3625F", false)]
    [InlineData("xxxxxxxx9532", false)]
    [InlineData("XXXX9532", false)]
    [InlineData("", false)]
    public void LooksMasked_recognises_only_mask_shaped_text(string value, bool expected)
        => Assert.Equal(expected, PisMasking.LooksMasked(value));
}
