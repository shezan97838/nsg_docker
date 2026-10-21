using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Infrastructure.Persistence;

namespace NSG.Mobile.Tests;

/// <summary>NsgTradeId / PdTradeId on /api/v1/users-pis: optional, existence-checked, independent of ranks and of each other.</summary>
[Collection("mysql")]
public class UsersPisTradeApiTests : IAsyncLifetime
{
    private const string Url = "/api/v1/users-pis";
    private const int FirstTestSrNo = 9500;

    private readonly ApiFactory _factory;
    private readonly HttpClient _admin;

    private int _rankA, _rankB, _pdRank;
    private int _nsgGd, _nsgTd, _nsgOld, _pdGd, _pdOld;

    public UsersPisTradeApiTests(DatabaseFixture fixture)
    {
        _factory = fixture.Factory;
        _admin = _factory.CreateClientFor(DomainConstants.Roles.PisAdmin);
    }

    public async Task InitializeAsync()
    {
        await CleanAsync();
        await using AppDbContext ctx = _factory.CreateContext();
        RankHierarchy a = new() { SrNo = FirstTestSrNo, RankName = "Rank A", ShortCode = "RA" };
        RankHierarchy b = new() { SrNo = FirstTestSrNo + 1, RankName = "Rank B", ShortCode = "RB" };
        ParentDepartmentRank pd = new() { Name = "Parent A" };
        NsgTrade gd = new() { NsgTradeName = "GD" }, td = new() { NsgTradeName = "TD" }, oldNsg = new() { NsgTradeName = "OLD", IsActive = false };
        PdTrade pdGd = new() { PdTradeName = "GD" }, pdOld = new() { PdTradeName = "OLD", IsActive = false };
        ctx.AddRange(a, b, pd, gd, td, oldNsg, pdGd, pdOld);
        await ctx.SaveChangesAsync();
        (_rankA, _rankB, _pdRank) = (a.Id, b.Id, pd.Id);
        (_nsgGd, _nsgTd, _nsgOld, _pdGd, _pdOld) = (gd.Id, td.Id, oldNsg.Id, pdGd.Id, pdOld.Id);
    }

    public async Task DisposeAsync() => await CleanAsync();

    private async Task CleanAsync()
    {
        await using AppDbContext ctx = _factory.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `users_pis`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `nsg_trade`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `pd_trade`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `parent_department_ranks`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `audit_logs`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `rank_hierarchy` WHERE `sr_no` >= " + FirstTestSrNo);
    }

    private static Dictionary<string, object?> Create(string nsg = "T-1") => new() { ["nsgNumber"] = nsg, ["serviceNumber"] = "S-1" };
    private static Dictionary<string, object?> Update() => new() { ["serviceNumber"] = "S-1", ["isActive"] = true };

    private async Task<long> CreatePersonAsync(Dictionary<string, object?> body)
    {
        HttpResponseMessage r = await _admin.PostAsJsonAsync(Url, body);
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
    }

    private async Task<JsonElement> GetAsync(long id) => await (await _admin.GetAsync($"{Url}/{id}")).Content.ReadFromJsonAsync<JsonElement>();

    private async Task<int> CountAsync() { await using AppDbContext c = _factory.CreateContext(); return await c.UsersPis.CountAsync(); }

    // ── Create ──

    [Fact]
    public async Task Create_with_both_trades_stores_them_in_the_agreed_columns_and_returns_ids_and_names()
    {
        Dictionary<string, object?> body = Create(); body["nsgTradeId"] = _nsgGd; body["pdTradeId"] = _pdGd;

        HttpResponseMessage r = await _admin.PostAsJsonAsync(Url, body);

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        JsonElement created = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(_nsgGd, created.GetProperty("nsgTradeId").GetInt32());
        Assert.Equal("GD", created.GetProperty("nsgTradeName").GetString());
        Assert.Equal(_pdGd, created.GetProperty("pdTradeId").GetInt32());
        Assert.Equal("GD", created.GetProperty("pdTradeName").GetString());

        await using AppDbContext ctx = _factory.CreateContext();
        Assert.Equal("nsg_trade_id=" + _nsgGd + ";pd_trade_id=" + _pdGd, (await ctx.Database.SqlQueryRaw<string>(
            "SELECT CONCAT('nsg_trade_id=', `nsg_trade_id`, ';pd_trade_id=', `pd_trade_id`) AS `Value` FROM `users_pis`").SingleAsync()));
    }

    [Fact]
    public async Task Both_trades_are_optional_and_each_works_without_the_other()
    {
        JsonElement none = await GetAsync(await CreatePersonAsync(Create("T-1")));
        Dictionary<string, object?> nsgOnly = Create("T-2"); nsgOnly["nsgTradeId"] = _nsgTd;
        Dictionary<string, object?> pdOnly = Create("T-3"); pdOnly["pdTradeId"] = _pdGd;
        JsonElement a = await GetAsync(await CreatePersonAsync(nsgOnly));
        JsonElement b = await GetAsync(await CreatePersonAsync(pdOnly));

        Assert.Equal(JsonValueKind.Null, none.GetProperty("nsgTradeId").ValueKind);
        Assert.Equal(JsonValueKind.Null, none.GetProperty("pdTradeId").ValueKind);
        Assert.Equal("TD", a.GetProperty("nsgTradeName").GetString());
        Assert.Equal(JsonValueKind.Null, a.GetProperty("pdTradeId").ValueKind);
        Assert.Equal("GD", b.GetProperty("pdTradeName").GetString());
        Assert.Equal(JsonValueKind.Null, b.GetProperty("nsgTradeId").ValueKind);
    }

