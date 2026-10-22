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

/// <summary>
/// Integration tests for the NSG unit, parent department rank and religion lookup APIs. They share one set of behaviours,
/// so every test runs once per resource route.
/// </summary>
[Collection("mysql")]
public class PisLookupApiTests : IAsyncLifetime
{
    private const string NsgUnits = "nsg-units";
    private const string ParentDepartmentRanks = "parent-department-ranks";
    private const string Religions = "religions";

    private readonly ApiFactory _factory;
    private readonly HttpClient _admin;
    private readonly HttpClient _employee;
    private readonly HttpClient _anonymous;

    public PisLookupApiTests(DatabaseFixture fixture)
    {
        _factory = fixture.Factory;
        _admin = _factory.CreateClientFor(DomainConstants.Roles.PisAdmin);
        _employee = _factory.CreateClientFor(DomainConstants.Roles.Employee);
        _anonymous = _factory.CreateClientFor(null);
    }

    public static TheoryData<string> Resources => new() { NsgUnits, ParentDepartmentRanks, Religions };

    public async Task InitializeAsync()
    {
        await using AppDbContext context = _factory.CreateContext();
        await context.Database.ExecuteSqlRawAsync("DELETE FROM `users_pis`");
        await context.Database.ExecuteSqlRawAsync("DELETE FROM `nsg_units`");
        await context.Database.ExecuteSqlRawAsync("DELETE FROM `parent_department_ranks`");
        await context.Database.ExecuteSqlRawAsync("DELETE FROM `religions`");
        await context.Database.ExecuteSqlRawAsync("DELETE FROM `audit_logs`");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static string EntityType(string resource) => resource switch
    {
        NsgUnits => "NsgUnit",
        ParentDepartmentRanks => "ParentDepartmentRank",
        _ => "Religion"
    };

    private async Task<JsonElement> CreateAsync(string resource, string name, bool isActive = true)
    {
        HttpResponseMessage r = await _admin.PostAsJsonAsync($"/api/v1/{resource}", new { name, isActive });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return await r.ReadDataAsync();
    }

    private async Task<int> CountAuditAsync(string resource, int id, string operation)
    {
        await using AppDbContext context = _factory.CreateContext();
        List<AuditLog> logs = await context.AuditLogs
            .Where(a => a.EntityType == EntityType(resource) && a.EntityId == id.ToString() && a.Action == AuditAction.AdminAction)
            .ToListAsync();
        return logs.Count(a => a.AdditionalData != null && a.AdditionalData.Contains(operation));
    }

    private async Task AddPersonnelReferencingAsync(string resource, int id, bool viaSideStep = false)
    {
        await using AppDbContext context = _factory.CreateContext();
        UserPis person = new() { NsgNumber = "N-" + Guid.NewGuid().ToString("N")[..8], ServiceNumber = "S-1", CreatedAtIst = IndianStandardTime.Now };
        if (resource == NsgUnits)
        {
            if (viaSideStep)
                person.SideSteppedFromNsgUnitId = id;
            else
                person.NsgUnitId = id;
        }
        else if (resource == Religions)
        {
            person.ReligionId = id;
        }
        else
        {
            person.ParentDepartmentRankId = id;
        }
        context.UsersPis.Add(person);
        await context.SaveChangesAsync();
    }

    // ── Authorization ──

    [Theory, MemberData(nameof(Resources))]
    public async Task Anonymous_requests_are_rejected_with_401(string resource)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _anonymous.GetAsync($"/api/v1/{resource}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _anonymous.GetAsync($"/api/v1/{resource}/dropdown")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _anonymous.GetAsync($"/api/v1/{resource}/1")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _anonymous.PostAsJsonAsync($"/api/v1/{resource}", new { name = "X" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _anonymous.PutAsJsonAsync($"/api/v1/{resource}/1", new { name = "X" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _anonymous.DeleteAsync($"/api/v1/{resource}/1")).StatusCode);
    }

    [Theory, MemberData(nameof(Resources))]
    public async Task Non_admin_can_read_but_writes_are_forbidden(string resource)
    {
        JsonElement created = await CreateAsync(resource, "Readable");
        int id = created.GetProperty("id").GetInt32();

        Assert.Equal(HttpStatusCode.OK, (await _employee.GetAsync($"/api/v1/{resource}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _employee.GetAsync($"/api/v1/{resource}/dropdown")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _employee.GetAsync($"/api/v1/{resource}/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _employee.PostAsJsonAsync($"/api/v1/{resource}", new { name = "X" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _employee.PutAsJsonAsync($"/api/v1/{resource}/{id}", new { name = "X", isActive = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _employee.DeleteAsync($"/api/v1/{resource}/{id}")).StatusCode);

        // The forbidden write attempts changed nothing.
        JsonElement after = await (await _admin.GetAsync($"/api/v1/{resource}/{id}")).ReadDataAsync();
        Assert.Equal("Readable", after.GetProperty("name").GetString());
    }

    // ── Create ──

    [Theory, MemberData(nameof(Resources))]
    public async Task Create_returns_201_with_location_trims_name_and_audits(string resource)
    {
        HttpResponseMessage r = await _admin.PostAsJsonAsync($"/api/v1/{resource}", new { name = "  13 SRG  ", isActive = true });

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        JsonElement body = await r.ReadDataAsync();
        int id = body.GetProperty("id").GetInt32();
        Assert.Equal("13 SRG", body.GetProperty("name").GetString());
        Assert.True(body.GetProperty("isActive").GetBoolean());
        Assert.EndsWith($"/api/v1/{resource}/{id}", r.Headers.Location!.AbsolutePath);
        Assert.Equal(1, await CountAuditAsync(resource, id, $"{EntityType(resource)}Created"));
    }

    [Theory, MemberData(nameof(Resources))]
    public async Task Create_rejects_blank_and_overlong_names_with_400_problem_details(string resource)
    {
        foreach (string name in new[] { "", "   ", new string('x', 101) })
        {
            HttpResponseMessage r = await _admin.PostAsJsonAsync($"/api/v1/{resource}", new { name });
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            Assert.Equal(400, (int)r.StatusCode);
        }

        JsonElement list = await (await _admin.GetAsync($"/api/v1/{resource}")).ReadDataAsync();
        Assert.Equal(0, list.GetProperty("totalCount").GetInt32());
    }

    [Theory, MemberData(nameof(Resources))]
    public async Task Create_rejects_duplicate_name_with_409_ignoring_case_and_whitespace(string resource)
    {
        await CreateAsync(resource, "Alpha");

        foreach (string duplicate in new[] { "Alpha", "  Alpha ", "ALPHA" })
        {
            HttpResponseMessage r = await _admin.PostAsJsonAsync($"/api/v1/{resource}", new { name = duplicate });
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            JsonElement problem = await r.ReadDataAsync();
            Assert.False(problem.GetProperty("success").GetBoolean());
        }
    }

    // ── Read ──

    [Theory, MemberData(nameof(Resources))]
    public async Task Get_by_id_returns_record_or_404(string resource)
    {
        JsonElement created = await CreateAsync(resource, "Findable");
        int id = created.GetProperty("id").GetInt32();

        HttpResponseMessage found = await _admin.GetAsync($"/api/v1/{resource}/{id}");
        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
        Assert.Equal("Findable", (await found.ReadDataAsync()).GetProperty("name").GetString());

        HttpResponseMessage missing = await _admin.GetAsync($"/api/v1/{resource}/{id + 999}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(404, (int)missing.StatusCode);
    }

    [Theory, MemberData(nameof(Resources))]
    public async Task Dropdown_returns_only_active_records_ordered_by_name(string resource)
    {
        await CreateAsync(resource, "Charlie");
        await CreateAsync(resource, "Alpha");
        await CreateAsync(resource, "Bravo (inactive)", isActive: false);

        JsonElement items = await (await _employee.GetAsync($"/api/v1/{resource}/dropdown")).ReadDataAsync();

        Assert.Equal(new[] { "Alpha", "Charlie" }, items.EnumerateArray().Select(i => i.GetProperty("name").GetString()).ToArray());
        Assert.False(items[0].TryGetProperty("isActive", out _)); // dropdown items are minimal
    }

    [Theory, MemberData(nameof(Resources))]
    public async Task List_supports_pagination_search_and_active_filter(string resource)
    {
        foreach (string name in new[] { "Delta 1", "Delta 2", "Delta 3", "Echo" })
            await CreateAsync(resource, name);
        await CreateAsync(resource, "Delta Retired", isActive: false);

        JsonElement page1 = await (await _admin.GetAsync($"/api/v1/{resource}?pageNumber=1&pageSize=2")).ReadDataAsync();
        Assert.Equal(5, page1.GetProperty("totalCount").GetInt32());
        Assert.Equal(3, page1.GetProperty("totalPages").GetInt32());
        Assert.Equal(2, page1.GetProperty("items").GetArrayLength());
        Assert.True(page1.GetProperty("hasNextPage").GetBoolean());

        JsonElement page3 = await (await _admin.GetAsync($"/api/v1/{resource}?pageNumber=3&pageSize=2")).ReadDataAsync();
        Assert.Equal(1, page3.GetProperty("items").GetArrayLength());

        JsonElement search = await (await _admin.GetAsync($"/api/v1/{resource}?search=delta")).ReadDataAsync();
        Assert.Equal(4, search.GetProperty("totalCount").GetInt32());

        JsonElement activeOnly = await (await _admin.GetAsync($"/api/v1/{resource}?search=delta&isActive=true")).ReadDataAsync();
        Assert.Equal(3, activeOnly.GetProperty("totalCount").GetInt32());

        JsonElement inactiveOnly = await (await _admin.GetAsync($"/api/v1/{resource}?isActive=false")).ReadDataAsync();
        Assert.Equal("Delta Retired", inactiveOnly.GetProperty("items")[0].GetProperty("name").GetString());
    }

    // ── Update ──

    [Theory, MemberData(nameof(Resources))]
    public async Task Update_changes_name_and_active_flag_sets_timestamp_and_audits(string resource)
    {
        JsonElement created = await CreateAsync(resource, "Before");
        int id = created.GetProperty("id").GetInt32();

        HttpResponseMessage r = await _admin.PutAsJsonAsync($"/api/v1/{resource}/{id}", new { name = " After ", isActive = false });

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        JsonElement body = await r.ReadDataAsync();
        Assert.Equal("After", body.GetProperty("name").GetString());
        Assert.False(body.GetProperty("isActive").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null, body.GetProperty("updatedAtIst").ValueKind);
        // Created response carries .NET ticks; MySQL datetime(6) keeps microseconds, so compare to the second.
        Assert.True(Math.Abs((created.GetProperty("createdAtIst").GetDateTime() - body.GetProperty("createdAtIst").GetDateTime()).TotalSeconds) < 1);
        Assert.Equal(1, await CountAuditAsync(resource, id, $"{EntityType(resource)}Updated"));
    }

    [Theory, MemberData(nameof(Resources))]
    public async Task Update_allows_keeping_own_name_but_rejects_another_records_name(string resource)
    {
        JsonElement one = await CreateAsync(resource, "One");
        JsonElement two = await CreateAsync(resource, "Two");
        int oneId = one.GetProperty("id").GetInt32();
        int twoId = two.GetProperty("id").GetInt32();

        Assert.Equal(HttpStatusCode.OK, (await _admin.PutAsJsonAsync($"/api/v1/{resource}/{oneId}", new { name = "One", isActive = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _admin.PutAsJsonAsync($"/api/v1/{resource}/{twoId}", new { name = "one", isActive = true })).StatusCode);
    }

    [Theory, MemberData(nameof(Resources))]
    public async Task Update_unknown_id_returns_404_and_invalid_body_returns_400(string resource)
    {
        JsonElement created = await CreateAsync(resource, "Existing");
        int id = created.GetProperty("id").GetInt32();

        Assert.Equal(HttpStatusCode.NotFound, (await _admin.PutAsJsonAsync($"/api/v1/{resource}/{id + 999}", new { name = "X", isActive = true })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.PutAsJsonAsync($"/api/v1/{resource}/{id}", new { name = "", isActive = true })).StatusCode);
    }

    // ── Delete ──

    [Theory, MemberData(nameof(Resources))]
    public async Task Delete_unreferenced_record_returns_204_removes_it_and_audits(string resource)
    {
        JsonElement created = await CreateAsync(resource, "Disposable");
        int id = created.GetProperty("id").GetInt32();

        Assert.Equal(HttpStatusCode.OK, (await _admin.DeleteAsync($"/api/v1/{resource}/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.GetAsync($"/api/v1/{resource}/{id}")).StatusCode);
        Assert.Equal(1, await CountAuditAsync(resource, id, $"{EntityType(resource)}Deleted"));
    }

    [Theory, MemberData(nameof(Resources))]
    public async Task Delete_unknown_id_returns_404(string resource)
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.DeleteAsync($"/api/v1/{resource}/424242")).StatusCode);
    }

    [Theory, MemberData(nameof(Resources))]
    public async Task Delete_is_rejected_with_409_when_referenced_by_personnel(string resource)
    {
        JsonElement created = await CreateAsync(resource, "In use");
        int id = created.GetProperty("id").GetInt32();
        await AddPersonnelReferencingAsync(resource, id);

        HttpResponseMessage r = await _admin.DeleteAsync($"/api/v1/{resource}/{id}");

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal(409, (int)r.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _admin.GetAsync($"/api/v1/{resource}/{id}")).StatusCode);
        Assert.Equal(0, await CountAuditAsync(resource, id, $"{EntityType(resource)}Deleted"));

        await using AppDbContext context = _factory.CreateContext();
        Assert.Equal(1, await context.UsersPis.CountAsync()); // personnel record untouched

        // Deactivating is the supported alternative and still works while referenced.
        Assert.Equal(HttpStatusCode.OK, (await _admin.PutAsJsonAsync($"/api/v1/{resource}/{id}", new { name = "In use", isActive = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _admin.DeleteAsync($"/api/v1/{resource}/{id}")).StatusCode);
    }

    [Fact]
    public async Task Nsg_unit_delete_is_rejected_when_referenced_as_side_stepped_from_unit()
    {
        JsonElement created = await CreateAsync(NsgUnits, "Former unit");
        int id = created.GetProperty("id").GetInt32();
        await AddPersonnelReferencingAsync(NsgUnits, id, viaSideStep: true);

        Assert.Equal(HttpStatusCode.Conflict, (await _admin.DeleteAsync($"/api/v1/{NsgUnits}/{id}")).StatusCode);
    }

    [Fact]
    public async Task Lookups_are_independent_of_each_other()
    {
        JsonElement unit = await CreateAsync(NsgUnits, "Shared name");
        JsonElement dept = await CreateAsync(ParentDepartmentRanks, "Shared name"); // same name allowed across different lookups

        await AddPersonnelReferencingAsync(NsgUnits, unit.GetProperty("id").GetInt32());

        Assert.Equal(HttpStatusCode.OK, (await _admin.DeleteAsync($"/api/v1/{ParentDepartmentRanks}/{dept.GetProperty("id").GetInt32()}")).StatusCode);
    }
}
