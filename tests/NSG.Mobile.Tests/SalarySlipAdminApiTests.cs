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
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Application.SalarySlips;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;
using NSG.Mobile.Infrastructure.Options;
using NSG.Mobile.Infrastructure.Services;

namespace NSG.Mobile.Tests;

// Phase 4: the Salary Slip admin controller over the real services, real PDF libraries and real temporary files, with the database replaced by
// in-memory fakes. NOT covered: the SQL of SalarySlipQueryRepository / SalarySlipRepository and the JWT role check (a host test needs the
// NSG_TEST_MYSQL database). The role is asserted through the controller's [Authorize] policy attribute.

/// <summary>Query fake with the contract of SalarySlipQueryRepository: every read is filtered by the uploading admin.</summary>
internal sealed class SlipFakeQueryRepository : ISalarySlipQueryRepository
{
    private readonly SlipFakeRepository _data;

    public SlipFakeQueryRepository(SlipFakeRepository data) => _data = data;

    private int CountOf(long batchId) => _data.Slips.Count(s => s.OriginalBatchId == batchId);

    public Task<(IReadOnlyList<SalarySlipBatchRow> Items, int TotalCount)> GetBatchesAsync(long adminId, DateOnly? salaryMonth, int pageNumber, int pageSize, CancellationToken cancellationToken)
    {
        List<SalarySlipUploadBatch> own = _data.Batches.Where(b => b.UploadedBy == adminId && (salaryMonth == null || b.SalaryMonth == salaryMonth)).OrderByDescending(b => b.Id).ToList();
        IReadOnlyList<SalarySlipBatchRow> page = own.Skip((pageNumber - 1) * pageSize).Take(pageSize).Select(b => new SalarySlipBatchRow(b, CountOf(b.Id))).ToList();
        return Task.FromResult((page, own.Count));
    }

    public Task<SalarySlipBatchRow?> GetBatchAsync(long adminId, long batchId, CancellationToken cancellationToken)
    {
        SalarySlipUploadBatch? b = _data.Batches.FirstOrDefault(x => x.Id == batchId && x.UploadedBy == adminId);
        return Task.FromResult(b == null ? null : new SalarySlipBatchRow(b, CountOf(b.Id)));
    }

    public Task<(IReadOnlyList<SalarySlip> Items, int TotalCount)> GetSlipsOfBatchAsync(long batchId, int pageNumber, int pageSize, CancellationToken cancellationToken)
    {
        List<SalarySlip> all = _data.Slips.Where(s => s.OriginalBatchId == batchId).OrderBy(s => s.Id).ToList();
        return Task.FromResult(((IReadOnlyList<SalarySlip>)all.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList(), all.Count));
    }

    public Task<SalarySlip?> GetSlipOfAdminAsync(long adminId, long slipId, CancellationToken cancellationToken)
        => Task.FromResult(_data.Slips.FirstOrDefault(s => s.Id == slipId && _data.Batches.Any(b => b.Id == s.OriginalBatchId && b.UploadedBy == adminId)));
}

