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
/// Reviewer identity for mobile approval decisions. A Registration Admin's token has a non-numeric 'sub' ("admin:{id}") and carries the
/// account id in 'admin_profile_id'; staff tokens keep resolving through the numeric 'sub'. No database needed.
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

    // ───── decisions by a Registration Admin ─────

    [Theory]
    [InlineData(MobileAppActivationStatus.Approved, AuditAction.ITApprovalGranted, "Mobile approval request approved successfully.")]
    [InlineData(MobileAppActivationStatus.Rejected, AuditAction.ITApprovalRejected, "Mobile approval request rejected successfully.")]
    public async Task Registration_admin_decision_records_the_admin_id_for_the_request_and_the_audit(
        MobileAppActivationStatus decision, AuditAction expectedAction, string expectedMessage)
    {
        FakeRequestRepository repo = new();
        FakeAudit audit = new();
        FakeCurrentUser admin = new() { AdminProfileId = 17 };
        MobileApprovalService service = new(repo, audit, admin, Resolver(admin), new TestClock());

        Result<ApiResult<MobileApprovalRequestDto>> result = await service.DecideAsync(1, new MobileApprovalDecisionRequestDto { Decision = decision });

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedMessage, result.Data!.Message);
        Assert.Equal(17, repo.LastDecision!.Value.Approver);
        Assert.Equal(decision, repo.LastDecision.Value.Decision);

        (AuditAction Action, string Result, int? UserId, string? EntityId, string? Data) entry = Assert.Single(audit.Entries);
        Assert.Equal(expectedAction, entry.Action);
        Assert.Equal(17, entry.UserId);
        Assert.Null(entry.Data); // a real token carries no display name
    }

    [Fact]
    public async Task Staff_user_decision_still_records_the_staff_user_id()
    {
        FakeRequestRepository repo = new();
        FakeAudit audit = new();
        FakeCurrentUser staff = new() { UserId = 42 };
        MobileApprovalService service = new(repo, audit, staff, Resolver(staff), new TestClock());

        Assert.True((await service.DecideAsync(1, new MobileApprovalDecisionRequestDto { Decision = MobileAppActivationStatus.Approved })).IsSuccess);

        Assert.Equal(42, repo.LastDecision!.Value.Approver);
        Assert.Equal(42, Assert.Single(audit.Entries).UserId);
    }

    [Fact]
    public async Task A_decision_without_a_reviewer_records_nothing()
    {
        FakeRequestRepository repo = new();
        FakeAudit audit = new();
        FakeCurrentUser nobody = new();
        MobileApprovalService service = new(repo, audit, nobody, Resolver(nobody), new TestClock());

        Result<ApiResult<MobileApprovalRequestDto>> result = await service.DecideAsync(1, new MobileApprovalDecisionRequestDto { Decision = MobileAppActivationStatus.Approved });

        Assert.False(result.IsSuccess);
        Assert.Equal(ApiErrorCodes.Unauthorized, result.ErrorCode);
        Assert.Null(repo.LastDecision);
        Assert.Empty(audit.Entries);
    }

    // ───── authorization wiring ─────

    [Fact]
    public void Every_approval_endpoint_requires_the_registration_admin_policy_and_no_action_weakens_it()
    {
        Type controller = typeof(MobileApprovalRequestsController);

        AuthorizeAttribute classLevel = controller.GetCustomAttribute<AuthorizeAttribute>()!;
        Assert.Equal(DomainConstants.Policies.RequireRegistrationAdmin, classLevel.Policy);

        foreach (MethodInfo action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            Assert.Null(action.GetCustomAttribute<AllowAnonymousAttribute>());
            AuthorizeAttribute? own = action.GetCustomAttribute<AuthorizeAttribute>();
            if (own != null) Assert.Equal(DomainConstants.Policies.RequireRegistrationAdmin, own.Policy);
        }
    }

    [Fact]
    public void The_registration_admin_policy_is_a_different_policy_from_the_staff_and_super_admin_ones()
    {
        Assert.NotEqual(DomainConstants.Policies.RequirePisAdmin, DomainConstants.Policies.RequireRegistrationAdmin);
        Assert.NotEqual(DomainConstants.Policies.RequireSuperAdmin, DomainConstants.Policies.RequireRegistrationAdmin);
        Assert.Equal("REGISTRATION_ADMIN", AdminRoleCodes.RegistrationAdmin);
    }
}