    [Fact]
    public async Task There_is_no_rank_and_trade_matching_any_trade_goes_with_any_rank_or_with_no_rank()
    {
        Dictionary<string, object?> withRankA = Create("R-1"); withRankA["nsgRankId"] = _rankA; withRankA["nsgTradeId"] = _nsgTd;
        Dictionary<string, object?> withRankB = Create("R-2"); withRankB["nsgRankId"] = _rankB; withRankB["nsgTradeId"] = _nsgTd;
        Dictionary<string, object?> noRank = Create("R-3"); noRank["nsgTradeId"] = _nsgTd; noRank["pdTradeId"] = _pdGd;
        Dictionary<string, object?> otherRank = Create("R-4"); otherRank["parentDepartmentRankId"] = _pdRank; otherRank["nsgRankId"] = _rankB; otherRank["pdTradeId"] = _pdGd;

        foreach (Dictionary<string, object?> body in new[] { withRankA, withRankB, noRank, otherRank })
            await CreatePersonAsync(body);

        Assert.Equal(4, await CountAsync());
    }

    [Fact]
    public async Task Unknown_trade_ids_return_400_naming_the_fields_and_store_nothing()
    {
        Dictionary<string, object?> body = Create(); body["nsgTradeId"] = 987654; body["pdTradeId"] = 987655;

        HttpResponseMessage r = await _admin.PostAsJsonAsync(Url, body);

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        string text = await r.Content.ReadAsStringAsync();
        Assert.Contains("NsgTradeId", text);
        Assert.Contains("PdTradeId", text);
        Assert.Equal(0, await CountAsync());
    }

