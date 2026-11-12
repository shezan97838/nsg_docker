using System.Data.Common;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Application.Pis.Services;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;
using NSG.Mobile.Infrastructure.Persistence;
using NSG.Mobile.Infrastructure.Repositories;

namespace NSG.Mobile.Tests;

/// <summary>
/// DATABASE-BACKED tests of the Super Admin ownership transfer on real MySQL 8 through the real host: authorization of the five caller kinds,
/// validation of the target, the update of <c>assigned_to</c> alone, the history row, the pending approval following the owner, the old
/// owner's CRUD refusal, and the row-lock behaviour against a registration decision and a PIS Admin edit. They run ONLY when NSG_TEST_MYSQL
/// names a disposable database ending in "_test" (ApiFactory drops and recreates it through the migrations, including
/// <c>Add_PIS_Ownership_Transfers</c>), and are SKIPPED otherwise: then none of the transaction, row-lock or concurrency behaviour below
/// has been verified.
/// </summary>
[Collection("mysql-approval-ownership")]
public class PisOwnershipTransferDatabaseTests : IAsyncLifetime
{
    private const string Transfer = "/api/v1/admin/pis-personnel/transfer-ownership", Approvals = "/api/v1/mobile-approval-requests";
    private const string SuperNsg = "RAS-0001", PisANsg = "RAS-0002", PisBNsg = "RAS-0003", RegistrationNsg = "RAS-0004";

    private ApiFactory _factory = null!;
    private ApprovalAdmins _admins = null!;
    private int _counter;

    public async Task InitializeAsync()
    {
        _factory = new ApiFactory();
        await _factory.ResetDatabaseAsync();
        _admins = await ApprovalAdminSession.CreateAsync(_factory);
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private async Task<UserPis> SeedPersonAsync(string nsg, long? owner, int? createdBy = 5, int? updatedBy = 6)
    {
        await using AppDbContext ctx = _factory.CreateContext();
        UserPis person = new()
        {
            NsgNumber = nsg, ServiceNumber = "S-" + nsg, FullName = "Person " + nsg, MobileNumber = (9840000000L + ++_counter).ToString(),
            DateOfBirth = new DateOnly(1990, 5, 15), AssignedTo = owner, CreatedBy = createdBy, UpdatedBy = updatedBy, IsActive = true
        };
        ctx.UsersPis.Add(person);
        await ctx.SaveChangesAsync();
        return person;
    }

    private async Task<long> SeedRequestAsync(UserPis person)
    {
        await using AppDbContext ctx = _factory.CreateContext();
        ApproveMobileUser request = new() { NsgNumber = person.NsgNumber, MobileNumber = person.MobileNumber, DateOfBirth = new DateOnly(1990, 5, 15) };
        ctx.ApproveMobileUsers.Add(request);
        await ctx.SaveChangesAsync();
        return request.Id;
    }

    private static StringContent Json(object body) => new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    private static async Task<(HttpStatusCode Status, JsonElement Body)> SendAsync(HttpClient client, HttpMethod method, string url, object? body = null)
    {
        using HttpRequestMessage request = new(method, url) { Content = body == null ? null : Json(body) };
        HttpResponseMessage response = await client.SendAsync(request);
        string text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    private static Task<(HttpStatusCode Status, JsonElement Body)> TransferAsync(HttpClient client, string person, string target, string? reason = null)
        => SendAsync(client, HttpMethod.Post, Transfer, new { personnelNsgNumber = person, targetPisAdminNsgNumber = target, reason });

    private async Task<UserPis> PersonAsync(string nsg)
    {
        await using AppDbContext ctx = _factory.CreateContext();
        return await ctx.UsersPis.AsNoTracking().SingleAsync(p => p.NsgNumber == nsg);
    }

    private async Task<List<PisOwnershipTransfer>> HistoryAsync(string nsg)
    {
        await using AppDbContext ctx = _factory.CreateContext();
        return await ctx.PisOwnershipTransfers.AsNoTracking().Where(h => h.NsgNumber == nsg).OrderBy(h => h.Id).ToListAsync();
    }

    // ═══════════ Authorization through the real pipeline ═══════════

    [MySqlFact]
    public async Task Only_a_super_admin_can_transfer_every_other_caller_is_refused_and_nothing_changes()
    {
        await SeedPersonAsync("OWNT-1", _admins.PisAId);

        Assert.Equal(HttpStatusCode.Forbidden, (await TransferAsync(_admins.PisA, "OWNT-1", PisBNsg)).Status);           // PIS Admin
        Assert.Equal(HttpStatusCode.Forbidden, (await TransferAsync(_admins.PisB, "OWNT-1", PisBNsg)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await TransferAsync(_admins.Registration, "OWNT-1", PisBNsg)).Status);   // Registration Admin
        foreach (string role in new[] { DomainConstants.Roles.SuperAdmin, "SUPER_ADMIN", DomainConstants.Roles.PisAdmin })
            Assert.Equal(HttpStatusCode.Unauthorized, (await TransferAsync(_factory.CreateClientFor(role), "OWNT-1", PisBNsg)).Status);   // staff Bearer tokens
        Assert.Equal(HttpStatusCode.Unauthorized, (await TransferAsync(_factory.CreateClientFor(null), "OWNT-1", PisBNsg)).Status);       // anonymous

        Assert.Equal(_admins.PisAId, (await PersonAsync("OWNT-1")).AssignedTo);
        Assert.Empty(await HistoryAsync("OWNT-1"));

        Assert.Equal(HttpStatusCode.OK, (await TransferAsync(_admins.Super, "OWNT-1", PisBNsg)).Status);                  // Super Admin
        Assert.Equal(_admins.PisBId, (await PersonAsync("OWNT-1")).AssignedTo);
    }

    [MySqlFact]
    public async Task Super_admin_gains_no_pis_read_update_or_approval_access_from_this_api()
    {
        await SeedPersonAsync("OWNT-1", _admins.PisAId);

        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(_admins.Super, HttpMethod.Get, "/api/v1/admin/users-pis")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(_admins.Super, HttpMethod.Get, Approvals)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(_admins.Super, HttpMethod.Delete, "/api/v1/users-pis/1")).Status);
    }

