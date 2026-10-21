using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NSG.Mobile.Domain.Common;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;
using NSG.Mobile.Infrastructure.Persistence;

namespace NSG.Mobile.Tests;

/// <summary>Rank "delete" is a deactivation: never a hard delete, and refused while personnel hold the rank.</summary>
[Collection("mysql")]
public class RankDeleteApiTests : IAsyncLifetime
{
    private const int FirstTestSrNo = 9100;

    private readonly ApiFactory _factory;
    private readonly HttpClient _admin;
    private readonly HttpClient _employee;

    public RankDeleteApiTests(DatabaseFixture fixture)
    {
        _factory = fixture.Factory;
        _admin = _factory.CreateClientFor(DomainConstants.Roles.PisAdmin);
        _employee = _factory.CreateClientFor(DomainConstants.Roles.Employee);
    }

    public async Task InitializeAsync() => await CleanAsync();

    public async Task DisposeAsync() => await CleanAsync();

    private async Task CleanAsync()
    {
        await using AppDbContext ctx = _factory.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `users_pis`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `audit_logs`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `rank_hierarchy` WHERE `sr_no` >= " + FirstTestSrNo);
    }

    private async Task<int> CreateRankAsync(int srNo)
    {
        HttpResponseMessage r = await _admin.PostAsJsonAsync("/api/v1/ranks", new { srNo, rankName = "Test rank " + srNo, shortCode = "T" + srNo, isActive = true });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
    }

    private async Task AssignRankAsync(int rankId)
    {
        await using AppDbContext ctx = _factory.CreateContext();
        ctx.UsersPis.Add(new UserPis { NsgNumber = "RD-" + rankId, ServiceNumber = "S", CreatedAtIst = IndianStandardTime.Now, NsgRankId = rankId });
        await ctx.SaveChangesAsync();
    }

    private async Task<bool> IsActiveAsync(int id)
        => (await (await _admin.GetAsync($"/api/v1/ranks/{id}")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("isActive").GetBoolean();

    [Fact]
    public async Task Delete_of_an_unassigned_rank_deactivates_it_and_keeps_the_row()
    {
        int id = await CreateRankAsync(FirstTestSrNo);

        Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteAsync($"/api/v1/ranks/{id}")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await _admin.GetAsync($"/api/v1/ranks/{id}")).StatusCode); // still there
        Assert.False(await IsActiveAsync(id));
        JsonElement dropdown = await (await _employee.GetAsync("/api/v1/ranks/dropdown")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.DoesNotContain(dropdown.EnumerateArray(), r => r.GetProperty("id").GetInt32() == id);

        await using AppDbContext ctx = _factory.CreateContext();
        Assert.Equal(1, await ctx.RankHierarchies.CountAsync(r => r.Id == id)); // no hard delete
        Assert.Equal(1, await ctx.AuditLogs.CountAsync(a => a.EntityId == id.ToString() && a.AdditionalData!.Contains("RankDeactivated")));
        Assert.Equal(0, await ctx.AuditLogs.CountAsync(a => a.AdditionalData!.Contains("RankDeleted")));
    }

    [Fact]
    public async Task Delete_is_idempotent_for_an_already_inactive_rank()
    {
        int id = await CreateRankAsync(FirstTestSrNo + 1);

        Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteAsync($"/api/v1/ranks/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteAsync($"/api/v1/ranks/{id}")).StatusCode);

        await using AppDbContext ctx = _factory.CreateContext();
        Assert.Equal(1, await ctx.AuditLogs.CountAsync(a => a.EntityId == id.ToString() && a.AdditionalData!.Contains("RankDeactivated")));
    }

    [Fact]
    public async Task Delete_of_a_rank_assigned_to_personnel_returns_409_and_changes_nothing()
    {
        int id = await CreateRankAsync(FirstTestSrNo + 2);
        await AssignRankAsync(id);

        HttpResponseMessage r = await _admin.DeleteAsync($"/api/v1/ranks/{id}");

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal(409, (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetInt32());
        Assert.True(await IsActiveAsync(id));

        await using AppDbContext ctx = _factory.CreateContext();
        Assert.Equal(1, await ctx.UsersPis.CountAsync(p => p.NsgRankId == id)); // personnel record untouched
        Assert.Equal(0, await ctx.AuditLogs.CountAsync(a => a.EntityId == id.ToString() && a.AdditionalData!.Contains("RankDeactivated")));
    }

    [Fact]
    public async Task Delete_becomes_possible_once_no_personnel_hold_the_rank()
    {
        int id = await CreateRankAsync(FirstTestSrNo + 3);
        await AssignRankAsync(id);
        Assert.Equal(HttpStatusCode.Conflict, (await _admin.DeleteAsync($"/api/v1/ranks/{id}")).StatusCode);

        await using (AppDbContext ctx = _factory.CreateContext())
            await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `users_pis`");

        Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteAsync($"/api/v1/ranks/{id}")).StatusCode);
        Assert.False(await IsActiveAsync(id));
    }

    [Fact]
    public async Task Delete_unknown_rank_returns_404_and_non_admins_get_403()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.DeleteAsync("/api/v1/ranks/987654")).StatusCode);

        int id = await CreateRankAsync(FirstTestSrNo + 4);
        Assert.Equal(HttpStatusCode.Forbidden, (await _employee.DeleteAsync($"/api/v1/ranks/{id}")).StatusCode);
        Assert.True(await IsActiveAsync(id));
    }
}
