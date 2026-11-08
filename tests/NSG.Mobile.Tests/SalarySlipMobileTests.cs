using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSG.Mobile.API.Controllers;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Interfaces.Infrastructure;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Application.SalarySlips;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Infrastructure.Options;
using NSG.Mobile.Infrastructure.Services;

namespace NSG.Mobile.Tests;

// Phase 5: the mobile salary slip endpoints over the real service, real storage service and real temporary files, with the database replaced by
// in-memory fakes. NOT covered: the SQL of SalarySlipMobileRepository / UserPisRepository and the JWT validation itself (a host test needs the
// NSG_TEST_MYSQL database). Unauthenticated access is asserted through the [Authorize(RequireMobileProfile)] attribute and the 401 for an
// unresolvable identity.

internal sealed class SlipFakeMobileRepository : ISalarySlipMobileRepository
{
    public List<SalarySlip> Slips { get; } = new();
    public int Reads { get; private set; }

    public Task<IReadOnlyList<SalarySlipCandidate>> GetCandidatesAsync(DateOnly? salaryMonth, CancellationToken cancellationToken)
    {
        Reads++;
        return Task.FromResult<IReadOnlyList<SalarySlipCandidate>>(Slips.Where(s => salaryMonth == null || s.SalaryMonth == salaryMonth)
            .OrderByDescending(s => s.SalaryMonth).ThenByDescending(s => s.Id)
            .Select(s => new SalarySlipCandidate(s.Id, s.SalaryMonth, s.AccountNumberEncrypted, s.PageCount, s.CreatedAtIst)).ToList());
    }

    public Task<SalarySlip?> GetByIdAsync(long salarySlipId, CancellationToken cancellationToken)
        => Task.FromResult(Slips.FirstOrDefault(s => s.Id == salarySlipId));
}