    // ═══════════ Validation ═══════════

    [MySqlFact]
    public async Task Unknown_people_and_unsuitable_targets_are_refused()
    {
        await SeedPersonAsync("OWNT-1", _admins.PisAId);

        Assert.Equal(HttpStatusCode.NotFound, (await TransferAsync(_admins.Super, "NOPE", PisBNsg)).Status);           // personnel not found
        Assert.Equal(HttpStatusCode.NotFound, (await TransferAsync(_admins.Super, "OWNT-1", "NOBODY")).Status);        // target not found
        Assert.Equal(HttpStatusCode.BadRequest, (await TransferAsync(_admins.Super, "OWNT-1", RegistrationNsg)).Status); // wrong role
        Assert.Equal(HttpStatusCode.BadRequest, (await TransferAsync(_admins.Super, "OWNT-1", SuperNsg)).Status);       // never to a Super Admin
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(_admins.Super, HttpMethod.Post, Transfer, new { personnelNsgNumber = "", targetPisAdminNsgNumber = PisBNsg })).Status);

        Assert.Equal(_admins.PisAId, (await PersonAsync("OWNT-1")).AssignedTo);
        Assert.Empty(await HistoryAsync("OWNT-1"));
    }

    [MySqlFact]
    public async Task An_inactive_target_and_a_target_that_never_activated_are_refused()
    {
        await SeedPersonAsync("OWNT-1", _admins.PisAId);

        // Inactive: the Super Admin deactivates PIS Admin B through the existing account API.
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(_admins.Super, HttpMethod.Post, $"/api/v1/admin/accounts/{_admins.PisBId}/deactivate")).Status);
        Assert.Equal(HttpStatusCode.Conflict, (await TransferAsync(_admins.Super, "OWNT-1", PisBNsg)).Status);

        // Never activated: a provisioned PIS_ADMIN account that has not completed the OTP activation.
        UserPis pending = await SeedPersonAsync("OWNT-PENDING", owner: null);
        HttpResponseMessage created = await _admins.Super.PostAsync("/api/v1/admin/accounts",
            Json(new { nsgNumber = pending.NsgNumber, mobileNumber = pending.MobileNumber, dateOfBirth = "1990-05-15", roleCode = AdminRoleCodes.PisAdmin }));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await TransferAsync(_admins.Super, "OWNT-1", "OWNT-PENDING")).Status);

        Assert.Equal(_admins.PisAId, (await PersonAsync("OWNT-1")).AssignedTo);
        Assert.Empty(await HistoryAsync("OWNT-1"));
    }

    [MySqlFact]
    public async Task Transferring_to_the_current_owner_is_a_no_op_with_no_history()
    {
        await SeedPersonAsync("OWNT-1", _admins.PisAId);

        (HttpStatusCode status, JsonElement body) = await TransferAsync(_admins.Super, "OWNT-1", PisANsg);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.False(body.GetProperty("data").GetProperty("transferred").GetBoolean());
        Assert.Empty(await HistoryAsync("OWNT-1"));
    }

    // ═══════════ Ownership behaviour ═══════════

    [MySqlFact]
    public async Task A_transfer_changes_only_assigned_to_and_writes_one_history_row()
    {
        await SeedPersonAsync("OWNT-1", _admins.PisAId, createdBy: 5, updatedBy: 6);
        // Re-read the seeded row: the in-memory entity holds 100 ns ticks, the datetime(6) column keeps microseconds, so only a stored value is comparable.
        UserPis before = await PersonAsync("OWNT-1");

        (HttpStatusCode status, JsonElement body) = await TransferAsync(_admins.Super, "OWNT-1", PisBNsg, "Reassigned after posting change");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("Personnel ownership transferred successfully.", body.GetProperty("message").GetString());
        Assert.True(body.GetProperty("data").GetProperty("transferred").GetBoolean());
        Assert.EndsWith("+05:30", body.GetProperty("data").GetProperty("transferredAt").GetString());

        UserPis after = await PersonAsync("OWNT-1");
        Assert.Equal(_admins.PisBId, after.AssignedTo);
        Assert.Equal((before.CreatedBy, before.UpdatedBy, before.CreatedAtIst, before.UpdatedAtIst, before.FullName, before.MobileNumber, before.IsActive),
            (after.CreatedBy, after.UpdatedBy, after.CreatedAtIst, after.UpdatedAtIst, after.FullName, after.MobileNumber, after.IsActive));

        PisOwnershipTransfer history = Assert.Single(await HistoryAsync("OWNT-1"));
        Assert.Equal((before.Id, _admins.PisAId, _admins.PisBId, "Reassigned after posting change"), (history.PisRecordId, history.PreviousAssignedTo, history.NewAssignedTo, history.Reason));
        Assert.Equal(await AdminIdAsync(SuperNsg), history.TransferredBy);   // the actor is the token's admin, not a request value
        Assert.NotEqual(default, history.TransferredAtIst);
    }

    private async Task<long> AdminIdAsync(string nsg)
    {
        await using AppDbContext ctx = _factory.CreateContext();
        return await ctx.AdminProfiles.AsNoTracking().Where(p => p.NsgNumber == nsg).Select(p => p.Id).SingleAsync();
    }

    [MySqlFact]
    public async Task An_unassigned_record_can_be_assigned_with_a_null_previous_owner_in_the_history()
    {
        await SeedPersonAsync("OWNT-1", owner: null);

        Assert.Equal(HttpStatusCode.OK, (await TransferAsync(_admins.Super, "OWNT-1", PisANsg)).Status);

        Assert.Equal(_admins.PisAId, (await PersonAsync("OWNT-1")).AssignedTo);
        PisOwnershipTransfer history = Assert.Single(await HistoryAsync("OWNT-1"));
        Assert.Null(history.PreviousAssignedTo);
        Assert.Equal(_admins.PisAId, history.NewAssignedTo);
    }

    [MySqlFact]
    public async Task A_pending_registration_request_follows_the_new_owner_and_the_old_owner_can_no_longer_edit()
    {
        UserPis person = await SeedPersonAsync("OWNT-1", _admins.PisAId);
        long requestId = await SeedRequestAsync(person);
        Assert.Equal(1, (await SendAsync(_admins.PisA, HttpMethod.Get, Approvals)).Body.GetProperty("data").GetProperty("totalCount").GetInt32());

        Assert.Equal(HttpStatusCode.OK, (await TransferAsync(_admins.Super, "OWNT-1", PisBNsg)).Status);

        Assert.True((await SendAsync(_admins.PisA, HttpMethod.Get, Approvals)).Body.TryGetProperty("message", out _));   // A: nothing left
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(_admins.PisA, HttpMethod.Put, $"{Approvals}/{requestId}/decision", new { decision = "Approved" })).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(_admins.PisA, HttpMethod.Delete, $"/api/v1/users-pis/{person.Id}")).Status);   // CRUD refused
        Assert.True((await PersonAsync("OWNT-1")).IsActive);

        Assert.Equal(1, (await SendAsync(_admins.PisB, HttpMethod.Get, Approvals)).Body.GetProperty("data").GetProperty("totalCount").GetInt32());
        await using (AppDbContext ctx = _factory.CreateContext())
        {
            ApproveMobileUser row = await ctx.ApproveMobileUsers.AsNoTracking().SingleAsync(a => a.Id == requestId);
            Assert.Equal((MobileAppActivationStatus.Pending, (int?)null, (int?)null), (row.ApprovalStatus, row.ApprovedBy, row.UpdatedBy));   // not rewritten
        }
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(_admins.PisB, HttpMethod.Put, $"{Approvals}/{requestId}/decision", new { decision = "Approved" })).Status);
    }

    // ═══════════ Concurrency (transaction + row lock) ═══════════

    [MySqlFact]
    public async Task A_transfer_waits_for_an_open_registration_decision_lock_then_applies()
    {
        UserPis person = await SeedPersonAsync("OWNT-1", _admins.PisAId);

        // A decision transaction that holds the personnel row lock (its first statement) and has not finished.
        await using AppDbContext decisionContext = _factory.CreateContext();
        await using var decisionTx = await decisionContext.Database.BeginTransactionAsync();
        await decisionContext.UsersPis.Where(p => p.NsgNumber == "OWNT-1" && p.AssignedTo == _admins.PisAId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.AssignedTo, _admins.PisAId));

        await using AppDbContext transferContext = _factory.CreateContext();
        Task<PisOwnershipTransferResult> transfer = new PisOwnershipRepository(transferContext)
            .TransferAsync("OWNT-1", _admins.PisBId, await AdminIdAsync(SuperNsg), null, DateTime.Now);

        await Task.Delay(1500);
        Assert.False(transfer.IsCompleted, "the transfer must wait for the decision's lock");
        Assert.Equal(_admins.PisAId, (await PersonAsync("OWNT-1")).AssignedTo);

        await decisionTx.CommitAsync();
        PisOwnershipTransferResult result = await transfer.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(PisOwnershipTransferOutcome.Transferred, result.Outcome);
        Assert.Equal(_admins.PisAId, result.PreviousAssignedTo);   // the owner found under the lock
        Assert.Equal(_admins.PisBId, (await PersonAsync("OWNT-1")).AssignedTo);
        Assert.Equal(person.Id, Assert.Single(await HistoryAsync("OWNT-1")).PisRecordId);
    }

    [MySqlFact]
    public async Task Racing_transfers_and_registration_decisions_never_leave_a_half_applied_state()
    {
        long superId = await AdminIdAsync(SuperNsg);
        const int rounds = 12;
        int decided = 0, refused = 0;
        for (int i = 0; i < rounds; i++)
        {
            string nsg = $"OWNT-R{i}";
            UserPis person = await SeedPersonAsync(nsg, _admins.PisAId);
            long requestId = await SeedRequestAsync(person);

            await using AppDbContext decisionContext = _factory.CreateContext();
            await using AppDbContext transferContext = _factory.CreateContext();
            Task<ApprovalDecisionResult> decide = Task.Run(() => new ApproveMobileUserRepository(decisionContext)
                .DecideAsync(requestId, MobileAppActivationStatus.Approved, _admins.PisAId, null, DateTime.Now));
            Task<PisOwnershipTransferResult> transfer = Task.Run(() => new PisOwnershipRepository(transferContext)
                .TransferAsync(nsg, _admins.PisBId, superId, null, DateTime.Now));
            await Task.WhenAll(decide, transfer);

            Assert.Equal(PisOwnershipTransferOutcome.Transferred, transfer.Result.Outcome);   // the transfer always lands
            Assert.Equal(_admins.PisBId, (await PersonAsync(nsg)).AssignedTo);
            Assert.Single(await HistoryAsync(nsg));

            await using AppDbContext ctx = _factory.CreateContext();
            ApproveMobileUser row = await ctx.ApproveMobileUsers.AsNoTracking().SingleAsync(a => a.Id == requestId);
            if (decide.Result.Outcome == ApprovalDecisionOutcome.Decided)
            {
                decided++;   // the decision serialised before the transfer: one coherent decision by A
                Assert.Equal((MobileAppActivationStatus.Approved, (int?)_admins.PisAId), (row.ApprovalStatus, row.ApprovedBy));
            }
            else
            {
                refused++;   // the transfer committed first: A decided nothing
                Assert.Equal(ApprovalDecisionOutcome.NotOwner, decide.Result.Outcome);
                Assert.Equal((MobileAppActivationStatus.Pending, (int?)null), (row.ApprovalStatus, row.ApprovedBy));
            }
        }

        Assert.Equal(rounds, decided + refused);
    }

    [MySqlFact]
    public async Task A_pis_admin_edit_saved_after_a_transfer_committed_is_refused_and_one_saved_before_it_is_kept()
    {
        UserPis person = await SeedPersonAsync("OWNT-1", _admins.PisAId);

        // The PIS Admin loaded the record (owner = A) and edits it; meanwhile a transfer is open and holds the row lock.
        await using AppDbContext editContext = _factory.CreateContext();
        UserPisRepository editRepo = new(editContext);
        UserPis loaded = (await editRepo.GetByIdAsync(person.Id))!;
        loaded.ServiceNumber = "S-EDITED";

        await using AppDbContext transferContext = _factory.CreateContext();
        await using var transferTx = await transferContext.Database.BeginTransactionAsync();
        await transferContext.Database.ExecuteSqlRawAsync("UPDATE `users_pis` SET `assigned_to` = {0} WHERE `nsg_number` = 'OWNT-1'", _admins.PisBId);

        Task<PisSaveOutcome> save = editRepo.UpdateAsync(loaded, _admins.PisAId);
        await Task.Delay(1500);
        Assert.False(save.IsCompleted, "the edit must wait for the transfer's row lock");

        await transferTx.CommitAsync();
        Assert.Equal(PisSaveOutcome.NotOwner, await save.WaitAsync(TimeSpan.FromSeconds(20)));
        Assert.Equal("S-OWNT-1", (await PersonAsync("OWNT-1")).ServiceNumber);   // nothing of the edit was saved
        Assert.Equal(_admins.PisBId, (await PersonAsync("OWNT-1")).AssignedTo);

        // The owner (now B) can still edit normally.
        await using AppDbContext secondContext = _factory.CreateContext();
        UserPisRepository secondRepo = new(secondContext);
        UserPis again = (await secondRepo.GetByIdAsync(person.Id))!;
        again.ServiceNumber = "S-BY-B";
        Assert.Equal(PisSaveOutcome.Saved, await secondRepo.UpdateAsync(again, _admins.PisBId));
        Assert.Equal("S-BY-B", (await PersonAsync("OWNT-1")).ServiceNumber);
    }
}

