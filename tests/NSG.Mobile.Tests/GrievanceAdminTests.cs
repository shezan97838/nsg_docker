using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Grievances;
using NSG.Mobile.Application.Grievances.DTOs;
using NSG.Mobile.Application.Grievances.Services;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Tests;

/// <summary>Grievance Admin: list filters, employee identity, audited detail and downloads, dashboard numbers, comments and forwarding.</summary>
public class GrievanceAdminTests
{
    private static (GrvKit Kit, GrievanceCategory Category) Arrange()
    {
        GrvKit kit = new();
        kit.Repo.Employees[GrvKit.Employee] = new GrievanceEmployeeInfo(GrvKit.Employee, "NSG-1001", "Asha Verma", "13 SRG");
        return (kit, kit.Repo.AddCategory("Medical"));
    }

    [Fact]
    public async Task The_list_shows_every_employees_grievances_with_name_nsg_number_and_unit()
    {
        (GrvKit kit, GrievanceCategory category) = Arrange();
        kit.Repo.AddGrievance(GrvKit.Employee, category);
        kit.Repo.AddGrievance(GrvKit.OtherEmployee, category);               // no personnel record for this one

        var result = await kit.Admins.GetPagedAsync(1, 20, null, null, null, null, null, null, default);

        Assert.Equal(2, result.Data!.TotalCount);
        GrievanceAdminSummaryDto known = result.Data.Items.Single(i => i.CreatedBy == GrvKit.Employee);
        Assert.Equal("NSG-1001", known.Employee!.NsgNumber);
        Assert.Equal("Asha Verma", known.Employee.Name);
        Assert.Equal("13 SRG", known.Employee.Unit);
        Assert.Null(result.Data.Items.Single(i => i.CreatedBy == GrvKit.OtherEmployee).Employee);
        Assert.Null(kit.Repo.LastFilter!.CreatedBy);                         // admins are not limited to one owner
    }

    [Fact]
    public async Task Filters_are_passed_on_and_dates_are_whole_ist_days()
    {
        (GrvKit kit, GrievanceCategory category) = Arrange();

        await kit.Admins.GetPagedAsync(1, 20, GrievanceStatus.UnderReview, category.Id, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), "GRV-2026", 42, default);

