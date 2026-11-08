using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging.Abstractions;
using NSG.Mobile.API.Controllers;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Application.MobileAuth.DTOs;
using NSG.Mobile.Application.MobileAuth.Services;
using NSG.Mobile.Application.Pis.DTOs;
using NSG.Mobile.Application.Pis.Services;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Tests;

/// <summary>
/// Super Admin ownership transfer over in-memory fakes that mirror the repository's semantics (no database). The SQL, the row lock and the
/// real authorization pipeline are covered by <see cref="PisOwnershipTransferDatabaseTests"/>, which needs MySQL and is NOT executed here.
/// </summary>
public class PisOwnershipTransferTests
{
    private const long SuperId = 1, PisA = 101, PisB = 202;

    private sealed class FakeAdminProfiles : IAdminProfileRepository
    {
        public List<AdminProfile> Profiles { get; } = new();
        public Task<AdminProfile?> GetByNsgNumberAsync(string nsgNumber, CancellationToken ct = default) => Task.FromResult(Profiles.FirstOrDefault(p => p.NsgNumber == nsgNumber));
        public Task<AdminProfile?> GetByIdAsync(long id, CancellationToken ct = default) => Task.FromResult(Profiles.FirstOrDefault(p => p.Id == id));
        public Task<AdminAccountRow?> GetRowByIdAsync(long id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<(IReadOnlyList<AdminAccountRow> Items, int TotalCount)> GetPagedAsync(int pageNumber, int pageSize, AdminAccountFilter filter, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AdminCreateOutcome> AddAsync(AdminProfile profile, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> AnySuperAdminAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> CountActiveSuperAdminsAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> ActivateAsync(long id, string passwordHash, DateTime nowIst, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AdminStateChangeOutcome> SetEnabledAsync(long id, bool enabled, long actorId, DateTime nowIst, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<FailedLoginState> RegisterFailedLoginAsync(long id, int maxAttempts, TimeSpan lockoutDuration, DateTime nowIst, CancellationToken ct = default) => throw new NotSupportedException();
        public Task ClearExpiredLockoutAsync(long id, DateTime nowIst, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RecordSuccessfulLoginAsync(long id, DateTime nowIst, CancellationToken ct = default) => throw new NotSupportedException();
    }

    /// <summary>Mirrors <c>PisOwnershipRepository</c>: finds the person, no-ops for the current owner, re-checks the target, changes only assigned_to, appends history.</summary>
    private sealed class FakeOwnershipRepo : IPisOwnershipRepository
    {
        private readonly FakeAdminProfiles _admins;
        private readonly FakeRequestRepository _requests;
        public List<UserPis> People { get; } = new();
        public List<PisOwnershipTransfer> History { get; } = new();
        public int Calls { get; private set; }

        public FakeOwnershipRepo(FakeAdminProfiles admins, FakeRequestRepository requests) { _admins = admins; _requests = requests; }

        public Task<PisOwnershipTransferResult> TransferAsync(string nsgNumber, long targetAdminId, long actorAdminId, string? reason, DateTime nowIst, CancellationToken ct = default)
        {
            Calls++;
            UserPis? person = People.FirstOrDefault(p => string.Equals(p.NsgNumber, nsgNumber, StringComparison.OrdinalIgnoreCase));
            if (person == null) return Task.FromResult(new PisOwnershipTransferResult(PisOwnershipTransferOutcome.PersonnelNotFound));
            if (person.AssignedTo == targetAdminId) return Task.FromResult(new PisOwnershipTransferResult(PisOwnershipTransferOutcome.AlreadyAssigned, person.Id, person.NsgNumber, person.AssignedTo));

            AdminProfile? target = _admins.Profiles.FirstOrDefault(p => p.Id == targetAdminId);
            if (target is not { IsActive: true, ActivationStatus: AdminActivationStatus.Active, Role.RoleCode: AdminRoleCodes.PisAdmin })
                return Task.FromResult(new PisOwnershipTransferResult(PisOwnershipTransferOutcome.TargetNotEligible, person.Id, person.NsgNumber, person.AssignedTo));

            long? previous = person.AssignedTo;
            person.AssignedTo = targetAdminId;                       // the only column that changes
            _requests.Owners[person.NsgNumber] = targetAdminId;      // approvals read the owner live from here, exactly as the SQL join does
            History.Add(new PisOwnershipTransfer
            {
                Id = History.Count + 1, PisRecordId = person.Id, NsgNumber = person.NsgNumber, PreviousAssignedTo = previous, NewAssignedTo = targetAdminId,
                TransferredBy = actorAdminId, Reason = reason, TransferredAtIst = nowIst
            });
            return Task.FromResult(new PisOwnershipTransferResult(PisOwnershipTransferOutcome.Transferred, person.Id, person.NsgNumber, previous));
        }
    }

    private sealed class Kit
    {
        public FakeAdminProfiles Admins { get; } = new();
        public FakeRequestRepository Requests { get; } = new();
        public FakeOwnershipRepo Ownership { get; }
        public FakeCurrentUser User { get; } = new() { AdminProfileId = SuperId };
        public FakeAudit Audit { get; } = new();
        public PisOwnershipTransferService Service { get; }

        public Kit()
        {
            Ownership = new FakeOwnershipRepo(Admins, Requests);
            Service = new PisOwnershipTransferService(Admins, Ownership, Audit, User, new TestClock());
            AddAdmin(PisA, "PA-1", AdminRoleCodes.PisAdmin);
            AddAdmin(PisB, "PB-1", AdminRoleCodes.PisAdmin);
        }

        public AdminProfile AddAdmin(long id, string nsg, string role, bool active = true, AdminActivationStatus status = AdminActivationStatus.Active)
        {
            AdminProfile p = new() { Id = id, NsgNumber = nsg, RoleId = 1, IsActive = active, ActivationStatus = status, Role = new AdminRole { RoleCode = role, IsActive = true } };
            Admins.Profiles.Add(p);
            return p;
        }

        public UserPis AddPerson(string nsg, long? owner, int? createdBy = 7, int? updatedBy = 9)
        {
            UserPis p = new() { Id = Ownership.People.Count + 500, NsgNumber = nsg, MobileNumber = "9811111111", CreatedBy = createdBy, UpdatedBy = updatedBy, AssignedTo = owner, IsActive = true };
            Ownership.People.Add(p);
            Requests.Owners[nsg] = owner;
            return p;
        }

        public Task<Result<ApiResult<PisOwnershipTransferDto>>> Transfer(string person, string target, string? reason = null)
            => Service.TransferAsync(new PisOwnershipTransferRequestDto { PersonnelNsgNumber = person, TargetPisAdminNsgNumber = target, Reason = reason });
    }

    // ═══════════ Authorization wiring ═══════════

    [Fact]
    public void Only_the_super_admin_policy_guards_the_single_transfer_action()
    {
        Type controller = typeof(PisOwnershipController);
        Assert.Equal(DomainConstants.Policies.RequireSuperAdmin, Assert.Single(controller.GetCustomAttributes<AuthorizeAttribute>()).Policy);

        MethodInfo[] actions = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        MethodInfo action = Assert.Single(actions);
        Assert.Null(action.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Null(action.GetCustomAttribute<AuthorizeAttribute>());   // nothing weakens or replaces the class policy
        Assert.Equal("transfer-ownership", action.GetCustomAttribute<Microsoft.AspNetCore.Mvc.HttpPostAttribute>()!.Template);
        Assert.Equal("api/v1/admin/pis-personnel", controller.GetCustomAttribute<Microsoft.AspNetCore.Mvc.RouteAttribute>()!.Template);
    }

    [Fact]
    public void The_request_names_people_by_nsg_number_and_cannot_carry_an_actor_or_an_internal_id()
    {
        string[] names = typeof(PisOwnershipTransferRequestDto).GetProperties().Select(p => p.Name).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "PersonnelNsgNumber", "Reason", "TargetPisAdminNsgNumber" }, names);
    }

    [Fact]
    public async Task A_caller_without_an_admin_portal_identity_is_unauthorized_and_nothing_is_touched()
    {
        Kit kit = new();
        kit.AddPerson("P-1", owner: null);
        kit.User.AdminProfileId = null;
        kit.User.UserId = 9001;   // a legacy staff token

        Result<ApiResult<PisOwnershipTransferDto>> result = await kit.Transfer("P-1", "PA-1");

        Assert.Equal(ApiErrorCodes.Unauthorized, result.ErrorCode);
        Assert.Equal(0, kit.Ownership.Calls);
        Assert.Empty(kit.Audit.Entries);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData((long)int.MaxValue + 1)]
    public async Task An_unusable_actor_id_is_unauthorized_with_the_standard_message_and_nothing_is_touched(long actor)
    {
        Kit kit = new();
        UserPis person = kit.AddPerson("P-1", owner: null);
        kit.User.AdminProfileId = actor;   // the audit user column is an int: an id that does not fit is refused, never truncated

        Result<ApiResult<PisOwnershipTransferDto>> result = await kit.Transfer("P-1", "PA-1");

        Assert.Equal((ApiErrorCodes.Unauthorized, "The administrator could not be identified."), (result.ErrorCode, result.ErrorMessage));
        Assert.Equal(0, kit.Ownership.Calls);
        Assert.Empty(kit.Audit.Entries);
        Assert.Null(person.AssignedTo);
    }

    [Fact]
    public async Task The_transfer_is_audited_with_the_super_admins_profile_id_in_the_audit_user_column()
    {
        Kit kit = new();
        kit.AddPerson("P-1", PisA);

        Result<ApiResult<PisOwnershipTransferDto>> result = await kit.Transfer("P-1", "PB-1");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal((int)SuperId, Assert.Single(kit.Audit.Entries).UserId);
    }

    // ═══════════ Validation ═══════════

    [Fact]
    public async Task An_unknown_personnel_record_is_404()
    {
        Kit kit = new();
        Result<ApiResult<PisOwnershipTransferDto>> result = await kit.Transfer("NOPE", "PA-1");
        Assert.Equal((ApiErrorCodes.NotFound, PisOwnershipTransferService.PersonnelNotFoundMessage), (result.ErrorCode, result.ErrorMessage));
        Assert.Empty(kit.Ownership.History);
    }

    [Fact]
    public async Task An_unknown_target_is_404_and_changes_nothing()
    {
        Kit kit = new();
        UserPis person = kit.AddPerson("P-1", PisA);

        Result<ApiResult<PisOwnershipTransferDto>> result = await kit.Transfer("P-1", "NOBODY");

        Assert.Equal((ApiErrorCodes.NotFound, PisOwnershipTransferService.TargetNotFoundMessage), (result.ErrorCode, result.ErrorMessage));
        Assert.Equal((long?)PisA, person.AssignedTo);
        Assert.Empty(kit.Ownership.History);
    }

    [Fact]
    public async Task An_inactive_target_is_refused()
    {
        Kit kit = new();
        UserPis person = kit.AddPerson("P-1", PisA);
        kit.AddAdmin(303, "PX-1", AdminRoleCodes.PisAdmin, active: false);

        Result<ApiResult<PisOwnershipTransferDto>> result = await kit.Transfer("P-1", "PX-1");

        Assert.Equal((ApiErrorCodes.Conflict, PisOwnershipTransferService.TargetInactiveMessage), (result.ErrorCode, result.ErrorMessage));
        Assert.Equal((long?)PisA, person.AssignedTo);
        Assert.Equal(0, kit.Ownership.Calls);
    }

    [Fact]
    public async Task A_target_that_never_completed_activation_is_refused()
    {
        Kit kit = new();
        UserPis person = kit.AddPerson("P-1", PisA);
        kit.AddAdmin(303, "PX-1", AdminRoleCodes.PisAdmin, status: AdminActivationStatus.PendingActivation);

        Result<ApiResult<PisOwnershipTransferDto>> result = await kit.Transfer("P-1", "PX-1");

        Assert.Equal((ApiErrorCodes.Conflict, PisOwnershipTransferService.TargetNotActivatedMessage), (result.ErrorCode, result.ErrorMessage));
        Assert.Equal((long?)PisA, person.AssignedTo);
    }

    [Theory]
    [InlineData(AdminRoleCodes.SuperAdmin)]
    [InlineData(AdminRoleCodes.RegistrationAdmin)]
    [InlineData(AdminRoleCodes.GrievanceAdmin)]
    [InlineData(AdminRoleCodes.LeaveAdmin)]
    public async Task A_target_with_any_role_other_than_pis_admin_is_refused(string role)
    {
        Kit kit = new();
        UserPis person = kit.AddPerson("P-1", PisA);
        kit.AddAdmin(303, "OTHER-1", role);

        Result<ApiResult<PisOwnershipTransferDto>> result = await kit.Transfer("P-1", "OTHER-1");

        Assert.Equal((ApiErrorCodes.ValidationFailed, PisOwnershipTransferService.TargetNotPisAdminMessage), (result.ErrorCode, result.ErrorMessage));
        Assert.Equal((long?)PisA, person.AssignedTo);
        Assert.Empty(kit.Ownership.History);
    }

    [Fact]
    public async Task Transferring_to_the_current_owner_is_a_clear_no_op_without_history_or_audit()
    {
        Kit kit = new();
        UserPis person = kit.AddPerson("P-1", PisA);

        Result<ApiResult<PisOwnershipTransferDto>> result = await kit.Transfer("P-1", "PA-1");

        Assert.True(result.IsSuccess);
        Assert.Equal(PisOwnershipTransferService.AlreadyAssignedMessage, result.Data!.Message);
        Assert.False(result.Data.Data!.Transferred);
        Assert.Null(result.Data.Data.TransferredAt);
        Assert.Empty(kit.Ownership.History);
        Assert.Empty(kit.Audit.Entries);
        Assert.Equal((long?)PisA, person.AssignedTo);
    }

    // ═══════════ Ownership behaviour ═══════════

    [Fact]
    public async Task A_record_moves_from_one_pis_admin_to_another_and_the_history_names_both_and_the_actor()
    {
        Kit kit = new();
        UserPis person = kit.AddPerson("P-1", PisA);

        Result<ApiResult<PisOwnershipTransferDto>> result = await kit.Transfer("  P-1 ", "PB-1", "  PIS Admin A left  ");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(PisOwnershipTransferService.TransferredMessage, result.Data!.Message);
        PisOwnershipTransferDto dto = result.Data.Data!;
        Assert.Equal(("P-1", "PB-1", true, true), (dto.PersonnelNsgNumber, dto.TargetPisAdminNsgNumber, dto.Transferred, dto.WasAssigned));
        Assert.Equal(TimeSpan.FromHours(5.5), dto.TransferredAt!.Value.Offset);
        Assert.Equal((long?)PisB, person.AssignedTo);

        PisOwnershipTransfer row = Assert.Single(kit.Ownership.History);
        Assert.Equal((person.Id, "P-1", (long?)PisA, PisB, SuperId, "PIS Admin A left"), (row.PisRecordId, row.NsgNumber, row.PreviousAssignedTo, row.NewAssignedTo, row.TransferredBy, row.Reason));
        Assert.NotEqual(default, row.TransferredAtIst);   // the IST instant comes from the server clock

        (AuditAction Action, string Result, int? UserId, string? EntityId, string? Data) audit = Assert.Single(kit.Audit.Entries);
        Assert.Equal(AuditAction.AdminAction, audit.Action);
        Assert.Contains("PisOwnershipTransferred", audit.Data);
    }

    [Fact]
    public async Task An_unassigned_record_can_be_assigned_and_the_history_has_no_previous_owner()
    {
        Kit kit = new();
        UserPis person = kit.AddPerson("P-1", owner: null);

        Result<ApiResult<PisOwnershipTransferDto>> result = await kit.Transfer("P-1", "PA-1");

        Assert.True(result.IsSuccess);
        Assert.False(result.Data!.Data!.WasAssigned);
        Assert.Equal((long?)PisA, person.AssignedTo);
        PisOwnershipTransfer row = Assert.Single(kit.Ownership.History);
        Assert.Equal(((long?)null, PisA), (row.PreviousAssignedTo, row.NewAssignedTo));
    }

    [Fact]
    public async Task Provenance_fields_are_untouched_by_a_transfer()
    {
        Kit kit = new();
        UserPis person = kit.AddPerson("P-1", PisA, createdBy: 7, updatedBy: 9);
        DateTime createdAt = person.CreatedAtIst;
        DateTime? updatedAt = person.UpdatedAtIst;

        await kit.Transfer("P-1", "PB-1");

        Assert.Equal((7, 9, createdAt, updatedAt), (person.CreatedBy, person.UpdatedBy, person.CreatedAtIst, person.UpdatedAtIst));
    }

    [Fact]
    public async Task A_pending_registration_request_follows_the_new_owner_and_is_not_rewritten()
    {
        Kit kit = new();
        kit.AddPerson("P-1", PisA);
        ApproveMobileUser request = new() { Id = 1, NsgNumber = "P-1", MobileNumber = "9811111111", RequestedAtIst = new DateTime(2026, 11, 1) };
        kit.Requests.Requests.Add(request);
        string before = $"{request.Id}|{request.ApprovalStatus}|{request.ApprovedBy}|{request.UpdatedBy}";

        Assert.Equal(1, (await kit.Requests.GetPagedAsync(PisA, 1, 20, null, null)).TotalCount);
        Assert.Equal(0, (await kit.Requests.GetPagedAsync(PisB, 1, 20, null, null)).TotalCount);

        await kit.Transfer("P-1", "PB-1");

        Assert.Equal(0, (await kit.Requests.GetPagedAsync(PisA, 1, 20, null, null)).TotalCount);   // the previous owner loses it at once
        Assert.Equal(1, (await kit.Requests.GetPagedAsync(PisB, 1, 20, null, null)).TotalCount);   // the new owner sees it
        Assert.Equal(before, $"{request.Id}|{request.ApprovalStatus}|{request.ApprovedBy}|{request.UpdatedBy}");
    }

    // ═══════════ Existing PIS Admin CRUD after / during a transfer ═══════════

    /// <summary>CRUD repository whose update guard reads the live owner, like the SQL guard. <see cref="BeforeSave"/> runs after the record was loaded.</summary>
    private sealed class GuardedCrudRepo : IUserPisRepository
    {
        public List<UserPis> Rows { get; } = new();
        public Action? BeforeSave { get; set; }
        public Task<UserPis?> GetByIdAsync(long id, CancellationToken ct = default) => Task.FromResult(Rows.FirstOrDefault(r => r.Id == id));
        public Task<UserPis?> GetDetailAsync(long id, CancellationToken ct = default) => Task.FromResult(Rows.FirstOrDefault(r => r.Id == id));
        public Task<UserPis?> GetByNsgNumberAsync(string nsgNumber, CancellationToken ct = default) => Task.FromResult(Rows.FirstOrDefault(r => r.NsgNumber == nsgNumber));
        public Task<(IReadOnlyList<UserPis> Items, int TotalCount)> GetPagedAsync(int pageNumber, int pageSize, UserPisFilter filter, long assignedTo, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PisSaveOutcome> AddAsync(UserPis entity, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PisSaveOutcome> UpdateAsync(UserPis entity, long ownerAdminId, CancellationToken ct = default)
        {
            BeforeSave?.Invoke();
            return Task.FromResult(entity.AssignedTo == ownerAdminId ? PisSaveOutcome.Saved : PisSaveOutcome.NotOwner);
        }
    }

    private static UserPisService CrudService(GuardedCrudRepo repo, long admin)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new NSG.Mobile.Infrastructure.Options.EncryptionOptions { Key = Convert.ToBase64String(new byte[32]) });
        return new UserPisService(repo, new NSG.Mobile.Infrastructure.Security.AesEncryptionService(options), new FakeAudit(),
            new FakeCurrentUser { AdminProfileId = admin }, NullLogger<UserPisService>.Instance);
    }

    private static UpdateUserPisRequestDto Change() => new() { ServiceNumber = "S-NEW", MobileNumber = "9876543210", IsActive = true };

    [Fact]
    public async Task The_previous_owner_cannot_update_or_deactivate_after_the_transfer_and_the_new_owner_can()
    {
        Kit kit = new();
        UserPis person = kit.AddPerson("P-1", PisA);
        person.ServiceNumber = "S-OLD";
        GuardedCrudRepo repo = new();
        repo.Rows.Add(person);

        await kit.Transfer("P-1", "PB-1");

        Assert.Equal(ApiErrorCodes.Forbidden, (await CrudService(repo, PisA).UpdateAsync(person.Id, Change(), default)).ErrorCode);
        Assert.Equal(ApiErrorCodes.Forbidden, (await CrudService(repo, PisA).DeactivateAsync(person.Id, default)).ErrorCode);
        Assert.True((await CrudService(repo, PisB).UpdateAsync(person.Id, Change(), default)).IsSuccess);
    }

    [Fact]
    public async Task A_transfer_that_lands_between_loading_and_saving_an_edit_is_refused_by_the_save_guard()
    {
        Kit kit = new();
        UserPis person = kit.AddPerson("P-1", PisA);
        GuardedCrudRepo repo = new();
        repo.Rows.Add(person);
        // The owner's edit passes the service check (owner = A when loaded), then the transfer commits, then the save guard runs.
        repo.BeforeSave = () => { person.AssignedTo = PisB; };

        Result<UserPisResponseDto> update = await CrudService(repo, PisA).UpdateAsync(person.Id, Change(), default);

        Assert.Equal(ApiErrorCodes.Forbidden, update.ErrorCode);
    }
}
