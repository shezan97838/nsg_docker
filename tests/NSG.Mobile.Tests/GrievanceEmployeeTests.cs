using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Grievances;
using NSG.Mobile.Application.Grievances.DTOs;
using NSG.Mobile.Application.Grievances.Services;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Tests;

/// <summary>What an employee (any mobile token) can do: raise grievances, see only their own, never someone else's.</summary>
public class GrievanceEmployeeTests
{
    private static readonly IReadOnlyList<GrievanceUploadFile> NoFiles = Array.Empty<GrievanceUploadFile>();

    private static CreateGrievanceRequestDto Create(int categoryId, string note = "This is a detailed note about the problem.")
        => new() { CategoryId = categoryId, Subject = "  Leave not granted  ", Note = note };

    // ── Creating ──

    [Fact]
    public async Task A_grievance_is_created_as_Submitted_for_the_token_user_and_returns_both_ids()
    {
        GrvKit kit = new();
        kit.User.ProfileId = 777;
        GrievanceCategory category = kit.Repo.AddCategory("Medical");

        var result = await kit.Employees.CreateAsync(Create(category.Id), NoFiles, default);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        GrievanceDetailDto dto = result.Data!.Data!;
        Assert.True(dto.Id > 0);                                             // numeric key for routes
        Assert.Matches(@"^NSG-\d{4}-\d{6}$", dto.GrievanceId);               // the ticket number
        Assert.Contains(dto.GrievanceId, result.Data.Message);
        Assert.Equal((int)GrievanceStatus.Submitted, dto.Status);
        Assert.Equal("Leave not granted", dto.Subject);                      // trimmed

        Grievance stored = Assert.Single(kit.Repo.Grievances);
        Assert.Equal(777, stored.CreatedBy);                                 // from the token
        GrievanceStatusLog first = Assert.Single(kit.Repo.Logs);
        Assert.Null(first.FromStatus);
        Assert.Equal(GrievanceStatus.Submitted, first.ToStatus);
        Assert.Equal(GrievanceActorType.Employee, first.ChangedByType);
        Assert.Equal(777, first.ChangedBy);
    }

