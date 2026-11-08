using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NSG.Mobile.Domain.Constants;

namespace NSG.Mobile.Tests;

[Collection("mysql")]
public class GeographyApiTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _admin;
    private readonly HttpClient _employee;
    private readonly HttpClient _anonymous;

    public GeographyApiTests(DatabaseFixture fixture)
    {
        _factory = fixture.Factory;
        _admin = _factory.CreateClientFor(DomainConstants.Roles.PisAdmin);
        _employee = _factory.CreateClientFor(DomainConstants.Roles.Employee);
        _anonymous = _factory.CreateClientFor(null);
    }

    public Task InitializeAsync() => _factory.ClearGeographyAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static object NewState(string name, string? code, string type = "State") => new { name, code, administrativeType = type, isActive = true };

    private async Task<JsonElement> CreateStateAsync(string name, string? code, string type = "State")
    {
        HttpResponseMessage r = await _admin.PostAsJsonAsync("/api/v1/states", NewState(name, code, type));
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return await r.ReadDataAsync();
    }

    private async Task<JsonElement> GetDataAsync(string url) =>
        await (await _admin.GetAsync(url)).ReadDataAsync();

    // ── Authorization ──

    [Fact]
    public async Task Anonymous_requests_are_rejected_with_401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _anonymous.GetAsync("/api/v1/states")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _anonymous.PostAsJsonAsync("/api/v1/states", NewState("X", "99"))).StatusCode);
    }

    [Fact]
    public async Task Non_admin_writes_are_forbidden_but_reads_are_allowed()
    {
        Assert.Equal(HttpStatusCode.OK, (await _employee.GetAsync("/api/v1/states")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _employee.PostAsJsonAsync("/api/v1/states", NewState("X", "99"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _employee.PutAsJsonAsync("/api/v1/states/1", NewState("X", "99"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _employee.DeleteAsync("/api/v1/states/1")).StatusCode);
    }

    // ── States ──

    [Fact]
    public async Task Create_state_returns_201_with_location_and_UT_type()
    {
        HttpResponseMessage r = await _admin.PostAsJsonAsync("/api/v1/states", NewState("Delhi", "07", "UnionTerritory"));

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.NotNull(r.Headers.Location);
        JsonElement body = await r.ReadDataAsync();
        Assert.Equal("UnionTerritory", body.GetProperty("administrativeType").GetString());
        Assert.Equal("07", body.GetProperty("code").GetString());
        Assert.EndsWith($"/api/v1/states/{body.GetProperty("id").GetInt32()}", r.Headers.Location!.AbsolutePath);
    }

    [Fact]
    public async Task Create_state_rejects_duplicate_code_and_name_with_409()
    {
        await CreateStateAsync("Goa", "30");

        HttpResponseMessage dupCode = await _admin.PostAsJsonAsync("/api/v1/states", NewState("Other", "30"));
        HttpResponseMessage dupName = await _admin.PostAsJsonAsync("/api/v1/states", NewState("goa", "31"));

        Assert.Equal(HttpStatusCode.Conflict, dupCode.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, dupName.StatusCode);
        Assert.Equal(409, (int)dupCode.StatusCode);
    }

    [Theory]
    [InlineData("", "01", "State")]
    [InlineData("Valid", "bad code!", "State")]
    [InlineData("Valid", "01", "Province")]
    public async Task Create_state_validation_failures_return_400(string name, string code, string type)
    {
        HttpResponseMessage r = await _admin.PostAsJsonAsync("/api/v1/states", NewState(name, code, type));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Get_state_by_id_returns_200_or_404()
    {
        JsonElement s = await CreateStateAsync("Kerala", "32");
        int id = s.GetProperty("id").GetInt32();

        Assert.Equal(HttpStatusCode.OK, (await _admin.GetAsync($"/api/v1/states/{id}")).StatusCode);
        HttpResponseMessage missing = await _admin.GetAsync("/api/v1/states/999999");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(404, (int)missing.StatusCode);
    }

    [Fact]
    public async Task Update_state_succeeds_returns_404_and_detects_duplicates()
    {
        JsonElement a = await CreateStateAsync("Alpha", "11");
        await CreateStateAsync("Beta", "12");
        int id = a.GetProperty("id").GetInt32();

        HttpResponseMessage ok = await _admin.PutAsJsonAsync($"/api/v1/states/{id}",
            new { name = "Alpha Renamed", code = "11", administrativeType = "UnionTerritory", isActive = false });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        JsonElement updated = await ok.ReadDataAsync();
        Assert.Equal("Alpha Renamed", updated.GetProperty("name").GetString());
        Assert.False(updated.GetProperty("isActive").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null, updated.GetProperty("updatedAtIst").ValueKind);

        Assert.Equal(HttpStatusCode.NotFound,
            (await _admin.PutAsJsonAsync("/api/v1/states/999999", NewState("Zed", "77"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await _admin.PutAsJsonAsync($"/api/v1/states/{id}", NewState("Alpha Renamed", "12"))).StatusCode);
    }

    [Fact]
    public async Task List_states_supports_search_filter_pagination_and_stable_sorting()
    {
        await CreateStateAsync("Bihar", "10");
        await CreateStateAsync("Assam", "18");
        await CreateStateAsync("Chandigarh", "04", "UnionTerritory");
        await CreateStateAsync("Delhi", "07", "UnionTerritory");

        JsonElement all = await GetDataAsync("/api/v1/states?pageSize=2&pageNumber=1");
        Assert.Equal(4, all.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, all.GetProperty("totalPages").GetInt32());
        Assert.True(all.GetProperty("hasNextPage").GetBoolean());
        Assert.Equal(new[] { "Assam", "Bihar" }, all.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString()).ToArray());

        JsonElement page2 = await GetDataAsync("/api/v1/states?pageSize=2&pageNumber=2");
        Assert.Equal(new[] { "Chandigarh", "Delhi" }, page2.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString()).ToArray());

        JsonElement uts = await GetDataAsync("/api/v1/states?administrativeType=UnionTerritory");
        Assert.Equal(2, uts.GetProperty("totalCount").GetInt32());

        JsonElement search = await GetDataAsync("/api/v1/states?search=ihar");
        Assert.Equal("Bihar", search.GetProperty("items")[0].GetProperty("name").GetString());

        JsonElement desc = await GetDataAsync("/api/v1/states?sortBy=name&sortDirection=desc&pageSize=1");
        Assert.Equal("Delhi", desc.GetProperty("items")[0].GetProperty("name").GetString());

        // Unknown sort field must not error or reach SQL — it falls back to the default order.
        HttpResponseMessage badSort = await _admin.GetAsync("/api/v1/states?sortBy=Id;DROP TABLE States");
        Assert.Equal(HttpStatusCode.OK, badSort.StatusCode);

        // Page size is capped at the shared maximum of 100.
        JsonElement capped = await GetDataAsync("/api/v1/states?pageSize=5000");
        Assert.Equal(100, capped.GetProperty("pageSize").GetInt32());
    }

    // ── State database protection (CHECK constraints, leading zeros, unique indexes) ──

    [Fact]
    public async Task Database_check_constraints_reject_bad_state_type_and_bad_code_and_keep_leading_zeros()
    {
        await using var ctx = _factory.CreateContext();

        // ck_states_administrative_type: only 1 (State) and 2 (UnionTerritory) are valid.
        await Assert.ThrowsAnyAsync<Exception>(() => ctx.Database.ExecuteSqlRawAsync(
            "INSERT INTO `states` (`name`,`code`,`administrative_type`,`is_active`,`created_at_ist`) VALUES ('Bad Type','50',3,1,NOW(6))"));
        // ck_states_code_format: the code is NULL or exactly two digits.
        await Assert.ThrowsAnyAsync<Exception>(() => ctx.Database.ExecuteSqlRawAsync(
            "INSERT INTO `states` (`name`,`code`,`administrative_type`,`is_active`,`created_at_ist`) VALUES ('Bad Code','1',1,1,NOW(6))"));
        Assert.Equal(0, await ctx.States.CountAsync());

        // A valid row is accepted and the leading zero survives the round trip.
        await ctx.Database.ExecuteSqlRawAsync(
            "INSERT INTO `states` (`name`,`code`,`administrative_type`,`is_active`,`created_at_ist`) VALUES ('Good','07',2,1,NOW(6))");
        Assert.Equal("07", (await ctx.States.AsNoTracking().SingleAsync()).Code);
    }

    [Fact]
    public async Task State_unique_indexes_exist_in_the_mysql_schema()
    {
        await using var ctx = _factory.CreateContext();
        await ctx.Database.OpenConnectionAsync();
        await using var cmd = ctx.Database.GetDbConnection().CreateCommand();
        cmd.CommandText =
            "SELECT INDEX_NAME FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND NON_UNIQUE = 0 AND TABLE_NAME = 'states'";
        var names = new List<string>();
        await using (var reader = await cmd.ExecuteReaderAsync())
            while (await reader.ReadAsync()) names.Add(reader.GetString(0));

        Assert.Contains("ix_states_code", names);
        Assert.Contains("ix_states_name", names);
    }

    // ── State delete and removed City routes ──

    [Fact]
    public async Task Deleting_a_state_returns_200_then_404()
    {
        int stateId = (await CreateStateAsync("Goa", "30")).GetProperty("id").GetInt32();

        Assert.Equal(HttpStatusCode.OK, (await _admin.DeleteAsync($"/api/v1/states/{stateId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.DeleteAsync($"/api/v1/states/{stateId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.GetAsync($"/api/v1/states/{stateId}")).StatusCode);
    }

    [Fact]
    public async Task City_routes_no_longer_exist()
    {
        int stateId = (await CreateStateAsync("Goa", "30")).GetProperty("id").GetInt32();

        Assert.Equal(HttpStatusCode.NotFound, (await _admin.GetAsync("/api/v1/cities")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.GetAsync("/api/v1/cities/1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.GetAsync($"/api/v1/states/{stateId}/cities")).StatusCode);
    }
}
