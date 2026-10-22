using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NSG.Mobile.Domain.Common;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Infrastructure.Persistence;

namespace NSG.Mobile.Tests;

/// <summary>
/// Integration tests for /api/v1/nsg-trades (table nsg_trade) and /api/v1/pd-trades (table pd_trade). The two lookups behave
/// identically and are independent of each other, so every test runs once per resource.
/// </summary>
[Collection("mysql")]
public class TradeLookupApiTests : IAsyncLifetime
{
    private const string Nsg = "nsg";
    private const string Pd = "pd";

    private readonly ApiFactory _factory;
    private readonly HttpClient _admin;
    private readonly HttpClient _employee;
    private readonly HttpClient _anonymous;

    public TradeLookupApiTests(DatabaseFixture fixture)
    {
        _factory = fixture.Factory;
        _admin = _factory.CreateClientFor(DomainConstants.Roles.PisAdmin);
        _employee = _factory.CreateClientFor(DomainConstants.Roles.Employee);
        _anonymous = _factory.CreateClientFor(null);
    }

    public static TheoryData<string> Resources => new() { Nsg, Pd };

    public async Task InitializeAsync() => await CleanAsync();

    public async Task DisposeAsync() => await CleanAsync();

    private async Task CleanAsync()
    {
        await using AppDbContext ctx = _factory.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `users_pis`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `nsg_trade`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `pd_trade`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `audit_logs`");
    }

    private static string Url(string r) => r == Nsg ? "/api/v1/nsg-trades" : "/api/v1/pd-trades";
    private static string NameField(string r) => r == Nsg ? "nsgTradeName" : "pdTradeName";
    private static string EntityType(string r) => r == Nsg ? "NsgTrade" : "PdTrade";

    private Dictionary<string, object?> Body(string r, string? name, bool? isActive = true)
        => new() { [NameField(r)] = name, ["isActive"] = isActive };

    private async Task<JsonElement> CreateAsync(string r, string name, bool isActive = true)
    {
        HttpResponseMessage resp = await _admin.PostAsJsonAsync(Url(r), Body(r, name, isActive));
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return await resp.ReadDataAsync();
    }

    private async Task UseByPersonnelAsync(string r, int tradeId)
    {
        await using AppDbContext ctx = _factory.CreateContext();
        UserPis p = new() { NsgNumber = "TR-" + r + tradeId, ServiceNumber = "S", CreatedAtIst = IndianStandardTime.Now };
        if (r == Nsg) p.NsgTradeId = tradeId; else p.PdTradeId = tradeId;
        ctx.UsersPis.Add(p);
        await ctx.SaveChangesAsync();
    }

    private async Task<int> AuditCountAsync(string r, int id, string operation)
    {
        await using AppDbContext ctx = _factory.CreateContext();
        List<AuditLog> logs = await ctx.AuditLogs.Where(a => a.EntityType == EntityType(r) && a.EntityId == id.ToString()).ToListAsync();
        return logs.Count(a => a.AdditionalData != null && a.AdditionalData.Contains(operation));
    }

    // ── Authentication and authorization ──