public class SalarySlipAdminApiTests : IDisposable
{
    private const long AdminA = 11, AdminB = 22;
    private static readonly DateOnly October = new(2026, 10, 1);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "nsg-slip-api-" + Guid.NewGuid().ToString("N"));
    private readonly SlipFakeRepository _data = new();
    private readonly FakeCurrentUser _user = new() { AdminProfileId = AdminA };
    private readonly SlipFakeEncryption _encryption = new();
    private readonly SalarySlipStorageService _storage;
    private readonly SalarySlipAdminController _controller;

    public SalarySlipAdminApiTests()
    {
        _storage = new SalarySlipStorageService(TestStorage.Central(_root), NullLogger<SalarySlipStorageService>.Instance);
        FakeAudit audit = new();
        SalarySlipProcessingService processing = new(_data, _storage, new SalarySlipPdfExtractionService(NullLogger<SalarySlipPdfExtractionService>.Instance),
            new SalarySlipPdfGenerationService(), _encryption, audit, _user, new SalarySlipSettings { MaxPages = 100 }, NullLogger<SalarySlipProcessingService>.Instance);
        SalarySlipAdminService admin = new(new SlipFakeQueryRepository(_data), _storage, _encryption, audit, _user, NullLogger<SalarySlipAdminService>.Instance);
        _controller = new SalarySlipAdminController(processing, admin) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private static IFormFile Pdf(byte[] bytes, string name = "October_2026.pdf")
        => new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", name);

    private Task<IActionResult> Upload(byte[] pdf, string? month = "2026-10", string name = "October_2026.pdf")
        => _controller.Upload(new SalarySlipUploadForm { File = Pdf(pdf, name), SalaryMonth = month }, CancellationToken.None);

    private static T Data<T>(IActionResult result)
    {
        ApiResponse<T> body = Assert.IsType<ApiResponse<T>>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.True(body.Success);
        return body.Data!;
    }

    private static ProblemDetails Problem(IActionResult result, int status)
    {
        ObjectResult o = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(status, o.StatusCode);
        return Assert.IsType<ProblemDetails>(o.Value);
    }

    private static byte[] Slips(params string[] accounts)
        => SlipTestPdf.Make(accounts.Select((a, i) => SlipTestPdf.Slip("p" + (i + 1), a)).ToArray());

    private async Task<long> UploadAs(long admin, byte[] pdf, string month = "2026-10")
    {
        _user.AdminProfileId = admin;
        return Data<SalarySlipUploadResultDto>(await Upload(pdf, month)).BatchId;
    }

    // ── Upload ──

    [Fact]
    public async Task Upload_returns_the_summary_with_the_uploader_taken_from_the_token()
    {
        SalarySlipUploadResultDto dto = Data<SalarySlipUploadResultDto>(await Upload(
            SlipTestPdf.Make(SlipTestPdf.Slip("p1", "111111111"), "Salary Slip p2\nno account", SlipTestPdf.Slip("p3", "111111111"))));

        Assert.Equal(SalarySlipBatchStatus.CompletedWithErrors, dto.Status);
        Assert.Equal((3, 2, 1, 1), (dto.TotalPages, dto.ProcessedPages, dto.UnmatchedPages, dto.SalarySlipsCreated));
        Assert.Equal(new[] { 2 }, dto.UnmatchedPageNumbers);
        Assert.Equal(AdminA, _data.Batches.Single().UploadedBy);
        Assert.Equal(October, dto.SalaryMonth);

        string json = JsonSerializer.Serialize(dto);   // enums read as names, not numbers
        Assert.Contains("\"Status\":\"CompletedWithErrors\"", json);
        Assert.Contains("\"Reason\":\"AccountNumberNotFound\"", json);
        Assert.DoesNotContain("111111111", json);
    }

    [Fact]
    public async Task Upload_without_a_file_or_with_a_bad_month_is_a_400_with_a_message()
    {
        ProblemDetails noFile = Problem(await _controller.Upload(new SalarySlipUploadForm { File = null, SalaryMonth = "2026-10" }, CancellationToken.None), 400);
        Assert.Contains("file", noFile.Detail);

        byte[] pdf = Slips("111111111");
        foreach (string? month in new[] { null, "", "2026-13", "October 2026", "2026-10-01" })
            Assert.Contains("salaryMonth", Problem(await Upload(pdf, month), 400).Detail);

        Assert.Empty(_data.Batches);
        Assert.Equal(0, _data.LocksAcquired);
    }

    [Fact]
    public async Task Upload_of_an_invalid_or_non_pdf_file_is_a_400_and_stores_nothing()
    {
        Assert.NotNull(Problem(await Upload(Encoding.ASCII.GetBytes("not a pdf at all")), 400).Detail);                       // wrong signature: no batch
        Assert.Empty(_data.Batches);

        Assert.NotNull(Problem(await Upload(Encoding.ASCII.GetBytes("%PDF-1.4\ngarbage\n%%EOF")), 400).Detail);                // damaged: failed batch
        Assert.Equal(SalarySlipBatchStatus.Failed, Assert.Single(_data.Batches).Status);

        Assert.NotNull(Problem(await Upload(Slips("111111111"), name: "slips.docx"), 400).Detail);
        Assert.Empty(_data.Slips);
        Assert.Empty(Directory.GetFiles(_root, "*.pdf", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(_root, "*.upload", SearchOption.AllDirectories));
        Assert.Equal(0, _data.LocksHeld);
    }

    [Fact]
    public async Task A_processing_failure_is_a_500_with_a_safe_message_and_leaves_no_files()
    {
        _data.ThrowOnSave = new InvalidOperationException("Server=secret-db;Pwd=hunter2");

        ProblemDetails problem = Problem(await Upload(Slips("111111111")), 500);

        Assert.DoesNotContain("secret-db", problem.Detail);
        Assert.DoesNotContain("hunter2", problem.Detail);
        Assert.Empty(_data.Slips);
        Assert.Empty(Directory.GetFiles(_root, "*.pdf", SearchOption.AllDirectories));
        Assert.Equal(0, _data.LocksHeld);
    }

    [Fact]
    public async Task Upload_while_another_upload_of_the_month_is_running_is_a_409()
    {
        _data.DenyLock = true;

        Assert.NotNull(Problem(await Upload(Slips("111111111")), 409).Detail);
        Assert.Empty(_data.Batches);
    }

    [Fact]
    public async Task Upload_without_an_identifiable_admin_is_a_401()
    {
        _user.AdminProfileId = null;

        Problem(await Upload(Slips("111111111")), 401);
        Assert.Empty(_data.Batches);
    }

    // ── Ownership: admin A versus admin B ──

    [Fact]
    public async Task An_admin_sees_only_their_own_batches_in_the_list()
    {
        await UploadAs(AdminA, Slips("111111111"));
        await UploadAs(AdminB, Slips("222222222"), "2026-11");
        await UploadAs(AdminA, Slips("333333333"), "2026-11");

        _user.AdminProfileId = AdminA;
        PaginatedResponse<SalarySlipBatchSummaryDto> a = Data<PaginatedResponse<SalarySlipBatchSummaryDto>>(await _controller.GetBatches());
        Assert.Equal(2, a.TotalCount);
        Assert.Equal(new long[] { 3, 1 }, a.Items.Select(b => b.BatchId).ToArray());   // newest first, never batch 2

        _user.AdminProfileId = AdminB;
        PaginatedResponse<SalarySlipBatchSummaryDto> b = Data<PaginatedResponse<SalarySlipBatchSummaryDto>>(await _controller.GetBatches());
        Assert.Equal(new long[] { 2 }, b.Items.Select(x => x.BatchId).ToArray());
    }

    [Fact]
    public async Task An_admin_cannot_open_another_admins_batch_and_gets_the_same_404_as_for_a_missing_one()
    {
        long batchOfA = await UploadAs(AdminA, Slips("111111111"));
        _user.AdminProfileId = AdminB;

        ProblemDetails foreign = Problem(await _controller.GetBatch(batchOfA), 404);
        ProblemDetails missing = Problem(await _controller.GetBatch(9999), 404);

        Assert.Equal(missing.Detail, foreign.Detail);
        _user.AdminProfileId = AdminA;
        Assert.Equal(batchOfA, Data<SalarySlipBatchDetailDto>(await _controller.GetBatch(batchOfA)).Batch.BatchId);
    }

    [Fact]
    public async Task An_admin_cannot_download_another_admins_slip()
    {
        await UploadAs(AdminA, Slips("111111111"));
        long slipOfA = _data.Slips.Single().Id;

        _user.AdminProfileId = AdminB;
        ProblemDetails foreign = Problem(await _controller.Download(slipOfA), 404);
        ProblemDetails missing = Problem(await _controller.Download(9999), 404);
        Assert.Equal(missing.Detail, foreign.Detail);

        _user.AdminProfileId = AdminA;
        await using (Stream opened = Assert.IsType<FileStreamResult>(await _controller.Download(slipOfA)).FileStream) { Assert.True(opened.CanRead); }
    }

    [Fact]
    public async Task The_uploader_comes_only_from_the_token_so_an_unidentified_caller_gets_401_on_every_read()
    {
        await UploadAs(AdminA, Slips("111111111"));
        _user.AdminProfileId = null;

        Problem(await _controller.GetBatches(), 401);
        Problem(await _controller.GetBatch(1), 401);
        Problem(await _controller.Download(1), 401);
    }

    // ── Month filter and pagination ──

    [Fact]
    public async Task The_list_filters_by_month_and_rejects_a_malformed_month()
    {
        await UploadAs(AdminA, Slips("111111111"), "2026-10");
        await UploadAs(AdminA, Slips("222222222"), "2026-11");
        await UploadAs(AdminA, Slips("333333333"), "2026-12");

        PaginatedResponse<SalarySlipBatchSummaryDto> november = Data<PaginatedResponse<SalarySlipBatchSummaryDto>>(await _controller.GetBatches(month: "2026-11"));
        Assert.Equal(1, november.TotalCount);
        Assert.Equal(new DateOnly(2026, 11, 1), november.Items.Single().SalaryMonth);

        Problem(await _controller.GetBatches(month: "11-2026"), 400);

        // No match: one message, never an empty list.
        ApiMessageResponse none = Assert.IsType<ApiMessageResponse>(Assert.IsType<OkObjectResult>(await _controller.GetBatches(month: "2025-01")).Value);
        Assert.Equal("No salary slip upload batches found.", none.Message);
    }

    [Fact]
    public async Task The_list_is_paged_and_page_arguments_are_validated()
    {
        for (int i = 0; i < 5; i++) await UploadAs(AdminA, Slips("10000000" + i), "2026-" + (i + 1).ToString("00"));

        PaginatedResponse<SalarySlipBatchSummaryDto> first = Data<PaginatedResponse<SalarySlipBatchSummaryDto>>(await _controller.GetBatches(pageNumber: 1, pageSize: 2));
        PaginatedResponse<SalarySlipBatchSummaryDto> last = Data<PaginatedResponse<SalarySlipBatchSummaryDto>>(await _controller.GetBatches(pageNumber: 3, pageSize: 2));

        Assert.Equal((5, 3, 2, true, false), (first.TotalCount, first.TotalPages, first.Items.Count, first.HasNextPage, first.HasPreviousPage));
        Assert.Equal((1, true, false), (last.Items.Count, last.HasPreviousPage, last.HasNextPage));
        Assert.Empty(first.Items.Select(i => i.BatchId).Intersect(last.Items.Select(i => i.BatchId)));

        Problem(await _controller.GetBatches(pageNumber: 0), 400);
        Problem(await _controller.GetBatches(pageSize: 0), 400);
        Problem(await _controller.GetBatches(pageSize: 101), 400);
        Problem(await _controller.GetBatch(1, pageSize: 101), 400);
    }

    // ── Batch detail ──

    [Fact]
    public async Task Batch_detail_shows_status_counts_unmatched_pages_and_masked_slips_without_paths_or_full_accounts()
    {
        long batch = await UploadAs(AdminA, SlipTestPdf.Make(
            SlipTestPdf.Slip("p1", "34280226082"), "Salary Slip p2\nPRAN No : 99999999999", SlipTestPdf.Slip("p3", "55566677788"), "blank-ish\nnothing"));

        SalarySlipBatchDetailDto detail = Data<SalarySlipBatchDetailDto>(await _controller.GetBatch(batch));

        Assert.Equal("CompletedWithErrors", detail.Batch.Status);
        Assert.Equal((4, 2, 2, 2), (detail.Batch.TotalPages, detail.Batch.ProcessedPages, detail.Batch.UnmatchedPages, detail.Batch.SalarySlipCount));
        Assert.Equal(new[] { 2, 4 }, detail.UnmatchedPageNumbers);
        Assert.Equal(2, detail.SalarySlips.TotalCount);
        Assert.Contains(detail.SalarySlips.Items, s => s.AccountNumberMasked == "XXXXXXX6082");
        Assert.All(detail.SalarySlips.Items, s => Assert.Equal($"api/v1/admin/salary-slips/{s.SalarySlipId}/download", s.DownloadUrl));

        string json = JsonSerializer.Serialize(detail);
        Assert.DoesNotContain("34280226082", json);
        Assert.DoesNotContain("55566677788", json);
        Assert.DoesNotContain("salary-slips/2026", json);                 // no storage path
        Assert.DoesNotContain(_root.Replace("\\", "\\\\"), json);
        Assert.DoesNotContain("AccountNumberEncrypted", json);
        Assert.DoesNotContain("FilePath", json);
    }

    [Fact]
    public async Task A_failed_batch_detail_carries_the_failure_message_and_no_slips()
    {
        _user.AdminProfileId = AdminA;
        await Upload(SlipTestPdf.Make(null, null));   // no extractable text

        SalarySlipBatchDetailDto detail = Data<SalarySlipBatchDetailDto>(await _controller.GetBatch(1));

        Assert.Equal("Failed", detail.Batch.Status);
        Assert.False(string.IsNullOrWhiteSpace(detail.Batch.FailureMessage));
        Assert.Equal(0, detail.SalarySlips.TotalCount);
    }

    // ── Download ──

    [Fact]
    public async Task Download_streams_the_pdf_with_a_safe_file_name_and_no_store_header()
    {
        await UploadAs(AdminA, Slips("34280226082"));
        long slip = _data.Slips.Single().Id;

        FileStreamResult file = Assert.IsType<FileStreamResult>(await _controller.Download(slip));
        await using Stream stream = file.FileStream;
        byte[] head = new byte[5];
        await stream.ReadExactlyAsync(head);

        Assert.Equal("application/pdf", file.ContentType);
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(head));
        Assert.Equal($"SalarySlip_2026-10_{slip}.pdf", file.FileDownloadName);
        Assert.DoesNotContain("3428", file.FileDownloadName);
        Assert.Equal("no-store", _controller.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task Download_of_a_slip_whose_file_is_missing_is_a_404_with_a_message_and_no_path()
    {
        await UploadAs(AdminA, Slips("111111111"));
        SalarySlip slip = _data.Slips.Single();
        File.Delete(TestStorage.SlipPath(_root, slip.FilePath));

        ProblemDetails problem = Problem(await _controller.Download(slip.Id), 404);

        Assert.Equal("The salary slip file is not available.", problem.Detail);
        Assert.DoesNotContain(slip.FilePath, problem.Detail);
    }

    [Fact]
    public async Task Download_with_a_tampered_stored_path_is_refused_not_followed()
    {
        await UploadAs(AdminA, Slips("111111111"));
        SalarySlip slip = _data.Slips.Single();
        slip.FilePath = "../../outside.pdf";

        Problem(await _controller.Download(slip.Id), 500);
    }

    // ── Controller contract ──

    [Fact]
    public void Every_action_is_protected_by_the_salary_management_admin_policy_and_none_is_anonymous()
    {
        Type type = typeof(SalarySlipAdminController);
        AuthorizeAttribute authorize = Assert.Single(type.GetCustomAttributes<AuthorizeAttribute>());
        Assert.Equal(DomainConstants.Policies.RequireSalaryManagementAdmin, authorize.Policy);
        Assert.Null(type.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.All(type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
            m => Assert.Null(m.GetCustomAttribute<AllowAnonymousAttribute>()));

        Assert.Equal("api/v1/admin/salary-slips", type.GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Equal("upload", type.GetMethod(nameof(SalarySlipAdminController.Upload))!.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal("{id:long}/download", type.GetMethod(nameof(SalarySlipAdminController.Download))!.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Contains(typeof(SalarySlipUploadForm).GetProperties(), p => p.Name == nameof(SalarySlipUploadForm.SalaryMonth));
        Assert.DoesNotContain(typeof(SalarySlipUploadForm).GetProperties(), p => p.Name.Contains("Upload", StringComparison.OrdinalIgnoreCase) && p.Name.Contains("By", StringComparison.OrdinalIgnoreCase));
    }
}