        GrievanceListFilter f = kit.Repo.LastFilter!;
        Assert.Equal(GrievanceStatus.UnderReview, f.Status);
        Assert.Equal(category.Id, f.CategoryId);
        Assert.Equal(new DateTime(2026, 10, 1), f.FromIst);
        Assert.Equal(new DateTime(2026, 11, 1), f.ToIstExclusive);           // the whole of 31 October is included
        Assert.Equal("GRV-2026", f.TicketSearch);
        Assert.Equal(42, f.AssignedTo);
    }

    [Fact]
    public async Task A_reversed_date_range_and_a_malformed_search_are_400s()
    {
        (GrvKit kit, _) = Arrange();

        var reversed = await kit.Admins.GetPagedAsync(1, 20, null, null, new DateOnly(2026, 10, 31), new DateOnly(2026, 10, 1), null, null, default);
        var badSearch = await kit.Admins.GetPagedAsync(1, 20, null, null, null, null, "'; DROP TABLE grievance;--", null, default);
        var longSearch = await kit.Admins.GetPagedAsync(1, 20, null, null, null, null, new string('A', 31), null, default);

        Assert.All(new[] { reversed.ErrorCode, badSearch.ErrorCode, longSearch.ErrorCode }, c => Assert.Equal(ApiErrorCodes.ValidationFailed, c));
    }

    [Fact]
    public async Task Opening_the_detail_is_audited_with_ids_only_and_shows_the_employee()
    {
        (GrvKit kit, GrievanceCategory category) = Arrange();
        Grievance g = kit.Repo.AddGrievance(GrvKit.Employee, category);

        var result = await kit.Admins.GetByIdAsync(g.Id, default);

        Assert.True(result.IsSuccess);
        Assert.Equal("Asha Verma", result.Data!.Employee!.Name);
        var entry = Assert.Single(kit.Audit.Entries, e => e.Action == AuditAction.GrievanceViewed);
        Assert.Equal(g.Id.ToString(), entry.EntityId);
        Assert.Contains($"\"actorId\":{GrvKit.Admin}", entry.Data);
        Assert.DoesNotContain("Asha", entry.Data);                           // no personal data in the audit row
        Assert.DoesNotContain("NSG-1001", entry.Data);
    }

    [Fact]
    public async Task A_status_change_does_not_write_a_view_audit_row_and_the_response_carries_the_employee()
    {
        (GrvKit kit, GrievanceCategory category) = Arrange();
        Grievance g = kit.Repo.AddGrievance(GrvKit.Employee, category);

        var result = await kit.Admins.UpdateStatusAsync(g.Id, new UpdateGrievanceStatusRequestDto { ToStatus = 2 }, default);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(kit.Audit.Entries, e => e.Action == AuditAction.GrievanceViewed);
        Assert.Equal("Asha Verma", result.Data!.Data!.Employee!.Name);
        Assert.Equal(2, Assert.Single(result.Data.Data.Timeline).ToStatus);
    }

    [Fact]
    public async Task A_missing_grievance_is_a_404_for_the_admin_and_not_audited()
    {
        (GrvKit kit, _) = Arrange();
        var result = await kit.Admins.GetByIdAsync(555, default);
        Assert.Equal(ApiErrorCodes.NotFound, result.ErrorCode);
        Assert.DoesNotContain(kit.Audit.Entries, e => e.Action == AuditAction.GrievanceViewed);
    }

    // ── Downloads (employee and admin) ──

    private static (GrvKit Kit, Grievance Grievance, GrievanceDocumentRecord Document) WithDocument()
    {
        (GrvKit kit, GrievanceCategory category) = Arrange();
        GrievanceDocumentRecord doc = new("grievances/2026/10/44444444-4444-4444-4444-444444444444.pdf", "Medical report.pdf", "application/pdf", GrvKit.PdfBytes.Length, new DateTime(2026, 10, 15, 11, 0, 0));
        Grievance g = kit.Repo.AddGrievance(GrvKit.Employee, category, documents: new[] { doc });
        kit.Storage.Objects[doc.Key] = GrvKit.PdfBytes;
        return (kit, g, doc);
    }

    [Fact]
    public async Task Both_the_owner_and_an_admin_can_download_and_each_download_is_audited_with_ids_only()
    {
        (GrvKit kit, Grievance g, GrievanceDocumentRecord doc) = WithDocument();

        var mine = await kit.Employees.OpenMyDocumentAsync(g.Id, 0, default);
        var admin = await kit.Admins.OpenDocumentAsync(g.Id, 0, default);

        Assert.True(mine.IsSuccess, mine.ErrorMessage);
        Assert.True(admin.IsSuccess, admin.ErrorMessage);
        Assert.Equal("Medical report.pdf", mine.Data!.FileName);             // the stored (sanitised) name
        Assert.Equal("application/pdf", mine.Data.ContentType);              // the stored validated type, not what storage reports

        var downloads = kit.Audit.Entries.Where(e => e.Action == AuditAction.DocumentDownloaded).ToList();
        Assert.Equal(2, downloads.Count);
        Assert.Contains(downloads, d => d.Data!.Contains("\"actorType\":\"Employee\"") && d.Data.Contains($"\"actorId\":{GrvKit.Employee}"));
        Assert.Contains(downloads, d => d.Data!.Contains("\"actorType\":\"Admin\"") && d.Data.Contains($"\"actorId\":{GrvKit.Admin}"));
        Assert.All(downloads, d => Assert.DoesNotContain("Medical report", d.Data));      // no file names (may hold personal data)
        Assert.All(downloads, d => Assert.DoesNotContain(doc.Key, d.Data));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(99)]
    public async Task A_document_index_that_does_not_exist_is_a_404(int index)
    {
        (GrvKit kit, Grievance g, _) = WithDocument();
        var result = await kit.Admins.OpenDocumentAsync(g.Id, index, default);
        Assert.Equal(ApiErrorCodes.NotFound, result.ErrorCode);
        Assert.Empty(kit.Storage.GetCalls);
    }

    [Fact]
    public async Task A_recorded_document_missing_from_storage_is_a_404_and_a_storage_outage_a_503_code()
    {
        (GrvKit kit, Grievance g, GrievanceDocumentRecord doc) = WithDocument();

        kit.Storage.Objects.Remove(doc.Key);
        Assert.Equal(GrievanceMessages.DocumentFileMissing, (await kit.Admins.OpenDocumentAsync(g.Id, 0, default)).ErrorMessage);

        kit.Storage.UnavailableOnGet = true;
        Assert.Equal(GrievanceErrorCodes.StorageUnavailable, (await kit.Admins.OpenDocumentAsync(g.Id, 0, default)).ErrorCode);
        Assert.DoesNotContain(kit.Audit.Entries, e => e.Action == AuditAction.DocumentDownloaded);   // nothing was downloaded
    }

    [Fact]
    public async Task A_stored_content_type_that_is_not_pdf_or_jpeg_is_served_as_an_opaque_download()
    {
        (GrvKit kit, Grievance g, GrievanceDocumentRecord doc) = WithDocument();
        kit.Repo.Grievances.Single().DocumentsJson = GrievanceDocumentRules.SerializeDocuments(new List<GrievanceDocumentRecord> { doc with { ContentType = "text/html" } });

        var result = await kit.Admins.OpenDocumentAsync(g.Id, 0, default);

        Assert.Equal("application/octet-stream", result.Data!.ContentType);  // never an active content type
    }

    // ── Dashboard ──

    [Fact]
    public async Task The_dashboard_counts_all_four_statuses_pending_as_1_to_3_and_the_resolution_time()
    {
        (GrvKit kit, GrievanceCategory category) = Arrange();
        kit.Repo.AddGrievance(GrvKit.Employee, category, GrievanceStatus.Submitted);
        kit.Repo.AddGrievance(GrvKit.Employee, category, GrievanceStatus.Submitted);
        kit.Repo.AddGrievance(GrvKit.Employee, category, GrievanceStatus.ActionTaken);
        kit.Repo.AddGrievance(GrvKit.Employee, category, GrievanceStatus.Closed);   // closed 3 days after creation

        var result = await kit.Dashboard.GetSummaryAsync(null, null, default);

        GrievanceDashboardDto d = result.Data!;
        Assert.Equal(4, d.TotalCount);
        Assert.Equal(3, d.PendingCount);
        Assert.Equal(new[] { 1, 2, 3, 4 }, d.ByStatus.Select(s => s.Status).ToArray());            // UnderReview is present with a zero
        Assert.Equal(new[] { 2, 0, 1, 1 }, d.ByStatus.Select(s => s.Count).ToArray());
        Assert.Equal(new[] { true, true, true, false }, d.ByStatus.Select(s => s.IsPending).ToArray());
        Assert.Equal(3.0, d.AverageResolutionDays);
        Assert.Equal("2026-10", Assert.Single(d.ByMonth).Month);
        Assert.Equal("Medical", Assert.Single(d.ByCategory).CategoryName);
    }

    [Fact]
    public async Task With_nothing_submitted_the_dashboard_still_lists_all_statuses_with_zero_and_no_average()
    {
        GrvKit kit = new();
        var result = await kit.Dashboard.GetSummaryAsync(null, null, default);
        Assert.Equal(0, result.Data!.TotalCount);
        Assert.Equal(4, result.Data.ByStatus.Count);
        Assert.Null(result.Data.AverageResolutionDays);
    }

    [Fact]
    public async Task A_reversed_dashboard_date_range_is_a_400()
    {
        GrvKit kit = new();
        var result = await kit.Dashboard.GetSummaryAsync(new DateOnly(2026, 10, 31), new DateOnly(2026, 10, 1), default);
        Assert.Equal(ApiErrorCodes.ValidationFailed, result.ErrorCode);
    }

    // ── Comments ──

    [Fact]
    public async Task A_comment_is_appended_to_the_history_without_changing_the_status()
    {
        (GrvKit kit, GrievanceCategory category) = Arrange();
        Grievance g = kit.Repo.AddGrievance(GrvKit.Employee, category, GrievanceStatus.UnderReview);

        var result = await kit.Admins.AddCommentAsync(g.Id, new AddGrievanceCommentRequestDto { Comment = "  Waiting for the unit report.  " }, default);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal("Comment added successfully.", result.Data!.Message);
        Assert.Equal(GrievanceStatus.UnderReview, kit.Repo.Grievances.Single().CurrentStatus);
        GrievanceStatusLog log = Assert.Single(kit.Repo.Logs);
        Assert.Equal(GrievanceLogAction.Comment, log.LogAction);
        Assert.Equal("Waiting for the unit report.", log.Remarks);
        Assert.Equal(GrievanceStatus.UnderReview, log.FromStatus);
        Assert.Equal(GrievanceStatus.UnderReview, log.ToStatus);
        Assert.Equal(GrvKit.Admin, log.ChangedBy);                           // from the token
        Assert.Equal(GrievanceActorType.Admin, log.ChangedByType);
        Assert.Equal("Comment", result.Data.Data!.Timeline.Last().ActionName);
        var audit = Assert.Single(kit.Audit.Entries, e => e.Action == AuditAction.GrievanceCommentAdded);
        Assert.DoesNotContain("unit report", audit.Data);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_comment_is_rejected(string comment)
    {
        (GrvKit kit, GrievanceCategory category) = Arrange();
        Grievance g = kit.Repo.AddGrievance(GrvKit.Employee, category);

        var result = await kit.Admins.AddCommentAsync(g.Id, new AddGrievanceCommentRequestDto { Comment = comment }, default);

        Assert.Equal(ApiErrorCodes.ValidationFailed, result.ErrorCode);
        Assert.Empty(kit.Repo.Logs);
    }

    [Fact]
    public async Task A_closed_grievance_takes_no_comment_and_an_unknown_one_is_a_404()
    {
        (GrvKit kit, GrievanceCategory category) = Arrange();
        Grievance closed = kit.Repo.AddGrievance(GrvKit.Employee, category, GrievanceStatus.Closed);

        var onClosed = await kit.Admins.AddCommentAsync(closed.Id, new AddGrievanceCommentRequestDto { Comment = "Late." }, default);
        var missing = await kit.Admins.AddCommentAsync(999, new AddGrievanceCommentRequestDto { Comment = "Hello." }, default);

        Assert.Equal(ApiErrorCodes.Conflict, onClosed.ErrorCode);
        Assert.Equal(GrievanceMessages.GrievanceClosed, onClosed.ErrorMessage);
        Assert.Equal(ApiErrorCodes.NotFound, missing.ErrorCode);
        Assert.Empty(kit.Repo.Logs);
    }

    [Fact]
    public async Task Without_an_admin_identity_nothing_is_commented_or_forwarded()
    {
        (GrvKit kit, GrievanceCategory category) = Arrange();
        Grievance g = kit.Repo.AddGrievance(GrvKit.Employee, category);
        kit.User.AdminProfileId = null;

        Assert.Equal(ApiErrorCodes.Unauthorized, (await kit.Admins.AddCommentAsync(g.Id, new AddGrievanceCommentRequestDto { Comment = "x" }, default)).ErrorCode);
        Assert.Equal(ApiErrorCodes.Unauthorized, (await kit.Admins.ForwardAsync(g.Id, new ForwardGrievanceRequestDto { ToAdminId = 42 }, default)).ErrorCode);
        Assert.Empty(kit.Repo.Logs);
    }

    // ── Forwarding / reassigning ──

    [Fact]
    public async Task Forwarding_assigns_the_grievance_and_records_who_to_whom_and_why()
    {
        (GrvKit kit, GrievanceCategory category) = Arrange();
        kit.Repo.ActiveGrievanceAdmins.Add(42);
        Grievance g = kit.Repo.AddGrievance(GrvKit.Employee, category, GrievanceStatus.UnderReview);

        var result = await kit.Admins.ForwardAsync(g.Id, new ForwardGrievanceRequestDto { ToAdminId = 42, Remarks = "  Falls under your unit.  " }, default);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal("Grievance forwarded successfully.", result.Data!.Message);
        Assert.Equal(42, kit.Repo.Grievances.Single().AssignedTo);
        Assert.Equal(42, result.Data.Data!.AssignedTo);
        Assert.Equal(GrievanceStatus.UnderReview, kit.Repo.Grievances.Single().CurrentStatus);   // forwarding is not a status change
        GrievanceStatusLog log = Assert.Single(kit.Repo.Logs);
        Assert.Equal(GrievanceLogAction.Forward, log.LogAction);
        Assert.Equal(42, log.ForwardedTo);
        Assert.Equal("Falls under your unit.", log.Remarks);
        Assert.Equal(GrvKit.Admin, log.ChangedBy);
        var admin = result.Data.Data.Timeline.Last();
        Assert.Equal("Forwarded", admin.ActionName);
        Assert.Equal(42, admin.ForwardedTo);
        Assert.Single(kit.Audit.Entries, e => e.Action == AuditAction.GrievanceForwarded);
    }

    [Fact]
    public async Task Reassigning_to_another_admin_adds_a_second_history_row_and_the_same_admin_again_is_a_409()
    {
        (GrvKit kit, GrievanceCategory category) = Arrange();
        kit.Repo.ActiveGrievanceAdmins.Add(42);
        kit.Repo.ActiveGrievanceAdmins.Add(43);
        Grievance g = kit.Repo.AddGrievance(GrvKit.Employee, category);

        Assert.True((await kit.Admins.ForwardAsync(g.Id, new ForwardGrievanceRequestDto { ToAdminId = 42 }, default)).IsSuccess);
        var again = await kit.Admins.ForwardAsync(g.Id, new ForwardGrievanceRequestDto { ToAdminId = 42 }, default);
        Assert.True((await kit.Admins.ForwardAsync(g.Id, new ForwardGrievanceRequestDto { ToAdminId = 43 }, default)).IsSuccess);

        Assert.Equal(ApiErrorCodes.Conflict, again.ErrorCode);
        Assert.Equal(43, kit.Repo.Grievances.Single().AssignedTo);
        Assert.Equal(new long?[] { 42, 43 }, kit.Repo.Logs.Select(l => l.ForwardedTo));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    [InlineData(-5L)]
    [InlineData(77L)]                                                        // not an active Grievance Admin
    public async Task The_target_must_be_an_active_grievance_admin(long? target)
    {
        (GrvKit kit, GrievanceCategory category) = Arrange();
        Grievance g = kit.Repo.AddGrievance(GrvKit.Employee, category);

        var result = await kit.Admins.ForwardAsync(g.Id, new ForwardGrievanceRequestDto { ToAdminId = target }, default);

        Assert.Equal(ApiErrorCodes.ValidationFailed, result.ErrorCode);
        Assert.Null(kit.Repo.Grievances.Single().AssignedTo);
        Assert.Empty(kit.Repo.Logs);
    }

    [Fact]
    public async Task A_closed_grievance_cannot_be_forwarded_and_an_unknown_one_is_a_404()
    {
        (GrvKit kit, GrievanceCategory category) = Arrange();
        kit.Repo.ActiveGrievanceAdmins.Add(42);
        Grievance closed = kit.Repo.AddGrievance(GrvKit.Employee, category, GrievanceStatus.Closed);

        Assert.Equal(ApiErrorCodes.Conflict, (await kit.Admins.ForwardAsync(closed.Id, new ForwardGrievanceRequestDto { ToAdminId = 42 }, default)).ErrorCode);
        Assert.Equal(ApiErrorCodes.NotFound, (await kit.Admins.ForwardAsync(999, new ForwardGrievanceRequestDto { ToAdminId = 42 }, default)).ErrorCode);
        Assert.Empty(kit.Repo.Logs);
    }

    [Fact]
    public void The_new_requests_have_no_field_that_could_carry_the_acting_admin()
    {
        Assert.Equal(new[] { "Comment" }, typeof(AddGrievanceCommentRequestDto).GetProperties().Select(p => p.Name));
        Assert.Equal(new[] { "Remarks", "ToAdminId" }, typeof(ForwardGrievanceRequestDto).GetProperties().Select(p => p.Name).OrderBy(n => n));
    }

    [Fact]
    public void The_comment_and_forward_validators_check_the_structure()
    {
        var comment = new NSG.Mobile.Application.Grievances.Validators.AddGrievanceCommentRequestValidator();
        var forward = new NSG.Mobile.Application.Grievances.Validators.ForwardGrievanceRequestValidator();

        Assert.True(comment.Validate(new AddGrievanceCommentRequestDto { Comment = "ok" }).IsValid);
        Assert.False(comment.Validate(new AddGrievanceCommentRequestDto { Comment = " " }).IsValid);
        Assert.False(comment.Validate(new AddGrievanceCommentRequestDto { Comment = new string('x', GrievanceLimits.RemarksMaxLength + 1) }).IsValid);
        Assert.True(forward.Validate(new ForwardGrievanceRequestDto { ToAdminId = 5 }).IsValid);
        Assert.False(forward.Validate(new ForwardGrievanceRequestDto()).IsValid);
        Assert.False(forward.Validate(new ForwardGrievanceRequestDto { ToAdminId = 0 }).IsValid);
    }
}