    [Theory, MemberData(nameof(Resources))]
    public async Task Anonymous_requests_get_401(string r)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _anonymous.GetAsync(Url(r))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _anonymous.GetAsync(Url(r) + "/1")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _anonymous.PostAsJsonAsync(Url(r), Body(r, "GD"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _anonymous.PutAsJsonAsync(Url(r) + "/1", Body(r, "GD"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _anonymous.DeleteAsync(Url(r) + "/1")).StatusCode);
    }

    [Theory, MemberData(nameof(Resources))]
    public async Task Employees_can_read_but_not_write(string r)
    {
        int id = (await CreateAsync(r, "GD")).GetProperty("id").GetInt32();

        Assert.Equal(HttpStatusCode.OK, (await _employee.GetAsync(Url(r))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _employee.GetAsync($"{Url(r)}/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _employee.PostAsJsonAsync(Url(r), Body(r, "TD"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _employee.PutAsJsonAsync($"{Url(r)}/{id}", Body(r, "X"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _employee.DeleteAsync($"{Url(r)}/{id}")).StatusCode);
        Assert.Equal("GD", (await (await _admin.GetAsync($"{Url(r)}/{id}")).ReadDataAsync()).GetProperty(NameField(r)).GetString());
    }

    // ── Create ──

    [Theory, MemberData(nameof(Resources))]
    public async Task Create_returns_201_trims_the_name_defaults_to_active_and_audits(string r)
    {
        HttpResponseMessage resp = await _admin.PostAsJsonAsync(Url(r), new Dictionary<string, object?> { [NameField(r)] = "  GD  " });

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        JsonElement body = await resp.ReadDataAsync();
        int id = body.GetProperty("id").GetInt32();
        Assert.EndsWith($"{Url(r)}/{id}", resp.Headers.Location!.AbsolutePath);
        Assert.Equal("GD", body.GetProperty(NameField(r)).GetString());
        Assert.True(body.GetProperty("isActive").GetBoolean());
        Assert.True(body.GetProperty("createdAtIst").GetDateTime().Year >= 2026);
        Assert.Equal(1, await AuditCountAsync(r, id, EntityType(r) + "Created"));
    }

    [Theory, MemberData(nameof(Resources))]
    public async Task The_response_contains_only_the_four_agreed_fields(string r)
    {
        JsonElement body = await CreateAsync(r, "GD");

        Assert.Equal(
            new[] { "createdAtIst", "id", "isActive", NameField(r) }.OrderBy(n => n),
            body.EnumerateObject().Select(p => p.Name).OrderBy(n => n));
    }

    [Theory, MemberData(nameof(Resources))]
    public async Task Create_rejects_blank_or_overlong_names_with_400(string r)
    {
        foreach (string? name in new[] { "", "   ", null, new string('x', 101) })
            Assert.Equal(HttpStatusCode.BadRequest, (await _admin.PostAsJsonAsync(Url(r), Body(r, name))).StatusCode);

        Assert.Equal(0, (await (await _admin.GetAsync(Url(r))).ReadDataAsync()).GetProperty("totalCount").GetInt32());
    }

    [Theory, MemberData(nameof(Resources))]
    public async Task Duplicate_names_return_409_ignoring_case_and_spaces_and_inactive_trades_still_count(string r)
    {
        await CreateAsync(r, "GD");
        await CreateAsync(r, "Retired", isActive: false);

        foreach (string duplicate in new[] { "GD", " gd ", "GD ", "retired" })
            Assert.Equal(HttpStatusCode.Conflict, (await _admin.PostAsJsonAsync(Url(r), Body(r, duplicate))).StatusCode);
    }

    [Fact]
    public async Task The_same_name_may_exist_in_both_tables_because_they_are_independent()
    {
        await CreateAsync(Nsg, "GD");
        await CreateAsync(Pd, "GD");

        // ...and the two lists do not see each other.
        Assert.Equal(1, (await (await _admin.GetAsync(Url(Nsg))).ReadDataAsync()).GetProperty("totalCount").GetInt32());
        Assert.Equal(1, (await (await _admin.GetAsync(Url(Pd))).ReadDataAsync()).GetProperty("totalCount").GetInt32());
    }

    // ── Read ──

    [Theory, MemberData(nameof(Resources))]
    public async Task Get_by_id_returns_the_trade_or_404(string r)
    {
        int id = (await CreateAsync(r, "GD")).GetProperty("id").GetInt32();

        HttpResponseMessage found = await _admin.GetAsync($"{Url(r)}/{id}");
        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
        Assert.Equal("GD", (await found.ReadDataAsync()).GetProperty(NameField(r)).GetString());

        HttpResponseMessage missing = await _admin.GetAsync($"{Url(r)}/{id + 999}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(404, (int)missing.StatusCode);
    }

    [Theory, MemberData(nameof(Resources))]
    public async Task List_supports_pagination_search_and_the_active_filter(string r)
    {
        foreach (string name in new[] { "Signals", "GD", "Driver", "Cook" })
            await CreateAsync(r, name);
        await CreateAsync(r, "Old GD", isActive: false);

        async Task<JsonElement> List(string query) => await (await _admin.GetAsync($"{Url(r)}?{query}")).ReadDataAsync();
        string[] Names(JsonElement page) => page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty(NameField(r)).GetString()!).ToArray();

        JsonElement page1 = await List("pageNumber=1&pageSize=2");
        Assert.Equal(5, page1.GetProperty("totalCount").GetInt32());
        Assert.Equal(3, page1.GetProperty("totalPages").GetInt32());
        Assert.Equal(new[] { "Cook", "Driver" }, Names(page1));   // ordered by name
        Assert.Equal(new[] { "Signals" }, Names(await List("pageNumber=3&pageSize=2")));

        Assert.Equal(new[] { "GD", "Old GD" }, Names(await List("search=gd")));
        Assert.Equal(new[] { "Old GD" }, Names(await List("isActive=false")));
        Assert.Equal(new[] { "GD" }, Names(await List("search=gd&isActive=true")));
        Assert.Equal(4, (await List("isActive=true")).GetProperty("totalCount").GetInt32());
        Assert.Empty(Names(await List("search=nothing-matches")));
    }

    // ── Update ──

    [Theory, MemberData(nameof(Resources))]
    public async Task Update_changes_name_and_state_and_audits(string r)
    {
        int id = (await CreateAsync(r, "GD")).GetProperty("id").GetInt32();

        HttpResponseMessage resp = await _admin.PutAsJsonAsync($"{Url(r)}/{id}", Body(r, " General Duty ", isActive: false));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        JsonElement body = await resp.ReadDataAsync();
        Assert.Equal("General Duty", body.GetProperty(NameField(r)).GetString());
        Assert.False(body.GetProperty("isActive").GetBoolean());
        Assert.Equal(1, await AuditCountAsync(r, id, EntityType(r) + "Updated"));
    }

    [Theory, MemberData(nameof(Resources))]
    public async Task Update_requires_isActive_so_an_omitted_value_never_changes_state(string r)
    {
        int id = (await CreateAsync(r, "GD")).GetProperty("id").GetInt32();

        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.PutAsJsonAsync($"{Url(r)}/{id}", Body(r, "GD", isActive: null))).StatusCode);
        Assert.True((await (await _admin.GetAsync($"{Url(r)}/{id}")).ReadDataAsync()).GetProperty("isActive").GetBoolean());
    }

    [Theory, MemberData(nameof(Resources))]
    public async Task Update_validates_input_unknown_ids_and_duplicates(string r)
    {
        int gd = (await CreateAsync(r, "GD")).GetProperty("id").GetInt32();
        await CreateAsync(r, "TD");

        Assert.Equal(HttpStatusCode.NotFound, (await _admin.PutAsJsonAsync($"{Url(r)}/{gd + 999}", Body(r, "X"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.PutAsJsonAsync($"{Url(r)}/{gd}", Body(r, ""))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _admin.PutAsJsonAsync($"{Url(r)}/{gd}", Body(r, "td"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _admin.PutAsJsonAsync($"{Url(r)}/{gd}", Body(r, "GD"))).StatusCode);   // keeping its own name is fine
    }

    // ── Deactivation and protection ──

    [Theory, MemberData(nameof(Resources))]
    public async Task Delete_deactivates_keeps_the_row_is_idempotent_and_audited(string r)
    {
        int id = (await CreateAsync(r, "GD")).GetProperty("id").GetInt32();

        Assert.Equal(HttpStatusCode.OK, (await _admin.DeleteAsync($"{Url(r)}/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _admin.DeleteAsync($"{Url(r)}/{id}")).StatusCode);

        Assert.False((await (await _admin.GetAsync($"{Url(r)}/{id}")).ReadDataAsync()).GetProperty("isActive").GetBoolean());
        Assert.Equal(1, await AuditCountAsync(r, id, EntityType(r) + "Deactivated"));
        await using AppDbContext ctx = _factory.CreateContext();
        Assert.Equal(1, r == Nsg ? await ctx.NsgTrades.CountAsync(t => t.Id == id) : await ctx.PdTrades.CountAsync(t => t.Id == id));   // never hard-deleted
    }

    [Theory, MemberData(nameof(Resources))]
    public async Task Delete_unknown_trade_returns_404(string r)
        => Assert.Equal(HttpStatusCode.NotFound, (await _admin.DeleteAsync($"{Url(r)}/987654")).StatusCode);

    [Theory, MemberData(nameof(Resources))]
    public async Task A_trade_used_by_personnel_cannot_be_deactivated_but_can_be_renamed(string r)
    {
        int id = (await CreateAsync(r, "GD")).GetProperty("id").GetInt32();
        await UseByPersonnelAsync(r, id);

        Assert.Equal(HttpStatusCode.Conflict, (await _admin.DeleteAsync($"{Url(r)}/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _admin.PutAsJsonAsync($"{Url(r)}/{id}", Body(r, "GD", isActive: false))).StatusCode);

        JsonElement renamed = await (await _admin.PutAsJsonAsync($"{Url(r)}/{id}", Body(r, "General Duty"))).ReadDataAsync();
        Assert.Equal("General Duty", renamed.GetProperty(NameField(r)).GetString());
        Assert.True(renamed.GetProperty("isActive").GetBoolean());
        Assert.Equal(0, await AuditCountAsync(r, id, EntityType(r) + "Deactivated"));
        await using AppDbContext ctx = _factory.CreateContext();
        Assert.Equal(1, await ctx.UsersPis.CountAsync());   // the personnel record is untouched
    }

    [Theory, MemberData(nameof(Resources))]
    public async Task Deactivation_becomes_possible_once_no_personnel_use_the_trade(string r)
    {
        int id = (await CreateAsync(r, "GD")).GetProperty("id").GetInt32();
        await UseByPersonnelAsync(r, id);
        Assert.Equal(HttpStatusCode.Conflict, (await _admin.DeleteAsync($"{Url(r)}/{id}")).StatusCode);

        await using (AppDbContext ctx = _factory.CreateContext())
            await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `users_pis`");

        Assert.Equal(HttpStatusCode.OK, (await _admin.DeleteAsync($"{Url(r)}/{id}")).StatusCode);
    }

    [Fact]
    public async Task Use_of_one_trade_table_does_not_protect_the_other()
    {
        int nsg = (await CreateAsync(Nsg, "GD")).GetProperty("id").GetInt32();
        int pd = (await CreateAsync(Pd, "GD")).GetProperty("id").GetInt32();
        await UseByPersonnelAsync(Nsg, nsg);

        Assert.Equal(HttpStatusCode.Conflict, (await _admin.DeleteAsync($"{Url(Nsg)}/{nsg}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _admin.DeleteAsync($"{Url(Pd)}/{pd}")).StatusCode);
    }

    // ── Schema (real MySQL, created by the migrations) ──

    [Fact]
    public async Task Tables_have_exactly_the_agreed_columns_a_unique_name_and_no_foreign_keys()
    {
        await using AppDbContext ctx = _factory.CreateContext();

        async Task<List<string>> Query(string sql) => await ctx.Database.SqlQueryRaw<string>(sql).ToListAsync();

        foreach ((string table, string nameColumn) in new[] { ("nsg_trade", "nsg_trade_name"), ("pd_trade", "pd_trade_name") })
        {
            List<string> columns = await Query($"SELECT CONCAT(COLUMN_NAME,' ',COLUMN_TYPE,' ',IS_NULLABLE) AS `Value` FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='{table}' ORDER BY ORDINAL_POSITION");
            Assert.Equal(new[] { "id int NO", $"{nameColumn} varchar(100) NO", "created_at_ist datetime(6) NO", "is_active tinyint(1) NO" }, columns.ToArray());

            Assert.Contains($"ix_{table}_{nameColumn}", await Query($"SELECT INDEX_NAME AS `Value` FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='{table}' AND NON_UNIQUE=0"));
            Assert.Empty(await Query($"SELECT CONSTRAINT_NAME AS `Value` FROM information_schema.KEY_COLUMN_USAGE WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='{table}' AND REFERENCED_TABLE_NAME IS NOT NULL"));   // no rank or cross-table link
        }
    }
}
