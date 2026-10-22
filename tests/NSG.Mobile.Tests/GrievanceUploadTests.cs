using Microsoft.Extensions.Logging;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Grievances;
using NSG.Mobile.Application.Grievances.DTOs;
using NSG.Mobile.Application.Grievances.Services;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Tests;

/// <summary>
/// Attachments of the single creation call: validation before storage, fixed limits (3 files, 5 MB, PDF/JPEG), all-or-nothing storage,
/// no orphan objects, the storage guard. There is no separate upload operation any more.
/// </summary>
public class GrievanceUploadTests
{
    private static (GrvKit Kit, GrievanceCategory Category) Arrange()
    {
        GrvKit kit = new();
        return (kit, kit.Repo.AddCategory("Medical"));
    }

    private static CreateGrievanceRequestDto Request(GrievanceCategory category)
        => new() { CategoryId = category.Id, Subject = "Reimbursement", Note = "My claim from August is still pending." };

    private static Task<Result<ApiResult<GrievanceDetailDto>>> Create(GrvKit kit, GrievanceCategory category, params GrievanceUploadFile[] files)
        => kit.Employees.CreateAsync(Request(category), files, default);

    private static List<GrievanceDocumentRecord> Stored(GrvKit kit) => GrievanceDocumentRules.ParseDocuments(kit.Repo.Grievances.Single().DocumentsJson);

    // ── What is accepted ──

    [Fact]
    public async Task A_pdf_and_a_jpeg_are_stored_and_recorded_with_the_grievance()
    {
        (GrvKit kit, GrievanceCategory category) = Arrange();

        var result = await Create(kit, category, GrvKit.Pdf("medical report.pdf"), GrvKit.Jpeg("scan.JPEG"));

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(2, result.Data!.Data!.DocumentCount);
        Assert.Equal(new[] { 0, 1 }, result.Data.Data.Documents.Select(d => d.Index));

        List<GrievanceDocumentRecord> docs = Stored(kit);
        Assert.Equal(2, docs.Count);
        Assert.All(docs, d => Assert.StartsWith("grievances/", d.Key));      // an object key, not a URL
        Assert.All(docs, d => Assert.DoesNotContain("http", d.Key));
        Assert.Equal("medical report.pdf", docs[0].Name);
        Assert.Equal("scan.jpg", docs[1].Name);                              // extension normalised to the validated one
        Assert.Equal("application/pdf", docs[0].ContentType);
        Assert.Equal("image/jpeg", docs[1].ContentType);
        Assert.Equal(GrvKit.PdfBytes.Length, docs[0].SizeBytes);
        Assert.Equal(2, kit.Audit.Entries.Count(e => e.Action == AuditAction.GrievanceDocumentUploaded));
        Assert.Single(kit.Audit.Entries, e => e.Action == AuditAction.GrievanceCreated);
    }

    [Fact]
    public async Task Attachments_are_optional()
    {
        (GrvKit kit, GrievanceCategory category) = Arrange();

        var result = await Create(kit, category);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(0, result.Data!.Data!.DocumentCount);
        Assert.Null(kit.Repo.Grievances.Single().DocumentsJson);
        Assert.Empty(kit.Storage.StoreCalls);
    }

    [Theory]
    [InlineData("..\\..\\windows\\evil.pdf", "evil.pdf")]
    [InlineData("../../etc/passwd.pdf", "passwd.pdf")]
    [InlineData("na\"me;with<bad>chars.pdf", "na_me_with_bad_chars.pdf")]
    [InlineData("   ....pdf", "document.pdf")]
    public async Task The_stored_file_name_is_sanitised_for_headers_and_paths(string original, string expected)
    {
        (GrvKit kit, GrievanceCategory category) = Arrange();

        await Create(kit, category, GrvKit.Upload(GrvKit.PdfBytes, original, "application/pdf"));

        Assert.Equal(expected, Stored(kit).Single().Name);
        Assert.Equal(expected, kit.Storage.LastStoredFileName);
    }

    // ── What is refused, always before storage and before the grievance exists ──

    [Fact]
    public async Task A_file_whose_bytes_are_not_what_its_extension_claims_is_rejected_and_nothing_is_stored_or_created()
    {
        (GrvKit kit, GrievanceCategory category) = Arrange();

        var fakePdf = await Create(kit, category, GrvKit.Upload(GrvKit.PngBytes, "invoice.pdf", "application/pdf"));
        var fakeJpeg = await Create(kit, category, GrvKit.Upload(GrvKit.PdfBytes, "photo.jpg", "image/jpeg"));
        var shortFile = await Create(kit, category, GrvKit.Upload(new byte[] { 0x25, 0x50 }, "tiny.pdf", "application/pdf"));

        Assert.All(new[] { fakePdf.ErrorCode, fakeJpeg.ErrorCode, shortFile.ErrorCode }, c => Assert.Equal(ApiErrorCodes.ValidationFailed, c));
        Assert.Contains("content does not match", fakePdf.ErrorMessage);
        Assert.Empty(kit.Storage.StoreCalls);
        Assert.Empty(kit.Repo.Grievances);
    }

