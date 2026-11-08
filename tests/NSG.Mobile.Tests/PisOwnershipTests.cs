using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging.Abstractions;
using NSG.Mobile.API.Controllers;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Application.Pis.DTOs;
using NSG.Mobile.Application.Pis.Import;
using NSG.Mobile.Application.Pis.Services;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;
using NSG.Mobile.Infrastructure.Repositories;

namespace NSG.Mobile.Tests;

/// <summary>
/// Ownership of personnel records (<c>users_pis.assigned_to</c>) in the PIS insert and update workflows, over in-memory fakes (no database).
/// The SQL ownership guard inside the row transaction is NOT exercised here (it needs MySQL); the service decisions, the protected
/// column list, the identity source and the authorization wiring are.
/// </summary>
public class PisOwnershipTests
{
    private const long AdminA = 101, AdminB = 202;

    // ═══════════ Excel upload ═══════════

    private sealed class ImportKit
    {
        public PisImportFakeRepository Repo { get; } = new();
        public FakeCurrentUser User { get; } = new() { AdminProfileId = AdminA };
        public PisImportService Service { get; }

        public ImportKit() => Service = new PisImportService(Repo, new PisFakeEncryption(), new FakeAudit(), User, NullLogger<PisImportService>.Instance);

        public async Task<PisUploadResultDto> UploadAsync(object?[][] people, PisUploadType type, long? admin = null)
        {
            if (admin != null) User.AdminProfileId = admin;
            byte[] bytes = PisXlsx.Build(people);
            Result<PisUploadResultDto> result = await Service.UploadAsync(new PisUploadFile(new MemoryStream(bytes), "pis.xlsx", bytes.Length), type, CancellationToken.None);
            Assert.True(result.IsSuccess, result.ErrorMessage);
            return result.Data!;
        }

        public UserPis Seed(string nsg, string mobile, long? assignedTo, string name = "Old Name")
        {
            UserPis p = new()
            {
                Id = Repo.People.Count + 500, NsgNumber = nsg, MobileNumber = mobile, FullName = name, IsActive = true,
                MobileAppActivationStatus = MobileAppActivationStatus.Approved, CreatedBy = 7, CreatedAtIst = new DateTime(2020, 1, 1),
                AadhaarNumberEncrypted = "enc:old", AssignedTo = assignedTo
            };
            Repo.People.Add(p);
            return p;
        }
    }

    [Theory]
    [InlineData(AdminA)]
    [InlineData(AdminB)]
    public async Task An_inserted_record_is_assigned_to_the_uploading_admin_and_created_by_keeps_its_meaning(long admin)
    {
        ImportKit kit = new() { };
        PisUploadResultDto result = await kit.UploadAsync(new[] { PisXlsx.Person("N1", "9811111101") }, PisUploadType.Upsert, admin);

        Assert.Equal(1, result.Inserted);
        UserPis p = Assert.Single(kit.Repo.People);
        Assert.Equal(admin, p.AssignedTo);
        Assert.Equal((int)admin, p.CreatedBy);   // unchanged existing behaviour
    }

    [Theory]
    [InlineData(PisUploadType.Upsert)]
    [InlineData(PisUploadType.UpdateOnly)]
    public async Task The_owner_can_update_and_ownership_and_created_by_are_unchanged(PisUploadType type)
    {
        ImportKit kit = new();
        UserPis stored = kit.Seed("N1", "9811111101", AdminA);

        PisUploadResultDto result = await kit.UploadAsync(new[] { PisXlsx.Person("N1", "9811111101", "New Name") }, type);

        Assert.Equal((0, 1, 0, 0), (result.Inserted, result.Updated, result.Failed, result.Conflicts));
        Assert.Equal("New Name", stored.FullName);
        Assert.Equal(AdminA, stored.AssignedTo);
        Assert.Equal(7, stored.CreatedBy);
        Assert.Equal((int)AdminA, stored.UpdatedBy);
    }

