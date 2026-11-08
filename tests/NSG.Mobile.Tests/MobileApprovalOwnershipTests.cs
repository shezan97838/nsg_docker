using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using NSG.Mobile.API.Controllers;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.MobileAuth.DTOs;
using NSG.Mobile.Application.MobileAuth.Services;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Tests;

/// <summary>
/// Registration approval is owned by the PIS Admin the personnel record is CURRENTLY assigned to (<c>users_pis.assigned_to</c>). These
/// tests run the service over an in-memory repository that mirrors the SQL (inner join on the personnel row, <c>assigned_to = caller</c>,
/// ownership checked before the pending claim). The SQL itself, the row lock and the real authorization pipeline are covered by
/// <see cref="MobileApprovalOwnershipDatabaseTests"/>, which needs MySQL (NSG_TEST_MYSQL) and is NOT executed by this file.
/// </summary>
public class MobileApprovalOwnershipTests
{
    private const long AdminA = 101, AdminB = 202;

    private sealed class Kit
    {
        public FakeRequestRepository Repo { get; } = new();
        public FakeCurrentUser User { get; } = new() { AdminProfileId = AdminA };
        public FakeAudit Audit { get; } = new();
        public MobileApprovalService Service { get; }
        private long _id;

        public Kit() => Service = new MobileApprovalService(Repo, Audit, User, new TestClock());

        /// <summary>A request plus its personnel ownership. <paramref name="owner"/> null = unassigned; <paramref name="personnelExists"/> false = no users_pis row.</summary>
        public ApproveMobileUser Add(string nsg, long? owner, MobileAppActivationStatus status = MobileAppActivationStatus.Pending, bool personnelExists = true)
        {
            ApproveMobileUser r = new()
            {
                Id = ++_id, NsgNumber = nsg, MobileNumber = "9876543210", ApprovalStatus = status,
                RequestedAtIst = new DateTime(2026, 11, 1).AddMinutes(_id)
            };
            Repo.Requests.Add(r);
            if (personnelExists) Repo.Owners[nsg] = owner;
            return r;
        }

        public Task<Result<PaginatedResponse<MobileApprovalRequestDto>>> List(int page = 1, int size = 20, MobileAppActivationStatus? status = null, string? search = null)
            => Service.GetPagedAsync(page, size, status, search);

        public Task<Result<ApiResult<MobileApprovalRequestDto>>> Decide(long id, MobileAppActivationStatus decision = MobileAppActivationStatus.Approved)
            => Service.DecideAsync(id, new MobileApprovalDecisionRequestDto { Decision = decision });

        public static string[] Nsgs(Result<PaginatedResponse<MobileApprovalRequestDto>> r) => r.Data!.Items.Select(i => i.NsgNumber).OrderBy(n => n).ToArray();
    }

    // ═══════════ GET: ownership isolation ═══════════

    [Fact]
    public async Task Each_pis_admin_sees_only_the_requests_of_their_own_personnel()
    {
        Kit kit = new();
        kit.Add("A-1", AdminA); kit.Add("A-2", AdminA); kit.Add("B-1", AdminB);

        Assert.Equal(new[] { "A-1", "A-2" }, Kit.Nsgs(await kit.List()));

        kit.User.AdminProfileId = AdminB;
        Assert.Equal(new[] { "B-1" }, Kit.Nsgs(await kit.List()));
    }

    [Fact]
    public async Task Unassigned_and_missing_personnel_requests_are_hidden()
    {
        Kit kit = new();
        kit.Add("MINE", AdminA); kit.Add("NOBODY", owner: null); kit.Add("GONE", AdminA, personnelExists: false);

        Result<PaginatedResponse<MobileApprovalRequestDto>> result = await kit.List();

        Assert.Equal(new[] { "MINE" }, Kit.Nsgs(result));
        Assert.Equal(1, result.Data!.TotalCount);
    }

    [Fact]
    public async Task Paging_and_total_count_are_computed_inside_the_ownership_scope()
    {
        Kit kit = new();
        for (int i = 0; i < 5; i++) { kit.Add($"A-{i}", AdminA); kit.Add($"B-{i}", AdminB); }

        Result<PaginatedResponse<MobileApprovalRequestDto>> first = await kit.List(page: 1, size: 2);
        Result<PaginatedResponse<MobileApprovalRequestDto>> last = await kit.List(page: 3, size: 2);

        Assert.Equal((5, 2), (first.Data!.TotalCount, first.Data.Items.Count));
        Assert.Equal((5, 1), (last.Data!.TotalCount, last.Data.Items.Count));
        Assert.All(first.Data.Items.Concat(last.Data.Items), i => Assert.StartsWith("A-", i.NsgNumber));
    }