/// <summary>
/// DATABASE-BACKED tests of the Super Admin BULK ownership transfer on real MySQL 8 through the real host and repository:
/// authorization of all caller kinds, validation of source &amp; target admins (including inactive source), atomic multi-record
/// updates, per-record history entries, transaction rollback on update or history failure, concurrent transfer consistency,
/// zero-record transfers, and compatibility with existing individual ownership transfers.
/// </summary>
[Collection("mysql-approval-ownership")]
public class PisBulkOwnershipTransferDatabaseTests : IAsyncLifetime
{
    private const string BulkTransfer = "/api/v1/admin/pis-personnel/bulk-transfer-ownership";
    private const string SingleTransfer = "/api/v1/admin/pis-personnel/transfer-ownership";
    private const string Approvals = "/api/v1/mobile-approval-requests";
    private const string SuperNsg = "RAS-0001", PisBNsg = "RAS-0003", RegistrationNsg = "RAS-0004";

    private ApiFactory _factory = null!;
    private ApprovalAdmins _admins = null!;
    private int _counter;

    public async Task InitializeAsync()
    {
        _factory = new ApiFactory();
        await _factory.ResetDatabaseAsync();
        _admins = await ApprovalAdminSession.CreateAsync(_factory);
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private async Task<UserPis> SeedPersonAsync(long? owner, int? createdBy = 5, int? updatedBy = 6)
    {
        await using AppDbContext ctx = _factory.CreateContext();
        string nsg = $"BULK-{++_counter}";
        UserPis person = new()
        {
            NsgNumber = nsg, ServiceNumber = "S-" + nsg, FullName = "Person " + nsg,
            MobileNumber = (9840000000L + _counter).ToString(),
            DateOfBirth = new DateOnly(1990, 5, 15), AssignedTo = owner,
            CreatedBy = createdBy, UpdatedBy = updatedBy, IsActive = true
        };
        ctx.UsersPis.Add(person);
        await ctx.SaveChangesAsync();
        return person;
    }

    private async Task<long> SeedRequestAsync(UserPis person)
    {
        await using AppDbContext ctx = _factory.CreateContext();
        ApproveMobileUser request = new() { NsgNumber = person.NsgNumber, MobileNumber = person.MobileNumber, DateOfBirth = new DateOnly(1990, 5, 15) };
        ctx.ApproveMobileUsers.Add(request);
        await ctx.SaveChangesAsync();
        return request.Id;
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> SendAsync(HttpClient client, HttpMethod method, string url, object? body = null)
    {
        using HttpRequestMessage request = new(method, url)
        {
            Content = body == null ? null : new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        HttpResponseMessage response = await client.SendAsync(request);
        string text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    private static Task<(HttpStatusCode Status, JsonElement Body)> BulkTransferAsync(HttpClient client, long sourceAdminId, long targetAdminId, string? reason = null)
        => SendAsync(client, HttpMethod.Post, BulkTransfer, new { sourceAdminId, targetAdminId, reason });

    private async Task<UserPis> PersonAsync(string nsg)
    {
        await using AppDbContext ctx = _factory.CreateContext();
        return await ctx.UsersPis.AsNoTracking().SingleAsync(p => p.NsgNumber == nsg);
    }

    private async Task<List<PisOwnershipTransfer>> HistoryAsync(string nsg)
    {
        await using AppDbContext ctx = _factory.CreateContext();
        return await ctx.PisOwnershipTransfers.AsNoTracking().Where(h => h.NsgNumber == nsg).OrderBy(h => h.Id).ToListAsync();
    }

    private async Task<long> AdminIdAsync(string nsg)
    {
        await using AppDbContext ctx = _factory.CreateContext();
        return await ctx.AdminProfiles.AsNoTracking().Where(p => p.NsgNumber == nsg).Select(p => p.Id).SingleAsync();
    }

    private AppDbContext CreateInterceptedContext(DbCommandInterceptor interceptor)
    {
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(_factory.ConnectionString, ServerVersion.AutoDetect(_factory.ConnectionString))
            .AddInterceptors(interceptor)
            .Options;
        return new AppDbContext(options);
    }

    private sealed class FailOnCommandInterceptor : DbCommandInterceptor
    {
        private readonly Func<string, bool> _shouldFail;
        public FailOnCommandInterceptor(Func<string, bool> shouldFail) => _shouldFail = shouldFail;

        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            if (_shouldFail(command.CommandText)) throw new InvalidOperationException("Simulated SQL command failure.");
            return base.NonQueryExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (_shouldFail(command.CommandText)) throw new InvalidOperationException("Simulated SQL command failure.");
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            if (_shouldFail(command.CommandText)) throw new InvalidOperationException("Simulated SQL command failure.");
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (_shouldFail(command.CommandText)) throw new InvalidOperationException("Simulated SQL command failure.");
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    // ═══════════ Authorization ═══════════

    [MySqlFact]
    public async Task Only_a_super_admin_can_bulk_transfer_every_other_caller_is_refused()
    {
        UserPis p1 = await SeedPersonAsync(_admins.PisAId);

        Assert.Equal(HttpStatusCode.Forbidden, (await BulkTransferAsync(_admins.PisA, _admins.PisAId, _admins.PisBId)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await BulkTransferAsync(_admins.PisB, _admins.PisAId, _admins.PisBId)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await BulkTransferAsync(_admins.Registration, _admins.PisAId, _admins.PisBId)).Status);
        foreach (string role in new[] { DomainConstants.Roles.SuperAdmin, "SUPER_ADMIN", DomainConstants.Roles.PisAdmin })
            Assert.Equal(HttpStatusCode.Unauthorized, (await BulkTransferAsync(_factory.CreateClientFor(role), _admins.PisAId, _admins.PisBId)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await BulkTransferAsync(_factory.CreateClientFor(null), _admins.PisAId, _admins.PisBId)).Status);

        Assert.Equal(_admins.PisAId, (await PersonAsync(p1.NsgNumber)).AssignedTo);
        Assert.Empty(await HistoryAsync(p1.NsgNumber));

        Assert.Equal(HttpStatusCode.OK, (await BulkTransferAsync(_admins.Super, _admins.PisAId, _admins.PisBId)).Status);
        Assert.Equal(_admins.PisBId, (await PersonAsync(p1.NsgNumber)).AssignedTo);
    }

    // ═══════════ Validation & Inactive Source ═══════════

    [MySqlFact]
    public async Task Validation_failures_and_unsuitable_targets_are_refused_and_nothing_changes()
    {
        UserPis p1 = await SeedPersonAsync(_admins.PisAId);
        long superId = await AdminIdAsync(SuperNsg);
        long regId = await AdminIdAsync(RegistrationNsg);

        Assert.Equal(HttpStatusCode.BadRequest, (await BulkTransferAsync(_admins.Super, 0, _admins.PisBId)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await BulkTransferAsync(_admins.Super, _admins.PisAId, 0)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await BulkTransferAsync(_admins.Super, _admins.PisAId, _admins.PisAId)).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await BulkTransferAsync(_admins.Super, 999999, _admins.PisBId)).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await BulkTransferAsync(_admins.Super, _admins.PisAId, 999999)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await BulkTransferAsync(_admins.Super, superId, _admins.PisBId)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await BulkTransferAsync(_admins.Super, _admins.PisAId, regId)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await BulkTransferAsync(_admins.Super, _admins.PisAId, superId)).Status);

        // Deactivate target B -> 409 Conflict
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(_admins.Super, HttpMethod.Post, $"/api/v1/admin/accounts/{_admins.PisBId}/deactivate")).Status);
        Assert.Equal(HttpStatusCode.Conflict, (await BulkTransferAsync(_admins.Super, _admins.PisAId, _admins.PisBId)).Status);

        // Pending activation target -> 409 Conflict
        UserPis pending = await SeedPersonAsync(owner: null);
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(_admins.Super, HttpMethod.Post, "/api/v1/admin/accounts",
            new { nsgNumber = pending.NsgNumber, mobileNumber = pending.MobileNumber, dateOfBirth = "1990-05-15", roleCode = AdminRoleCodes.PisAdmin })).Status);
        long pendingId = await AdminIdAsync(pending.NsgNumber);
        Assert.Equal(HttpStatusCode.Conflict, (await BulkTransferAsync(_admins.Super, _admins.PisAId, pendingId)).Status);