    [Theory]
    [InlineData(PisUploadType.Upsert)]
    [InlineData(PisUploadType.UpdateOnly)]
    public async Task Another_admin_cannot_update_the_record_and_nothing_changes(PisUploadType type)
    {
        ImportKit kit = new();
        UserPis stored = kit.Seed("N1", "9811111101", AdminA);

        PisUploadResultDto result = await kit.UploadAsync(new[] { PisXlsx.Person("N1", "9811111101", "Hijack") }, type, AdminB);

        Assert.Equal((0, 0, 1, 0), (result.Inserted, result.Updated, result.Failed, result.Conflicts));
        Assert.Equal(PisImportConflictTypes.AssignedToAnotherAdmin, Assert.Single(kit.Repo.Errors).ConflictType);
        Assert.Equal(("Old Name", AdminA, (int?)null, "enc:old"), (stored.FullName, stored.AssignedTo, stored.UpdatedBy, stored.AadhaarNumberEncrypted));
        Assert.Empty(kit.Repo.Records);
    }

    [Theory]
    [InlineData(PisUploadType.Upsert)]
    [InlineData(PisUploadType.UpdateOnly)]
    public async Task An_unassigned_record_is_not_updated_and_not_claimed_by_any_upload(PisUploadType type)
    {
        ImportKit kit = new();
        UserPis stored = kit.Seed("N1", "9811111101", assignedTo: null);

        PisUploadResultDto result = await kit.UploadAsync(new[] { PisXlsx.Person("N1", "9811111101", "Claim") }, type);

        Assert.Equal((0, 0, 1, 0), (result.Inserted, result.Updated, result.Failed, result.Conflicts));
        Assert.Equal(PisImportConflictTypes.RecordUnassigned, Assert.Single(kit.Repo.Errors).ConflictType);
        Assert.Equal(("Old Name", (long?)null, (int?)null), (stored.FullName, stored.AssignedTo, stored.UpdatedBy));
        Assert.Empty(kit.Repo.Records);
    }

    [Fact]
    public async Task The_rejection_is_reported_with_the_worksheet_row_and_a_clear_message_without_touching_other_rows()
    {
        ImportKit kit = new();
        UserPis foreign = kit.Seed("N1", "9811111101", AdminB);
        UserPis mine = kit.Seed("N2", "9811111102", AdminA);
        object?[] bad = PisXlsx.Person("N4", "9811111104"); bad[PisXlsx.Dob] = "garbage";

        PisUploadResultDto result = await kit.UploadAsync(new[]
        {
            PisXlsx.Person("N1", "9811111101", "Hijack"), PisXlsx.Person("N2", "9811111102", "Mine Changed"),
            PisXlsx.Person("N3", "9811111103", "Brand New"), bad
        }, PisUploadType.Upsert);

        Assert.Equal((4, 1, 1, 2, 0), (result.TotalRows, result.Inserted, result.Updated, result.Failed, result.Conflicts));
        Assert.Equal(result.TotalRows, result.Inserted + result.Updated + result.Failed + result.Conflicts);   // upload reporting still adds up
        PisUploadBatchError ownership = kit.Repo.Errors.Single(e => e.ConflictType == PisImportConflictTypes.AssignedToAnotherAdmin);
        Assert.Equal(("N1", 4), (ownership.NsgNumber, ownership.RowNumber));
        Assert.Contains("assigned to another PIS Admin", ownership.Message);
        Assert.Contains(kit.Repo.Errors, e => e.ConflictType == PisImportConflictTypes.InvalidDate);   // ordinary validation still reported
        Assert.Equal(("Old Name", AdminB), (foreign.FullName, foreign.AssignedTo));
        Assert.Equal(("Mine Changed", AdminA), (mine.FullName, mine.AssignedTo));
        Assert.Equal(AdminA, kit.Repo.People.Single(p => p.NsgNumber == "N3").AssignedTo);
    }

