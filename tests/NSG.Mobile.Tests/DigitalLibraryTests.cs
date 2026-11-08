using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSG.Mobile.API.Controllers;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.DigitalLibrary;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Infrastructure.Services;

namespace NSG.Mobile.Tests;

// The Digital Library through DigitalLibraryAdminController / DigitalLibraryController over the real services and the real storage in a temporary
// directory (never D:\NSGStorage). The database is replaced by an in-memory fake that follows the contract of DigitalLibraryRepository.
// NOT covered here: the SQL of DigitalLibraryRepository (ordering / filters run in the fake) and the JWT role check (needs NSG_TEST_MYSQL);
// the role is asserted through the [Authorize] policy on both controllers.

/// <summary>In-memory fake with the contract of <see cref="IDigitalLibraryRepository"/>. <see cref="FailNextSave"/> simulates a database outage.</summary>
internal sealed class DigitalLibraryFakeRepository : IDigitalLibraryRepository
{
    private long _nextId = 1;
    public List<DigitalLibraryItem> Rows { get; } = new();
    public bool FailNextSave { get; set; }

    private void ThrowIfFailing()
    {
        if (!FailNextSave) return;
        FailNextSave = false;
        throw new InvalidOperationException("simulated database outage");
    }

    public Task AddAsync(DigitalLibraryItem item, CancellationToken cancellationToken)
    {
        ThrowIfFailing();
        item.Id = _nextId++;
        Rows.Add(item);
        return Task.CompletedTask;
    }

    // The real repository returns detached copies for reads; the fake does the same so a failed save does not leak edits into "the database".
    private static DigitalLibraryItem Copy(DigitalLibraryItem i) => new()
    {
        Id = i.Id, Description = i.Description, FilePath = i.FilePath, FileType = i.FileType, IsImportant = i.IsImportant, IsActive = i.IsActive,
        CreatedAtIst = i.CreatedAtIst, CreatedBy = i.CreatedBy, UpdatedAtIst = i.UpdatedAtIst, UpdatedBy = i.UpdatedBy
    };

    private DigitalLibraryItem? _tracked;
    public Task<DigitalLibraryItem?> GetByIdAsync(long id, CancellationToken cancellationToken)
        => Task.FromResult(Rows.Where(r => r.Id == id).Select(Copy).FirstOrDefault());

    public Task<DigitalLibraryItem?> GetForUpdateAsync(long id, CancellationToken cancellationToken)
    {
        DigitalLibraryItem? row = Rows.FirstOrDefault(r => r.Id == id);
        _tracked = row == null ? null : Copy(row);
        return Task.FromResult(_tracked);
    }

    public Task UpdateAsync(DigitalLibraryItem item, CancellationToken cancellationToken)
    {
        ThrowIfFailing();
        int index = Rows.FindIndex(r => r.Id == item.Id);
        Rows[index] = Copy(item);
        return Task.CompletedTask;
    }

    private static List<DigitalLibraryItem> Newest(IEnumerable<DigitalLibraryItem> q)
        => q.OrderByDescending(r => r.CreatedAtIst).ThenByDescending(r => r.Id).ToList();

    public Task<(IReadOnlyList<DigitalLibraryItem> Items, int TotalCount)> GetAdminPagedAsync(DigitalLibraryAdminFilter filter, string? fileTypeLabel, int pageNumber, int pageSize, CancellationToken cancellationToken)
    {
        List<DigitalLibraryItem> all = Newest(Rows
            .Where(r => filter.Search == null || r.Description.Contains(filter.Search, StringComparison.OrdinalIgnoreCase))
            .Where(r => filter.IsActive == null || r.IsActive == filter.IsActive)
            .Where(r => filter.IsImportant == null || r.IsImportant == filter.IsImportant)
            .Where(r => fileTypeLabel == null || r.FileType == fileTypeLabel));
        return Task.FromResult(((IReadOnlyList<DigitalLibraryItem>)all.Skip((pageNumber - 1) * pageSize).Take(pageSize).Select(Copy).ToList(), all.Count));
    }

