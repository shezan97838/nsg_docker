using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Grievances.DTOs;
using NSG.Mobile.Application.Grievances.Validators;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Tests;

/// <summary>Status changes by a Grievance Admin: only 1→2, 2→3 (remarks required), 3→4 = resolved (remarks required); every change is logged in the same step.</summary>
public class GrievanceStatusTests
{
    private static (GrvKit Kit, Grievance Grievance) Arrange(GrievanceStatus status)
    {
        GrvKit kit = new();
        GrievanceCategory category = kit.Repo.AddCategory("Medical");
        return (kit, kit.Repo.AddGrievance(GrvKit.Employee, category, status));
    }

    private static UpdateGrievanceStatusRequestDto Request(GrievanceStatus to, string? remarks = null) => new() { ToStatus = (int)to, Remarks = remarks };

    [Theory]
    [InlineData(GrievanceStatus.Submitted, GrievanceStatus.UnderReview, null)]
    [InlineData(GrievanceStatus.UnderReview, GrievanceStatus.ActionTaken, "Inquiry completed.")]
    [InlineData(GrievanceStatus.ActionTaken, GrievanceStatus.Closed, "Resolved after the inquiry.")]
    public async Task Each_allowed_step_succeeds_and_writes_a_status_log_row(GrievanceStatus from, GrievanceStatus to, string? remarks)
    {
        (GrvKit kit, Grievance g) = Arrange(from);

        var result = await kit.Admins.UpdateStatusAsync(g.Id, Request(to, remarks), default);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal((int)to, result.Data!.Data!.Status);
        GrievanceStatusLog log = Assert.Single(kit.Repo.Logs);
        Assert.Equal(from, log.FromStatus);
        Assert.Equal(to, log.ToStatus);
        Assert.Equal(GrvKit.Admin, log.ChangedBy);                          // the admin comes from the token
        Assert.Equal(GrievanceActorType.Admin, log.ChangedByType);
        Assert.Contains(kit.Audit.Entries, e => e.Action == AuditAction.GrievanceStatusChanged);
    }