        Assert.Equal(_admins.PisAId, (await PersonAsync(p1.NsgNumber)).AssignedTo);
        Assert.Empty(await HistoryAsync(p1.NsgNumber));
    }

    [MySqlFact]
    public async Task Inactive_source_admin_bulk_transfer_succeeds()
    {
        UserPis p1 = await SeedPersonAsync(_admins.PisAId);
        UserPis p2 = await SeedPersonAsync(_admins.PisAId);

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(_admins.Super, HttpMethod.Post, $"/api/v1/admin/accounts/{_admins.PisAId}/deactivate")).Status);

        (HttpStatusCode status, JsonElement body) = await BulkTransferAsync(_admins.Super, _admins.PisAId, _admins.PisBId, "Retired admin");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(2, body.GetProperty("data").GetProperty("transferredCount").GetInt32());
        Assert.Equal(_admins.PisBId, (await PersonAsync(p1.NsgNumber)).AssignedTo);
        Assert.Equal(_admins.PisBId, (await PersonAsync(p2.NsgNumber)).AssignedTo);
        Assert.Single(await HistoryAsync(p1.NsgNumber));
        Assert.Single(await HistoryAsync(p2.NsgNumber));
    }

    // ═══════════ Bulk Transfer Behaviour & History ═══════════

    [MySqlFact]
    public async Task Bulk_transfer_moves_all_source_records_writes_per_record_history_and_leaves_provenance_and_other_records_untouched()
    {
        UserPis a1 = await SeedPersonAsync(_admins.PisAId, createdBy: 5, updatedBy: 6);
        UserPis a2 = await SeedPersonAsync(_admins.PisAId, createdBy: 7, updatedBy: 8);
        UserPis a3 = await SeedPersonAsync(_admins.PisAId, createdBy: 9, updatedBy: null);
        UserPis b1 = await SeedPersonAsync(_admins.PisBId);
        UserPis unassigned = await SeedPersonAsync(owner: null);

        UserPis a1Before = await PersonAsync(a1.NsgNumber);
        long requestId = await SeedRequestAsync(a1);
        long superId = await AdminIdAsync(SuperNsg);

        (HttpStatusCode status, JsonElement body) = await BulkTransferAsync(_admins.Super, _admins.PisAId, _admins.PisBId, "Annual rotation");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(body.GetProperty("success").GetBoolean());
        Assert.Equal(PisBulkOwnershipTransferService.BulkTransferredMessage, body.GetProperty("message").GetString());
        Assert.Equal(3, body.GetProperty("data").GetProperty("transferredCount").GetInt32());
        Assert.EndsWith("+05:30", body.GetProperty("data").GetProperty("transferredAt").GetString());

        foreach (UserPis moved in new[] { a1, a2, a3 })
        {
            UserPis after = await PersonAsync(moved.NsgNumber);
            Assert.Equal(_admins.PisBId, after.AssignedTo);

            PisOwnershipTransfer history = Assert.Single(await HistoryAsync(moved.NsgNumber));
            Assert.Equal((moved.Id, _admins.PisAId, _admins.PisBId, superId, "Annual rotation"),
                (history.PisRecordId, history.PreviousAssignedTo, history.NewAssignedTo, history.TransferredBy, history.Reason));
            Assert.NotEqual(default, history.TransferredAtIst);
        }

        UserPis a1After = await PersonAsync(a1.NsgNumber);
        Assert.Equal((a1Before.CreatedBy, a1Before.UpdatedBy, a1Before.CreatedAtIst, a1Before.UpdatedAtIst, a1Before.FullName, a1Before.MobileNumber, a1Before.IsActive),
            (a1After.CreatedBy, a1After.UpdatedBy, a1After.CreatedAtIst, a1After.UpdatedAtIst, a1After.FullName, a1After.MobileNumber, a1After.IsActive));

        // Unrelated records unchanged and no history written for them
        Assert.Equal(_admins.PisBId, (await PersonAsync(b1.NsgNumber)).AssignedTo);
        Assert.Null((await PersonAsync(unassigned.NsgNumber)).AssignedTo);
        Assert.Empty(await HistoryAsync(b1.NsgNumber));
        Assert.Empty(await HistoryAsync(unassigned.NsgNumber));

        // Pending registration request follows new owner automatically
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(_admins.PisA, HttpMethod.Put, $"{Approvals}/{requestId}/decision", new { decision = "Approved" })).Status);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(_admins.PisB, HttpMethod.Put, $"{Approvals}/{requestId}/decision", new { decision = "Approved" })).Status);
    }

    [MySqlFact]
    public async Task Bulk_transfer_with_zero_records_returns_success_with_zero_count_and_no_history()
    {
        UserPis other = await SeedPersonAsync(_admins.PisBId);

        (HttpStatusCode status, JsonElement body) = await BulkTransferAsync(_admins.Super, _admins.PisAId, _admins.PisBId);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(body.GetProperty("success").GetBoolean());
        Assert.Equal(PisBulkOwnershipTransferService.BulkNothingToTransferMessage, body.GetProperty("message").GetString());
        Assert.Equal(0, body.GetProperty("data").GetProperty("transferredCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("data").GetProperty("transferredAt").ValueKind);
        Assert.Empty(await HistoryAsync(other.NsgNumber));
    }

    // ═══════════ Transaction Rollback ═══════════

    [MySqlFact]
    public async Task Transaction_rolls_back_all_updates_if_history_insertion_fails()
    {
        UserPis p1 = await SeedPersonAsync(_admins.PisAId);
        UserPis p2 = await SeedPersonAsync(_admins.PisAId);
        UserPis p3 = await SeedPersonAsync(_admins.PisAId);
        long superId = await AdminIdAsync(SuperNsg);

        FailOnCommandInterceptor failHistory = new(sql =>
            sql.Contains("pis_ownership_transfers", StringComparison.OrdinalIgnoreCase)
            && sql.Contains("INSERT", StringComparison.OrdinalIgnoreCase));

        await using AppDbContext ctx = CreateInterceptedContext(failHistory);
        PisOwnershipRepository repo = new(ctx);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repo.BulkTransferAsync(_admins.PisAId, _admins.PisBId, superId, "Should roll back", DateTime.Now));

        foreach (UserPis p in new[] { p1, p2, p3 })
        {
            Assert.Equal(_admins.PisAId, (await PersonAsync(p.NsgNumber)).AssignedTo);
            Assert.Empty(await HistoryAsync(p.NsgNumber));
        }
    }

    [MySqlFact]
    public async Task Transaction_rolls_back_if_ownership_update_fails()
    {
        UserPis p1 = await SeedPersonAsync(_admins.PisAId);
        UserPis p2 = await SeedPersonAsync(_admins.PisAId);
        long superId = await AdminIdAsync(SuperNsg);

        // Fail on the second UPDATE (the actual assignment update; the first UPDATE is the no-op lock).
        int updateCalls = 0;
        FailOnCommandInterceptor failUpdate = new(sql =>
            sql.Contains("users_pis", StringComparison.OrdinalIgnoreCase)
            && sql.Contains("UPDATE", StringComparison.OrdinalIgnoreCase)
            && Interlocked.Increment(ref updateCalls) == 2);

        await using AppDbContext ctx = CreateInterceptedContext(failUpdate);
        PisOwnershipRepository repo = new(ctx);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repo.BulkTransferAsync(_admins.PisAId, _admins.PisBId, superId, "Update fails", DateTime.Now));

        foreach (UserPis p in new[] { p1, p2 })
        {
            Assert.Equal(_admins.PisAId, (await PersonAsync(p.NsgNumber)).AssignedTo);
            Assert.Empty(await HistoryAsync(p.NsgNumber));
        }
    }

    // ═══════════ Concurrency ═══════════

    [MySqlFact]
    public async Task Concurrent_bulk_transfers_on_same_source_never_duplicate_history()
    {
        List<UserPis> people = new();
        for (int i = 0; i < 6; i++) people.Add(await SeedPersonAsync(_admins.PisAId));
        long superId = await AdminIdAsync(SuperNsg);

        await using AppDbContext ctx1 = _factory.CreateContext();
        await using AppDbContext ctx2 = _factory.CreateContext();

        Task<PisBulkOwnershipTransferResult> t1 = Task.Run(() => new PisOwnershipRepository(ctx1)
            .BulkTransferAsync(_admins.PisAId, _admins.PisBId, superId, "Bulk-1", DateTime.Now));
        Task<PisBulkOwnershipTransferResult> t2 = Task.Run(() => new PisOwnershipRepository(ctx2)
            .BulkTransferAsync(_admins.PisAId, _admins.PisBId, superId, "Bulk-2", DateTime.Now));

        await Task.WhenAll(t1, t2);

        Assert.Equal(6, t1.Result.TransferredCount + t2.Result.TransferredCount);
        Assert.Contains(new[] { t1.Result.Outcome, t2.Result.Outcome }, o => o == PisBulkOwnershipTransferOutcome.Transferred);
        Assert.Contains(new[] { t1.Result.Outcome, t2.Result.Outcome }, o => o == PisBulkOwnershipTransferOutcome.NothingToTransfer);

        foreach (UserPis p in people)
        {
            Assert.Equal(_admins.PisBId, (await PersonAsync(p.NsgNumber)).AssignedTo);
            PisOwnershipTransfer h = Assert.Single(await HistoryAsync(p.NsgNumber));
            Assert.Equal((_admins.PisAId, _admins.PisBId), (h.PreviousAssignedTo, h.NewAssignedTo));
        }
    }

    [MySqlFact]
    public async Task Racing_bulk_and_individual_transfers_produce_consistent_owner_and_history_chain()
    {
        long superId = await AdminIdAsync(SuperNsg);
        // Provision a third active PIS_ADMIN (C) so individual transfers can target C while bulk targets B.
        UserPis adminCPerson = await SeedPersonAsync(owner: null);
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(_admins.Super, HttpMethod.Post, "/api/v1/admin/accounts",
            new { nsgNumber = adminCPerson.NsgNumber, mobileNumber = adminCPerson.MobileNumber, dateOfBirth = "1990-05-15", roleCode = AdminRoleCodes.PisAdmin })).Status);
        long pisCId = await AdminIdAsync(adminCPerson.NsgNumber);
        await using (AppDbContext actCtx = _factory.CreateContext())
        {
            await actCtx.AdminProfiles.Where(p => p.Id == pisCId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.ActivationStatus, AdminActivationStatus.Active));
        }

        for (int round = 0; round < 6; round++)
        {
            UserPis p1 = await SeedPersonAsync(_admins.PisAId);
            UserPis p2 = await SeedPersonAsync(_admins.PisAId);

            await using AppDbContext bulkCtx = _factory.CreateContext();
            await using AppDbContext singleCtx = _factory.CreateContext();

            Task<PisBulkOwnershipTransferResult> bulk = Task.Run(() => new PisOwnershipRepository(bulkCtx)
                .BulkTransferAsync(_admins.PisAId, _admins.PisBId, superId, "Bulk", DateTime.Now));
            Task<PisOwnershipTransferResult> single = Task.Run(() => new PisOwnershipRepository(singleCtx)
                .TransferAsync(p1.NsgNumber, pisCId, superId, "Single", DateTime.Now));

            await Task.WhenAll(bulk, single);

            Assert.Equal(PisOwnershipTransferOutcome.Transferred, single.Result.Outcome);
            Assert.Equal(_admins.PisBId, (await PersonAsync(p2.NsgNumber)).AssignedTo);
            PisOwnershipTransfer h2 = Assert.Single(await HistoryAsync(p2.NsgNumber));
            Assert.Equal((_admins.PisAId, _admins.PisBId), (h2.PreviousAssignedTo, h2.NewAssignedTo));

            // p1's final owner must match the last history row, and history chain must be continuous from PisAId.
            long? finalOwner1 = (await PersonAsync(p1.NsgNumber)).AssignedTo;
            List<PisOwnershipTransfer> h1 = await HistoryAsync(p1.NsgNumber);
            Assert.NotEmpty(h1);
            Assert.Equal(_admins.PisAId, h1[0].PreviousAssignedTo);
            Assert.Equal(finalOwner1, h1[^1].NewAssignedTo);
            for (int i = 1; i < h1.Count; i++)
                Assert.Equal(h1[i - 1].NewAssignedTo, h1[i].PreviousAssignedTo);
        }
    }

    // ═══════════ Compatibility with Individual Transfer ═══════════

    [MySqlFact]
    public async Task Individual_transfer_continues_to_work_alongside_bulk_transfer()
    {
        UserPis solo = await SeedPersonAsync(_admins.PisAId);

        (HttpStatusCode status, JsonElement body) = await SendAsync(_admins.Super, HttpMethod.Post, SingleTransfer,
            new { personnelNsgNumber = solo.NsgNumber, targetPisAdminNsgNumber = PisBNsg, reason = "Solo" });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(body.GetProperty("data").GetProperty("transferred").GetBoolean());
        Assert.Equal(_admins.PisBId, (await PersonAsync(solo.NsgNumber)).AssignedTo);
        Assert.Single(await HistoryAsync(solo.NsgNumber));
    }
}