    public Task<(IReadOnlyList<DigitalLibraryItem> Items, int TotalCount)> GetActivePagedAsync(bool? isImportant, int pageNumber, int pageSize, CancellationToken cancellationToken)
    {
        List<DigitalLibraryItem> all = Newest(Rows.Where(r => r.IsActive).Where(r => isImportant == null || r.IsImportant == isImportant));
        return Task.FromResult(((IReadOnlyList<DigitalLibraryItem>)all.Skip((pageNumber - 1) * pageSize).Take(pageSize).Select(Copy).ToList(), all.Count));
    }

    public Task<DigitalLibraryItem?> GetActiveByIdAsync(long id, CancellationToken cancellationToken)
        => Task.FromResult(Rows.Where(r => r.Id == id && r.IsActive).Select(Copy).FirstOrDefault());
}

public class DigitalLibraryTests : IDisposable
{
    private const long Admin = 21;
    private const long MaxBytes = 64 * 1024;

    private static readonly byte[] PdfHead = "%PDF-1.4\n"u8.ToArray();
    private static readonly byte[] JpegHead = { 0xFF, 0xD8, 0xFF, 0xE0 };
    private static readonly byte[] PngHead = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    private readonly string _root = Path.Combine(Path.GetTempPath(), "nsg-digital-library-" + Guid.NewGuid().ToString("N"));
    private readonly DigitalLibraryFakeRepository _repo = new();
    private readonly FakeAudit _audit = new();
    private readonly FakeCurrentUser _user = new() { AdminProfileId = Admin };
    private readonly DigitalLibraryAdminController _admin;
    private readonly DigitalLibraryController _mobile;

