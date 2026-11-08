using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSG.Mobile.API.Controllers;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Form16;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;
using NSG.Mobile.Infrastructure.Services;

namespace NSG.Mobile.Tests;

// Phase 3 of the Form 16 module: the four mobile endpoints through Form16Controller over the real services and real storage in a temporary
// directory; the database is replaced by in-memory fakes. Records are created through the real admin service. NOT covered: the SQL of
// Form16Repository and the JWT validation itself (needs the NSG_TEST_MYSQL database); the policy is asserted via the [Authorize] attribute and
// the 401 for an unresolvable identity.

public class Form16MobileApiTests : IDisposable
{
    private const long AliceProfile = 1, BobProfile = 2, DaveProfile = 3, GoneProfile = 4;
    private const string Alice = "NSG-1001", Bob = "NSG-1002", Year = "2026-27";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "nsg-form16-mobile-" + Guid.NewGuid().ToString("N"));
    private readonly FakePisRepository _pis = new();
    private readonly Form16FakeRepository _repo;
    private readonly FakeProfileRepository _profiles = new(new FakeGrantRepository(new TestClock()));
    private readonly FakeCurrentUser _user = new() { ProfileId = AliceProfile };
    private readonly FakeAudit _audit = new();
    private readonly Form16StorageService _storage;
    private readonly Form16AdminService _admin;
    private readonly Form16Controller _controller;

    public Form16MobileApiTests()
    {
        _pis.Rows.Add(new UserPis { Id = 1, NsgNumber = Alice, FullName = "Alice" });
        _pis.Rows.Add(new UserPis { Id = 2, NsgNumber = Bob, FullName = "Bob" });
        _pis.Rows.Add(new UserPis { Id = 3, NsgNumber = "NSG-1003", FullName = "Dave" });
        _profiles.Profiles.Add(new MobileUserProfile { Id = AliceProfile, NsgNumber = Alice, IsActive = true });
        _profiles.Profiles.Add(new MobileUserProfile { Id = BobProfile, NsgNumber = Bob, IsActive = true });
        _profiles.Profiles.Add(new MobileUserProfile { Id = DaveProfile, NsgNumber = "NSG-1003", IsActive = true });
        _profiles.Profiles.Add(new MobileUserProfile { Id = GoneProfile, NsgNumber = Alice, IsActive = false });

        _repo = new Form16FakeRepository(_pis);
        _storage = Form16StorageTests.Create(_root);
        _admin = new Form16AdminService(_repo, _pis, _storage, new FakeAudit(), new FakeCurrentUser { AdminProfileId = 9 },
            new Form16Settings { MaxPdfSizeBytes = 1024 * 1024 }, NullLogger<Form16AdminService>.Instance);
        Form16MobileService mobile = new(_user, _profiles, _repo, _storage, _audit, NullLogger<Form16MobileService>.Instance);
        _controller = new Form16Controller(_admin, mobile) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private static Form16UploadFile Pdf(string marker)
    {
        byte[] bytes = Encoding.ASCII.GetBytes("%PDF-1.4\n" + marker);
        return new Form16UploadFile(new MemoryStream(bytes), bytes.Length);
    }

    private async Task Seed(string nsg, string year, string? a = null, string? b = null)
    {
        Result<Form16SaveOutcome> result = await _admin.UpsertAsync(
            new Form16UploadRequest(nsg, year, a == null ? null : Pdf(a), b == null ? null : Pdf(b)), CancellationToken.None);
        Assert.True(result.IsSuccess, result.ErrorMessage);
    }

    private static ProblemDetails Problem(IActionResult result, int status)
    {
        ObjectResult o = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(status, o.StatusCode);
        return Assert.IsType<ProblemDetails>(o.Value);
    }

    private static ApiResponse<T> Body<T>(IActionResult result)
    {
        ApiResponse<T> body = Assert.IsType<ApiResponse<T>>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.True(body.Success);
        return body;
    }

    private static string Text(IActionResult result)
    {
        FileStreamResult file = Assert.IsType<FileStreamResult>(result);
        using StreamReader reader = new(file.FileStream, Encoding.ASCII);
        return reader.ReadToEnd();
    }

    private Task<IActionResult> PartA(string year = Year) => _controller.DownloadPartA(year);
    private Task<IActionResult> PartB(string year = Year) => _controller.DownloadPartB(year);

    // ── GET available ──

    [Fact]
    public async Task GetMine_ListsOnlyMyYears_NewestFirst_WithAvailability()
    {
        await Seed(Alice, "2025-26", b: "B");
        await Seed(Alice, Year, a: "A", b: "B");
        await Seed(Bob, "2024-25", a: "bob");

        IReadOnlyList<Form16MobileDto> mine = Body<IReadOnlyList<Form16MobileDto>>(await _controller.GetMine()).Data!;

        Assert.Equal(new[] { Year, "2025-26" }, mine.Select(m => m.AssessmentYear));
        Assert.True(mine[0].PartAAvailable && mine[0].PartBAvailable);
        Assert.False(mine[1].PartAAvailable);
        Assert.True(mine[1].PartBAvailable);
        Assert.Null(mine[1].PartADownloadUrl);
        Assert.Equal("api/v1/mobile/form16/2026-27/part-a", mine[0].PartADownloadUrl);
        Assert.Equal(TimeSpan.FromHours(5.5), mine[0].PartAUploadedAt!.Value.Offset);
        Assert.Null(mine[1].PartAUploadedAt);
    }

    [Fact]
    public async Task GetMine_ExposesNoPathOrNsgNumber()
    {
        await Seed(Alice, Year, a: "A");

        string json = JsonSerializer.Serialize(Body<IReadOnlyList<Form16MobileDto>>(await _controller.GetMine()).Data);

        Assert.DoesNotContain(_root, json);
        Assert.DoesNotContain("FilePath", json);
        Assert.DoesNotContain("part-a.pdf", json);
        Assert.DoesNotContain(Alice, json);
    }

    [Fact]
    public async Task GetMine_WhenThereIsNone_IsA200WithAMessage()
    {
        await Seed(Bob, Year, a: "bob");

        ApiResponse<IReadOnlyList<Form16MobileDto>> body = Body<IReadOnlyList<Form16MobileDto>>(await _controller.GetMine());

        Assert.Equal("No Form 16 documents found.", body.Message);
        Assert.Empty(body.Data!);
    }

    // ── GET details ──

    [Fact]
    public async Task GetMineByYear_ReturnsAvailabilityAndTimestamps()
    {
        await Seed(Alice, Year, b: "B");

        Form16MobileDto dto = Body<Form16MobileDto>(await _controller.GetMineByYear(Year)).Data!;

        Assert.False(dto.PartAAvailable);
        Assert.True(dto.PartBAvailable);
        Assert.NotNull(dto.PartBUploadedAt);
        Assert.Null(dto.PartAUploadedAt);
    }

    [Fact]
    public async Task GetMineByYear_ForAYearIDoNotHave_Is404_EvenWhenSomeoneElseHasIt()
    {
        await Seed(Bob, Year, a: "bob", b: "bob");

        Assert.Equal(Form16MobileService.NotAvailableMessage, Problem(await _controller.GetMineByYear(Year), 404).Detail);
        Assert.Equal(Form16MobileService.NotAvailableMessage, Problem(await _controller.GetMineByYear("2020-21"), 404).Detail);
    }

    [Theory]
    [InlineData("2026")]
    [InlineData("..")]
    [InlineData("2026-30")]
    [InlineData("../../etc")]
    public async Task InvalidAssessmentYear_Is400_OnEveryEndpoint(string year)
    {
        Assert.Equal(Form16MobileService.InvalidAssessmentYearMessage, Problem(await _controller.GetMineByYear(year), 400).Detail);
        Problem(await PartA(year), 400);
        Problem(await PartB(year), 400);
    }

    // ── Streaming ──

    [Fact]
    public async Task PartA_StreamsMyPdf_WithSafeHeadersAndNoPathInTheName()
    {
        await Seed(Alice, Year, a: "alice-part-a", b: "alice-part-b");

        IActionResult result = await PartA();

        FileStreamResult file = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.Equal("Form16_2026-27_PartA.pdf", file.FileDownloadName);
        Assert.False(file.EnableRangeProcessing);
        Assert.Equal("private, no-store", _controller.Response.Headers.CacheControl.ToString());
        Assert.Equal("nosniff", _controller.Response.Headers["X-Content-Type-Options"].ToString());
        Assert.Contains("alice-part-a", Text(result));
    }

    [Fact]
    public async Task PartB_StreamsMyPartB()
    {
        await Seed(Alice, Year, a: "alice-part-a", b: "alice-part-b");

        IActionResult result = await PartB();

        Assert.Equal("Form16_2026-27_PartB.pdf", Assert.IsType<FileStreamResult>(result).FileDownloadName);
        Assert.Contains("alice-part-b", Text(result));
    }

    [Fact]
    public async Task Download_WritesAnAuditEntry_WithoutTheNsgNumberOrPath()
    {
        await Seed(Alice, Year, a: "A");

        using IDisposable _ = Assert.IsType<FileStreamResult>(await PartA()).FileStream;

        (AuditAction action, string result, int? _, string? entityId, string? data) = Assert.Single(_audit.Entries);
        Assert.Equal(AuditAction.DocumentDownloaded, action);
        Assert.NotNull(entityId);
        Assert.DoesNotContain(Alice, data);
        Assert.DoesNotContain(_root, data);
    }

    [Fact]
    public async Task Download_AfterAReplacement_ReturnsTheNewFile()
    {
        await Seed(Alice, Year, a: "old");
        await Seed(Alice, Year, a: "new");

        Assert.Contains("new", Text(await PartA()));
    }

    [Fact]
    public async Task UnavailablePart_Is404_WithAMessage()
    {
        await Seed(Alice, Year, a: "A");

        Assert.Equal("Form 16 Part B is not available for this assessment year.", Problem(await PartB(), 404).Detail);
        Assert.Equal("Form 16 Part A is not available for this assessment year.", Problem(await PartA("2020-21"), 404).Detail);
    }

    [Fact]
    public async Task RecordWhoseFileIsMissingOnDisk_Is404_WithoutRevealingThePath()
    {
        await Seed(Alice, Year, a: "A");
        File.Delete(Path.Combine(_root, "Form16", Year, Alice, "part-a.pdf"));

        ProblemDetails problem = Problem(await PartA(), 404);

        Assert.Equal(Form16MobileService.FileNotAvailableMessage, problem.Detail);
        Assert.DoesNotContain(_root, problem.Detail);
    }

    [Fact]
    public async Task RecordWithATamperedPath_Is404_AndNothingOutsideTheRootIsRead()
    {
        await Seed(Alice, Year, a: "A");
        string outside = Path.Combine(Path.GetTempPath(), "outside-" + Guid.NewGuid().ToString("N") + ".pdf");
        await File.WriteAllTextAsync(outside, "%PDF-secret");
        try
        {
            _repo.Rows.Single().PartAFilePath = "../../" + Path.GetFileName(outside);

            Assert.Equal(Form16MobileService.FileNotAvailableMessage, Problem(await PartA(), 404).Detail);
        }
        finally
        {
            File.Delete(outside);
        }
    }

    // ── Cross-employee access ──

    [Fact]
    public async Task AnotherEmployeesDocuments_AreNeverReachable()
    {
        await Seed(Bob, Year, a: "BOB-SECRET-A", b: "BOB-SECRET-B");

        // Alice (token) asks for the very year Bob has: nothing is hers.
        Problem(await PartA(), 404);
        Problem(await PartB(), 404);
        Problem(await _controller.GetMineByYear(Year), 404);
        Assert.Empty(Body<IReadOnlyList<Form16MobileDto>>(await _controller.GetMine()).Data!);

        // Bob, with his own token, gets his.
        _user.ProfileId = BobProfile;
        Assert.Contains("BOB-SECRET-A", Text(await PartA()));
        Assert.Contains("BOB-SECRET-B", Text(await PartB()));
    }

    [Fact]
    public async Task EachPersonSeesOnlyTheirOwnContent_ForTheSameYear()
    {
        await Seed(Alice, Year, a: "ALICE-A");
        await Seed(Bob, Year, a: "BOB-A");

        Assert.Contains("ALICE-A", Text(await PartA()));
        _user.ProfileId = BobProfile;
        Assert.Contains("BOB-A", Text(await PartA()));
    }

    [Fact]
    public async Task NoMobileEndpointAcceptsAnNsgNumberOrRecordId()
    {
        foreach (string name in new[] { nameof(Form16Controller.GetMine), nameof(Form16Controller.GetMineByYear), nameof(Form16Controller.DownloadPartA), nameof(Form16Controller.DownloadPartB) })
        {
            string[] parameters = typeof(Form16Controller).GetMethod(name)!.GetParameters().Select(p => p.Name!.ToLowerInvariant()).ToArray();
            Assert.DoesNotContain(parameters, p => p.Contains("nsg") || p == "id" || p.Contains("profile") || p.Contains("user"));
        }

        await Task.CompletedTask;
    }

    [Fact]
    public async Task ADifferentPersonWithNoDocuments_GetsNothing()
    {
        await Seed(Alice, Year, a: "A");
        _user.ProfileId = DaveProfile;

        Assert.Equal("No Form 16 documents found.", Body<IReadOnlyList<Form16MobileDto>>(await _controller.GetMine()).Message);
        Problem(await PartA(), 404);
    }

    // ── Identity ──

    [Fact]
    public async Task UnresolvableIdentity_Is401_OnEveryEndpoint()
    {
        await Seed(Alice, Year, a: "A");

        foreach (long? profileId in new long?[] { null, 999, GoneProfile })
        {
            _user.ProfileId = profileId;
            Problem(await _controller.GetMine(), 401);
            Problem(await _controller.GetMineByYear(Year), 401);
            Problem(await PartA(), 401);
            Problem(await PartB(), 401);
        }
    }

    [Theory]
    [InlineData(nameof(Form16Controller.GetMine))]
    [InlineData(nameof(Form16Controller.GetMineByYear))]
    [InlineData(nameof(Form16Controller.DownloadPartA))]
    [InlineData(nameof(Form16Controller.DownloadPartB))]
    public void EveryMobileAction_RequiresTheMobileProfilePolicy(string action)
    {
        MethodInfo method = typeof(Form16Controller).GetMethod(action)!;
        AuthorizeAttribute authorize = Assert.Single(method.GetCustomAttributes<AuthorizeAttribute>());
        Assert.Equal(DomainConstants.Policies.RequireMobileProfile, authorize.Policy);
        Assert.Empty(method.GetCustomAttributes<AllowAnonymousAttribute>());
    }

    [Fact]
    public void MobileRoutes_AreUnderApiV1MobileForm16()
    {
        string Template(string name) => typeof(Form16Controller).GetMethod(name)!.GetCustomAttributes<HttpGetAttribute>().Single().Template!;

        Assert.Equal("api/v1/mobile/form16", Template(nameof(Form16Controller.GetMine)));
        Assert.Equal("api/v1/mobile/form16/{assessmentYear}", Template(nameof(Form16Controller.GetMineByYear)));
        Assert.Equal("api/v1/mobile/form16/{assessmentYear}/part-a", Template(nameof(Form16Controller.DownloadPartA)));
        Assert.Equal("api/v1/mobile/form16/{assessmentYear}/part-b", Template(nameof(Form16Controller.DownloadPartB)));
    }
}
