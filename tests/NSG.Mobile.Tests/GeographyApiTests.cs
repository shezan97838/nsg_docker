using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
        return await r.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<JsonElement> CreateCityAsync(int stateId, string name, string? classification = "CensusTown")
    {
        HttpResponseMessage r = await _admin.PostAsJsonAsync("/api/v1/cities", new { name, stateId, classification, isActive = true });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return await r.Content.ReadFromJsonAsync<JsonElement>();
    }

    // ── Authorization ──

    [Fact]
    public async Task Anonymous_requests_are_rejected_with_401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _anonymous.GetAsync("/api/v1/states")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _anonymous.GetAsync("/api/v1/cities")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _anonymous.PostAsJsonAsync("/api/v1/states", NewState("X", "99"))).StatusCode);
    }

    [Fact]
    public async Task Non_admin_writes_are_forbidden_but_reads_are_allowed()
    {
        Assert.Equal(HttpStatusCode.OK, (await _employee.GetAsync("/api/v1/states")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _employee.PostAsJsonAsync("/api/v1/states", NewState("X", "99"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _employee.PutAsJsonAsync("/api/v1/states/1", NewState("X", "99"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _employee.DeleteAsync("/api/v1/states/1")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _employee.PostAsJsonAsync("/api/v1/cities", new { name = "X", stateId = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _employee.DeleteAsync("/api/v1/cities/1")).StatusCode);
    }

    // ── States ──

    [Fact]
    public async Task Create_state_returns_201_with_location_and_UT_type()
    {
        HttpResponseMessage r = await _admin.PostAsJsonAsync("/api/v1/states", NewState("Delhi", "07", "UnionTerritory"));

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.NotNull(r.Headers.Location);
        JsonElement body = await r.Content.ReadFromJsonAsync<JsonElement>();
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
        Assert.Equal(409, (await dupCode.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetInt32());
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
        Assert.Equal(404, (await missing.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetInt32());
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
        JsonElement updated = await ok.Content.ReadFromJsonAsync<JsonElement>();
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

        JsonElement all = await _admin.GetFromJsonAsync<JsonElement>("/api/v1/states?pageSize=2&pageNumber=1");
        Assert.Equal(4, all.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, all.GetProperty("totalPages").GetInt32());
        Assert.True(all.GetProperty("hasNextPage").GetBoolean());
        Assert.Equal(new[] { "Assam", "Bihar" }, all.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString()).ToArray());

        JsonElement page2 = await _admin.GetFromJsonAsync<JsonElement>("/api/v1/states?pageSize=2&pageNumber=2");
        Assert.Equal(new[] { "Chandigarh", "Delhi" }, page2.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString()).ToArray());

        JsonElement uts = await _admin.GetFromJsonAsync<JsonElement>("/api/v1/states?administrativeType=UnionTerritory");
        Assert.Equal(2, uts.GetProperty("totalCount").GetInt32());

        JsonElement search = await _admin.GetFromJsonAsync<JsonElement>("/api/v1/states?search=ihar");
        Assert.Equal("Bihar", search.GetProperty("items")[0].GetProperty("name").GetString());

        JsonElement desc = await _admin.GetFromJsonAsync<JsonElement>("/api/v1/states?sortBy=name&sortDirection=desc&pageSize=1");
        Assert.Equal("Delhi", desc.GetProperty("items")[0].GetProperty("name").GetString());

        // Unknown sort field must not error or reach SQL — it falls back to the default order.
        HttpResponseMessage badSort = await _admin.GetAsync("/api/v1/states?sortBy=Id;DROP TABLE States");
        Assert.Equal(HttpStatusCode.OK, badSort.StatusCode);

        // Page size is capped at the shared maximum of 100.
        JsonElement capped = await _admin.GetFromJsonAsync<JsonElement>("/api/v1/states?pageSize=5000");
        Assert.Equal(100, capped.GetProperty("pageSize").GetInt32());
    }

    // ── Cities ──

    [Fact]
    public async Task Create_city_with_valid_state_returns_201_and_state_name()
    {
        JsonElement s = await CreateStateAsync("Goa", "30");
        int stateId = s.GetProperty("id").GetInt32();

        JsonElement city = await CreateCityAsync(stateId, "Panaji", "StatutoryTown");

        Assert.Equal("Goa", city.GetProperty("stateName").GetString());
        Assert.Equal("StatutoryTown", city.GetProperty("classification").GetString());
    }

    [Fact]
    public async Task Create_city_rejects_unknown_state_duplicates_and_bad_input()
    {
        JsonElement s = await CreateStateAsync("Goa", "30");
        int stateId = s.GetProperty("id").GetInt32();

        Assert.Equal(HttpStatusCode.BadRequest,
            (await _admin.PostAsJsonAsync("/api/v1/cities", new { name = "Ghost", stateId = 424242 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _admin.PostAsJsonAsync("/api/v1/cities", new { name = "", stateId })).StatusCode);

        await CreateCityAsync(stateId, "Mapusa");
        Assert.Equal(HttpStatusCode.Conflict,
            (await _admin.PostAsJsonAsync("/api/v1/cities", new { name = "mapusa", stateId, classification = "CensusTown" })).StatusCode);
    }

    [Fact]
    public async Task Same_city_name_is_allowed_in_different_states()
    {
        int a = (await CreateStateAsync("Uttar Pradesh", "09")).GetProperty("id").GetInt32();
        int b = (await CreateStateAsync("Bihar", "10")).GetProperty("id").GetInt32();

        await CreateCityAsync(a, "Bijnor");
        await CreateCityAsync(b, "Bijnor");
    }

    [Fact]
    public async Task Get_and_update_city_and_filtering()
    {
        int goa = (await CreateStateAsync("Goa", "30")).GetProperty("id").GetInt32();
        int kerala = (await CreateStateAsync("Kerala", "32")).GetProperty("id").GetInt32();
        int cityId = (await CreateCityAsync(goa, "Margao", "StatutoryTown")).GetProperty("id").GetInt32();
        await CreateCityAsync(goa, "Curchorem", "CensusTown");
        await CreateCityAsync(kerala, "Kochi", "StatutoryTown");

        Assert.Equal(HttpStatusCode.OK, (await _admin.GetAsync($"/api/v1/cities/{cityId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.GetAsync("/api/v1/cities/999999")).StatusCode);

        HttpResponseMessage upd = await _admin.PutAsJsonAsync($"/api/v1/cities/{cityId}",
            new { name = "Madgaon", stateId = goa, classification = "StatutoryTown", isActive = true });
        Assert.Equal(HttpStatusCode.OK, upd.StatusCode);
        Assert.Equal("Madgaon", (await upd.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("name").GetString());

        Assert.Equal(HttpStatusCode.BadRequest,
            (await _admin.PutAsJsonAsync($"/api/v1/cities/{cityId}", new { name = "Madgaon", stateId = 424242, isActive = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await _admin.PutAsJsonAsync("/api/v1/cities/999999", new { name = "X", stateId = goa, isActive = true })).StatusCode);

        JsonElement byState = await _admin.GetFromJsonAsync<JsonElement>($"/api/v1/cities?stateId={goa}");
        Assert.Equal(2, byState.GetProperty("totalCount").GetInt32());
        JsonElement census = await _admin.GetFromJsonAsync<JsonElement>("/api/v1/cities?classification=CensusTown");
        Assert.Equal(1, census.GetProperty("totalCount").GetInt32());
        JsonElement search = await _admin.GetFromJsonAsync<JsonElement>("/api/v1/cities?search=koch");
        Assert.Equal("Kochi", search.GetProperty("items")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task State_cities_endpoint_returns_404_for_unknown_state_and_scopes_results()
    {
        int goa = (await CreateStateAsync("Goa", "30")).GetProperty("id").GetInt32();
        int kerala = (await CreateStateAsync("Kerala", "32")).GetProperty("id").GetInt32();
        await CreateCityAsync(goa, "Margao");
        await CreateCityAsync(kerala, "Kochi");

        Assert.Equal(HttpStatusCode.NotFound, (await _admin.GetAsync("/api/v1/states/999999/cities")).StatusCode);

        // A stateId query parameter must not override the route's parent.
        JsonElement r = await _admin.GetFromJsonAsync<JsonElement>($"/api/v1/states/{goa}/cities?stateId={kerala}");
        Assert.Equal(1, r.GetProperty("totalCount").GetInt32());
        Assert.Equal("Margao", r.GetProperty("items")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Deleting_a_state_with_cities_is_blocked_with_409_until_cities_are_removed()
    {
        int stateId = (await CreateStateAsync("Goa", "30")).GetProperty("id").GetInt32();
        int cityId = (await CreateCityAsync(stateId, "Margao")).GetProperty("id").GetInt32();

        Assert.Equal(HttpStatusCode.Conflict, (await _admin.DeleteAsync($"/api/v1/states/{stateId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _admin.GetAsync($"/api/v1/cities/{cityId}")).StatusCode); // city untouched

        Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteAsync($"/api/v1/cities/{cityId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.DeleteAsync($"/api/v1/cities/{cityId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteAsync($"/api/v1/states/{stateId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.DeleteAsync($"/api/v1/states/{stateId}")).StatusCode);
    }

    [Fact]
    public async Task Request_cannot_set_provenance_or_system_fields()
    {
        int stateId = (await CreateStateAsync("Goa", "30")).GetProperty("id").GetInt32();

        HttpResponseMessage r = await _admin.PostAsJsonAsync("/api/v1/cities", new
        {
            name = "Margao", stateId, id = 777, sourceSystem = "EVIL", sourceKey = "x", createdAtIst = "2000-01-01T00:00:00Z"
        });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        JsonElement body = await r.Content.ReadFromJsonAsync<JsonElement>();

        Assert.NotEqual(777, body.GetProperty("id").GetInt32());
        Assert.True(body.GetProperty("createdAtIst").GetDateTime().Year >= 2025);

        await using var ctx = _factory.CreateContext();
        var city = ctx.Cities.Single();
        Assert.Null(city.SourceSystem);
        Assert.Null(city.SourceKey);
    }
}
