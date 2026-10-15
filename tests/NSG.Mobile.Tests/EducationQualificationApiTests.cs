using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NSG.Mobile.Domain.Common;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Infrastructure.Persistence;

namespace NSG.Mobile.Tests;

/// <summary>Integration tests for /api/v1/education-qualifications (table education_qualifications).</summary>
[Collection("mysql")]
public class EducationQualificationApiTests : IAsyncLifetime
{
    private const string Url = "/api/v1/education-qualifications";

    private readonly ApiFactory _factory;
    private readonly HttpClient _admin;
    private readonly HttpClient _employee;
    private readonly HttpClient _anonymous;

    public EducationQualificationApiTests(DatabaseFixture fixture)
    {
        _factory = fixture.Factory;
        _admin = _factory.CreateClientFor(DomainConstants.Roles.ITAdmin);
        _employee = _factory.CreateClientFor(DomainConstants.Roles.Employee);
        _anonymous = _factory.CreateClientFor(null);
    }

    public async Task InitializeAsync() => await CleanAsync();

    public async Task DisposeAsync() => await CleanAsync();

    private async Task CleanAsync()
    {
        await using AppDbContext ctx = _factory.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `users_pis`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `education_qualifications`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `audit_logs`");
    }

    private static Dictionary<string, object?> Body(string? name, bool? isActive = true)
        => new() { ["courseName"] = name, ["isActive"] = isActive };

    private async Task<JsonElement> CreateAsync(string name, bool isActive = true)
    {
        HttpResponseMessage resp = await _admin.PostAsJsonAsync(Url, Body(name, isActive));
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
    }

    private async Task UseByPersonnelAsync(int educationId)
    {
        await using AppDbContext ctx = _factory.CreateContext();
        ctx.UsersPis.Add(new UserPis { NsgNumber = "ED-" + educationId, ServiceNumber = "S", EducationId = educationId, CreatedAtIst = IndianStandardTime.Now });
        await ctx.SaveChangesAsync();
    }

    private async Task<int> AuditCountAsync(int id, string operation)
    {
        await using AppDbContext ctx = _factory.CreateContext();
        List<AuditLog> logs = await ctx.AuditLogs.Where(a => a.EntityType == "EducationQualification" && a.EntityId == id.ToString()).ToListAsync();
        return logs.Count(a => a.AdditionalData != null && a.AdditionalData.Contains(operation));
    }

    private static async Task<JsonElement> Json(HttpResponseMessage resp) => await resp.Content.ReadFromJsonAsync<JsonElement>();

    // ── Authentication and authorization ──