    [Fact]
    public async Task A_row_lost_to_a_concurrent_reassignment_is_reported_as_not_assigned_and_nothing_is_counted_as_saved()
    {
        ImportKit kit = new();
        kit.Seed("N1", "9811111101", AdminA);
        kit.Repo.Faults["N1"] = PisRowSaveStatus.NotOwner;   // the SQL guard refused it inside the row transaction

        PisUploadResultDto result = await kit.UploadAsync(new[] { PisXlsx.Person("N1", "9811111101", "Late") }, PisUploadType.UpdateOnly);

        Assert.Equal((0, 0, 1), (result.Inserted, result.Updated, result.Failed));
        Assert.Equal(PisImportConflictTypes.AssignedToAnotherAdmin, Assert.Single(kit.Repo.Errors).ConflictType);
    }

    [Fact]
    public async Task An_extra_assigned_to_column_in_the_workbook_never_changes_ownership()
    {
        ImportKit kit = new();
        UserPis stored = kit.Seed("N1", "9811111101", AdminA);
        string[] main = PisXlsx.Main.Append("ASSIGNED TO").ToArray();
        string[] sub = PisXlsx.Sub.Append(null!).ToArray();
        object?[] row = PisXlsx.Person("N1", "9811111101", "Via Excel").Append((object?)AdminB).ToArray();
        byte[] bytes = PisXlsx.Build(new[] { row }, main, sub);

        Result<PisUploadResultDto> result = await kit.Service.UploadAsync(new PisUploadFile(new MemoryStream(bytes), "pis.xlsx", bytes.Length), PisUploadType.Upsert, CancellationToken.None);

        // Whether the unknown column is ignored or the file refused, ownership is never read from it.
        Assert.Equal(AdminA, stored.AssignedTo);
        Assert.DoesNotContain(kit.Repo.People, p => p.AssignedTo == AdminB);
        if (!result.IsSuccess) Assert.Equal("Old Name", stored.FullName);
    }

    [Fact]
    public void Assigned_to_is_not_an_excel_field_and_the_update_never_writes_it()
    {
        Assert.DoesNotContain(PisImportFields.All, f => f.Name == nameof(UserPis.AssignedTo));

        FieldInfo protectedList = typeof(PisImportRepository).GetField("ProtectedProperties", BindingFlags.NonPublic | BindingFlags.Static)!;
        HashSet<string> names = (HashSet<string>)protectedList.GetValue(null)!;
        Assert.Contains(nameof(UserPis.AssignedTo), names);
        Assert.Contains(nameof(UserPis.CreatedBy), names);   // the original uploader is protected too
    }

    [Fact]
    public async Task A_caller_without_an_admin_portal_identity_cannot_upload()
    {
        ImportKit kit = new();
        kit.User.AdminProfileId = null;
        kit.User.UserId = 9001;   // legacy staff token: an int user id, no admin_profile_id
        byte[] bytes = PisXlsx.Build(new[] { PisXlsx.Person("N1", "9811111101") });

        Result<PisUploadResultDto> result = await kit.Service.UploadAsync(new PisUploadFile(new MemoryStream(bytes), "pis.xlsx", bytes.Length), PisUploadType.Upsert, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApiErrorCodes.Unauthorized, result.ErrorCode);
        Assert.Empty(kit.Repo.People);
    }

    // ═══════════ CRUD (api/v1/users-pis) ═══════════

    private sealed class CrudRepo : IUserPisRepository
    {
        public List<UserPis> Rows { get; } = new();
        public Task<UserPis?> GetByIdAsync(long id, CancellationToken ct = default) => Task.FromResult(Rows.FirstOrDefault(r => r.Id == id));
        public Task<UserPis?> GetDetailAsync(long id, CancellationToken ct = default) => Task.FromResult(Rows.FirstOrDefault(r => r.Id == id));
        public Task<UserPis?> GetByNsgNumberAsync(string nsgNumber, CancellationToken ct = default) => Task.FromResult(Rows.FirstOrDefault(r => r.NsgNumber == nsgNumber));
        public long? LastAssignedTo { get; private set; }

