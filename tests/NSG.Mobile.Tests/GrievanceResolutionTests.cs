using System.Text.Json;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Grievances;
using NSG.Mobile.Application.Grievances.DTOs;
using NSG.Mobile.Application.Grievances.Services;
using NSG.Mobile.Application.Grievances.Validators;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Tests;

/// <summary>
/// Resolving a grievance (Action Taken → Closed) needs a resolution comment and one supporting file; the owner can download that file.
/// Services over in-memory fakes: no database, no storage, no network. Real SQL, MySQL and R2 behaviour is NOT covered here.
/// </summary>
public class GrievanceResolutionTests
{
    private static (GrvKit Kit, Grievance Grievance) Arrange(GrievanceStatus status = GrievanceStatus.ActionTaken)
    {
        GrvKit kit = new();
        GrievanceCategory category = kit.Repo.AddCategory("Medical");
        return (kit, kit.Repo.AddGrievance(GrvKit.Employee, category, status));
    }

    private static ResolveGrievanceRequestDto Remarks(string? text = "Reimbursed in full.") => new() { Remarks = text };

    // ── Success ──

    [Theory]
    [InlineData("proof.pdf", "application/pdf")]
    [InlineData("proof.jpg", "image/jpeg")]
    [InlineData("proof.png", "image/png")]
    public async Task Resolving_with_a_comment_and_a_valid_file_closes_the_grievance_and_stores_everything_together(string name, string contentType)
    {
        (GrvKit kit, Grievance g) = Arrange();
        byte[] bytes = contentType == "application/pdf" ? GrvKit.PdfBytes : contentType == "image/png" ? GrvKit.PngBytes : GrvKit.JpegBytes;

        var result = await kit.Admins.ResolveAsync(g.Id, Remarks("  Reimbursed in full.  "), GrvKit.Upload(bytes, name, contentType), default);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal("Grievance resolved successfully.", result.Data!.Message);
        Assert.Equal((int)GrievanceStatus.Closed, result.Data.Data!.Status);

        Grievance stored = kit.Repo.Grievances.Single();
        Assert.Equal(GrievanceStatus.Closed, stored.CurrentStatus);
        Assert.Equal(GrvKit.Admin, stored.ClosedBy);                                  // the admin comes from the token
        Assert.Equal("Reimbursed in full.", stored.ClosedRemarks);                    // trimmed
        Assert.NotNull(stored.ClosedAtIst);

        string key = Assert.Single(kit.Storage.StoreCalls);
        Assert.StartsWith("grievances/", key);
        Assert.Equal(contentType, kit.Storage.LastStoredContentType);
        Assert.Equal(key, stored.ResolutionDocument);                                 // the column holds only the storage reference, a plain string
        Assert.EndsWith(System.IO.Path.GetExtension(name), key);
        Assert.Equal(name, kit.Storage.LastStoredFileName);

        GrievanceStatusLog log = Assert.Single(kit.Repo.Logs);
        Assert.Equal(GrievanceStatus.ActionTaken, log.FromStatus);
        Assert.Equal(GrievanceStatus.Closed, log.ToStatus);
        Assert.Equal("Reimbursed in full.", log.Remarks);
        Assert.Equal(GrvKit.Admin, log.ChangedBy);
        Assert.Equal(GrievanceActorType.Admin, log.ChangedByType);

        Assert.Contains(kit.Audit.Entries, e => e.Action == AuditAction.GrievanceStatusChanged && e.Data!.Contains("\"resolved\":true"));
        Assert.Contains(kit.Audit.Entries, e => e.Action == AuditAction.GrievanceDocumentUploaded && e.Data!.Contains("resolutionDocument"));
    }

    [Fact]
public async Task The_answer_returns_the_stored_reference_as_a_string_that_is_not_a_url()    {        (GrvKit kit, Grievance g) = Arrange();        var result = await kit.Admins.ResolveAsync(g.Id, Remarks(), GrvKit.Pdf("proof.pdf"), default);        string reference = result.Data!.Data!.ResolutionDocument!;        Assert.Equal(kit.Storage.StoreCalls.Single(), reference);        Assert.DoesNotContain("http", reference, StringComparison.OrdinalIgnoreCase);        Assert.True(reference.Length <= 500);    }

