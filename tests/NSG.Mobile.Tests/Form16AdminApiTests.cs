using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using NSG.Mobile.API.Controllers;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Form16;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Infrastructure.Services;

namespace NSG.Mobile.Tests;

// Phase 2 of the Form 16 module: the four admin endpoints through Form16Controller over the real service and the real storage in a temporary
// directory. The database is replaced by an in-memory fake that follows the contract of Form16Repository (unique NSG number + year).
// NOT covered here: the SQL of Form16Repository and the JWT role check (a host test needs the NSG_TEST_MYSQL database); the role is asserted
// through the [Authorize] policy attribute on each action.

/// <summary>In-memory fake with the contract of <see cref="IForm16Repository"/>. <see cref="FailNextSave"/> simulates a database outage.</summary>
internal sealed class Form16FakeRepository : IForm16Repository
{
    private readonly FakePisRepository _pis;
    private long _nextId = 1;

    public Form16FakeRepository(FakePisRepository pis) => _pis = pis;

    public List<PersonnelForm16> Rows { get; } = new();
    public bool FailNextSave { get; set; }

    private static bool Same(PersonnelForm16 f, string nsg, string year)
        => string.Equals(f.NsgNumber, nsg, StringComparison.OrdinalIgnoreCase) && f.AssessmentYear == year;

    private Form16Row ToRow(PersonnelForm16 f) => new(f, _pis.Rows.FirstOrDefault(p => string.Equals(p.NsgNumber, f.NsgNumber, StringComparison.OrdinalIgnoreCase))?.FullName);

    public Task<PersonnelForm16?> GetByKeyForUpdateAsync(string nsgNumber, string assessmentYear, CancellationToken cancellationToken)
        => Task.FromResult(Rows.FirstOrDefault(f => Same(f, nsgNumber, assessmentYear)));

    public Task<PersonnelForm16?> GetByKeyAsync(string nsgNumber, string assessmentYear, CancellationToken cancellationToken)
        => Task.FromResult(Rows.FirstOrDefault(f => Same(f, nsgNumber, assessmentYear)));

    public Task<Form16Row?> GetRowByIdAsync(long id, CancellationToken cancellationToken)
        => Task.FromResult(Rows.Where(f => f.Id == id).Select(ToRow).FirstOrDefault());

    public Task<(IReadOnlyList<Form16Row> Items, int TotalCount)> GetPagedAsync(string? nsgNumber, string? assessmentYear, int pageNumber, int pageSize, CancellationToken cancellationToken)
    {
        List<PersonnelForm16> all = Rows
            .Where(f => nsgNumber == null || string.Equals(f.NsgNumber, nsgNumber, StringComparison.OrdinalIgnoreCase))
            .Where(f => assessmentYear == null || f.AssessmentYear == assessmentYear)
            .OrderByDescending(f => f.AssessmentYear).ThenBy(f => f.NsgNumber).ThenBy(f => f.Id).ToList();
        return Task.FromResult(((IReadOnlyList<Form16Row>)all.Skip((pageNumber - 1) * pageSize).Take(pageSize).Select(ToRow).ToList(), all.Count));
    }