        // Like the SQL repository: the ownership predicate comes first and feeds both the page and the total.
        public Task<(IReadOnlyList<UserPis> Items, int TotalCount)> GetPagedAsync(int pageNumber, int pageSize, UserPisFilter filter, long assignedTo, CancellationToken ct = default)
        {
            LastAssignedTo = assignedTo;
            List<UserPis> mine = Rows.Where(r => r.AssignedTo == assignedTo)
                .Where(r => filter.Search == null || r.NsgNumber.Contains(filter.Search)).OrderBy(r => r.NsgNumber).ToList();
            return Task.FromResult<(IReadOnlyList<UserPis>, int)>((mine.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList(), mine.Count));
        }
        public Task<PisSaveOutcome> AddAsync(UserPis entity, CancellationToken ct = default) { entity.Id = Rows.Count + 1; Rows.Add(entity); return Task.FromResult(PisSaveOutcome.Saved); }
        public Task<PisSaveOutcome> UpdateAsync(UserPis entity, long ownerAdminId, CancellationToken ct = default) => Task.FromResult(PisSaveOutcome.Saved);
    }

    private static (UserPisService Service, CrudRepo Repo, FakeCurrentUser User) Crud(long? admin = AdminA, int? staffUserId = null)
    {
        CrudRepo repo = new();
        FakeCurrentUser user = new() { AdminProfileId = admin, UserId = staffUserId };
        Microsoft.Extensions.Options.IOptions<NSG.Mobile.Infrastructure.Options.EncryptionOptions> options =
            Microsoft.Extensions.Options.Options.Create(new NSG.Mobile.Infrastructure.Options.EncryptionOptions { Key = Convert.ToBase64String(new byte[32]) });
        return (new UserPisService(repo, new NSG.Mobile.Infrastructure.Security.AesEncryptionService(options), new FakeAudit(), user, NullLogger<UserPisService>.Instance), repo, user);
    }

    private static CreateUserPisRequestDto NewPerson(string nsg) => new() { NsgNumber = nsg, ServiceNumber = "S-" + nsg, MobileNumber = "9876543210" };

    private static UpdateUserPisRequestDto Change(string service = "S-NEW") => new() { ServiceNumber = service, MobileNumber = "9876543210", IsActive = true };

    [Theory]
    [InlineData(AdminA)]
    [InlineData(AdminB)]
    public async Task A_record_created_through_the_api_is_assigned_to_the_calling_admin(long admin)
    {
        (UserPisService service, CrudRepo repo, _) = Crud(admin);

        Result<UserPisResponseDto> result = await service.CreateAsync(NewPerson("N1"), default);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(admin, Assert.Single(repo.Rows).AssignedTo);
    }

    [Fact]
    public async Task The_owner_can_update_and_deactivate_and_ownership_is_unchanged()
    {
        (UserPisService service, CrudRepo repo, _) = Crud(AdminA);
        repo.Rows.Add(new UserPis { Id = 1, NsgNumber = "N1", ServiceNumber = "S-OLD", AssignedTo = AdminA, CreatedBy = 7, IsActive = true });

        Assert.True((await service.UpdateAsync(1, Change(), default)).IsSuccess);
        Assert.True((await service.DeactivateAsync(1, default)).IsSuccess);

        UserPis row = repo.Rows.Single();
        Assert.Equal(("S-NEW", false, AdminA, 7), (row.ServiceNumber, row.IsActive, row.AssignedTo, row.CreatedBy));
    }