    [Fact]
    public async Task The_name_handed_to_storage_is_sanitised_and_never_the_client_path()
    {
        (GrvKit kit, Grievance g) = Arrange();

        await kit.Admins.ResolveAsync(g.Id, Remarks(), GrvKit.Upload(GrvKit.PdfBytes, @"..\..\evil;name"" .pdf", "application/pdf"), default);

        string name = kit.Storage.LastStoredFileName!;                               // the name handed to storage
        Assert.DoesNotContain("..", name);
        Assert.DoesNotContain("\\", name);
        Assert.DoesNotContain("/", name);
        Assert.DoesNotContain(";", name);
        Assert.DoesNotContain("\"", name);
        Assert.EndsWith(".pdf", name);
    }

    // ── The comment and the file are both mandatory ──

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_missing_comment_is_a_400_and_nothing_is_stored_or_changed(string? remarks)
    {
        (GrvKit kit, Grievance g) = Arrange();

        var result = await kit.Admins.ResolveAsync(g.Id, Remarks(remarks), GrvKit.Pdf(), default);

        Assert.Equal(ApiErrorCodes.ValidationFailed, result.ErrorCode);
        Assert.Equal(GrievanceMessages.ResolutionRemarksRequired, result.ErrorMessage);
        AssertUntouched(kit, GrievanceStatus.ActionTaken);
    }

    [Fact]
    public async Task A_missing_file_is_a_400_and_nothing_is_changed()
    {
        (GrvKit kit, Grievance g) = Arrange();

        var result = await kit.Admins.ResolveAsync(g.Id, Remarks(), null, default);

        Assert.Equal(ApiErrorCodes.ValidationFailed, result.ErrorCode);
        Assert.Equal(GrievanceMessages.ResolutionFileRequired, result.ErrorMessage);
        AssertUntouched(kit, GrievanceStatus.ActionTaken);
    }

    [Fact]
    public async Task A_comment_over_1000_characters_is_rejected_with_the_limit_in_the_message()
    {
        (GrvKit kit, Grievance g) = Arrange();

        var result = await kit.Admins.ResolveAsync(g.Id, Remarks(new string('x', 1001)), GrvKit.Pdf(), default);

        Assert.Equal(ApiErrorCodes.ValidationFailed, result.ErrorCode);
        Assert.Contains("1000 characters", result.ErrorMessage);
        AssertUntouched(kit, GrievanceStatus.ActionTaken);
    }

    // ── Invalid files ──