    [Theory]
    [InlineData("setup.exe", "application/octet-stream")]
    [InlineData("image.png", "image/png")]
    [InlineData("notes.txt", "text/plain")]
    [InlineData("noextension", "application/pdf")]
    [InlineData("report.pdf.exe", "application/pdf")]
    public async Task Other_extensions_are_rejected(string name, string contentType)
    {
        (GrvKit kit, GrievanceCategory category) = Arrange();

        var result = await Create(kit, category, GrvKit.Upload(GrvKit.PdfBytes, name, contentType));

        Assert.Equal(ApiErrorCodes.ValidationFailed, result.ErrorCode);
        Assert.Empty(kit.Storage.StoreCalls);
        Assert.Empty(kit.Repo.Grievances);
    }

    [Theory]
    [InlineData("report.pdf", "image/jpeg")]
    [InlineData("report.pdf", "application/octet-stream")]
    [InlineData("photo.jpg", "application/pdf")]
    [InlineData("photo.jpg", "")]
    public async Task The_content_type_must_agree_with_the_extension(string name, string contentType)
    {
        (GrvKit kit, GrievanceCategory category) = Arrange();

        var result = await Create(kit, category, GrvKit.Upload(GrvKit.PdfBytes, name, contentType));

        Assert.Equal(ApiErrorCodes.ValidationFailed, result.ErrorCode);
        Assert.Contains("content type", result.ErrorMessage);
        Assert.Empty(kit.Storage.StoreCalls);
    }

    [Fact]
    public async Task An_empty_file_is_rejected()
    {
        (GrvKit kit, GrievanceCategory category) = Arrange();
        var result = await Create(kit, category, GrvKit.Upload(Array.Empty<byte>(), "empty.pdf", "application/pdf"));
        Assert.Equal(ApiErrorCodes.ValidationFailed, result.ErrorCode);
        Assert.Empty(kit.Storage.StoreCalls);
    }

    [Fact]
    public async Task One_bad_file_among_good_ones_stores_nothing_at_all()
    {
        (GrvKit kit, GrievanceCategory category) = Arrange();

        var result = await Create(kit, category, GrvKit.Pdf("good.pdf"), GrvKit.Upload(GrvKit.PngBytes, "bad.pdf", "application/pdf"));

        Assert.Equal(ApiErrorCodes.ValidationFailed, result.ErrorCode);
        Assert.Empty(kit.Storage.StoreCalls);                                // every file is checked before the first one is stored
        Assert.Empty(kit.Repo.Grievances);
    }

    // ── Fixed limits ──

    [Fact]
    public async Task A_file_over_5_MB_is_a_400_that_names_the_limit_and_exactly_5_MB_is_accepted()
    {
        (GrvKit kit, GrievanceCategory category) = Arrange();

        var tooBig = await Create(kit, category, GrvKit.PdfOfSize(5 * 1024 * 1024 + 1));
        var exactly = await Create(kit, category, GrvKit.PdfOfSize(5 * 1024 * 1024));

        Assert.Equal(ApiErrorCodes.ValidationFailed, tooBig.ErrorCode);
        Assert.Contains("5 MB", tooBig.ErrorMessage);
        Assert.True(exactly.IsSuccess, exactly.ErrorMessage);
        Assert.Single(kit.Storage.StoreCalls);
    }

    [Fact]
    public async Task At_most_3_files_per_grievance()
    {
        (GrvKit kit, GrievanceCategory category) = Arrange();

        var four = await Create(kit, category, GrvKit.Pdf("a.pdf"), GrvKit.Pdf("b.pdf"), GrvKit.Pdf("c.pdf"), GrvKit.Pdf("d.pdf"));
        var three = await Create(kit, category, GrvKit.Pdf("a.pdf"), GrvKit.Pdf("b.pdf"), GrvKit.Pdf("c.pdf"));

        Assert.Equal(ApiErrorCodes.ValidationFailed, four.ErrorCode);
        Assert.Contains("at most 3", four.ErrorMessage);
        Assert.True(three.IsSuccess, three.ErrorMessage);
        Assert.Equal(3, kit.Storage.StoreCalls.Count);                       // the refused request stored nothing
    }