    [Fact]
    public void The_create_request_cannot_carry_a_user_or_status_so_nothing_can_be_spoofed()
    {
        string[] names = typeof(CreateGrievanceRequestDto).GetProperties().Select(p => p.Name).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "CategoryId", "Note", "Subject" }, names);
    }

    [Fact]
    public async Task Creation_is_audited_with_ids_only()
    {
        GrvKit kit = new();
        GrievanceCategory category = kit.Repo.AddCategory("Medical");

        await kit.Employees.CreateAsync(Create(category.Id, "Detailed note about the matter at hand."), NoFiles, default);

        var entry = Assert.Single(kit.Audit.Entries, e => e.Action == AuditAction.GrievanceCreated);
        Assert.DoesNotContain("Leave not granted", entry.Data);              // no subject or note text
        Assert.DoesNotContain("Detailed note", entry.Data);
        Assert.Contains("\"actorId\":501", entry.Data);
    }

    [Fact]
    public async Task A_new_grievance_starts_unassigned_until_an_admin_forwards_it()
    {
        GrvKit kit = new();
        GrievanceCategory category = kit.Repo.AddCategory("Medical");

        var result = await kit.Employees.CreateAsync(Create(category.Id), NoFiles, default);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Null(kit.Repo.Grievances.Single().AssignedTo);
    }

    [Fact]
    public async Task An_unknown_inactive_or_deleted_category_is_a_400()
    {
        GrvKit kit = new();
        GrievanceCategory inactive = kit.Repo.AddCategory("Old", active: false);
        GrievanceCategory deleted = kit.Repo.AddCategory("Gone", deleted: true);

        Assert.Equal(ApiErrorCodes.ValidationFailed, (await kit.Employees.CreateAsync(Create(deleted.Id), NoFiles, default)).ErrorCode);
        Assert.Equal(ApiErrorCodes.ValidationFailed, (await kit.Employees.CreateAsync(Create(inactive.Id), NoFiles, default)).ErrorCode);
        Assert.Equal(ApiErrorCodes.ValidationFailed, (await kit.Employees.CreateAsync(Create(9999), NoFiles, default)).ErrorCode);
        Assert.Empty(kit.Repo.Grievances);
    }

    [Fact]
    public async Task The_note_must_reach_the_minimum_length()
    {
        GrvKit kit = new();
        GrievanceCategory category = kit.Repo.AddCategory("Medical");

        var tooShort = await kit.Employees.CreateAsync(Create(category.Id, "Too short"), NoFiles, default);
        var longEnough = await kit.Employees.CreateAsync(Create(category.Id, "This note is long enough."), NoFiles, default);

        Assert.Equal(ApiErrorCodes.ValidationFailed, tooShort.ErrorCode);
        Assert.Contains("10 characters", tooShort.ErrorMessage);
        Assert.True(longEnough.IsSuccess);
    }

    [Fact]
    public async Task When_no_ticket_number_can_be_allocated_the_answer_is_the_ticket_unavailable_code()
    {
        GrvKit kit = new();
        kit.Repo.TicketsUnavailable = true;
        GrievanceCategory category = kit.Repo.AddCategory("Medical");

        var result = await kit.Employees.CreateAsync(Create(category.Id), NoFiles, default);

        Assert.Equal(GrievanceErrorCodes.TicketUnavailable, result.ErrorCode);
        Assert.Empty(kit.Repo.Grievances);
    }

    [Fact]
    public async Task Without_a_mobile_identity_nothing_is_created()
    {
        GrvKit kit = new();
        kit.User.ProfileId = null;
        GrievanceCategory category = kit.Repo.AddCategory("Medical");

        var result = await kit.Employees.CreateAsync(Create(category.Id), NoFiles, default);

        Assert.Equal(ApiErrorCodes.Unauthorized, result.ErrorCode);
        Assert.Empty(kit.Repo.Grievances);
    }

    // ── Isolation between users ──

    private static (GrvKit Kit, Grievance Mine, Grievance Theirs) TwoPeople()
    {
        GrvKit kit = new();
        GrievanceCategory category = kit.Repo.AddCategory("Medical");
        GrievanceDocumentRecord doc = new("grievances/2026/10/11111111-1111-1111-1111-111111111111.pdf", "a.pdf", "application/pdf", 10, new DateTime(2026, 10, 15));
        Grievance mine = kit.Repo.AddGrievance(GrvKit.Employee, category);
        Grievance theirs = kit.Repo.AddGrievance(GrvKit.OtherEmployee, category, documents: new[] { doc });
        kit.Storage.Objects[doc.Key] = GrvKit.PdfBytes;
        return (kit, mine, theirs);
    }

    [Fact]
    public async Task The_list_contains_only_the_callers_own_grievances()
    {
        (GrvKit kit, Grievance mine, _) = TwoPeople();

        var result = await kit.Employees.GetMineAsync(1, 20, null, default);

        Assert.Equal(mine.Id, Assert.Single(result.Data!.Items).Id);
        Assert.Equal(GrvKit.Employee, kit.Repo.LastFilter!.CreatedBy);       // the owner filter is applied by the server
    }

    [Fact]
    public async Task Detail_of_someone_elses_grievance_is_not_found()
    {
        (GrvKit kit, Grievance mine, Grievance theirs) = TwoPeople();

        Assert.True((await kit.Employees.GetMineByIdAsync(mine.Id, default)).IsSuccess);
        var result = await kit.Employees.GetMineByIdAsync(theirs.Id, default);

        Assert.Equal(ApiErrorCodes.NotFound, result.ErrorCode);              // 404, never 403
        Assert.Equal(GrievanceMessages.GrievanceNotFound, result.ErrorMessage);
    }

    [Fact]
    public async Task Downloading_a_document_of_someone_elses_grievance_is_not_found_and_storage_is_never_touched()
    {
        (GrvKit kit, _, Grievance theirs) = TwoPeople();

        var result = await kit.Employees.OpenMyDocumentAsync(theirs.Id, 0, default);

        Assert.Equal(ApiErrorCodes.NotFound, result.ErrorCode);
        Assert.Empty(kit.Storage.GetCalls);
        Assert.DoesNotContain(kit.Audit.Entries, e => e.Action == AuditAction.DocumentDownloaded);
    }

    [Fact]
    public async Task The_status_filter_is_passed_on_and_the_owner_filter_is_kept()
    {
        (GrvKit kit, _, _) = TwoPeople();

        await kit.Employees.GetMineAsync(1, 20, GrievanceStatus.Closed, default);

        Assert.Equal(GrievanceStatus.Closed, kit.Repo.LastFilter!.Status);
        Assert.Equal(GrvKit.Employee, kit.Repo.LastFilter.CreatedBy);
    }

    [Fact]
    public void Employee_responses_carry_no_employee_identity_and_no_internal_routing_fields()
    {
        Assert.Null(typeof(GrievanceSummaryDto).GetProperty("Employee"));
        Assert.Null(typeof(GrievanceDetailDto).GetProperty("Employee"));
        Assert.Null(typeof(GrievanceDetailDto).GetProperty("AssignedTo"));
        Assert.Null(typeof(GrievanceDetailDto).GetProperty("CreatedBy"));
        Assert.Null(typeof(GrievanceCategoryDto).GetProperty("Metadata"));
        Assert.Null(typeof(GrievanceDocumentDto).GetProperty("Key"));        // the object key never leaves the server
    }

    [Fact]
    public async Task The_detail_lists_documents_with_name_type_and_size_but_not_the_object_key()
    {
        GrvKit kit = new();
        GrievanceCategory category = kit.Repo.AddCategory("Medical");
        GrievanceDocumentRecord doc = new("grievances/2026/10/11111111-1111-1111-1111-111111111111.pdf", "report.pdf", "application/pdf", 1234, new DateTime(2026, 10, 15, 11, 0, 0));
        Grievance g = kit.Repo.AddGrievance(GrvKit.Employee, category, documents: new[] { doc });

        var result = await kit.Employees.GetMineByIdAsync(g.Id, default);

        GrievanceDocumentDto dto = Assert.Single(result.Data!.Documents);
        Assert.Equal(0, dto.Index);
        Assert.Equal("report.pdf", dto.Name);
        Assert.Equal("application/pdf", dto.ContentType);
        Assert.Equal(1234, dto.SizeBytes);
        Assert.Equal(TimeSpan.FromMinutes(330), dto.UploadedAt.Offset);      // IST, +05:30
    }

    // ── Complete history for the employee ──

    [Fact]
    public async Task The_detail_shows_the_whole_history_with_comments_forwarding_and_resolution()
    {
        GrvKit kit = new();
        kit.Repo.ActiveGrievanceAdmins.Add(42);
        GrievanceCategory category = kit.Repo.AddCategory("Medical");
        var created = await kit.Employees.CreateAsync(Create(category.Id), NoFiles, default);
        long id = created.Data!.Data!.Id;

        Assert.True((await kit.Admins.UpdateStatusAsync(id, new UpdateGrievanceStatusRequestDto { ToStatus = 2 }, default)).IsSuccess);
        Assert.True((await kit.Admins.AddCommentAsync(id, new AddGrievanceCommentRequestDto { Comment = "Checking with the unit." }, default)).IsSuccess);
        Assert.True((await kit.Admins.ForwardAsync(id, new ForwardGrievanceRequestDto { ToAdminId = 42, Remarks = "Please handle." }, default)).IsSuccess);
        Assert.True((await kit.Admins.UpdateStatusAsync(id, new UpdateGrievanceStatusRequestDto { ToStatus = 3, Remarks = "Claim processed." }, default)).IsSuccess);
        Assert.True((await kit.Admins.ResolveAsync(id, new ResolveGrievanceRequestDto { Remarks = "Paid." }, GrvKit.Pdf("proof.pdf"), default)).IsSuccess);

        var detail = (await kit.Employees.GetMineByIdAsync(id, default)).Data!;

        Assert.Equal(new[] { "Status change", "Status change", "Comment", "Forwarded", "Status change", "Status change" }, detail.Timeline.Select(t => t.ActionName));
        Assert.Equal("Employee", detail.Timeline[0].ActorType);
        Assert.All(detail.Timeline.Skip(1), t => Assert.Equal("Admin", t.ActorType));
        Assert.Equal("Checking with the unit.", detail.Timeline[2].Remarks);
        Assert.Equal("Please handle.", detail.Timeline[3].Remarks);
        Assert.Equal(TimeSpan.FromMinutes(330), detail.Timeline[3].ChangedAt.Offset);
        Assert.Equal("Claim processed.", detail.ActionRemarks);
        Assert.Equal("Paid.", detail.ClosedRemarks);
        Assert.NotNull(detail.ClosedAt);
        Assert.Equal(kit.Repo.Grievances.Single().ResolutionDocument, detail.ResolutionDocument);
        Assert.StartsWith("grievances/", detail.ResolutionDocument);
    }

    [Fact]
    public void The_employee_history_does_not_expose_admin_ids_or_the_forward_target()
    {
        Assert.Null(typeof(GrievanceStatusEntryDto).GetProperty("ChangedBy"));
        Assert.Null(typeof(GrievanceStatusEntryDto).GetProperty("ForwardedTo"));
        Assert.NotNull(typeof(GrievanceAdminStatusEntryDto).GetProperty("ForwardedTo"));
    }
}