    [Fact]
    public async Task Another_admin_cannot_update_or_deactivate_the_record_and_nothing_changes()
    {
        (UserPisService service, CrudRepo repo, _) = Crud(AdminB);
        repo.Rows.Add(new UserPis { Id = 1, NsgNumber = "N1", ServiceNumber = "S-OLD", AssignedTo = AdminA, IsActive = true });

        Result<UserPisResponseDto> update = await service.UpdateAsync(1, Change(), default);
        Result deactivate = await service.DeactivateAsync(1, default);

        Assert.Equal((ApiErrorCodes.Forbidden, ApiErrorCodes.Forbidden), (update.ErrorCode, deactivate.ErrorCode));
        UserPis row = repo.Rows.Single();
        Assert.Equal(("S-OLD", true, (long?)AdminA, (int?)null), (row.ServiceNumber, row.IsActive, row.AssignedTo, row.UpdatedBy));
    }

    [Fact]
    public async Task An_unassigned_record_cannot_be_updated_or_deactivated_through_the_api()
    {
        (UserPisService service, CrudRepo repo, _) = Crud(AdminA);
        repo.Rows.Add(new UserPis { Id = 1, NsgNumber = "N1", ServiceNumber = "S-OLD", AssignedTo = null, IsActive = true });

        Result<UserPisResponseDto> update = await service.UpdateAsync(1, Change(), default);
        Result deactivate = await service.DeactivateAsync(1, default);

        Assert.Equal((ApiErrorCodes.Forbidden, ApiErrorCodes.Forbidden), (update.ErrorCode, deactivate.ErrorCode));
        Assert.Contains("not assigned to any PIS Admin", update.ErrorMessage);
        UserPis row = repo.Rows.Single();
        Assert.Equal(("S-OLD", true, (long?)null), (row.ServiceNumber, row.IsActive, row.AssignedTo));
    }

    [Fact]
    public async Task A_missing_record_is_still_not_found()
    {
        (UserPisService service, _, _) = Crud(AdminA);
        Assert.Equal(ApiErrorCodes.NotFound, (await service.UpdateAsync(99, Change(), default)).ErrorCode);
        Assert.Equal(ApiErrorCodes.NotFound, (await service.DeactivateAsync(99, default)).ErrorCode);
    }

    [Fact]
    public async Task A_staff_identity_without_an_admin_profile_id_cannot_create_update_or_deactivate()
    {
        (UserPisService service, CrudRepo repo, _) = Crud(admin: null, staffUserId: 9001);
        repo.Rows.Add(new UserPis { Id = 1, NsgNumber = "N1", ServiceNumber = "S-OLD", AssignedTo = AdminA, IsActive = true });

        Assert.Equal(ApiErrorCodes.Unauthorized, (await service.CreateAsync(NewPerson("N2"), default)).ErrorCode);
        Assert.Equal(ApiErrorCodes.Unauthorized, (await service.UpdateAsync(1, Change(), default)).ErrorCode);
        Assert.Equal(ApiErrorCodes.Unauthorized, (await service.DeactivateAsync(1, default)).ErrorCode);
        Assert.Single(repo.Rows);
        Assert.Equal("S-OLD", repo.Rows[0].ServiceNumber);
    }