    [Fact]
    public async Task Search_and_status_filters_cannot_reach_another_admins_requests()
    {
        Kit kit = new();
        kit.Add("SHARED-A", AdminA);
        kit.Add("SHARED-B", AdminB);
        kit.Add("DONE-B", AdminB, MobileAppActivationStatus.Approved);

        Assert.Equal(new[] { "SHARED-A" }, Kit.Nsgs(await kit.List(search: "SHARED")));
        Assert.Equal(0, (await kit.List(search: "DONE-B")).Data!.TotalCount);                              // exact NSG of another admin's record
        Assert.Equal(0, (await kit.List(status: MobileAppActivationStatus.Approved)).Data!.TotalCount);    // the only Approved one belongs to B
        Assert.Equal(1, (await kit.List(status: MobileAppActivationStatus.Pending, search: "SHARED")).Data!.TotalCount);
    }

    [Fact]
    public async Task Listing_needs_an_admin_portal_identity()
    {
        Kit kit = new();
        kit.Add("A-1", AdminA);
        kit.User.AdminProfileId = null;
        kit.User.UserId = 9001;   // a legacy staff token has an int user id and no admin_profile_id

        Result<PaginatedResponse<MobileApprovalRequestDto>> result = await kit.List();

        Assert.Equal(ApiErrorCodes.Unauthorized, result.ErrorCode);
    }

    // ═══════════ PUT: ownership of the decision ═══════════

    [Theory]
    [InlineData(MobileAppActivationStatus.Approved)]
    [InlineData(MobileAppActivationStatus.Rejected)]
    public async Task The_assigned_admin_can_approve_and_reject(MobileAppActivationStatus decision)
    {
        Kit kit = new();
        ApproveMobileUser request = kit.Add("A-1", AdminA);

        Result<ApiResult<MobileApprovalRequestDto>> result = await kit.Decide(request.Id, decision);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal((decision, (int?)AdminA), (request.ApprovalStatus, request.ApprovedBy));
        Assert.Equal(AdminA, kit.Repo.LastDecision!.Value.Approver);
    }

    [Theory]
    [InlineData(MobileAppActivationStatus.Approved)]
    [InlineData(MobileAppActivationStatus.Rejected)]
    public async Task Another_pis_admin_cannot_approve_or_reject_and_nothing_changes(MobileAppActivationStatus decision)
    {
        Kit kit = new();
        ApproveMobileUser request = kit.Add("A-1", AdminA);
        kit.User.AdminProfileId = AdminB;

        Result<ApiResult<MobileApprovalRequestDto>> result = await kit.Decide(request.Id, decision);

        Assert.Equal(ApiErrorCodes.Forbidden, result.ErrorCode);
        Assert.Equal((MobileAppActivationStatus.Pending, (int?)null, (string?)null), (request.ApprovalStatus, request.ApprovedBy, request.Remarks));
        Assert.Empty(kit.Audit.Entries);
    }

    [Fact]
    public async Task An_unassigned_request_cannot_be_decided_by_any_pis_admin()
    {
        Kit kit = new();
        ApproveMobileUser request = kit.Add("NOBODY", owner: null);

        Result<ApiResult<MobileApprovalRequestDto>> result = await kit.Decide(request.Id);

        Assert.Equal(ApiErrorCodes.Forbidden, result.ErrorCode);
        Assert.Equal(MobileAppActivationStatus.Pending, request.ApprovalStatus);
        Assert.Empty(kit.Audit.Entries);
    }

    [Fact]
    public async Task A_missing_request_is_not_found_and_a_missing_personnel_record_keeps_its_conflict()
    {
        Kit kit = new();
        ApproveMobileUser orphan = kit.Add("GONE", AdminA, personnelExists: false);
        kit.Add("A-1", AdminA);   // makes the fake run in ownership mode

        Assert.Equal(ApiErrorCodes.NotFound, (await kit.Decide(9999)).ErrorCode);
        Result<ApiResult<MobileApprovalRequestDto>> missing = await kit.Decide(orphan.Id);
        Assert.Equal(ApiErrorCodes.Conflict, missing.ErrorCode);
        Assert.Contains("no longer exists", missing.ErrorMessage);
    }