    [Fact]
    public async Task Non_positive_trade_ids_are_rejected_as_invalid_input()
    {
        Dictionary<string, object?> a = Create("N-1"); a["nsgTradeId"] = 0;
        Dictionary<string, object?> b = Create("N-2"); b["pdTradeId"] = -3;
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.PostAsJsonAsync(Url, a)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.PostAsJsonAsync(Url, b)).StatusCode);
    }

    [Fact]
    public async Task Inactive_trades_follow_the_existing_lookup_rule_existence_only()
    {
        Dictionary<string, object?> body = Create(); body["nsgTradeId"] = _nsgOld; body["pdTradeId"] = _pdOld;

        JsonElement person = await GetAsync(await CreatePersonAsync(body));

        Assert.Equal(_nsgOld, person.GetProperty("nsgTradeId").GetInt32());
        Assert.Equal("OLD", person.GetProperty("pdTradeName").GetString());
    }

    // ── Update ──

    [Fact]
    public async Task Update_can_set_change_and_clear_each_trade_independently()
    {
        long id = await CreatePersonAsync(Create());

        Dictionary<string, object?> set = Update(); set["nsgTradeId"] = _nsgGd; set["pdTradeId"] = _pdGd;
        JsonElement afterSet = await (await _admin.PutAsJsonAsync($"{Url}/{id}", set)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("GD", afterSet.GetProperty("nsgTradeName").GetString());
        Assert.Equal("GD", afterSet.GetProperty("pdTradeName").GetString());

        Dictionary<string, object?> changeNsgClearPd = Update(); changeNsgClearPd["nsgTradeId"] = _nsgTd;   // PUT replaces: the omitted PD trade is cleared
        JsonElement afterChange = await (await _admin.PutAsJsonAsync($"{Url}/{id}", changeNsgClearPd)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("TD", afterChange.GetProperty("nsgTradeName").GetString());
        Assert.Equal(JsonValueKind.Null, afterChange.GetProperty("pdTradeId").ValueKind);

        JsonElement afterClear = await (await _admin.PutAsJsonAsync($"{Url}/{id}", Update())).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, afterClear.GetProperty("nsgTradeId").ValueKind);
    }

    [Fact]
    public async Task Changing_the_rank_does_not_touch_the_trade()
    {
        Dictionary<string, object?> create = Create(); create["nsgRankId"] = _rankA; create["nsgTradeId"] = _nsgGd;
        long id = await CreatePersonAsync(create);

        Dictionary<string, object?> moveRank = Update(); moveRank["nsgRankId"] = _rankB; moveRank["nsgTradeId"] = _nsgGd;
        JsonElement moved = await (await _admin.PutAsJsonAsync($"{Url}/{id}", moveRank)).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(_rankB, moved.GetProperty("nsgRankId").GetInt32());
        Assert.Equal(_nsgGd, moved.GetProperty("nsgTradeId").GetInt32());
    }

    [Fact]
    public async Task Update_rejects_unknown_trade_ids_and_saves_nothing()
    {
        Dictionary<string, object?> create = Create(); create["nsgTradeId"] = _nsgGd;
        long id = await CreatePersonAsync(create);

        Dictionary<string, object?> bad = Update(); bad["nsgTradeId"] = 987654; bad["fullName"] = "Must Not Save";

        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.PutAsJsonAsync($"{Url}/{id}", bad)).StatusCode);
        JsonElement unchanged = await GetAsync(id);
        Assert.Equal(_nsgGd, unchanged.GetProperty("nsgTradeId").GetInt32());
        Assert.Equal(JsonValueKind.Null, unchanged.GetProperty("fullName").ValueKind);
    }

    // ── Responses, list and existing behaviour ──

    [Fact]
    public async Task The_list_shows_the_nsg_trade_name_without_exposing_sensitive_fields()
    {
        Dictionary<string, object?> body = Create(); body["nsgRankId"] = _rankA; body["nsgTradeId"] = _nsgGd; body["panNumber"] = "AGIPR3625F";
        await CreatePersonAsync(body);

        string raw = await (await _admin.GetAsync(Url)).Content.ReadAsStringAsync();
        JsonElement item = JsonDocument.Parse(raw).RootElement.GetProperty("items")[0];

        Assert.Equal(_nsgGd, item.GetProperty("nsgTradeId").GetInt32());
        Assert.Equal("GD", item.GetProperty("nsgTradeName").GetString());
        Assert.Equal("Rank A", item.GetProperty("nsgRankName").GetString());   // rank and trade stay separate fields
        Assert.DoesNotContain("AGIPR", raw);
        Assert.DoesNotContain("Masked", raw);
    }

    [Fact]
    public async Task Records_without_any_trade_behave_exactly_as_before()
    {
        Dictionary<string, object?> body = Create(); body["fullName"] = "Plain Record"; body["mobileNumber"] = "9876543210";
        long id = await CreatePersonAsync(body);

        Dictionary<string, object?> update = Update(); update["fullName"] = "Renamed"; update["mobileNumber"] = "9876543210";
        JsonElement updated = await (await _admin.PutAsJsonAsync($"{Url}/{id}", update)).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("Renamed", updated.GetProperty("fullName").GetString());
        Assert.Equal("Pending", updated.GetProperty("mobileAppActivationStatus").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteAsync($"{Url}/{id}")).StatusCode);
    }

    [Fact]
    public async Task The_database_blocks_removing_a_trade_that_a_person_references()
    {
        Dictionary<string, object?> body = Create(); body["nsgTradeId"] = _nsgGd; body["pdTradeId"] = _pdGd;
        await CreatePersonAsync(body);

        await using AppDbContext ctx = _factory.CreateContext();
        await Assert.ThrowsAnyAsync<Exception>(() => ctx.Database.ExecuteSqlRawAsync("DELETE FROM `nsg_trade` WHERE `id` = {0}", _nsgGd));
        await Assert.ThrowsAnyAsync<Exception>(() => ctx.Database.ExecuteSqlRawAsync("DELETE FROM `pd_trade` WHERE `id` = {0}", _pdGd));
    }

    [Fact]
    public async Task Users_pis_has_nullable_trade_columns_with_restrict_foreign_keys_to_the_new_tables()
    {
        await using AppDbContext ctx = _factory.CreateContext();

        List<string> columns = await ctx.Database.SqlQueryRaw<string>(
            "SELECT CONCAT(COLUMN_NAME,' ',COLUMN_TYPE,' ',IS_NULLABLE) AS `Value` FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='users_pis' AND COLUMN_NAME IN ('nsg_trade_id','pd_trade_id') ORDER BY COLUMN_NAME").ToListAsync();
        Assert.Equal(new[] { "nsg_trade_id int YES", "pd_trade_id int YES" }, columns.ToArray());

        List<string> fks = await ctx.Database.SqlQueryRaw<string>(
            "SELECT CONCAT(k.COLUMN_NAME,'->',k.REFERENCED_TABLE_NAME,'.',k.REFERENCED_COLUMN_NAME,' ',r.DELETE_RULE) AS `Value` FROM information_schema.KEY_COLUMN_USAGE k JOIN information_schema.REFERENTIAL_CONSTRAINTS r ON r.CONSTRAINT_SCHEMA=k.CONSTRAINT_SCHEMA AND r.CONSTRAINT_NAME=k.CONSTRAINT_NAME WHERE k.TABLE_SCHEMA=DATABASE() AND k.TABLE_NAME='users_pis' AND k.COLUMN_NAME IN ('nsg_trade_id','pd_trade_id') ORDER BY k.COLUMN_NAME").ToListAsync();
        Assert.Equal(2, fks.Count);
        Assert.Contains(fks, f => f.StartsWith("nsg_trade_id->nsg_trade.id RESTRICT", StringComparison.Ordinal));
        Assert.Contains(fks, f => f.StartsWith("pd_trade_id->pd_trade.id RESTRICT", StringComparison.Ordinal));
    }
}