    [Fact]
    public async Task The_identity_comes_from_the_token_and_no_request_type_carries_an_owner()
    {
        // No request model of any PIS write path has an ownership property, so a client cannot supply or change it.
        foreach (Type request in new[] { typeof(CreateUserPisRequestDto), typeof(UpdateUserPisRequestDto), typeof(UserPisWriteDto), typeof(PisUploadForm) })
            Assert.DoesNotContain(request.GetProperties(), p => p.Name.Contains("Assigned", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("Owner", StringComparison.OrdinalIgnoreCase));

        // The admin who acts is the one named by the token (ICurrentUserService), whatever the record or request says.
        (UserPisService service, CrudRepo repo, FakeCurrentUser user) = Crud(AdminA);
        await service.CreateAsync(NewPerson("N1"), default);
        user.AdminProfileId = AdminB;
        await service.CreateAsync(NewPerson("N2"), default);
        Assert.Equal(new long?[] { AdminA, AdminB }, repo.Rows.Select(r => r.AssignedTo).ToArray());
    }

    // ═══════════ CRUD actor identity, provenance and audit ═══════════

    private static (UserPisService Service, CrudRepo Repo, FakeCurrentUser User, FakeAudit Audit) CrudAudited(long? admin)
    {
        CrudRepo repo = new();
        FakeAudit audit = new();
        FakeCurrentUser user = new() { AdminProfileId = admin, UserId = 9001 };   // 9001: a legacy id that must never be used as the actor
        Microsoft.Extensions.Options.IOptions<NSG.Mobile.Infrastructure.Options.EncryptionOptions> options =
            Microsoft.Extensions.Options.Options.Create(new NSG.Mobile.Infrastructure.Options.EncryptionOptions { Key = Convert.ToBase64String(new byte[32]) });
        return (new UserPisService(repo, new NSG.Mobile.Infrastructure.Security.AesEncryptionService(options), audit, user, NullLogger<UserPisService>.Instance), repo, user, audit);
    }

    [Theory]
    [InlineData(AdminA)]
    [InlineData(AdminB)]
    public async Task Create_records_the_acting_admin_as_created_by_and_assigned_to_and_audits_with_the_same_id(long admin)
    {
        (UserPisService service, CrudRepo repo, _, FakeAudit audit) = CrudAudited(admin);

        Result<UserPisResponseDto> result = await service.CreateAsync(NewPerson("N1"), default);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        UserPis row = Assert.Single(repo.Rows);
        Assert.Equal(((int?)admin, (long?)admin, (int?)null), (row.CreatedBy, row.AssignedTo, row.UpdatedBy));   // never the legacy 9001, never NULL
        (AuditAction Action, string Result, int? UserId, string? EntityId, string? Data) entry = Assert.Single(audit.Entries);
        Assert.Equal((int)admin, entry.UserId);
        Assert.Contains($"\"adminProfileId\":{admin}", entry.Data);
    }

    [Fact]
    public async Task A_different_owner_updating_keeps_created_by_and_sets_updated_by_to_that_admin()
    {
        (UserPisService service, CrudRepo repo, _, FakeAudit audit) = CrudAudited(AdminB);
        // Created by A, then handed to B through the ownership-transfer workflow.
        repo.Rows.Add(new UserPis { Id = 1, NsgNumber = "N1", ServiceNumber = "S-OLD", AssignedTo = AdminB, CreatedBy = (int)AdminA, IsActive = true });

        Assert.True((await service.UpdateAsync(1, Change(), default)).IsSuccess);

        UserPis row = repo.Rows.Single();
        Assert.Equal(((int?)AdminA, (int?)AdminB, (long?)AdminB), (row.CreatedBy, row.UpdatedBy, row.AssignedTo));
        Assert.Equal((int)AdminB, Assert.Single(audit.Entries).UserId);
    }

    [Fact]
    public async Task Deactivate_keeps_created_by_sets_updated_by_to_the_acting_admin_and_audits_with_the_admin_id()
    {
        (UserPisService service, CrudRepo repo, _, FakeAudit audit) = CrudAudited(AdminA);
        repo.Rows.Add(new UserPis { Id = 1, NsgNumber = "N1", ServiceNumber = "S", AssignedTo = AdminA, CreatedBy = 7, IsActive = true });

        Assert.True((await service.DeactivateAsync(1, default)).IsSuccess);

        UserPis row = repo.Rows.Single();
        Assert.Equal((false, (int?)7, (int?)AdminA, (long?)AdminA), (row.IsActive, row.CreatedBy, row.UpdatedBy, row.AssignedTo));
        Assert.Equal((int)AdminA, Assert.Single(audit.Entries).UserId);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-3L)]
    [InlineData((long)int.MaxValue + 1)]
    [InlineData(long.MaxValue)]
    public async Task An_unusable_admin_id_is_refused_with_401_before_anything_is_written_by_create_update_and_deactivate(long admin)
    {
        (UserPisService service, CrudRepo repo, _, FakeAudit audit) = CrudAudited(admin);
        // The record is owned by exactly this id, so only the identity check (not ownership) can be what refuses.
        repo.Rows.Add(new UserPis { Id = 1, NsgNumber = "N1", ServiceNumber = "S-OLD", AssignedTo = admin, CreatedBy = 7, IsActive = true });

        Result<UserPisResponseDto> create = await service.CreateAsync(NewPerson("N2"), default);
        Result<UserPisResponseDto> update = await service.UpdateAsync(1, Change(), default);
        Result deactivate = await service.DeactivateAsync(1, default);

        Assert.All(new[] { create.ErrorCode, update.ErrorCode, deactivate.ErrorCode }, c => Assert.Equal(ApiErrorCodes.Unauthorized, c));
        Assert.All(new[] { create.ErrorMessage, update.ErrorMessage, deactivate.ErrorMessage }, m => Assert.Equal("The administrator could not be identified.", m));
        UserPis row = Assert.Single(repo.Rows);   // no insert
        Assert.Equal(("S-OLD", true, (int?)null, (int?)7), (row.ServiceNumber, row.IsActive, row.UpdatedBy, row.CreatedBy));
        Assert.Empty(audit.Entries);
    }