    public Task<IReadOnlyList<PersonnelForm16>> ListByNsgNumberAsync(string nsgNumber, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<PersonnelForm16>>(Rows.Where(f => string.Equals(f.NsgNumber, nsgNumber, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f.AssessmentYear).ToList());

    public Task<Form16InsertOutcome> AddAsync(PersonnelForm16 entity, CancellationToken cancellationToken)
    {
        if (FailNextSave)
        {
            FailNextSave = false;
            throw new InvalidOperationException("simulated database outage");
        }

        if (Rows.Any(f => Same(f, entity.NsgNumber, entity.AssessmentYear))) return Task.FromResult(Form16InsertOutcome.Duplicate);
        entity.Id = _nextId++;
        Rows.Add(entity);
        return Task.FromResult(Form16InsertOutcome.Inserted);
    }

    public Task UpdateAsync(PersonnelForm16 entity, CancellationToken cancellationToken)
    {
        if (FailNextSave)
        {
            FailNextSave = false;
            throw new InvalidOperationException("simulated database outage");
        }

        return Task.CompletedTask;
    }
}

public class Form16AdminApiTests : IDisposable
{
    private const long Admin = 11;
    private const string Nsg = "NSG-1001";
    private const string Year = "2026-27";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "nsg-form16-api-" + Guid.NewGuid().ToString("N"));
    private readonly FakePisRepository _pis = new();
    private readonly Form16FakeRepository _repo;
    private readonly FakeAudit _audit = new();
    private readonly FakeCurrentUser _user = new() { AdminProfileId = Admin };
    private readonly Form16Controller _controller;

    public Form16AdminApiTests()
    {
        _pis.Rows.Add(new UserPis { Id = 1, NsgNumber = Nsg, FullName = "Test Person" });
        _pis.Rows.Add(new UserPis { Id = 2, NsgNumber = "NSG-2002", FullName = "Other Person" });
        _repo = new Form16FakeRepository(_pis);
        Form16StorageService storage = Form16StorageTests.Create(_root);
        Form16AdminService admin = new(_repo, _pis, storage, _audit, _user, new Form16Settings { MaxPdfSizeBytes = 1024 * 1024 }, NullLogger<Form16AdminService>.Instance);
        _controller = new Form16Controller(admin, null!) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    internal static IFormFile File(string field, string marker, bool pdf = true)
    {
        byte[] bytes = Encoding.ASCII.GetBytes((pdf ? "%PDF-1.4\n" : "NOTPDF\n") + marker);
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, field, field + ".pdf");
    }

    private static Form16UploadForm Form(string? nsg = Nsg, string? year = Year, IFormFile? a = null, IFormFile? b = null)
        => new() { NsgNumber = nsg, AssessmentYear = year, PartA = a, PartB = b };

    private string Abs(string relative) => Path.Combine(_root, "Form16", relative.Replace('/', Path.DirectorySeparatorChar));
    private string Stored(string nsg, string part) => System.IO.File.ReadAllText(Abs($"{Year}/{nsg}/{part}.pdf"));
    private string[] AllFiles() => Directory.Exists(_root) ? Directory.GetFiles(_root, "*", SearchOption.AllDirectories) : Array.Empty<string>();

    private static T Data<T>(IActionResult result, int status = 200)
    {
        ObjectResult o = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(status, o.StatusCode ?? 200);
        ApiResponse<T> body = Assert.IsType<ApiResponse<T>>(o.Value);
        Assert.True(body.Success);
        return body.Data!;
    }

    private static ProblemDetails Problem(IActionResult result, int status)
    {
        ObjectResult o = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(status, o.StatusCode);
        return Assert.IsType<ProblemDetails>(o.Value);
    }

    private Task<IActionResult> Post(Form16UploadForm f) => _controller.Upload(f, CancellationToken.None);
    private Task<IActionResult> Put(Form16UploadForm f) => _controller.Update(f, CancellationToken.None);

    // ── POST ──

    [Fact]
    public async Task Post_PartAOnly_CreatesTheRecordWithPartAOnly()
    {
        Form16AdminDto dto = Data<Form16AdminDto>(await Post(Form(a: File("partA", "A1"))), 201);

        Assert.True(dto.PartAAvailable);
        Assert.False(dto.PartBAvailable);
        Assert.NotNull(dto.PartAUploadedAt);
        Assert.Null(dto.PartBUploadedAt);
        Assert.Equal((Nsg, Year, "Test Person"), (dto.NsgNumber, dto.AssessmentYear, dto.EmployeeName));
        Assert.Contains("A1", Stored(Nsg, "part-a"));
        Assert.Single(AllFiles());
        Assert.Equal(Admin, _repo.Rows.Single().UploadedBy);
        Assert.Single(_audit.Entries);
    }

    [Fact]
    public async Task Post_PartBOnly_CreatesTheRecordWithPartBOnly()
    {
        Form16AdminDto dto = Data<Form16AdminDto>(await Post(Form(b: File("partB", "B1"))), 201);

        Assert.False(dto.PartAAvailable);
        Assert.True(dto.PartBAvailable);
        Assert.Contains("B1", Stored(Nsg, "part-b"));
        Assert.Single(AllFiles());
    }

    [Fact]
    public async Task Post_Both_StoresBothParts()
    {
        Form16AdminDto dto = Data<Form16AdminDto>(await Post(Form(a: File("partA", "A1"), b: File("partB", "B1"))), 201);

        Assert.True(dto.PartAAvailable && dto.PartBAvailable);
        Assert.Equal(2, AllFiles().Length);
        Assert.Equal(dto.PartAUploadedAt, dto.PartBUploadedAt);
    }

    [Fact]
    public async Task Post_ExistingRecord_UpdatesSuppliedPart_AndPreservesTheOther()
    {
        await Post(Form(a: File("partA", "A-old")));
        await Post(Form(b: File("partB", "B1")));

        ObjectResult o = Assert.IsAssignableFrom<ObjectResult>(await Post(Form(a: File("partA", "A-new"))));
        Assert.Equal(200, o.StatusCode);
        Form16AdminDto dto = ((ApiResponse<Form16AdminDto>)o.Value!).Data!;

        Assert.Equal(Form16AdminService.UpdatedMessage, ((ApiResponse<Form16AdminDto>)o.Value!).Message);
        Assert.Single(_repo.Rows);
        Assert.Contains("A-new", Stored(Nsg, "part-a"));
        Assert.Contains("B1", Stored(Nsg, "part-b"));
        Assert.True(dto.PartAAvailable && dto.PartBAvailable);
        Assert.Equal(2, AllFiles().Length);   // no staging or backup leftovers
    }

    [Fact]
    public async Task Post_Created_CarriesTheCreatedMessage()
    {
        ObjectResult o = Assert.IsAssignableFrom<ObjectResult>(await Post(Form(a: File("partA", "A"))));
        Assert.Equal(201, o.StatusCode);
        Assert.Equal(Form16AdminService.CreatedMessage, ((ApiResponse<Form16AdminDto>)o.Value!).Message);
    }

    [Fact]
    public async Task Post_WithoutAnyFile_IsRejectedWithAMessage()
    {
        Assert.Equal(Form16AdminService.NoDocumentMessage, Problem(await Post(Form()), 400).Detail);

        IFormFile empty = new FormFile(new MemoryStream(), 0, 0, "partA", "partA.pdf");
        Assert.Equal(Form16AdminService.NoDocumentMessage, Problem(await Post(Form(a: empty)), 400).Detail);

        Assert.Empty(_repo.Rows);
        Assert.Empty(AllFiles());
    }

    [Fact]
    public async Task Post_UnknownNsgNumber_IsA404_AndStoresNothing()
    {
        Assert.Equal(Form16AdminService.PersonnelNotFoundMessage, Problem(await Post(Form(nsg: "NSG-9999", a: File("partA", "A"))), 404).Detail);

        Assert.Empty(_repo.Rows);
        Assert.Empty(AllFiles());
    }

    [Fact]
    public async Task Post_NsgNumberCase_IsNormalisedToThePisSpelling()
    {
        Data<Form16AdminDto>(await Post(Form(nsg: "nsg-1001", a: File("partA", "A"))), 201);

        Assert.Equal(Nsg, _repo.Rows.Single().NsgNumber);
        Assert.True(System.IO.File.Exists(Abs($"{Year}/{Nsg}/part-a.pdf")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2026")]
    [InlineData("2026-28")]
    [InlineData("26-27")]
    [InlineData("../2026")]
    public async Task Post_InvalidAssessmentYear_IsRejected(string? year)
    {
        Assert.Equal(Form16AdminService.InvalidAssessmentYearMessage, Problem(await Post(Form(year: year, a: File("partA", "A"))), 400).Detail);
        Assert.Empty(_repo.Rows);
        Assert.Empty(AllFiles());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a\nb")]
    [InlineData("0123456789012345678901234567890")]
    public async Task Post_InvalidNsgNumber_IsRejected_BeforeAnythingIsWritten(string? nsg)
    {
        Assert.Equal(Form16AdminService.InvalidNsgNumberMessage, Problem(await Post(Form(nsg: nsg, a: File("partA", "A"))), 400).Detail);
        Assert.Empty(AllFiles());
    }

    [Theory]
    [InlineData("../../etc")]
    [InlineData("a/b")]
    [InlineData("..")]
    public async Task Post_TraversalLookingNsgNumber_NotInPis_Is404_AndWritesNothing(string nsg)
    {
        Assert.Equal(Form16AdminService.PersonnelNotFoundMessage, Problem(await Post(Form(nsg: nsg, a: File("partA", "A"))), 404).Detail);
        Assert.Empty(AllFiles());
    }

    [Theory]
    [InlineData("../../etc")]
    [InlineData("A/B.1")]
    [InlineData("..")]
    [InlineData("NSG 7")]
    public async Task Post_PisNsgNumberWithUnusualCharacters_IsAccepted_AndRoundTripsInsideTheRoot(string nsg)
    {
        _pis.Rows.Add(new UserPis { Id = 50, NsgNumber = nsg, FullName = "Unusual" });

        Form16AdminDto dto = Data<Form16AdminDto>(await Post(Form(nsg: nsg, b: File("partB", "B"))), 201);

        Assert.Equal(nsg, dto.NsgNumber);
        string stored = Assert.Single(AllFiles());
        Assert.StartsWith(Path.GetFullPath(_root) + Path.DirectorySeparatorChar, Path.GetFullPath(stored));
        Assert.Equal(Year, Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(stored))!));   // exactly year/folder/file
        Assert.Equal(nsg, _repo.Rows.Single().NsgNumber);

        Data<Form16AdminDto>(await Put(Form(nsg: nsg, b: File("partB", "B2"))));   // PUT finds it again
        Assert.Single(AllFiles());
    }

    [Fact]
    public async Task Post_NonPdf_IsRejected_AndLeavesNoRecordOrFile()
    {
        ProblemDetails problem = Problem(await Post(Form(a: File("partA", "x", pdf: false))), 400);

        Assert.Contains("Part A", problem.Detail);
        Assert.Contains("not a PDF", problem.Detail);
        Assert.Empty(_repo.Rows);
        Assert.Empty(AllFiles());
    }

    [Fact]
    public async Task Post_ValidPartAWithInvalidPartB_IsAllOrNothing()
    {
        await Post(Form(a: File("partA", "A-old")));

        Problem(await Post(Form(a: File("partA", "A-new"), b: File("partB", "x", pdf: false))), 400);

        Assert.Contains("A-old", Stored(Nsg, "part-a"));   // the valid part was not applied either
        Assert.Single(AllFiles());
        Assert.False(_repo.Rows.Single().PartBFilePath != null);
    }

    [Fact]
    public async Task Post_OversizedPdf_IsRejected()
    {
        byte[] big = new byte[2 * 1024 * 1024];
        Encoding.ASCII.GetBytes("%PDF-1.4").CopyTo(big, 0);
        IFormFile file = new FormFile(new MemoryStream(big), 0, big.Length, "partA", "partA.pdf");

        Assert.Contains("limit", Problem(await Post(Form(a: file)), 400).Detail);
        Assert.Empty(AllFiles());
    }

    [Fact]
    public async Task Post_WhenTheDatabaseFails_ForANewRecord_LeavesNoFile()
    {
        _repo.FailNextSave = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => Post(Form(a: File("partA", "A"), b: File("partB", "B"))));

        Assert.Empty(_repo.Rows);
        Assert.Empty(AllFiles());
    }

    [Fact]
    public async Task Post_WhenTheDatabaseFails_ForAReplacement_RestoresTheOldFile()
    {
        await Post(Form(a: File("partA", "A-old")));
        _repo.FailNextSave = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => Post(Form(a: File("partA", "A-new"))));

        Assert.Contains("A-old", Stored(Nsg, "part-a"));
        Assert.Single(AllFiles());
    }

    [Fact]
    public async Task Post_WithoutAnAdminInTheToken_IsA401()
    {
        _user.AdminProfileId = null;
        Problem(await Post(Form(a: File("partA", "A"))), 401);
        Assert.Empty(AllFiles());
    }

    // ── PUT ──

    [Fact]
    public async Task Put_PartAOnly_UpdatesA_AndKeepsB()
    {
        await Post(Form(a: File("partA", "A-old"), b: File("partB", "B-keep")));

        Form16AdminDto dto = Data<Form16AdminDto>(await Put(Form(a: File("partA", "A-new"))));

        Assert.Contains("A-new", Stored(Nsg, "part-a"));
        Assert.Contains("B-keep", Stored(Nsg, "part-b"));
        Assert.True(dto.PartAAvailable && dto.PartBAvailable);
        Assert.Equal(2, AllFiles().Length);
    }

    [Fact]
    public async Task Put_PartBOnly_UpdatesB_AndKeepsA()
    {
        await Post(Form(a: File("partA", "A-keep"), b: File("partB", "B-old")));

        Data<Form16AdminDto>(await Put(Form(b: File("partB", "B-new"))));

        Assert.Contains("A-keep", Stored(Nsg, "part-a"));
        Assert.Contains("B-new", Stored(Nsg, "part-b"));
    }

    [Fact]
    public async Task Put_Both_ReplacesBoth()
    {
        await Post(Form(a: File("partA", "A-old"), b: File("partB", "B-old")));

        Data<Form16AdminDto>(await Put(Form(a: File("partA", "A-new"), b: File("partB", "B-new"))));

        Assert.Contains("A-new", Stored(Nsg, "part-a"));
        Assert.Contains("B-new", Stored(Nsg, "part-b"));
        Assert.Equal(2, AllFiles().Length);
    }

    [Fact]
    public async Task Put_CanAddTheMissingPartToAnExistingRecord()
    {
        await Post(Form(a: File("partA", "A")));

        Form16AdminDto dto = Data<Form16AdminDto>(await Put(Form(b: File("partB", "B"))));

        Assert.True(dto.PartAAvailable && dto.PartBAvailable);
        Assert.NotNull(dto.PartBUploadedAt);
    }

    [Fact]
    public async Task Put_NonexistentRecord_Is404_AndNeverCreatesAnything()
    {
        Assert.Equal(Form16AdminService.NotFoundMessage, Problem(await Put(Form(a: File("partA", "A"))), 404).Detail);
        Assert.Equal(Form16AdminService.NotFoundMessage, Problem(await Put(Form(year: "2025-26", a: File("partA", "A"))), 404).Detail);

        Assert.Empty(_repo.Rows);
        Assert.Empty(AllFiles());
    }

    [Fact]
    public async Task Put_WithoutAnyFile_IsRejected()
    {
        await Post(Form(a: File("partA", "A")));
        Assert.Equal(Form16AdminService.NoDocumentMessage, Problem(await Put(Form()), 400).Detail);
    }

    [Fact]
    public async Task Put_InvalidPdf_LeavesTheExistingFileUntouched()
    {
        await Post(Form(a: File("partA", "A-old")));

        Problem(await Put(Form(a: File("partA", "x", pdf: false))), 400);

        Assert.Contains("A-old", Stored(Nsg, "part-a"));
        Assert.Single(AllFiles());
    }

    [Fact]
    public async Task Put_UpdatesTimestampsOnlyForSuppliedParts()
    {
        await Post(Form(a: File("partA", "A"), b: File("partB", "B")));
        PersonnelForm16 row = _repo.Rows.Single();
        DateTime? aBefore = row.PartAUploadedAtIst, bBefore = row.PartBUploadedAtIst;
        await Task.Delay(20);

        await Put(Form(b: File("partB", "B2")));

        Assert.Equal(aBefore, row.PartAUploadedAtIst);
        Assert.True(row.PartBUploadedAtIst > bBefore);
    }

    // ── GET ──

    [Fact]
    public async Task GetAll_FiltersByNsgNumberAndAssessmentYear_AndPages()
    {
        await Post(Form(a: File("partA", "1")));
        await Post(Form(year: "2025-26", b: File("partB", "2")));
        await Post(Form(nsg: "NSG-2002", a: File("partA", "3")));

        Assert.Equal(3, Data<PaginatedResponse<Form16AdminDto>>(await _controller.GetAll()).TotalCount);
        Assert.Equal(2, Data<PaginatedResponse<Form16AdminDto>>(await _controller.GetAll(nsgNumber: Nsg)).TotalCount);
        Assert.Equal(2, Data<PaginatedResponse<Form16AdminDto>>(await _controller.GetAll(assessmentYear: Year)).TotalCount);
        PaginatedResponse<Form16AdminDto> one = Data<PaginatedResponse<Form16AdminDto>>(await _controller.GetAll(nsgNumber: Nsg, assessmentYear: "2025-26"));
        Assert.Equal(1, one.TotalCount);
        Assert.False(one.Items.Single().PartAAvailable);

        PaginatedResponse<Form16AdminDto> page2 = Data<PaginatedResponse<Form16AdminDto>>(await _controller.GetAll(pageNumber: 2, pageSize: 2));
        Assert.Equal((3, 1, 2), (page2.TotalCount, page2.Items.Count, page2.PageNumber));
    }

    [Fact]
    public async Task GetAll_WhenNothingMatches_IsA200WithAMessage_NotAnEmptyList()
    {
        await Post(Form(a: File("partA", "1")));

        ObjectResult o = Assert.IsAssignableFrom<ObjectResult>(await _controller.GetAll(nsgNumber: "NSG-2002"));
        Assert.Equal(200, o.StatusCode ?? 200);
        Assert.Equal("No Form 16 records found.", Assert.IsType<ApiMessageResponse>(o.Value).Message);
    }

    [Theory]
    [InlineData("2026")]
    [InlineData("2026-30")]
    public async Task GetAll_InvalidYearFilter_Is400(string year)
        => Assert.Equal(Form16AdminService.InvalidAssessmentYearMessage, Problem(await _controller.GetAll(assessmentYear: year), 400).Detail);

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task GetAll_InvalidPaging_Is400(int page, int size)
        => Problem(await _controller.GetAll(pageNumber: page, pageSize: size), 400);

    [Fact]
    public async Task GetById_ReturnsEmployeeAvailabilityAndIstTimestamps_ButNoPath()
    {
        await Post(Form(a: File("partA", "A")));
        long id = _repo.Rows.Single().Id;

        Form16AdminDto dto = Data<Form16AdminDto>(await _controller.GetById(id, CancellationToken.None));

        Assert.Equal((Nsg, "Test Person", Year, true, false), (dto.NsgNumber, dto.EmployeeName, dto.AssessmentYear, dto.PartAAvailable, dto.PartBAvailable));
        Assert.Equal(TimeSpan.FromHours(5.5), dto.PartAUploadedAt!.Value.Offset);
        Assert.Equal(TimeSpan.FromHours(5.5), dto.CreatedAt.Offset);

        string json = JsonSerializer.Serialize(dto);
        Assert.DoesNotContain(".pdf", json);
        Assert.DoesNotContain(_root, json);
        Assert.DoesNotContain("FilePath", json);
    }

    [Fact]
    public async Task GetById_Unknown_Is404WithAMessage()
        => Assert.Equal(Form16AdminService.NotFoundMessage, Problem(await _controller.GetById(999, CancellationToken.None), 404).Detail);

    [Fact]
    public async Task GetEndpoints_WithoutAnAdminInTheToken_AreA401()
    {
        _user.AdminProfileId = null;
        Problem(await _controller.GetAll(), 401);
        Problem(await _controller.GetById(1, CancellationToken.None), 401);
    }

    // ── Authorization ──

    [Theory]
    [InlineData(nameof(Form16Controller.Upload))]
    [InlineData(nameof(Form16Controller.Update))]
    [InlineData(nameof(Form16Controller.GetAll))]
    [InlineData(nameof(Form16Controller.GetById))]
    public void EveryAdminAction_RequiresTheSalaryManagementAdminPolicy(string action)
    {
        MethodInfo method = typeof(Form16Controller).GetMethod(action)!;
        AuthorizeAttribute authorize = Assert.Single(method.GetCustomAttributes<AuthorizeAttribute>());
        Assert.Equal(DomainConstants.Policies.RequireSalaryManagementAdmin, authorize.Policy);
        Assert.Empty(method.GetCustomAttributes<AllowAnonymousAttribute>());
    }

    [Theory]
    [InlineData(nameof(Form16Controller.Upload), "POST")]
    [InlineData(nameof(Form16Controller.Update), "PUT")]
    [InlineData(nameof(Form16Controller.GetAll), "GET")]
    [InlineData(nameof(Form16Controller.GetById), "GET")]
    public void AdminRoutes_AreUnderApiV1AdminForm16(string action, string verb)
    {
        HttpMethodAttribute http = typeof(Form16Controller).GetMethod(action)!.GetCustomAttributes<HttpMethodAttribute>().Single();
        Assert.Equal(verb, http.HttpMethods.Single());
        Assert.StartsWith("api/v1/admin/form16", http.Template);
    }
}