    [Fact]
    public async Task Anonymous_requests_get_401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _anonymous.GetAsync(Url)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _anonymous.GetAsync(Url + "/1")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _anonymous.PostAsJsonAsync(Url, Body("B.Tech"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _anonymous.PutAsJsonAsync(Url + "/1", Body("B.Tech"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _anonymous.DeleteAsync(Url + "/1")).StatusCode);
    }

    [Fact]
    public async Task Employees_can_read_but_not_write()
    {
        int id = (await CreateAsync("B.Tech")).GetProperty("id").GetInt32();

        Assert.Equal(HttpStatusCode.OK, (await _employee.GetAsync(Url)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _employee.GetAsync($"{Url}/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _employee.PostAsJsonAsync(Url, Body("MBA"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _employee.PutAsJsonAsync($"{Url}/{id}", Body("X"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _employee.DeleteAsync($"{Url}/{id}")).StatusCode);
        Assert.Equal("B.Tech", (await Json(await _admin.GetAsync($"{Url}/{id}"))).GetProperty("courseName").GetString());
    }

    // ── Create ──

    [Fact]
    public async Task Create_returns_201_with_a_message_trims_the_name_defaults_to_active_and_audits()
    {
        HttpResponseMessage resp = await _admin.PostAsJsonAsync(Url, new Dictionary<string, object?> { ["courseName"] = "  B.Tech  " });

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        JsonElement body = await Json(resp);
        Assert.Equal("Education qualification created successfully.", body.GetProperty("message").GetString());
        JsonElement data = body.GetProperty("data");
        int id = data.GetProperty("id").GetInt32();
        Assert.EndsWith($"{Url}/{id}", resp.Headers.Location!.AbsolutePath);
        Assert.Equal("B.Tech", data.GetProperty("courseName").GetString());
        Assert.True(data.GetProperty("isActive").GetBoolean());
        Assert.True(data.GetProperty("createdAtIst").GetDateTime().Year >= 2026);
        Assert.Equal(1, await AuditCountAsync(id, "EducationQualificationCreated"));
    }

    [Fact]
    public async Task The_resource_contains_only_the_four_agreed_fields()
    {
        JsonElement data = await CreateAsync("B.Tech");

        Assert.Equal(
            new[] { "courseName", "createdAtIst", "id", "isActive" },
            data.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Create_rejects_blank_or_overlong_names_with_400()
    {
        foreach (string? name in new[] { "", "   ", null, new string('x', 101) })
            Assert.Equal(HttpStatusCode.BadRequest, (await _admin.PostAsJsonAsync(Url, Body(name))).StatusCode);

        JsonElement list = await Json(await _admin.GetAsync(Url));
        Assert.Equal("No education qualifications found.", list.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Create_accepts_a_name_of_exactly_100_characters()
        => Assert.Equal(HttpStatusCode.Created, (await _admin.PostAsJsonAsync(Url, Body(new string('x', 100)))).StatusCode);

    [Fact]
    public async Task Duplicate_names_return_409_ignoring_case_and_spaces_and_inactive_rows_still_count()
    {
        await CreateAsync("B.Tech");
        await CreateAsync("Retired", isActive: false);

        foreach (string duplicate in new[] { "B.Tech", " b.tech ", "B.TECH ", "retired" })
        {
            HttpResponseMessage resp = await _admin.PostAsJsonAsync(Url, Body(duplicate));
            Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
            Assert.False(string.IsNullOrWhiteSpace((await Json(resp)).GetProperty("detail").GetString()));
        }
    }

    // ── Read ──

    [Fact]
    public async Task Get_by_id_returns_the_qualification_or_404_with_a_message()
    {
        int id = (await CreateAsync("B.Tech")).GetProperty("id").GetInt32();

        HttpResponseMessage found = await _admin.GetAsync($"{Url}/{id}");
        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
        Assert.Equal("B.Tech", (await Json(found)).GetProperty("courseName").GetString());

        HttpResponseMessage missing = await _admin.GetAsync($"{Url}/{id + 999}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        JsonElement problem = await Json(missing);
        Assert.Equal(404, problem.GetProperty("status").GetInt32());
        Assert.Equal("Education qualification not found.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task List_supports_pagination_search_and_the_active_filter()
    {
        foreach (string name in new[] { "Diploma", "B.Tech", "MBA", "B.Sc" })
            await CreateAsync(name);
        await CreateAsync("Old B.Tech", isActive: false);

        async Task<JsonElement> List(string query) => await Json(await _admin.GetAsync($"{Url}?{query}"));
        string[] Names(JsonElement page) => page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("courseName").GetString()!).ToArray();

        JsonElement page1 = await List("pageNumber=1&pageSize=2");
        Assert.Equal(5, page1.GetProperty("totalCount").GetInt32());
        Assert.Equal(3, page1.GetProperty("totalPages").GetInt32());
        Assert.Equal(new[] { "B.Sc", "B.Tech" }, Names(page1));   // ordered by course name
        Assert.Equal(new[] { "Diploma", "MBA" }, Names(await List("pageNumber=2&pageSize=2")));
        Assert.Equal(new[] { "Old B.Tech" }, Names(await List("pageNumber=3&pageSize=2")));

        Assert.Equal(new[] { "B.Tech", "Old B.Tech" }, Names(await List("search=b.tech")));
        Assert.Equal(new[] { "Old B.Tech" }, Names(await List("isActive=false")));
        Assert.Equal(new[] { "B.Tech" }, Names(await List("search=b.tech&isActive=true")));
        Assert.Equal(4, (await List("isActive=true")).GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task List_with_no_matches_returns_200_with_one_message_and_no_items()
    {
        foreach (string query in new[] { "", "?search=nothing-matches" })
        {
            HttpResponseMessage resp = await _admin.GetAsync(Url + query);

            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            JsonElement body = await Json(resp);
            Assert.Equal(new[] { "message" }, body.EnumerateObject().Select(p => p.Name).ToArray());
            Assert.Equal("No education qualifications found.", body.GetProperty("message").GetString());
        }

        await CreateAsync("B.Tech");
        JsonElement beyondLastPage = await Json(await _admin.GetAsync(Url + "?pageNumber=9"));
        Assert.Equal("No education qualifications found.", beyondLastPage.GetProperty("message").GetString());
    }

    // ── Update ──

    [Fact]
    public async Task Update_changes_name_and_state_returns_a_message_and_audits()
    {
        int id = (await CreateAsync("B.Tech")).GetProperty("id").GetInt32();

        HttpResponseMessage resp = await _admin.PutAsJsonAsync($"{Url}/{id}", Body(" Bachelor of Technology ", isActive: false));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        JsonElement body = await Json(resp);
        Assert.Equal("Education qualification updated successfully.", body.GetProperty("message").GetString());
        Assert.Equal("Bachelor of Technology", body.GetProperty("data").GetProperty("courseName").GetString());
        Assert.False(body.GetProperty("data").GetProperty("isActive").GetBoolean());
        Assert.Equal(1, await AuditCountAsync(id, "EducationQualificationUpdated"));
    }

    [Fact]
    public async Task Update_requires_isActive_so_an_omitted_value_never_changes_state()
    {
        int id = (await CreateAsync("B.Tech")).GetProperty("id").GetInt32();

        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.PutAsJsonAsync($"{Url}/{id}", Body("B.Tech", isActive: null))).StatusCode);
        Assert.True((await Json(await _admin.GetAsync($"{Url}/{id}"))).GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task Update_validates_input_unknown_ids_and_duplicates()
    {
        int btech = (await CreateAsync("B.Tech")).GetProperty("id").GetInt32();
        await CreateAsync("MBA");

        Assert.Equal(HttpStatusCode.NotFound, (await _admin.PutAsJsonAsync($"{Url}/{btech + 999}", Body("X"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.PutAsJsonAsync($"{Url}/{btech}", Body(""))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.PutAsJsonAsync($"{Url}/{btech}", Body(new string('x', 101)))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _admin.PutAsJsonAsync($"{Url}/{btech}", Body("mba"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _admin.PutAsJsonAsync($"{Url}/{btech}", Body("B.Tech"))).StatusCode);   // keeping its own name is fine
    }

    // ── Deactivation and protection ──

    [Fact]
    public async Task Delete_deactivates_with_a_message_keeps_the_row_is_idempotent_and_audited()
    {
        int id = (await CreateAsync("B.Tech")).GetProperty("id").GetInt32();

        HttpResponseMessage first = await _admin.DeleteAsync($"{Url}/{id}");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("Education qualification deactivated successfully.", (await Json(first)).GetProperty("message").GetString());

        HttpResponseMessage second = await _admin.DeleteAsync($"{Url}/{id}");
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal("Education qualification is already inactive.", (await Json(second)).GetProperty("message").GetString());

        Assert.False((await Json(await _admin.GetAsync($"{Url}/{id}"))).GetProperty("isActive").GetBoolean());
        Assert.Equal(1, await AuditCountAsync(id, "EducationQualificationDeactivated"));
        await using AppDbContext ctx = _factory.CreateContext();
        Assert.Equal(1, await ctx.EducationQualifications.CountAsync(e => e.Id == id));   // never hard-deleted
    }

    [Fact]
    public async Task Delete_unknown_qualification_returns_404_with_a_message()
    {
        HttpResponseMessage resp = await _admin.DeleteAsync($"{Url}/987654");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        Assert.Equal("Education qualification not found.", (await Json(resp)).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task A_qualification_used_by_personnel_cannot_be_deactivated_but_can_be_renamed()
    {
        int id = (await CreateAsync("B.Tech")).GetProperty("id").GetInt32();
        await UseByPersonnelAsync(id);

        HttpResponseMessage delete = await _admin.DeleteAsync($"{Url}/{id}");
        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
        Assert.Contains("cannot be deactivated", (await Json(delete)).GetProperty("detail").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await _admin.PutAsJsonAsync($"{Url}/{id}", Body("B.Tech", isActive: false))).StatusCode);

        JsonElement renamed = (await Json(await _admin.PutAsJsonAsync($"{Url}/{id}", Body("Bachelor of Technology")))).GetProperty("data");
        Assert.Equal("Bachelor of Technology", renamed.GetProperty("courseName").GetString());
        Assert.True(renamed.GetProperty("isActive").GetBoolean());
        Assert.Equal(0, await AuditCountAsync(id, "EducationQualificationDeactivated"));
        await using AppDbContext ctx = _factory.CreateContext();
        Assert.Equal(1, await ctx.UsersPis.CountAsync());   // the personnel record is untouched
    }

    [Fact]
    public async Task Deactivation_becomes_possible_once_no_personnel_use_the_qualification()
    {
        int id = (await CreateAsync("B.Tech")).GetProperty("id").GetInt32();
        await UseByPersonnelAsync(id);
        Assert.Equal(HttpStatusCode.Conflict, (await _admin.DeleteAsync($"{Url}/{id}")).StatusCode);

        await using (AppDbContext ctx = _factory.CreateContext())
            await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `users_pis`");

        Assert.Equal(HttpStatusCode.OK, (await _admin.DeleteAsync($"{Url}/{id}")).StatusCode);
    }

    // ── Schema (real MySQL, created by the migrations) ──

    [Fact]
    public async Task Table_has_exactly_the_agreed_columns_a_unique_course_name_and_no_foreign_keys()
    {
        await using AppDbContext ctx = _factory.CreateContext();

        async Task<List<string>> Query(string sql) => await ctx.Database.SqlQueryRaw<string>(sql).ToListAsync();

        List<string> columns = await Query("SELECT CONCAT(COLUMN_NAME,' ',COLUMN_TYPE,' ',IS_NULLABLE) AS `Value` FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='education_qualifications' ORDER BY ORDINAL_POSITION");
        Assert.Equal(new[] { "id int NO", "course_name varchar(100) NO", "is_active tinyint(1) NO", "created_at_ist datetime(6) NO" }, columns.ToArray());

        Assert.Contains("ix_education_qualifications_course_name", await Query("SELECT INDEX_NAME AS `Value` FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='education_qualifications' AND NON_UNIQUE=0"));
        Assert.Empty(await Query("SELECT CONSTRAINT_NAME AS `Value` FROM information_schema.KEY_COLUMN_USAGE WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='education_qualifications' AND REFERENCED_TABLE_NAME IS NOT NULL"));
    }
}