    [Fact]
    public async Task The_largest_usable_admin_id_is_stored_exactly()
    {
        (UserPisService service, CrudRepo repo, _, FakeAudit audit) = CrudAudited(int.MaxValue);

        Assert.True((await service.CreateAsync(NewPerson("N1"), default)).IsSuccess);

        Assert.Equal((int.MaxValue, (long?)int.MaxValue), (Assert.Single(repo.Rows).CreatedBy, repo.Rows[0].AssignedTo));
        Assert.Equal(int.MaxValue, Assert.Single(audit.Entries).UserId);
    }

    [Fact]
    public async Task Reading_one_record_returns_only_the_callers_own_403_for_another_admins_and_404_for_unassigned_or_missing()
    {
        (UserPisService service, CrudRepo repo, FakeCurrentUser user, _) = CrudAudited(AdminA);
        repo.Rows.Add(new UserPis { Id = 1, NsgNumber = "OWN", ServiceNumber = "S", AssignedTo = AdminA });
        repo.Rows.Add(new UserPis { Id = 2, NsgNumber = "THEIRS", ServiceNumber = "S", AssignedTo = AdminB });
        repo.Rows.Add(new UserPis { Id = 3, NsgNumber = "NOBODY", ServiceNumber = "S", AssignedTo = null });

        Assert.True((await service.GetByIdAsync(1, default)).IsSuccess);
        Assert.Equal(ApiErrorCodes.Forbidden, (await service.GetByIdAsync(2, default)).ErrorCode);
        Assert.Equal(ApiErrorCodes.NotFound, (await service.GetByIdAsync(3, default)).ErrorCode);
        Assert.Equal(ApiErrorCodes.NotFound, (await service.GetByIdAsync(99, default)).ErrorCode);

        user.AdminProfileId = AdminB;   // the same service instance now serves the next request's identity: no cached actor
        Assert.True((await service.GetByIdAsync(2, default)).IsSuccess);
        Assert.Equal(ApiErrorCodes.Forbidden, (await service.GetByIdAsync(1, default)).ErrorCode);

        user.AdminProfileId = null;     // a legacy staff identity has no admin profile id
        Assert.Equal(ApiErrorCodes.Unauthorized, (await service.GetByIdAsync(1, default)).ErrorCode);
    }