    public static TheoryData<string, byte[], string, string> InvalidFiles => new()
    {
        { "script.exe", new byte[] { 0x4D, 0x5A, 0x90 }, "application/octet-stream", "Only PDF, JPG and PNG" },    // type not allowed
        { "doc.docx", new byte[] { 0x50, 0x4B, 0x03, 0x04 }, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "Only PDF, JPG and PNG" },
        { "noextension", new byte[] { 0x25, 0x50, 0x44, 0x46 }, "application/pdf", "Only PDF, JPG and PNG" },
        { "page.html", new byte[] { 0x3C, 0x68, 0x74, 0x6D, 0x6C }, "text/html", "Only PDF, JPG and PNG" },
        { "fake.pdf", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, "application/pdf", "content does not match" },   // PNG bytes called PDF
        { "fake.png", new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D }, "image/png", "content does not match" },
        { "fake.jpg", new byte[] { 0x3C, 0x73, 0x63, 0x72, 0x69, 0x70, 0x74, 0x3E }, "image/jpeg", "content does not match" },       // a script called a photo
        { "mismatch.pdf", new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D }, "image/jpeg", "content type does not match" },                 // extension and declared type disagree
    };

    [Theory]
    [MemberData(nameof(InvalidFiles))]
    public async Task An_invalid_file_is_a_400_and_nothing_is_stored_or_changed(string name, byte[] bytes, string contentType, string expectedFragment)
    {
        (GrvKit kit, Grievance g) = Arrange();

        var result = await kit.Admins.ResolveAsync(g.Id, Remarks(), GrvKit.Upload(bytes, name, contentType), default);

        Assert.Equal(ApiErrorCodes.ValidationFailed, result.ErrorCode);
        Assert.Contains(expectedFragment, result.ErrorMessage);
        AssertUntouched(kit, GrievanceStatus.ActionTaken);
    }

    [Fact]
    public async Task An_empty_file_is_a_400()
    {
        (GrvKit kit, Grievance g) = Arrange();

        var result = await kit.Admins.ResolveAsync(g.Id, Remarks(), GrvKit.Upload(Array.Empty<byte>(), "empty.pdf", "application/pdf"), default);

        Assert.Equal(ApiErrorCodes.ValidationFailed, result.ErrorCode);
        AssertUntouched(kit, GrievanceStatus.ActionTaken);
    }

    [Fact]
    public async Task A_file_over_5_MB_is_a_400_a_file_of_exactly_5_MB_is_accepted()
    {
        (GrvKit kit, Grievance g) = Arrange();

        var tooBig = await kit.Admins.ResolveAsync(g.Id, Remarks(), GrvKit.PdfOfSize(5 * 1024 * 1024 + 1), default);
        Assert.Equal(ApiErrorCodes.ValidationFailed, tooBig.ErrorCode);
        Assert.Contains("5 MB", tooBig.ErrorMessage);
        AssertUntouched(kit, GrievanceStatus.ActionTaken);

        var exact = await kit.Admins.ResolveAsync(g.Id, Remarks(), GrvKit.PdfOfSize(5 * 1024 * 1024), default);
        Assert.True(exact.IsSuccess, exact.ErrorMessage);
    }

    [Fact]
    public async Task A_stream_that_cannot_seek_is_still_checked_by_its_signature()
    {
        (GrvKit kit, Grievance g) = Arrange();
        using NonSeekableStream fake = new(GrvKit.PngBytes);

        var result = await kit.Admins.ResolveAsync(g.Id, Remarks(), new GrievanceUploadFile(fake, "fake.pdf", "application/pdf", GrvKit.PngBytes.Length), default);

        Assert.Equal(ApiErrorCodes.ValidationFailed, result.ErrorCode);
        AssertUntouched(kit, GrievanceStatus.ActionTaken);
    }

    private sealed class NonSeekableStream : MemoryStream
    {
        public NonSeekableStream(byte[] bytes) : base(bytes) { }
        public override bool CanSeek => false;
    }

    // ── Which grievances can be resolved ──

    [Theory]
    [InlineData(GrievanceStatus.Submitted)]
    [InlineData(GrievanceStatus.UnderReview)]
    public async Task Only_a_grievance_in_action_taken_can_be_resolved(GrievanceStatus status)
    {
        (GrvKit kit, Grievance g) = Arrange(status);

        var result = await kit.Admins.ResolveAsync(g.Id, Remarks(), GrvKit.Pdf(), default);

        Assert.Equal(ApiErrorCodes.ValidationFailed, result.ErrorCode);
        Assert.Contains("only be resolved after action has been taken", result.ErrorMessage);
        AssertUntouched(kit, status);
    }

    [Fact]
    public async Task An_already_closed_grievance_is_a_409_and_keeps_its_first_resolution()
    {
        (GrvKit kit, Grievance g) = Arrange();
        Assert.True((await kit.Admins.ResolveAsync(g.Id, Remarks("First."), GrvKit.Pdf("first.pdf"), default)).IsSuccess);
        string firstKey = kit.Storage.StoreCalls.Single();

        var second = await kit.Admins.ResolveAsync(g.Id, Remarks("Second."), GrvKit.Pdf("second.pdf"), default);

        Assert.Equal(ApiErrorCodes.Conflict, second.ErrorCode);
        Assert.Equal("First.", kit.Repo.Grievances.Single().ClosedRemarks);
        Assert.Single(kit.Storage.StoreCalls);                       // the second file never reached storage
        Assert.True(kit.Storage.Objects.ContainsKey(firstKey));
    }

    [Fact]
    public async Task An_unknown_grievance_is_a_404_and_stores_nothing()
    {
        GrvKit kit = new();

        var result = await kit.Admins.ResolveAsync(12345, Remarks(), GrvKit.Pdf(), default);

        Assert.Equal(ApiErrorCodes.NotFound, result.ErrorCode);
        Assert.Empty(kit.Storage.StoreCalls);
    }

    [Fact]
    public async Task Without_an_admin_identity_in_the_token_nothing_happens()
    {
        (GrvKit kit, Grievance g) = Arrange();
        kit.User.AdminProfileId = null;

        var result = await kit.Admins.ResolveAsync(g.Id, Remarks(), GrvKit.Pdf(), default);

        Assert.Equal(ApiErrorCodes.Unauthorized, result.ErrorCode);
        AssertUntouched(kit, GrievanceStatus.ActionTaken);
    }

    [Fact]
    public void The_resolve_request_has_no_field_that_could_carry_the_acting_admin_or_a_storage_key()
    {
        Assert.Equal(new[] { "Remarks" }, typeof(ResolveGrievanceRequestDto).GetProperties().Select(p => p.Name));
        Assert.Equal(new[] { "File", "Remarks" }, typeof(NSG.Mobile.API.Controllers.ResolveGrievanceForm).GetProperties().Select(p => p.Name).OrderBy(n => n));
    }

    // ── Failures keep status, comment, file and audit consistent ──

    [Fact]
    public async Task When_storage_is_down_nothing_is_changed_and_the_answer_is_a_503()
    {
        (GrvKit kit, Grievance g) = Arrange();
        kit.Storage.UnavailableOnStore = true;

        var result = await kit.Admins.ResolveAsync(g.Id, Remarks(), GrvKit.Pdf(), default);

        Assert.Equal(GrievanceErrorCodes.StorageUnavailable, result.ErrorCode);
        Assert.Equal(0, kit.Repo.ResolveCalls);
        AssertUntouched(kit, GrievanceStatus.ActionTaken);
    }

    [Fact]
    public async Task A_host_without_private_storage_refuses_before_touching_anything()
    {
        (GrvKit kit, Grievance g) = Arrange();
        kit.Availability.IsAvailable = false;

        var result = await kit.Admins.ResolveAsync(g.Id, Remarks(), GrvKit.Pdf(), default);

        Assert.Equal(GrievanceErrorCodes.StorageUnavailable, result.ErrorCode);
        Assert.Equal(0, kit.Repo.ResolveCalls);
        AssertUntouched(kit, GrievanceStatus.ActionTaken);
    }

    [Fact]
    public async Task When_recording_fails_the_stored_file_is_removed_again_and_the_grievance_is_untouched()
    {
        (GrvKit kit, Grievance g) = Arrange();
        kit.Repo.ThrowOnResolve = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => kit.Admins.ResolveAsync(g.Id, Remarks(), GrvKit.Pdf(), default));

        string key = Assert.Single(kit.Storage.StoreCalls);
        Assert.Contains(key, kit.Storage.Deleted);
        Assert.Empty(kit.Storage.Objects);                                       // no orphan left behind
        Assert.Equal(GrievanceStatus.ActionTaken, kit.Repo.Grievances.Single().CurrentStatus);
        Assert.Null(kit.Repo.Grievances.Single().ResolutionDocument);
        Assert.Empty(kit.Repo.Logs);
        Assert.DoesNotContain(kit.Audit.Entries, e => e.Action == AuditAction.GrievanceStatusChanged);
    }

    [Fact]
    public async Task When_someone_else_resolved_it_first_the_answer_is_a_409_and_our_file_is_removed()
    {
        (GrvKit kit, Grievance g) = Arrange();
        kit.Repo.ReportStatusChangedByOthers = GrievanceStatus.Closed;

        var result = await kit.Admins.ResolveAsync(g.Id, Remarks(), GrvKit.Pdf(), default);

        Assert.Equal(ApiErrorCodes.Conflict, result.ErrorCode);
        Assert.Contains("Closed", result.ErrorMessage);
        Assert.Empty(kit.Storage.Objects);
        Assert.Empty(kit.Repo.Logs);
        Assert.DoesNotContain(kit.Audit.Entries, e => e.Action == AuditAction.GrievanceStatusChanged);
    }

    [Fact]
    public async Task A_failed_cleanup_is_logged_for_manual_follow_up_and_the_original_error_still_surfaces()
    {
        (GrvKit kit, Grievance g) = Arrange();
        kit.Repo.ThrowOnResolve = true;
        kit.Storage.DeleteFails = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => kit.Admins.ResolveAsync(g.Id, Remarks(), GrvKit.Pdf(), default));

        Assert.Contains(kit.AdminLog.Entries, e => e.Level == Microsoft.Extensions.Logging.LogLevel.Error && e.Message.Contains("needs manual clean-up"));
    }

    // ── The status endpoint no longer closes ──

    [Fact]
    public async Task The_plain_status_change_refuses_to_close_and_points_to_the_resolve_endpoint()
    {
        (GrvKit kit, Grievance g) = Arrange();

        var result = await kit.Admins.UpdateStatusAsync(g.Id, new UpdateGrievanceStatusRequestDto { ToStatus = 4, Remarks = "Done." }, default);

        Assert.Equal(ApiErrorCodes.ValidationFailed, result.ErrorCode);
        Assert.Equal(GrievanceMessages.ResolveViaResolveEndpoint, result.ErrorMessage);
        Assert.Equal(0, kit.Repo.ChangeStatusCalls);
        AssertUntouched(kit, GrievanceStatus.ActionTaken);
    }

    [Fact]
    public void The_status_request_validator_no_longer_accepts_4()
    {
        UpdateGrievanceStatusRequestValidator validator = new();
        Assert.False(validator.Validate(new UpdateGrievanceStatusRequestDto { ToStatus = 4, Remarks = "Done." }).IsValid);
        Assert.True(validator.Validate(new UpdateGrievanceStatusRequestDto { ToStatus = 3, Remarks = "Done." }).IsValid);
    }

    // ── Reading the resolution: the owner and the admin ──

    private static async Task<(GrvKit Kit, long Id)> ResolvedAsync()
    {
        (GrvKit kit, Grievance g) = Arrange();
        Assert.True((await kit.Admins.ResolveAsync(g.Id, Remarks("Reimbursed in full."), GrvKit.Pdf("proof.pdf"), default)).IsSuccess);
        return (kit, g.Id);
    }

    [Fact]
    public async Task The_owner_sees_the_resolution_comment_and_reference_and_can_download_the_file()
    {
        (GrvKit kit, long id) = await ResolvedAsync();

        GrievanceDetailDto detail = (await kit.Employees.GetMineByIdAsync(id, default)).Data!;
        Assert.Equal("Reimbursed in full.", detail.ClosedRemarks);
        Assert.Equal(kit.Repo.Grievances.Single().ResolutionDocument, detail.ResolutionDocument);

        var download = await kit.Employees.OpenMyResolutionDocumentAsync(id, default);

        Assert.True(download.IsSuccess, download.ErrorMessage);
        Assert.Equal("application/pdf", download.Data!.ContentType);
        Assert.StartsWith("resolution-GRV-", download.Data.FileName);
        Assert.EndsWith(".pdf", download.Data.FileName);
        using MemoryStream copy = new();
        await download.Data.Stream.CopyToAsync(copy);
        Assert.Equal(GrvKit.PdfBytes, copy.ToArray());
        Assert.Contains(kit.Audit.Entries, e => e.Action == AuditAction.DocumentDownloaded && e.Data!.Contains("resolutionDocument") && e.Data.Contains("Employee"));
    }

    [Fact]
    public async Task Another_employee_cannot_download_it_and_is_told_the_grievance_does_not_exist()
    {
        (GrvKit kit, long id) = await ResolvedAsync();
        kit.User.ProfileId = GrvKit.OtherEmployee;
        kit.Storage.GetCalls.Clear();

        var download = await kit.Employees.OpenMyResolutionDocumentAsync(id, default);
        var detail = await kit.Employees.GetMineByIdAsync(id, default);

        Assert.Equal(ApiErrorCodes.NotFound, download.ErrorCode);
        Assert.Equal(GrievanceMessages.GrievanceNotFound, download.ErrorMessage);
        Assert.Equal(ApiErrorCodes.NotFound, detail.ErrorCode);
        Assert.Empty(kit.Storage.GetCalls);                                      // storage was never asked
    }

    [Fact]
    public async Task Without_a_mobile_identity_the_resolution_download_is_refused()
    {
        (GrvKit kit, long id) = await ResolvedAsync();
        kit.User.ProfileId = null;

        var download = await kit.Employees.OpenMyResolutionDocumentAsync(id, default);

        Assert.Equal(ApiErrorCodes.Unauthorized, download.ErrorCode);
    }

    [Fact]
    public async Task A_grievance_without_a_resolution_file_answers_404_with_a_message()
    {
        (GrvKit kit, Grievance g) = Arrange(GrievanceStatus.UnderReview);

        var download = await kit.Employees.OpenMyResolutionDocumentAsync(g.Id, default);
        var adminDownload = await kit.Admins.OpenResolutionDocumentAsync(g.Id, default);

        Assert.Equal(ApiErrorCodes.NotFound, download.ErrorCode);
        Assert.Equal(GrievanceMessages.ResolutionDocumentNotFound, download.ErrorMessage);
        Assert.Equal(ApiErrorCodes.NotFound, adminDownload.ErrorCode);
        Assert.Null((await kit.Employees.GetMineByIdAsync(g.Id, default)).Data!.ResolutionDocument);
    }

    [Fact]
    public async Task A_grievance_closed_before_resolution_files_existed_has_no_file_and_still_reads_normally()
    {
        (GrvKit kit, Grievance g) = Arrange(GrievanceStatus.Closed);

        var detail = await kit.Employees.GetMineByIdAsync(g.Id, default);

        Assert.True(detail.IsSuccess);
        Assert.Null(detail.Data!.ResolutionDocument);
        Assert.Equal(ApiErrorCodes.NotFound, (await kit.Employees.OpenMyResolutionDocumentAsync(g.Id, default)).ErrorCode);
    }

    [Fact]
    public async Task A_recorded_but_missing_object_is_a_404_and_an_outage_is_a_503()
    {
        (GrvKit kit, long id) = await ResolvedAsync();

        kit.Storage.UnavailableOnGet = true;
        Assert.Equal(GrievanceErrorCodes.StorageUnavailable, (await kit.Employees.OpenMyResolutionDocumentAsync(id, default)).ErrorCode);

        kit.Storage.UnavailableOnGet = false;
        kit.Storage.Objects.Clear();
        var missing = await kit.Employees.OpenMyResolutionDocumentAsync(id, default);
        Assert.Equal(ApiErrorCodes.NotFound, missing.ErrorCode);
        Assert.Equal(GrievanceMessages.DocumentFileMissing, missing.ErrorMessage);
    }

    [Fact]
    public async Task The_admin_can_read_detail_and_download_the_resolution_file_and_it_is_audited()
    {
        (GrvKit kit, long id) = await ResolvedAsync();

        GrievanceAdminDetailDto detail = (await kit.Admins.GetByIdAsync(id, default)).Data!;
        Assert.Equal(kit.Repo.Grievances.Single().ResolutionDocument, detail.ResolutionDocument);
        Assert.Equal(GrvKit.Admin, detail.ClosedBy);

        var download = await kit.Admins.OpenResolutionDocumentAsync(id, default);

        Assert.True(download.IsSuccess, download.ErrorMessage);
        Assert.Contains(kit.Audit.Entries, e => e.Action == AuditAction.DocumentDownloaded && e.Data!.Contains("resolutionDocument") && e.Data.Contains("Admin"));
    }

    [Fact]
    public async Task Without_an_admin_identity_the_admin_download_is_refused()
    {
        (GrvKit kit, long id) = await ResolvedAsync();
        kit.User.AdminProfileId = null;

        Assert.Equal(ApiErrorCodes.Unauthorized, (await kit.Admins.OpenResolutionDocumentAsync(id, default)).ErrorCode);
    }

    // ── The employee side keeps its own limits ──

    [Fact]
    public async Task Employee_attachments_still_accept_only_pdf_and_jpg_not_png()
    {
        GrvKit kit = new();
        GrievanceCategory category = kit.Repo.AddCategory("Medical");
        CreateGrievanceRequestDto request = new() { CategoryId = category.Id, Subject = "Subject", Note = "A note that is long enough." };

        var result = await kit.Employees.CreateAsync(request, new[] { GrvKit.Png() }, default);

        Assert.Equal(ApiErrorCodes.ValidationFailed, result.ErrorCode);
        Assert.Contains("Only PDF and JPG", result.ErrorMessage);
        Assert.Empty(kit.Storage.StoreCalls);
    }

    private static void AssertUntouched(GrvKit kit, GrievanceStatus expectedStatus)
    {
        Assert.Empty(kit.Storage.StoreCalls);
        Assert.Empty(kit.Storage.Objects);
        Grievance stored = kit.Repo.Grievances.Single();
        Assert.Equal(expectedStatus, stored.CurrentStatus);
        Assert.Null(stored.ResolutionDocument);
        Assert.Null(stored.ClosedBy);
        Assert.Empty(kit.Repo.Logs);
        Assert.DoesNotContain(kit.Audit.Entries, e => e.Action is AuditAction.GrievanceStatusChanged or AuditAction.GrievanceDocumentUploaded);
    }
}