    [Theory]
    [InlineData(GrievanceStatus.Submitted, GrievanceStatus.ActionTaken)]    // skipping a step
    [InlineData(GrievanceStatus.Submitted, GrievanceStatus.Closed)]
    [InlineData(GrievanceStatus.UnderReview, GrievanceStatus.Closed)]
    [InlineData(GrievanceStatus.UnderReview, GrievanceStatus.Submitted)]    // going back
    [InlineData(GrievanceStatus.ActionTaken, GrievanceStatus.UnderReview)]
    [InlineData(GrievanceStatus.ActionTaken, GrievanceStatus.Submitted)]
    [InlineData(GrievanceStatus.Closed, GrievanceStatus.ActionTaken)]       // nothing leaves Closed
    [InlineData(GrievanceStatus.Closed, GrievanceStatus.UnderReview)]
    [InlineData(GrievanceStatus.Submitted, GrievanceStatus.Submitted)]      // staying put
    [InlineData(GrievanceStatus.UnderReview, GrievanceStatus.UnderReview)]
    [InlineData(GrievanceStatus.Closed, GrievanceStatus.Closed)]
    public async Task Every_other_move_is_a_400_and_changes_nothing(GrievanceStatus from, GrievanceStatus to)
    {
        (GrvKit kit, Grievance g) = Arrange(from);

        var result = await kit.Admins.UpdateStatusAsync(g.Id, Request(to, "remarks"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApiErrorCodes.ValidationFailed, result.ErrorCode);
        Assert.Equal(0, kit.Repo.ChangeStatusCalls);
        Assert.Empty(kit.Repo.Logs);
        Assert.Equal(from, kit.Repo.Grievances.Single().CurrentStatus);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(-3)]
    public async Task A_target_status_outside_2_to_4_is_rejected(int toStatus)
    {
        (GrvKit kit, Grievance g) = Arrange(GrievanceStatus.Submitted);
        var result = await kit.Admins.UpdateStatusAsync(g.Id, new UpdateGrievanceStatusRequestDto { ToStatus = toStatus }, default);
        Assert.Equal(ApiErrorCodes.ValidationFailed, result.ErrorCode);
    }

    [Fact]
    public async Task A_missing_target_status_is_rejected()
    {
        (GrvKit kit, Grievance g) = Arrange(GrievanceStatus.Submitted);
        var result = await kit.Admins.UpdateStatusAsync(g.Id, new UpdateGrievanceStatusRequestDto(), default);
        Assert.Equal(ApiErrorCodes.ValidationFailed, result.ErrorCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Action_taken_requires_remarks(string? remarks)
    {
        (GrvKit kit, Grievance g) = Arrange(GrievanceStatus.UnderReview);

        var result = await kit.Admins.UpdateStatusAsync(g.Id, Request(GrievanceStatus.ActionTaken, remarks), default);

        Assert.Equal(ApiErrorCodes.ValidationFailed, result.ErrorCode);
        Assert.Contains("Remarks are required", result.ErrorMessage);
        Assert.Equal(GrievanceStatus.UnderReview, kit.Repo.Grievances.Single().CurrentStatus);
        Assert.Empty(kit.Repo.Logs);
    }

    [Fact]
    public async Task Action_taken_stores_who_when_and_the_remarks()
    {
        (GrvKit kit, Grievance g) = Arrange(GrievanceStatus.UnderReview);

        await kit.Admins.UpdateStatusAsync(g.Id, Request(GrievanceStatus.ActionTaken, "  Counselling arranged.  "), default);

        Grievance stored = kit.Repo.Grievances.Single();
        Assert.Equal(GrvKit.Admin, stored.ActionTakenBy);
        Assert.Equal("Counselling arranged.", stored.ActionRemarks);          // trimmed
        Assert.NotNull(stored.ActionTakenAtIst);
        Assert.Null(stored.ClosedBy);
    }

    [Fact]
    public async Task Resolving_stores_who_when_and_the_resolution_and_needs_remarks()
    {
        (GrvKit kit, Grievance g) = Arrange(GrievanceStatus.ActionTaken);

        var missing = await kit.Admins.UpdateStatusAsync(g.Id, Request(GrievanceStatus.Closed), default);
        Assert.Equal(ApiErrorCodes.ValidationFailed, missing.ErrorCode);
        Assert.Contains("resolving", missing.ErrorMessage);
        Assert.Equal(GrievanceStatus.ActionTaken, kit.Repo.Grievances.Single().CurrentStatus);

        var result = await kit.Admins.UpdateStatusAsync(g.Id, Request(GrievanceStatus.Closed, "Reimbursed in full."), default);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Grievance stored = kit.Repo.Grievances.Single();
        Assert.Equal(GrvKit.Admin, stored.ClosedBy);
        Assert.NotNull(stored.ClosedAtIst);
        Assert.Equal("Reimbursed in full.", stored.ClosedRemarks);
    }

    [Fact]
    public async Task Remarks_longer_than_1000_characters_are_rejected_and_the_message_states_the_limit()
    {
        (GrvKit kit, Grievance g) = Arrange(GrievanceStatus.UnderReview);

        var result = await kit.Admins.UpdateStatusAsync(g.Id, Request(GrievanceStatus.ActionTaken, new string('x', 1001)), default);

        Assert.Equal(ApiErrorCodes.ValidationFailed, result.ErrorCode);
        Assert.Contains("1000 characters", result.ErrorMessage);
    }

    [Fact]
    public void The_request_validator_states_the_1000_character_limit_and_requires_remarks_for_action_taken()
    {
        UpdateGrievanceStatusRequestValidator validator = new();

        var tooLong = validator.Validate(new UpdateGrievanceStatusRequestDto { ToStatus = 2, Remarks = new string('x', 1001) });
        Assert.Contains(tooLong.Errors, e => e.ErrorMessage.Contains("1000 characters"));

        var missing = validator.Validate(new UpdateGrievanceStatusRequestDto { ToStatus = 3, Remarks = " " });
        Assert.Contains(missing.Errors, e => e.ErrorMessage.Contains("Remarks are required"));

        Assert.True(validator.Validate(new UpdateGrievanceStatusRequestDto { ToStatus = 3, Remarks = "ok" }).IsValid);
        Assert.False(validator.Validate(new UpdateGrievanceStatusRequestDto { ToStatus = 7 }).IsValid);
        Assert.False(validator.Validate(new UpdateGrievanceStatusRequestDto()).IsValid);
    }

    [Fact]
    public async Task When_someone_else_moved_it_first_the_answer_is_a_409_with_the_current_status()
    {
        (GrvKit kit, Grievance g) = Arrange(GrievanceStatus.Submitted);
        // The request passes the pre-check (status 1), then another admin moves it before the conditional update runs.
        kit.Repo.Grievances.Single().CurrentStatus = GrievanceStatus.Submitted;
        kit.Repo.ReportStatusChangedByOthers = GrievanceStatus.UnderReview;

        var result = await kit.Admins.UpdateStatusAsync(g.Id, Request(GrievanceStatus.UnderReview), default);

        Assert.Equal(ApiErrorCodes.Conflict, result.ErrorCode);
        Assert.Contains("Under Review", result.ErrorMessage);
        Assert.Empty(kit.Repo.Logs);
    }

    [Fact]
    public async Task An_unknown_grievance_is_a_404()
    {
        GrvKit kit = new();
        var result = await kit.Admins.UpdateStatusAsync(12345, Request(GrievanceStatus.UnderReview), default);
        Assert.Equal(ApiErrorCodes.NotFound, result.ErrorCode);
    }

    [Fact]
    public async Task Without_an_admin_identity_in_the_token_nothing_is_changed()
    {
        (GrvKit kit, Grievance g) = Arrange(GrievanceStatus.Submitted);
        kit.User.AdminProfileId = null;

        var result = await kit.Admins.UpdateStatusAsync(g.Id, Request(GrievanceStatus.UnderReview), default);

        Assert.Equal(ApiErrorCodes.Unauthorized, result.ErrorCode);
        Assert.Equal(0, kit.Repo.ChangeStatusCalls);
    }

    [Fact]
    public void The_request_has_no_field_that_could_carry_the_acting_admin()
    {
        string[] names = typeof(UpdateGrievanceStatusRequestDto).GetProperties().Select(p => p.Name).ToArray();
        Assert.Equal(new[] { "ToStatus", "Remarks" }, names.OrderByDescending(n => n));
    }
}