    // ── Storage guard and outages ──

    [Fact]
    public async Task Outside_Development_without_R2_a_submission_with_files_is_503_and_downloads_never_touch_storage()
    {
        (GrvKit kit, GrievanceCategory category) = Arrange();
        Grievance g = kit.Repo.AddGrievance(GrvKit.Employee, category);
        GrievanceDocumentRecord doc = new("grievances/2026/10/22222222-2222-2222-2222-222222222222.pdf", "a.pdf", "application/pdf", 5, new DateTime(2026, 10, 15));
        g.DocumentsJson = GrievanceDocumentRules.SerializeDocuments(new List<GrievanceDocumentRecord> { doc });
        kit.Storage.Objects[doc.Key] = GrvKit.PdfBytes;
        kit.Availability.IsAvailable = false;

        var withFiles = await Create(kit, category, GrvKit.Pdf());
        var employeeDownload = await kit.Employees.OpenMyDocumentAsync(g.Id, 0, default);
        var adminDownload = await kit.Admins.OpenDocumentAsync(g.Id, 0, default);

        Assert.All(new[] { withFiles.ErrorCode, employeeDownload.ErrorCode, adminDownload.ErrorCode }, c => Assert.Equal(GrievanceErrorCodes.StorageUnavailable, c));
        Assert.Empty(kit.Storage.StoreCalls);
        Assert.Empty(kit.Storage.GetCalls);
        Assert.Single(kit.Repo.Grievances);                                  // only the seeded one
    }

    [Fact]
    public async Task Without_files_a_submission_does_not_need_storage()
    {
        (GrvKit kit, GrievanceCategory category) = Arrange();
        kit.Availability.IsAvailable = false;

        Assert.True((await Create(kit, category)).IsSuccess);
    }

    [Fact]
    public async Task A_storage_outage_while_storing_is_a_503_code_cleans_up_and_creates_nothing()
    {
        (GrvKit kit, GrievanceCategory category) = Arrange();
        kit.Storage.UnavailableOnStore = true;

        var result = await Create(kit, category, GrvKit.Pdf());

        Assert.Equal(GrievanceErrorCodes.StorageUnavailable, result.ErrorCode);
        Assert.Empty(kit.Repo.Grievances);
    }

    // ── No orphan objects ──

    [Fact]
    public async Task When_recording_the_grievance_fails_every_stored_object_is_deleted_and_the_error_surfaces()
    {
        (GrvKit kit, GrievanceCategory category) = Arrange();
        kit.Repo.ThrowOnCreate = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => Create(kit, category, GrvKit.Pdf("a.pdf"), GrvKit.Jpeg("b.jpg")));

        Assert.Equal(2, kit.Storage.StoreCalls.Count);
        Assert.Equal(kit.Storage.StoreCalls.OrderBy(k => k), kit.Storage.Deleted.OrderBy(k => k));   // compensated
        Assert.Empty(kit.Storage.Objects);                                   // no orphan left in the bucket
        Assert.DoesNotContain(kit.Audit.Entries, e => e.Action == AuditAction.GrievanceDocumentUploaded);
    }

    [Fact]
    public async Task When_no_ticket_number_can_be_allocated_the_stored_objects_are_deleted()
    {
        (GrvKit kit, GrievanceCategory category) = Arrange();
        kit.Repo.TicketsUnavailable = true;

        var result = await Create(kit, category, GrvKit.Pdf());

        Assert.Equal(GrievanceErrorCodes.TicketUnavailable, result.ErrorCode);
        Assert.Single(kit.Storage.Deleted);
        Assert.Empty(kit.Storage.Objects);
    }

    [Fact]
    public async Task A_failed_compensating_delete_is_logged_for_manual_clean_up_not_thrown()
    {
        (GrvKit kit, GrievanceCategory category) = Arrange();
        kit.Repo.TicketsUnavailable = true;
        kit.Storage.DeleteFails = true;

        var result = await Create(kit, category, GrvKit.Pdf());

        Assert.Equal(GrievanceErrorCodes.TicketUnavailable, result.ErrorCode);
        Assert.Contains(kit.EmployeeLog.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("manual clean-up"));
    }

    [Fact]
    public async Task Storage_keys_never_leave_the_server_in_the_response()
    {
        (GrvKit kit, GrievanceCategory category) = Arrange();

        var result = await Create(kit, category, GrvKit.Pdf());

        string key = Stored(kit).Single().Key;
        string json = System.Text.Json.JsonSerializer.Serialize(result.Data);
        Assert.DoesNotContain(key, json);
        Assert.DoesNotContain("grievances/", json);
    }
}