public class SalarySlipMobileTests : IDisposable
{
    private const string AliceAccount = "11111111111", BobAccount = "22222222222";
    private const long AliceProfile = 1, BobProfile = 2, EveProfile = 3, CarolProfile = 4, DaveProfile = 5, GoneProfile = 6;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "nsg-slip-mobile-" + Guid.NewGuid().ToString("N"));
    private readonly SlipFakeEncryption _encryption = new();
    private readonly SlipFakeMobileRepository _slips = new();
    private readonly FakePisRepository _pis = new();
    private readonly FakeProfileRepository _profiles = new(new FakeGrantRepository(new TestClock()));
    private readonly FakeCurrentUser _user = new() { ProfileId = AliceProfile };
    private readonly FakeAudit _audit = new();
    private readonly SalarySlipStorageService _storage;
    private readonly SalarySlipsController _controller;
    private long _nextSlipId = 1;

    public SalarySlipMobileTests()
    {
        _storage = new SalarySlipStorageService(TestStorage.Central(_root), NullLogger<SalarySlipStorageService>.Instance);

        AddPerson(AliceProfile, 101, "NSG-1001", _encryption.Encrypt(AliceAccount));
        AddPerson(BobProfile, 102, "NSG-1002", _encryption.Encrypt(BobAccount));
        AddPerson(EveProfile, 103, "NSG-1003", _encryption.Encrypt("33333333333"));   // has an account, will have no slips
        AddPerson(CarolProfile, 104, "NSG-1004", null);                               // no bank account registered
        _profiles.Profiles.Add(new MobileUserProfile { Id = DaveProfile, NsgNumber = "NSG-9999", IsActive = true });   // no PIS record
        _profiles.Profiles.Add(new MobileUserProfile { Id = GoneProfile, NsgNumber = "NSG-1001", IsActive = false });

        SalarySlipMobileService service = new(_user, _profiles, _pis, _slips, _storage, _encryption, _audit, NullLogger<SalarySlipMobileService>.Instance);
        _controller = new SalarySlipsController(service) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private void AddPerson(long profileId, long pisId, string nsg, string? accountCipher)
    {
        _pis.Rows.Add(new UserPis { Id = pisId, NsgNumber = nsg, SalaryAccountNumberEncrypted = accountCipher });
        _profiles.Profiles.Add(new MobileUserProfile { Id = profileId, NsgNumber = nsg, IsActive = true });
    }

    /// <summary>Stores a slip row (and, unless <paramref name="withFile"/> is false, a real PDF file) for an account.</summary>
    private SalarySlip AddSlip(string account, DateOnly month, long? userPisId = null, bool withFile = true)
    {
        SalarySlipFileLocation location = _storage.ReserveSlipLocation(month);
        if (withFile) File.WriteAllBytes(location.AbsolutePath, SlipTestPdf.Make(SlipTestPdf.Slip("p1", account)));
        SalarySlip slip = new()
        {
            Id = _nextSlipId++, AccountNumberEncrypted = _encryption.Encrypt(account), SalaryMonth = month, UserPisId = userPisId,
            FilePath = location.RelativePath, PageCount = 1, OriginalBatchId = 1
        };
        _slips.Slips.Add(slip);
        return slip;
    }

    private static DateOnly M(int year, int month) => new(year, month, 1);

    private static IReadOnlyList<MobileSalarySlipDto> Items(IActionResult result)
    {
        ApiResponse<IReadOnlyList<MobileSalarySlipDto>> body = Assert.IsType<ApiResponse<IReadOnlyList<MobileSalarySlipDto>>>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.True(body.Success);
        return body.Data!;
    }

    /// <summary>The empty answer: success, the message, and data as an empty array.</summary>
    private static string MessageOf(IActionResult result)
    {
        ApiResponse<IReadOnlyList<MobileSalarySlipDto>> body = Assert.IsType<ApiResponse<IReadOnlyList<MobileSalarySlipDto>>>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.True(body.Success);
        Assert.NotNull(body.Data);
        Assert.Empty(body.Data!);
        return body.Message;
    }

    private static ProblemDetails Problem(IActionResult result, int status)
    {
        ObjectResult o = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(status, o.StatusCode);
        return Assert.IsType<ProblemDetails>(o.Value);
    }

    // ── Listing ──

    [Fact]
    public async Task A_user_with_slips_in_several_months_gets_them_all_newest_first()
    {
        AddSlip(AliceAccount, M(2026, 9));
        AddSlip(AliceAccount, M(2026, 11));
        AddSlip(AliceAccount, M(2026, 10));
        AddSlip(BobAccount, M(2026, 10));   // someone else's

        IReadOnlyList<MobileSalarySlipDto> items = Items(await _controller.GetMine());

        Assert.Equal(new[] { M(2026, 11), M(2026, 10), M(2026, 9) }, items.Select(i => i.SalaryMonth).ToArray());
        Assert.All(items, i => Assert.Equal($"api/v1/mobile/salary-slips/{i.SalarySlipId}/download", i.DownloadUrl));
        Assert.DoesNotContain(items, i => _slips.Slips.Single(s => s.Id == i.SalarySlipId).AccountNumberEncrypted.Contains(BobAccount));
    }

    [Fact]
    public async Task Matching_is_by_the_decrypted_account_number_and_never_by_UserPisId()
    {
        SalarySlip unlinked = AddSlip(AliceAccount, M(2026, 9), userPisId: null);          // uploaded before the PIS link existed
        SalarySlip stale = AddSlip(AliceAccount, M(2026, 10), userPisId: 999);             // link points at a different record
        SalarySlip trap = AddSlip(BobAccount, M(2026, 11), userPisId: 101);                // Alice's PIS id, but Bob's account

        IReadOnlyList<MobileSalarySlipDto> items = Items(await _controller.GetMine());

        Assert.Equal(new[] { stale.Id, unlinked.Id }, items.Select(i => i.SalarySlipId).ToArray());
        Assert.DoesNotContain(items, i => i.SalarySlipId == trap.Id);
        Problem(await _controller.Download(trap.Id), 404);
    }

    [Fact]
    public async Task A_user_without_slips_gets_one_message_not_an_empty_list()
    {
        _user.ProfileId = EveProfile;
        AddSlip(AliceAccount, M(2026, 10));

        Assert.Equal("No salary slips found.", MessageOf(await _controller.GetMine()));
    }

    [Fact]
    public async Task The_month_filter_returns_only_that_month_and_a_message_when_nothing_matches()
    {
        AddSlip(AliceAccount, M(2026, 9));
        SalarySlip october = AddSlip(AliceAccount, M(2026, 10));
        AddSlip(AliceAccount, M(2026, 11));

        Assert.Equal(october.Id, Assert.Single(Items(await _controller.GetMine("2026-10"))).SalarySlipId);
        Assert.Equal("No salary slips found.", MessageOf(await _controller.GetMine("2025-01")));
        Assert.Equal(3, Items(await _controller.GetMine()).Count);
        Assert.Equal(3, Items(await _controller.GetMine("  ")).Count);   // blank means no filter

        foreach (string bad in new[] { "2026-13", "10-2026", "2026-10-01", "October", "2026/10" })
            Assert.Contains("yyyy-MM", Problem(await _controller.GetMine(bad), 400).Detail);
    }

    [Fact]
    public async Task The_person_is_resolved_from_the_token_through_NSG_number_to_the_right_PIS_record()
    {
        AddSlip(AliceAccount, M(2026, 10));
        SalarySlip bobs = AddSlip(BobAccount, M(2026, 10));

        _user.ProfileId = BobProfile;
        Assert.Equal(bobs.Id, Assert.Single(Items(await _controller.GetMine())).SalarySlipId);

        _user.ProfileId = AliceProfile;
        Assert.NotEqual(bobs.Id, Assert.Single(Items(await _controller.GetMine())).SalarySlipId);
        Assert.True(_pis.NsgLookups >= 2);

        // The endpoints take no person identifier from the caller.
        foreach (MethodInfo action in typeof(SalarySlipsController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            Assert.DoesNotContain(action.GetParameters(), p => p.Name!.Contains("nsg", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("account", StringComparison.OrdinalIgnoreCase)
                                                              || p.Name.Contains("profile", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("user", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task When_the_registered_account_changes_slips_of_the_old_account_are_no_longer_the_users()
    {
        AddSlip(AliceAccount, M(2026, 10));
        _pis.Rows.Single(r => r.NsgNumber == "NSG-1001").SalaryAccountNumberEncrypted = _encryption.Encrypt("77777777777");

        Assert.Equal("No salary slips found.", MessageOf(await _controller.GetMine()));
    }

    [Fact]
    public async Task Whitespace_in_the_registered_account_does_not_prevent_a_match()
    {
        SalarySlip slip = AddSlip(AliceAccount, M(2026, 10));
        _pis.Rows.Single(r => r.NsgNumber == "NSG-1001").SalaryAccountNumberEncrypted = _encryption.Encrypt(" 1111 1111 111 ");

        Assert.Equal(slip.Id, Assert.Single(Items(await _controller.GetMine())).SalarySlipId);
    }

    [Fact]
    public async Task An_unreadable_stored_slip_is_skipped_without_failing_the_list()
    {
        SalarySlip good = AddSlip(AliceAccount, M(2026, 10));
        SalarySlip broken = AddSlip(AliceAccount, M(2026, 11));
        broken.AccountNumberEncrypted = "enc:not-valid:%%%";

        Assert.Equal(good.Id, Assert.Single(Items(await _controller.GetMine())).SalarySlipId);
    }

    [Fact]
    public async Task The_response_carries_no_account_number_ciphertext_or_storage_path()
    {
        SalarySlip slip = AddSlip(AliceAccount, M(2026, 10));

        string json = JsonSerializer.Serialize(Items(await _controller.GetMine()));

        Assert.DoesNotContain(AliceAccount, json);
        Assert.DoesNotContain(slip.AccountNumberEncrypted, json);
        Assert.DoesNotContain(slip.FilePath, json);
        Assert.DoesNotContain("salary-slips/2026", json);
        Assert.DoesNotContain("pis", json, StringComparison.OrdinalIgnoreCase);
    }

    // ── Identity and PIS mapping failures ──

    [Fact]
    public async Task Without_a_resolvable_signed_in_profile_every_endpoint_is_a_401()
    {
        AddSlip(AliceAccount, M(2026, 10));

        foreach (long? profile in new long?[] { null, 424242, GoneProfile })   // no claim, unknown profile, deactivated profile
        {
            _user.ProfileId = profile;
            Problem(await _controller.GetMine(), 401);
            Problem(await _controller.Download(1), 401);
        }
    }

    [Fact]
    public async Task A_user_without_a_PIS_record_gets_a_404_with_a_message()
    {
        _user.ProfileId = DaveProfile;
        AddSlip(AliceAccount, M(2026, 10));

        Assert.Equal(SalarySlipMobileService.PisRecordNotFoundMessage, Problem(await _controller.GetMine(), 404).Detail);
        Assert.Equal(SalarySlipMobileService.PisRecordNotFoundMessage, Problem(await _controller.Download(1), 404).Detail);
        Assert.Equal(0, _slips.Reads);   // no slip is even read for an unresolved person
    }

    [Fact]
    public async Task A_user_whose_PIS_record_has_no_readable_bank_account_gets_a_404_with_a_message()
    {
        AddSlip(AliceAccount, M(2026, 10));

        _user.ProfileId = CarolProfile;   // account is null
        Assert.Equal(SalarySlipMobileService.AccountNotRegisteredMessage, Problem(await _controller.GetMine(), 404).Detail);
        Problem(await _controller.Download(1), 404);

        _user.ProfileId = AliceProfile;   // account cannot be decrypted (for example a rotated key)
        _pis.Rows.Single(r => r.NsgNumber == "NSG-1001").SalaryAccountNumberEncrypted = "enc:garbage:%%%";
        Assert.Equal(SalarySlipMobileService.AccountNotRegisteredMessage, Problem(await _controller.GetMine(), 404).Detail);
        Assert.Equal(0, _slips.Reads);
    }

    // ── Download ──

    [Fact]
    public async Task Download_streams_the_users_own_pdf_with_no_store_and_a_safe_name()
    {
        SalarySlip slip = AddSlip(AliceAccount, M(2026, 10));

        FileStreamResult file = Assert.IsType<FileStreamResult>(await _controller.Download(slip.Id));
        await using Stream stream = file.FileStream;
        byte[] head = new byte[5];
        await stream.ReadExactlyAsync(head);

        Assert.Equal("application/pdf", file.ContentType);
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(head));
        Assert.Equal("SalarySlip_2026-10.pdf", file.FileDownloadName);
        Assert.Equal("no-store", _controller.Response.Headers.CacheControl.ToString());

        string audit = Assert.Single(_audit.Entries).Data!;
        Assert.DoesNotContain(AliceAccount, audit);
        Assert.DoesNotContain(slip.FilePath, audit);
    }

    [Fact]
    public async Task Downloading_another_persons_slip_is_indistinguishable_from_an_unknown_id()
    {
        SalarySlip bobs = AddSlip(BobAccount, M(2026, 10));

        ProblemDetails foreign = Problem(await _controller.Download(bobs.Id), 404);
        ProblemDetails unknown = Problem(await _controller.Download(9999), 404);

        Assert.Equal(unknown.Detail, foreign.Detail);
        Assert.Empty(_audit.Entries);

        _user.ProfileId = BobProfile;
        await using (Stream own = Assert.IsType<FileStreamResult>(await _controller.Download(bobs.Id)).FileStream) { Assert.True(own.CanRead); }
    }

    [Fact]
    public async Task An_account_mismatch_blocks_the_download_even_when_the_slip_is_linked_to_the_users_PIS_id()
    {
        SalarySlip mismatched = AddSlip(BobAccount, M(2026, 10), userPisId: 101);   // linked to Alice's PIS record, Bob's account

        Problem(await _controller.Download(mismatched.Id), 404);
    }

    [Fact]
    public async Task A_missing_pdf_file_is_a_404_with_a_message_and_no_path()
    {
        SalarySlip slip = AddSlip(AliceAccount, M(2026, 10), withFile: false);

        ProblemDetails problem = Problem(await _controller.Download(slip.Id), 404);

        Assert.Equal("The salary slip file is not available.", problem.Detail);
        Assert.DoesNotContain(slip.FilePath, problem.Detail);
        Assert.Empty(_audit.Entries);
    }

    [Fact]
    public async Task A_tampered_stored_path_is_refused_with_the_same_404()
    {
        SalarySlip slip = AddSlip(AliceAccount, M(2026, 10));
        slip.FilePath = "../../outside.pdf";

        Assert.Equal("The salary slip file is not available.", Problem(await _controller.Download(slip.Id), 404).Detail);
    }

    // ── Contract ──

    [Fact]
    public void The_controller_admits_only_mobile_profiles_and_no_action_is_anonymous()
    {
        Type type = typeof(SalarySlipsController);
        Assert.Equal(DomainConstants.Policies.RequireMobileProfile, Assert.Single(type.GetCustomAttributes<AuthorizeAttribute>()).Policy);
        Assert.Null(type.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.All(type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly), m => Assert.Null(m.GetCustomAttribute<AllowAnonymousAttribute>()));
        Assert.Equal("api/v1/mobile/salary-slips", type.GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Equal("{id:long}/download", type.GetMethod(nameof(SalarySlipsController.Download))!.GetCustomAttribute<HttpGetAttribute>()!.Template);
    }
}