    [Fact]
    public async Task An_already_decided_request_keeps_its_conflict_for_the_owner_and_stays_private_from_others()
    {
        Kit kit = new();
        ApproveMobileUser request = kit.Add("A-1", AdminA);
        Assert.True((await kit.Decide(request.Id)).IsSuccess);

        Result<ApiResult<MobileApprovalRequestDto>> again = await kit.Decide(request.Id, MobileAppActivationStatus.Rejected);
        Assert.Equal(ApiErrorCodes.Conflict, again.ErrorCode);
        Assert.Contains("already been approved", again.ErrorMessage);
        Assert.Equal(MobileAppActivationStatus.Approved, request.ApprovalStatus);

        kit.User.AdminProfileId = AdminB;   // not the owner: no status of someone else's request is revealed
        Assert.Equal(ApiErrorCodes.Forbidden, (await kit.Decide(request.Id)).ErrorCode);
    }

    [Fact]
    public void No_request_model_can_carry_an_admin_id_and_the_reviewer_comes_from_the_token()
    {
        Assert.DoesNotContain(typeof(MobileApprovalDecisionRequestDto).GetProperties(),
            p => p.Name.Contains("Admin", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("Reviewer", StringComparison.OrdinalIgnoreCase)
                 || p.Name.Contains("Approver", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("Owner", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task An_admin_id_that_does_not_fit_the_int_approved_by_column_is_refused_not_truncated()
    {
        Kit kit = new();
        ApproveMobileUser request = kit.Add("A-1", (long)int.MaxValue + 1);
        kit.User.AdminProfileId = (long)int.MaxValue + 1;

        Result<ApiResult<MobileApprovalRequestDto>> result = await kit.Decide(request.Id);

        Assert.Equal(ApiErrorCodes.Unauthorized, result.ErrorCode);
        Assert.Null(kit.Repo.LastDecision);
        Assert.Equal(MobileAppActivationStatus.Pending, request.ApprovalStatus);
    }

    // ═══════════ Ownership transfer compatibility (the owner is read live from users_pis) ═══════════

    [Fact]
    public async Task After_a_transfer_the_old_owner_loses_the_request_and_the_new_owner_gains_it_without_any_request_being_rewritten()
    {
        Kit kit = new();
        ApproveMobileUser request = kit.Add("A-1", AdminA);
        string before = $"{request.Id}|{request.NsgNumber}|{request.ApprovalStatus}|{request.ApprovedBy}|{request.UpdatedBy}";
        Assert.Equal(1, (await kit.List()).Data!.TotalCount);

        kit.Repo.Owners["A-1"] = AdminB;   // the transfer: only users_pis.assigned_to changes

        Assert.Equal(0, (await kit.List()).Data!.TotalCount);
        Assert.Equal(ApiErrorCodes.Forbidden, (await kit.Decide(request.Id)).ErrorCode);

        kit.User.AdminProfileId = AdminB;
        Assert.Equal(new[] { "A-1" }, Kit.Nsgs(await kit.List()));
        Assert.Equal(before, $"{request.Id}|{request.NsgNumber}|{request.ApprovalStatus}|{request.ApprovedBy}|{request.UpdatedBy}");   // untouched by the transfer
        Assert.True((await kit.Decide(request.Id)).IsSuccess);
        Assert.Equal((MobileAppActivationStatus.Approved, (int?)AdminB), (request.ApprovalStatus, request.ApprovedBy));
    }

    // ═══════════ Authorization wiring ═══════════

    [Fact]
    public void Both_approval_endpoints_require_the_admin_portal_pis_policy_and_no_action_weakens_it()
    {
        Type controller = typeof(MobileApprovalRequestsController);

        AuthorizeAttribute classLevel = controller.GetCustomAttribute<AuthorizeAttribute>()!;
        Assert.Equal(DomainConstants.Policies.RequirePisAdminPortal, classLevel.Policy);

        MethodInfo[] actions = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.Equal(2, actions.Length);
        foreach (MethodInfo action in actions)
        {
            Assert.Null(action.GetCustomAttribute<AllowAnonymousAttribute>());
            AuthorizeAttribute? own = action.GetCustomAttribute<AuthorizeAttribute>();
            if (own != null) Assert.Equal(DomainConstants.Policies.RequirePisAdminPortal, own.Policy);
        }
    }

    [Fact]
    public void The_registration_admin_policy_is_gone_but_the_role_itself_is_untouched()
    {
        Assert.DoesNotContain(typeof(DomainConstants.Policies).GetFields(), f => f.Name == "RequireRegistrationAdmin");
        Assert.NotEqual(DomainConstants.Policies.RequirePisAdmin, DomainConstants.Policies.RequirePisAdminPortal);   // the staff-token path is a different policy
        Assert.Equal("REGISTRATION_ADMIN", AdminRoleCodes.RegistrationAdmin);                                         // seeded role kept, no row altered
    }
}