    public DigitalLibraryTests()
    {
        DigitalLibraryStorageService storage = new(TestStorage.Central(_root), NullLogger<DigitalLibraryStorageService>.Instance);
        DigitalLibrarySettings settings = new() { MaxFileSizeBytes = MaxBytes };
        DigitalLibraryAdminService admin = new(_repo, storage, _audit, _user, settings, NullLogger<DigitalLibraryAdminService>.Instance);
        DigitalLibraryMobileService mobile = new(_repo, storage, NullLogger<DigitalLibraryMobileService>.Instance);
        _admin = new DigitalLibraryAdminController(admin) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        _mobile = new DigitalLibraryController(mobile) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    // ── helpers ──

    private static IFormFile Upload(string fileName, byte[] head, string marker = "x", int padding = 0)
    {
        byte[] bytes = head.Concat(Encoding.ASCII.GetBytes(marker)).Concat(new byte[padding]).ToArray();
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", fileName);
    }

    private static IFormFile Pdf(string marker = "pdf") => Upload("circular.pdf", PdfHead, marker);

    private static DigitalLibraryCreateForm CreateForm(string? description = "Circular", bool? important = null, IFormFile? file = null)
        => new() { Description = description, IsImportant = important, File = file };

    private string DiskFolder => Path.Combine(_root, "DigitalLibrary");
    private string Abs(string relative) => Path.Combine(DiskFolder, relative.Replace('/', Path.DirectorySeparatorChar));
    private string[] LibraryFiles() => Directory.GetFiles(DiskFolder, "*", SearchOption.AllDirectories);
    private string[] AllFiles() => Directory.GetFiles(_root, "*", SearchOption.AllDirectories);

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

    private async Task<DigitalLibraryAdminDto> CreateOk(string description = "Circular", bool important = false, IFormFile? file = null)
        => Data<DigitalLibraryAdminDto>(await _admin.Create(CreateForm(description, important, file ?? Pdf()), CancellationToken.None), 201);

    private DigitalLibraryItem Seed(string description, bool active, bool important, DateTime createdAt, string fileType = "PDF")
    {
        DigitalLibraryItem item = new()
        {
            Description = description, FilePath = "2026/11/seed.pdf", FileType = fileType, IsActive = active, IsImportant = important, CreatedAtIst = createdAt, CreatedBy = Admin
        };
        _repo.AddAsync(item, CancellationToken.None).GetAwaiter().GetResult();
        return item;
    }

    // ── authorization ──

    [Fact]
    public void AdminController_RequiresTheMediaCellAdminPolicy_AndMobileTheMobileProfilePolicy()
    {
        Assert.Equal(DomainConstants.Policies.RequireMediaCellAdmin, typeof(DigitalLibraryAdminController).GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.Equal(DomainConstants.Policies.RequireMobileProfile, typeof(DigitalLibraryController).GetCustomAttribute<AuthorizeAttribute>()!.Policy);

        foreach (Type type in new[] { typeof(DigitalLibraryAdminController), typeof(DigitalLibraryController) })
        {
            Assert.Null(type.GetCustomAttribute<AllowAnonymousAttribute>());
            foreach (MethodInfo m in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                Assert.Null(m.GetCustomAttribute<AllowAnonymousAttribute>());
        }
    }

    [Fact]
    public void Routes_AreTheDocumentedOnes()
    {
        Assert.Equal("api/v1/admin/digital-library", typeof(DigitalLibraryAdminController).GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Equal("api/v1/digital-library", typeof(DigitalLibraryController).GetCustomAttribute<RouteAttribute>()!.Template);
    }

    [Fact]
    public async Task Admin_WithoutAnAdminIdentity_IsUnauthorized()
    {
        _user.AdminProfileId = null;
        Problem(await _admin.GetAll(cancellationToken: CancellationToken.None), 401);
        Problem(await _admin.GetById(1, CancellationToken.None), 401);
        Problem(await _admin.Create(CreateForm(file: Pdf()), CancellationToken.None), 401);
        Problem(await _admin.Delete(1, CancellationToken.None), 401);
        Assert.Empty(AllFiles());
    }

    // ── POST ──

    [Fact]
    public async Task Post_CreatesTheRecordAndSavesTheFileUnderTheCentralRoot()
    {
        DigitalLibraryAdminDto dto = await CreateOk("Personnel circular", important: true);

        Assert.True(dto.IsActive);
        Assert.True(dto.IsImportant);
        Assert.Equal("PDF", dto.FileType);
        Assert.Equal(Admin, dto.CreatedBy);
        Assert.Null(dto.UpdatedAt);

        DigitalLibraryItem row = _repo.Rows.Single();
        Assert.Matches(@"^\d{4}/\d{2}/[0-9a-f-]{36}\.pdf$", row.FilePath);   // relative, GUID name, yyyy/MM
        Assert.False(Path.IsPathRooted(row.FilePath));
        Assert.Equal(Abs(row.FilePath), LibraryFiles().Single());
        Assert.StartsWith(Path.GetFullPath(_root), Path.GetFullPath(LibraryFiles().Single()));
        Assert.StartsWith("%PDF-", System.IO.File.ReadAllText(LibraryFiles().Single()));
        Assert.Single(_audit.Entries);
        Assert.DoesNotContain("_root", row.FilePath);
    }

    [Fact]
    public async Task Post_DefaultsIsImportantToFalse()
    {
        DigitalLibraryAdminDto dto = Data<DigitalLibraryAdminDto>(await _admin.Create(CreateForm(important: null, file: Pdf()), CancellationToken.None), 201);
        Assert.False(dto.IsImportant);
    }

    [Theory]
    [InlineData("a.jpg", "JPG")]
    [InlineData("a.jpeg", "JPEG")]
    [InlineData("A.PNG", "PNG")]
    public async Task Post_AcceptsImages_AndKeepsTheValidatedExtension(string name, string expectedType)
    {
        byte[] head = expectedType == "PNG" ? PngHead : JpegHead;
        DigitalLibraryAdminDto dto = await CreateOk(file: Upload(name, head));

        Assert.Equal(expectedType, dto.FileType);
        Assert.EndsWith("." + expectedType.ToLowerInvariant(), _repo.Rows.Single().FilePath);
    }

    [Theory]
    [InlineData("a.doc"), InlineData("a.docx"), InlineData("a.xls"), InlineData("a.xlsx"), InlineData("a.zip"), InlineData("a.mp4"), InlineData("a.mp3"), InlineData("a.exe"), InlineData("noextension"), InlineData("a.pdf.exe")]
    public async Task Post_RejectsUnsupportedExtensions_WithoutARecordOrFile(string name)
    {
        Problem(await _admin.Create(CreateForm(file: Upload(name, PdfHead)), CancellationToken.None), 400);

        Assert.Empty(_repo.Rows);
        Assert.Empty(AllFiles());
    }

    [Theory]
    [InlineData("a.pdf", "png")]    // PDF name, PNG content
    [InlineData("a.png", "pdf")]
    [InlineData("a.jpg", "pdf")]
    [InlineData("a.pdf", "text")]
    public async Task Post_RejectsAContentSignatureThatDoesNotMatchTheExtension(string name, string actual)
    {
        byte[] head = actual switch { "png" => PngHead, "pdf" => PdfHead, _ => "just text"u8.ToArray() };
        ProblemDetails problem = Problem(await _admin.Create(CreateForm(file: Upload(name, head)), CancellationToken.None), 400);

        Assert.False(string.IsNullOrWhiteSpace(problem.Detail));
        Assert.Empty(_repo.Rows);
        Assert.Empty(AllFiles());
    }

    [Fact]
    public async Task Post_RejectsAnEmptyFile()
    {
        IFormFile empty = new FormFile(new MemoryStream(), 0, 0, "file", "empty.pdf");
        Problem(await _admin.Create(CreateForm(file: empty), CancellationToken.None), 400);

        Assert.Empty(_repo.Rows);
        Assert.Empty(AllFiles());
    }

    [Fact]
    public async Task Post_RejectsAFileOverTheLimit_AndLeavesNoPartialFile()
    {
        ProblemDetails problem = Problem(await _admin.Create(CreateForm(file: Upload("big.pdf", PdfHead, padding: (int)MaxBytes)), CancellationToken.None), 400);

        Assert.Contains("larger", problem.Detail);
        Assert.Empty(_repo.Rows);
        Assert.Empty(AllFiles());
    }

    [Fact]
    public async Task Post_AFileExactlyAtTheLimitIsAccepted()
    {
        byte[] bytes = PdfHead.Concat(new byte[MaxBytes - PdfHead.Length]).ToArray();
        await CreateOk(file: new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "edge.pdf"));
        Assert.Single(_repo.Rows);
    }

    [Theory]
    [InlineData(null), InlineData(""), InlineData("   ")]
    public async Task Post_RequiresADescription(string? description)
    {
        Problem(await _admin.Create(CreateForm(description, file: Pdf()), CancellationToken.None), 400);
        Assert.Empty(_repo.Rows);
        Assert.Empty(AllFiles());
    }

    [Fact]
    public async Task Post_RejectsADescriptionOver1000Characters_AndAcceptsExactly1000()
    {
        Problem(await _admin.Create(CreateForm(new string('d', 1001), file: Pdf()), CancellationToken.None), 400);
        Assert.Empty(_repo.Rows);

        await CreateOk(new string('d', 1000));
        Assert.Single(_repo.Rows);
    }

    [Fact]
    public async Task Post_RequiresAFile()
    {
        Problem(await _admin.Create(CreateForm(file: null), CancellationToken.None), 400);
        Assert.Empty(_repo.Rows);
    }

    [Fact]
    public async Task Post_WhenTheInsertFails_RemovesTheUploadedFile()
    {
        _repo.FailNextSave = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => _admin.Create(CreateForm(file: Pdf()), CancellationToken.None));

        Assert.Empty(_repo.Rows);
        Assert.Empty(AllFiles());
    }

    // ── PUT ──

    [Fact]
    public async Task Put_WithoutAFile_UpdatesMetadataAndKeepsTheFile()
    {
        DigitalLibraryAdminDto created = await CreateOk("Old", important: false);
        string path = _repo.Rows.Single().FilePath;

        DigitalLibraryAdminDto dto = Data<DigitalLibraryAdminDto>(await _admin.Update(created.Id, new DigitalLibraryUpdateForm { Description = "New", IsImportant = true }, CancellationToken.None));

        Assert.Equal(("New", true), (dto.Description, dto.IsImportant));
        Assert.NotNull(dto.UpdatedAt);
        Assert.Equal(Admin, dto.UpdatedBy);
        Assert.Equal(created.CreatedAt, dto.CreatedAt);
        Assert.Equal(created.CreatedBy, dto.CreatedBy);
        Assert.Equal(path, _repo.Rows.Single().FilePath);
        Assert.Single(LibraryFiles());
    }

    [Fact]
    public async Task Put_WithAValidFile_ReplacesTheFileAndDropsTheOldOne()
    {
        DigitalLibraryAdminDto created = await CreateOk(file: Pdf("first"));
        string oldPath = _repo.Rows.Single().FilePath;

        DigitalLibraryAdminDto dto = Data<DigitalLibraryAdminDto>(await _admin.Update(created.Id,
            new DigitalLibraryUpdateForm { Description = "Image now", IsImportant = false, File = Upload("scan.png", PngHead) }, CancellationToken.None));

        string newPath = _repo.Rows.Single().FilePath;
        Assert.Equal("PNG", dto.FileType);
        Assert.NotEqual(oldPath, newPath);
        Assert.EndsWith(".png", newPath);
        Assert.Equal(Abs(newPath), LibraryFiles().Single());   // old file gone, new file present, nothing left staged
        Assert.DoesNotContain(AllFiles(), f => f.Contains("_incoming"));
    }

    [Fact]
    public async Task Put_WhenTheDatabaseUpdateFails_PreservesTheOldFileAndRemovesTheNewOne()
    {
        DigitalLibraryAdminDto created = await CreateOk(file: Pdf("keep-me"));
        string oldPath = _repo.Rows.Single().FilePath;
        _repo.FailNextSave = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => _admin.Update(created.Id,
            new DigitalLibraryUpdateForm { Description = "x", IsImportant = true, File = Upload("new.pdf", PdfHead, "replacement") }, CancellationToken.None));

        Assert.Equal(oldPath, _repo.Rows.Single().FilePath);
        Assert.Equal("Circular", _repo.Rows.Single().Description);
        Assert.Equal(Abs(oldPath), LibraryFiles().Single());
        Assert.Contains("keep-me", System.IO.File.ReadAllText(Abs(oldPath)));
    }

    [Fact]
    public async Task Put_WithAnInvalidReplacement_ChangesNothing()
    {
        DigitalLibraryAdminDto created = await CreateOk();
        string oldPath = _repo.Rows.Single().FilePath;

        Problem(await _admin.Update(created.Id, new DigitalLibraryUpdateForm { Description = "x", IsImportant = true, File = Upload("evil.exe", PdfHead) }, CancellationToken.None), 400);
        Problem(await _admin.Update(created.Id, new DigitalLibraryUpdateForm { Description = "x", IsImportant = true, File = Upload("fake.pdf", "nope"u8.ToArray()) }, CancellationToken.None), 400);

        Assert.Equal(("Circular", oldPath), (_repo.Rows.Single().Description, _repo.Rows.Single().FilePath));
        Assert.Equal(Abs(oldPath), LibraryFiles().Single());
    }

    [Fact]
    public async Task Put_ValidatesDescriptionAndRequiresIsImportant()
    {
        DigitalLibraryAdminDto created = await CreateOk();

        Problem(await _admin.Update(created.Id, new DigitalLibraryUpdateForm { Description = "", IsImportant = true }, CancellationToken.None), 400);
        Problem(await _admin.Update(created.Id, new DigitalLibraryUpdateForm { Description = new string('x', 1001), IsImportant = true }, CancellationToken.None), 400);
        Problem(await _admin.Update(created.Id, new DigitalLibraryUpdateForm { Description = "ok", IsImportant = null }, CancellationToken.None), 400);
    }

    [Fact]
    public async Task Put_UnknownId_Is404()
        => Problem(await _admin.Update(999, new DigitalLibraryUpdateForm { Description = "x", IsImportant = true }, CancellationToken.None), 404);

    [Fact]
    public async Task Put_DoesNotReactivateAnInactiveRecord()
    {
        DigitalLibraryAdminDto created = await CreateOk();
        await _admin.Delete(created.Id, CancellationToken.None);

        DigitalLibraryAdminDto dto = Data<DigitalLibraryAdminDto>(await _admin.Update(created.Id, new DigitalLibraryUpdateForm { Description = "edited", IsImportant = true }, CancellationToken.None));

        Assert.False(dto.IsActive);
        Assert.False(_repo.Rows.Single().IsActive);
        Assert.Equal("edited", _repo.Rows.Single().Description);
    }

    // ── DELETE (soft) ──

    [Fact]
    public async Task Delete_OnlyDeactivates_KeepsTheRowAndThePhysicalFile()
    {
        DigitalLibraryAdminDto created = await CreateOk();
        string path = _repo.Rows.Single().FilePath;

        ObjectResult ok = Assert.IsAssignableFrom<ObjectResult>(await _admin.Delete(created.Id, CancellationToken.None));
        ApiResult<DigitalLibraryAdminDto> body = Assert.IsType<ApiResult<DigitalLibraryAdminDto>>(ok.Value);

        Assert.Equal(DigitalLibraryRules.DeactivatedMessage, body.Message);
        DigitalLibraryItem row = _repo.Rows.Single();   // still there
        Assert.False(row.IsActive);
        Assert.NotNull(row.UpdatedAtIst);
        Assert.Equal(Admin, row.UpdatedBy);
        Assert.Equal(path, row.FilePath);
        Assert.Equal(Abs(path), LibraryFiles().Single());   // file preserved
    }

    [Fact]
    public async Task Delete_Repeated_AnswersAlreadyInactive_WithoutChangingTheRecord()
    {
        DigitalLibraryAdminDto created = await CreateOk();
        await _admin.Delete(created.Id, CancellationToken.None);
        DateTime? firstUpdate = _repo.Rows.Single().UpdatedAtIst;

        ObjectResult again = Assert.IsAssignableFrom<ObjectResult>(await _admin.Delete(created.Id, CancellationToken.None));

        Assert.Equal(200, again.StatusCode ?? 200);
        Assert.Equal(DigitalLibraryRules.AlreadyInactiveMessage, Assert.IsType<ApiResult<DigitalLibraryAdminDto>>(again.Value).Message);
        Assert.Equal(firstUpdate, _repo.Rows.Single().UpdatedAtIst);
        Assert.Single(LibraryFiles());
    }

    [Fact]
    public async Task Delete_UnknownId_Is404()
        => Problem(await _admin.Delete(12345, CancellationToken.None), 404);

    // ── Admin GET ──

    [Fact]
    public async Task AdminList_IncludesInactiveRecords_AndFiltersWork()
    {
        DateTime t = new(2026, 11, 1, 10, 0, 0);
        Seed("Alpha circular", active: true, important: true, t);
        Seed("Beta notice", active: false, important: false, t.AddHours(1), "PNG");
        Seed("Gamma notice", active: true, important: false, t.AddHours(2), "JPG");

        PaginatedResponse<DigitalLibraryAdminDto> all = Data<PaginatedResponse<DigitalLibraryAdminDto>>(await _admin.GetAll(cancellationToken: CancellationToken.None));
        Assert.Equal(new[] { "Gamma notice", "Beta notice", "Alpha circular" }, all.Items.Select(i => i.Description));   // newest first, inactive included

        Assert.Single(Data<PaginatedResponse<DigitalLibraryAdminDto>>(await _admin.GetAll(isActive: false, cancellationToken: CancellationToken.None)).Items);
        Assert.Single(Data<PaginatedResponse<DigitalLibraryAdminDto>>(await _admin.GetAll(isImportant: true, cancellationToken: CancellationToken.None)).Items);
        Assert.Equal(2, Data<PaginatedResponse<DigitalLibraryAdminDto>>(await _admin.GetAll(search: "notice", cancellationToken: CancellationToken.None)).TotalCount);
        Assert.Equal("Beta notice", Data<PaginatedResponse<DigitalLibraryAdminDto>>(await _admin.GetAll(fileType: "png", cancellationToken: CancellationToken.None)).Items.Single().Description);
        Problem(await _admin.GetAll(fileType: "docx", cancellationToken: CancellationToken.None), 400);
        Problem(await _admin.GetAll(pageSize: 101, cancellationToken: CancellationToken.None), 400);
        Problem(await _admin.GetAll(pageNumber: 0, cancellationToken: CancellationToken.None), 400);
    }

    [Fact]
    public async Task AdminList_WithNoMatch_AnswersWithAMessage()
    {
        ObjectResult o = Assert.IsAssignableFrom<ObjectResult>(await _admin.GetAll(cancellationToken: CancellationToken.None));
        Assert.Equal(DigitalLibraryRules.NoRecordsMessage, Assert.IsType<ApiMessageResponse>(o.Value).Message);
    }

    [Fact]
    public async Task AdminGetById_ReturnsInactiveRecords_And404ForUnknown_AndNeverAStoragePath()
    {
        DigitalLibraryAdminDto created = await CreateOk();
        await _admin.Delete(created.Id, CancellationToken.None);

        DigitalLibraryAdminDto dto = Data<DigitalLibraryAdminDto>(await _admin.GetById(created.Id, CancellationToken.None));
        Assert.False(dto.IsActive);
        Assert.DoesNotContain(typeof(DigitalLibraryAdminDto).GetProperties(), p => p.Name.Contains("Path", StringComparison.OrdinalIgnoreCase));

        Problem(await _admin.GetById(404404, CancellationToken.None), 404);
    }

    // ── Mobile list ──

    [Fact]
    public async Task MobileList_ReturnsOnlyActiveRecords_NewestFirst_WithIsImportantAndNoStoragePath()
    {
        DateTime t = new(2026, 11, 1, 10, 0, 0);
        Seed("old", active: true, important: false, t);
        Seed("hidden", active: false, important: true, t.AddHours(5));
        Seed("new", active: true, important: true, t.AddHours(2));

        PaginatedResponse<DigitalLibraryMobileDto> page = Data<PaginatedResponse<DigitalLibraryMobileDto>>(await _mobile.GetAll(cancellationToken: CancellationToken.None));

        Assert.Equal(new[] { "new", "old" }, page.Items.Select(i => i.Description));
        Assert.Equal(2, page.TotalCount);
        Assert.True(page.Items[0].IsImportant);
        Assert.False(page.Items[1].IsImportant);
        Assert.Equal("PDF", page.Items[0].FileType);
        Assert.Equal($"/api/v1/digital-library/{page.Items[0].Id}/file", page.Items[0].FileUrl);
        Assert.DoesNotContain(typeof(DigitalLibraryMobileDto).GetProperties(), p => p.Name.Contains("Path", StringComparison.OrdinalIgnoreCase) && p.Name != "FileUrl");
        Assert.DoesNotContain("seed", page.Items[0].FileUrl);
    }

    [Fact]
    public async Task MobileList_EqualTimestamps_AreOrderedByIdDescending()
    {
        DateTime same = new(2026, 11, 1, 10, 0, 0);
        for (int i = 1; i <= 5; i++) Seed("r" + i, active: true, important: false, same);

        PaginatedResponse<DigitalLibraryMobileDto> page = Data<PaginatedResponse<DigitalLibraryMobileDto>>(await _mobile.GetAll(cancellationToken: CancellationToken.None));

        Assert.Equal(new long[] { 5, 4, 3, 2, 1 }, page.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task MobileList_Paginates_WithoutGapsOrDuplicates()
    {
        DateTime same = new(2026, 11, 1, 10, 0, 0);
        for (int i = 1; i <= 5; i++) Seed("r" + i, active: true, important: false, same.AddMinutes(i % 2));

        List<long> seen = new();
        for (int pageNumber = 1; pageNumber <= 3; pageNumber++)
        {
            PaginatedResponse<DigitalLibraryMobileDto> page = Data<PaginatedResponse<DigitalLibraryMobileDto>>(await _mobile.GetAll(pageNumber, 2, null, CancellationToken.None));
            Assert.Equal(5, page.TotalCount);
            Assert.Equal(3, page.TotalPages);
            seen.AddRange(page.Items.Select(i => i.Id));
        }

        Assert.Equal(5, seen.Distinct().Count());
        Assert.Equal(new long[] { 5, 3, 1, 4, 2 }, seen);   // the three odd ids (newer minute) first, each group id descending
    }

    [Fact]
    public async Task MobileList_IsImportantFilter_Works()
    {
        DateTime t = new(2026, 11, 1, 10, 0, 0);
        Seed("important", active: true, important: true, t);
        Seed("plain", active: true, important: false, t.AddMinutes(1));

        Assert.Equal("important", Data<PaginatedResponse<DigitalLibraryMobileDto>>(await _mobile.GetAll(1, 20, true, CancellationToken.None)).Items.Single().Description);
        Assert.Equal("plain", Data<PaginatedResponse<DigitalLibraryMobileDto>>(await _mobile.GetAll(1, 20, false, CancellationToken.None)).Items.Single().Description);
        Assert.Equal(2, Data<PaginatedResponse<DigitalLibraryMobileDto>>(await _mobile.GetAll(1, 20, null, CancellationToken.None)).TotalCount);
    }

    [Fact]
    public async Task MobileList_WithNoRecords_AnswersWithAMessage_AndRejectsBadPaging()
    {
        ObjectResult o = Assert.IsAssignableFrom<ObjectResult>(await _mobile.GetAll(cancellationToken: CancellationToken.None));
        Assert.Equal(DigitalLibraryRules.NoRecordsMessage, Assert.IsType<ApiMessageResponse>(o.Value).Message);

        Problem(await _mobile.GetAll(0, 20, null, CancellationToken.None), 400);
        Problem(await _mobile.GetAll(1, 0, null, CancellationToken.None), 400);
        Problem(await _mobile.GetAll(1, 101, null, CancellationToken.None), 400);
    }

    // ── Mobile file ──

    [Theory]
    [InlineData("a.pdf", "application/pdf", "pdf")]
    [InlineData("a.jpg", "image/jpeg", "jpg")]
    [InlineData("a.jpeg", "image/jpeg", "jpeg")]
    [InlineData("a.png", "image/png", "png")]
    public async Task MobileFile_StreamsPdfAndImages_WithTheRightContentType(string name, string contentType, string ext)
    {
        byte[] head = ext == "pdf" ? PdfHead : ext == "png" ? PngHead : JpegHead;
        DigitalLibraryAdminDto created = await CreateOk(file: Upload(name, head, "payload-" + ext));

        FileStreamResult file = Assert.IsType<FileStreamResult>(await _mobile.GetFile(created.Id, CancellationToken.None));
        await using Stream stream = file.FileStream;
        using MemoryStream copy = new();
        await stream.CopyToAsync(copy);

        Assert.Equal(contentType, file.ContentType);
        Assert.EndsWith("." + ext, file.FileDownloadName);
        Assert.Contains("payload-" + ext, Encoding.Latin1.GetString(copy.ToArray()));
        Assert.Equal("nosniff", _mobile.Response.Headers["X-Content-Type-Options"]);
    }

    [Fact]
    public async Task MobileFile_ForAnInactiveRecord_Is404_ButTheFileStaysOnDisk()
    {
        DigitalLibraryAdminDto created = await CreateOk();
        await _admin.Delete(created.Id, CancellationToken.None);

        Problem(await _mobile.GetFile(created.Id, CancellationToken.None), 404);
        Assert.Single(LibraryFiles());
    }

    [Fact]
    public async Task MobileFile_ForAMissingRecordOrMissingFile_Is404()
    {
        Problem(await _mobile.GetFile(777, CancellationToken.None), 404);

        DigitalLibraryAdminDto created = await CreateOk();
        System.IO.File.Delete(LibraryFiles().Single());
        ProblemDetails problem = Problem(await _mobile.GetFile(created.Id, CancellationToken.None), 404);
        Assert.Equal(DigitalLibraryRules.FileUnavailableMessage, problem.Detail);

        Problem(await _mobile.GetFile(0, CancellationToken.None), 400);
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("/etc/passwd")]
    [InlineData("C:/Windows/win.ini")]
    [InlineData("2026/11/../../../x.pdf")]
    public async Task MobileFile_ATamperedStoredPath_IsNeverServed(string stored)
    {
        Seed("tampered", active: true, important: false, new DateTime(2026, 11, 1)).FilePath = stored;
        _repo.Rows.Single().FilePath = stored;

        Problem(await _mobile.GetFile(_repo.Rows.Single().Id, CancellationToken.None), 404);
    }

    [Fact]
    public void Storage_UsesTheCentralRootOnly_AndNoModuleSpecificRootExists()
    {
        Assert.Equal(Path.Combine(Path.GetFullPath(_root), "DigitalLibrary"), TestStorage.Central(_root).ModuleRoot(NSG.Mobile.Infrastructure.Services.Storage.StorageModule.DigitalLibrary));
        Assert.DoesNotContain(typeof(DigitalLibrarySettings).GetProperties(), p => p.Name.Contains("Root", StringComparison.OrdinalIgnoreCase));
    }
}
