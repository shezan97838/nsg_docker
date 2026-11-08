using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Hosting;
using NSG.Mobile.API.Controllers;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Interfaces.Services;
using NSG.Mobile.Application.MobileAuth.DTOs;
using NSG.Mobile.Application.MobileAuth.Services;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;
using NSG.Mobile.Infrastructure.Services;

namespace NSG.Mobile.Tests;

/// <summary>
/// Reviewer identity. The approval service reads the PIS Admin's id from the token's admin_profile_id (long) only; the older
/// ReviewerIdentityResolver, which no longer feeds approvals, is still exercised here. No database needed.
/// </summary>
public class MobileApprovalReviewerTests
{
    private static ReviewerIdentityResolver Resolver(FakeCurrentUser user, string environment = "Production")
        => new(user, new TestEnv(environment), Microsoft.Extensions.Options.Options.Create(new DevelopmentReviewerOptions()));

    // ───── reviewer identity resolution ─────

    [Fact]
    public void Admin_token_resolves_to_the_admin_profile_id()
    {
        ReviewerIdentity? reviewer = Resolver(new FakeCurrentUser { AdminProfileId = 17 }).Resolve();

        Assert.NotNull(reviewer);
        Assert.Equal(17, reviewer!.UserId);
        Assert.Null(reviewer.DisplayName);
    }

    [Fact]
    public void Admin_profile_id_wins_over_a_numeric_user_id()
    {
        // Never expected in practice (an admin token's sub is not numeric); proves the admin claim is the authority when present.
        Assert.Equal(17, Resolver(new FakeCurrentUser { AdminProfileId = 17, UserId = 9001 }).Resolve()!.UserId);
    }

    [Fact]
    public void Staff_user_behaviour_is_preserved()
    {
        ReviewerIdentity? reviewer = Resolver(new FakeCurrentUser { UserId = 42 }).Resolve();

        Assert.Equal(42, reviewer!.UserId);
        Assert.Null(reviewer.DisplayName);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-5L)]
    [InlineData((long)int.MaxValue + 1)]
    public void An_out_of_range_admin_id_is_never_used_and_no_staff_id_means_no_reviewer(long adminId)
        => Assert.Null(Resolver(new FakeCurrentUser { AdminProfileId = adminId }).Resolve());

    [Fact]
    public void No_identity_in_production_resolves_to_nobody()
        => Assert.Null(Resolver(new FakeCurrentUser()).Resolve());

    [Fact]
    public void A_mobile_profile_id_is_not_a_reviewer()
        => Assert.Null(Resolver(new FakeCurrentUser { ProfileId = 5 }).Resolve());

    // ───── decisions by a PIS Admin (reviewer = admin_profile_id; ownership is covered by MobileApprovalOwnershipTests) ─────

    private static (MobileApprovalService Service, FakeRequestRepository Repo, FakeAudit Audit) OwnedKit(long admin)
    {
        FakeRequestRepository repo = new();
        repo.Requests.Add(new ApproveMobileUser { Id = 1, NsgNumber = "N", MobileNumber = "9876543210" });
        repo.Owners["N"] = admin;
        FakeAudit audit = new();
        return (new MobileApprovalService(repo, audit, new FakeCurrentUser { AdminProfileId = admin }, new TestClock()), repo, audit);
    }

    [Theory]
    [InlineData(MobileAppActivationStatus.Approved, AuditAction.ITApprovalGranted, "Mobile approval request approved successfully.")]
    [InlineData(MobileAppActivationStatus.Rejected, AuditAction.ITApprovalRejected, "Mobile approval request rejected successfully.")]
    public async Task Pis_admin_decision_records_the_admin_id_for_the_request_and_the_audit(
        MobileAppActivationStatus decision, AuditAction expectedAction, string expectedMessage)
    {
        (MobileApprovalService service, FakeRequestRepository repo, FakeAudit audit) = OwnedKit(17);

        Result<ApiResult<MobileApprovalRequestDto>> result = await service.DecideAsync(1, new MobileApprovalDecisionRequestDto { Decision = decision });

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedMessage, result.Data!.Message);
        Assert.Equal(17L, repo.LastDecision!.Value.Approver);
        Assert.Equal(decision, repo.LastDecision.Value.Decision);

        (AuditAction Action, string Result, int? UserId, string? EntityId, string? Data) entry = Assert.Single(audit.Entries);
        Assert.Equal(expectedAction, entry.Action);
        Assert.Equal(17, entry.UserId);
    }

    [Fact]
    public async Task A_staff_user_id_is_not_a_reviewer()
    {
        FakeRequestRepository repo = new();
        FakeAudit audit = new();
        MobileApprovalService service = new(repo, audit, new FakeCurrentUser { UserId = 42 }, new TestClock());

        Result<ApiResult<MobileApprovalRequestDto>> result = await service.DecideAsync(1, new MobileApprovalDecisionRequestDto { Decision = MobileAppActivationStatus.Approved });

        Assert.Equal(ApiErrorCodes.Unauthorized, result.ErrorCode);
        Assert.Null(repo.LastDecision);
        Assert.Empty(audit.Entries);
    }

    [Fact]
    public async Task A_decision_without_a_reviewer_records_nothing()
    {
        FakeRequestRepository repo = new();
        FakeAudit audit = new();
        MobileApprovalService service = new(repo, audit, new FakeCurrentUser(), new TestClock());

        Result<ApiResult<MobileApprovalRequestDto>> result = await service.DecideAsync(1, new MobileApprovalDecisionRequestDto { Decision = MobileAppActivationStatus.Approved });

        Assert.False(result.IsSuccess);
        Assert.Equal(ApiErrorCodes.Unauthorized, result.ErrorCode);
        Assert.Null(repo.LastDecision);
        Assert.Empty(audit.Entries);
    }
}