    [Fact]
    public async Task The_list_contains_only_the_callers_records_with_a_scoped_total_and_pages()
    {
        (UserPisService service, CrudRepo repo, FakeCurrentUser user, _) = CrudAudited(AdminA);
        for (int i = 1; i <= 5; i++) repo.Rows.Add(new UserPis { Id = i, NsgNumber = "A-" + i, ServiceNumber = "S", AssignedTo = AdminA });
        for (int i = 1; i <= 3; i++) repo.Rows.Add(new UserPis { Id = 10 + i, NsgNumber = "B-" + i, ServiceNumber = "S", AssignedTo = AdminB });
        repo.Rows.Add(new UserPis { Id = 20, NsgNumber = "NOBODY", ServiceNumber = "S", AssignedTo = null });

        PaginatedResponse<UserPisListItemDto> first = (await service.GetPagedAsync(1, 2, new UserPisFilter(), default)).Data!;
        PaginatedResponse<UserPisListItemDto> last = (await service.GetPagedAsync(3, 2, new UserPisFilter(), default)).Data!;
        PaginatedResponse<UserPisListItemDto> searched = (await service.GetPagedAsync(1, 20, new UserPisFilter { Search = "B-" }, default)).Data!;

        Assert.Equal(5, first.TotalCount);                                                    // the total counts only A's records
        Assert.Equal(new[] { "A-1", "A-2" }, first.Items.Select(i => i.NsgNumber).ToArray());
        Assert.Equal(new[] { "A-5" }, last.Items.Select(i => i.NsgNumber).ToArray());
        Assert.Equal(0, searched.TotalCount);                                                 // a search cannot reach B's records
        Assert.Equal(AdminA, repo.LastAssignedTo);                                            // the id sent to the repository is the caller's own

        user.AdminProfileId = AdminB;                                                         // the next request's identity: nothing cached
        Assert.Equal(3, (await service.GetPagedAsync(1, 20, new UserPisFilter(), default)).Data!.TotalCount);
        Assert.Equal(AdminB, repo.LastAssignedTo);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    [InlineData((long)int.MaxValue + 1)]
    public async Task The_list_is_refused_with_401_when_the_admin_id_is_missing_or_unusable(long? admin)
    {
        (UserPisService service, CrudRepo repo, _, _) = CrudAudited(admin);
        repo.Rows.Add(new UserPis { Id = 1, NsgNumber = "A-1", ServiceNumber = "S", AssignedTo = admin });

        Result<PaginatedResponse<UserPisListItemDto>> result = await service.GetPagedAsync(1, 20, new UserPisFilter(), default);

        Assert.Equal((ApiErrorCodes.Unauthorized, "The administrator could not be identified."), (result.ErrorCode, result.ErrorMessage));
        Assert.Null(repo.LastAssignedTo);   // the repository was never asked
    }
    // ═══════════ Authorization wiring ═══════════

    [Fact]
    public void Personnel_writes_and_uploads_require_the_admin_portal_policy_and_never_the_legacy_staff_one()
    {
        AssertPolicy(typeof(UsersPisController), "Create", DomainConstants.Policies.RequirePisAdminPortal);
        AssertPolicy(typeof(UsersPisController), "Update", DomainConstants.Policies.RequirePisAdminPortal);
        AssertPolicy(typeof(UsersPisController), "Deactivate", DomainConstants.Policies.RequirePisAdminPortal);
        AssertPolicy(typeof(PisAdminController), "Upload", DomainConstants.Policies.RequirePisAdminPortal);
        AssertPolicy(typeof(PisAdminController), "UpdateUpload", DomainConstants.Policies.RequirePisAdminPortal);

        // No class-level policy remains that could widen a write action, and no write action is anonymous. Super Admin is in neither policy.
        Assert.Empty(typeof(UsersPisController).GetCustomAttributes(typeof(AuthorizeAttribute), true));
        Assert.Empty(typeof(PisAdminController).GetCustomAttributes(typeof(AuthorizeAttribute), true));
        Assert.NotEqual(DomainConstants.Policies.RequirePisAdmin, DomainConstants.Policies.RequirePisAdminPortal);
    }

    private static void AssertPolicy(Type controller, string action, string policy)
    {
        MethodInfo method = controller.GetMethod(action)!;
        AuthorizeAttribute authorize = Assert.Single(method.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Equal(policy, authorize.Policy);
        Assert.Empty(method.GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
    }
}
