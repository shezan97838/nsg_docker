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

/// <summary>Integration tests for /api/v1/users-pis against a real MySQL database.</summary>
[Collection("mysql")]
public class UsersPisApiTests : IAsyncLifetime
{
    private const string Url = "/api/v1/users-pis";
    private const int FirstTestSrNo = 9300;
    private const string Aadhaar = "123546789532", Pran = "321654987321", Account = "30927989329";

    private readonly ApiFactory _factory;
    private readonly HttpClient _admin;
    private readonly HttpClient _employee;
    private readonly HttpClient _anonymous;

    private int _rank, _unit, _otherUnit, _dept, _education, _religion, _state;

    public UsersPisApiTests(DatabaseFixture fixture)
    {
        _factory = fixture.Factory;
        _admin = _factory.CreateClientFor(DomainConstants.Roles.PisAdmin);
        _employee = _factory.CreateClientFor(DomainConstants.Roles.Employee);
        _anonymous = _factory.CreateClientFor(null);
    }

    public async Task InitializeAsync()
    {
        await CleanAsync();
        await using AppDbContext ctx = _factory.CreateContext();
        RankHierarchy rank = new() { SrNo = FirstTestSrNo, RankName = "PIS test rank", ShortCode = "PT" };
        NsgUnit unit = new() { Name = "13 SRG" }, other = new() { Name = "11 SRG" };
        ParentDepartmentRank dept = new() { Name = "Inspector(GD)" };
        EducationQualification education = new() { CourseName = "BA" };
        Religion religion = new() { Name = "HINDU" };
        State state = new() { Code = "99", Name = "Test State", AdministrativeType = AdministrativeUnitType.State, CreatedAtIst = IndianStandardTime.Now };
        ctx.AddRange(rank, unit, other, dept, education, religion, state);
        await ctx.SaveChangesAsync();
        (_rank, _unit, _otherUnit, _dept, _education, _religion, _state) = (rank.Id, unit.Id, other.Id, dept.Id, education.Id, religion.Id, state.Id);
    }

    public async Task DisposeAsync() => await CleanAsync();

    private async Task CleanAsync()
    {
        await _factory.ClearGeographyAsync(); // UsersPIS, Cities, States
        await using AppDbContext ctx = _factory.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `nsg_units`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `education_qualifications`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `religions`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `parent_department_ranks`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `audit_logs`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `rank_hierarchy` WHERE `sr_no` >= " + FirstTestSrNo);
    }

    // ── Helpers ──

    private Dictionary<string, object?> Minimal(string nsg = "N-1", string service = "S-1") => new() { ["nsgNumber"] = nsg, ["serviceNumber"] = service, ["gender"] = "Male" };

    private Dictionary<string, object?> Full(string nsg = "123456") => new()
    {
        ["nsgNumber"] = nsg, ["serviceNumber"] = "123456789", ["fullName"] = "Ranvir Singh", ["gender"] = "Female",
        ["nsgRankId"] = _rank, ["parentDepartmentRankId"] = _dept, ["nsgUnitId"] = _unit, ["educationId"] = _education,
        ["dateOfInductionInNsg"] = "2024-02-04", ["sideSteppedOn"] = "2025-01-10", ["sideSteppedFromNsgUnitId"] = _otherUnit,
        ["dateOfRepatriation"] = "2031-01-31", ["dateOfBirth"] = "1992-02-04", ["dateOfEnrollmentInGovtService"] = "2012-03-04",
        ["dateOfSuperannuation"] = "2052-02-29", ["category"] = "ST", ["religionId"] = _religion,
        ["fatherName"] = "SHRI MATHUR SINGH", ["motherName"] = "SMT BHAIRATHI DEVI", ["spouseName"] = "ASHA",
        ["nextOfKinMobileNumber"] = "9234567893", ["numberOfChildren"] = 0, ["mobileNumber"] = "6598741235",
        ["bloodGroup"] = "OPositive", ["heightCm"] = 172, ["weightKg"] = 81.96m,
        ["aadhaarNumber"] = Aadhaar, ["panNumber"] = "agipr3625f", ["pranNumber"] = Pran, ["salaryAccountNumber"] = Account,
        ["permanentVillage"] = "BAGAFA", ["permanentPostOffice"] = "SANTIR BAZAR", ["permanentPoliceStation"] = "BAGAFA",
        ["permanentDistrict"] = "SOUTH TRIPURA", ["permanentStateId"] = _state, ["permanentPinCode"] = "799144",
        ["dateOfAme"] = "2026-03-10", ["dateOfImmovablePropertyReturn"] = "2026-01-10",
        ["earnedLeaveDue"] = 220, ["halfPayLeaveDue"] = 360, ["paternityLeaveAvailed"] = "2 TIMES",
        ["maternityLeaveAvailed"] = "0", ["childCareLeaveAvailed"] = "0"
    };

    private async Task<JsonElement> CreateAsync(Dictionary<string, object?> body)
    {
        HttpResponseMessage r = await _admin.PostAsJsonAsync(Url, body);
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return await r.ReadDataAsync();
    }

    private static long IdOf(JsonElement e) => e.GetProperty("id").GetInt64();

    private async Task<JsonElement> GetAsync(long id)
        => await (await _admin.GetAsync($"{Url}/{id}")).ReadDataAsync();

    private async Task<int> CountAsync() { await using AppDbContext c = _factory.CreateContext(); return await c.UsersPis.CountAsync(); }

    private async Task<int> AuditCountAsync(long id, string operation)
    {
        await using AppDbContext c = _factory.CreateContext();
        List<AuditLog> logs = await c.AuditLogs.Where(a => a.EntityType == "UserPis" && a.EntityId == id.ToString()).ToListAsync();
        return logs.Count(a => a.AdditionalData != null && a.AdditionalData.Contains(operation));
    }

    // ── Authentication and authorization ──

    [Fact]
    public async Task Anonymous_requests_get_401_and_employees_get_403_on_every_endpoint()
    {
        object body = Minimal();
        foreach (HttpClient client in new[] { _anonymous, _employee })
        {
            HttpStatusCode expected = client == _anonymous ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
            Assert.Equal(expected, (await client.GetAsync(Url)).StatusCode);
            Assert.Equal(expected, (await client.GetAsync($"{Url}/1")).StatusCode);
            Assert.Equal(expected, (await client.PostAsJsonAsync(Url, body)).StatusCode);
            Assert.Equal(expected, (await client.PutAsJsonAsync($"{Url}/1", body)).StatusCode);
            Assert.Equal(expected, (await client.DeleteAsync($"{Url}/1")).StatusCode);
        }
        Assert.Equal(0, await CountAsync());
    }

    // ── Create ──

    [Fact]
    public async Task Create_minimal_record_returns_201_active_pending_with_server_managed_metadata()
    {
        HttpResponseMessage r = await _admin.PostAsJsonAsync(Url, Minimal());

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        JsonElement body = await r.ReadDataAsync();
        long id = IdOf(body);
        Assert.EndsWith($"{Url}/{id}", r.Headers.Location!.AbsolutePath);
        Assert.Equal("N-1", body.GetProperty("nsgNumber").GetString());
        Assert.Equal("Pending", body.GetProperty("mobileAppActivationStatus").GetString());
        Assert.True(body.GetProperty("isActive").GetBoolean());
        Assert.Equal(9001, body.GetProperty("createdBy").GetInt32());   // from the JWT, not the request
        Assert.Equal(JsonValueKind.Null, body.GetProperty("updatedAtIst").ValueKind);
        Assert.Equal(1, await AuditCountAsync(id, "UserPisCreated"));
    }

    [Fact]
    public async Task Create_full_record_stores_everything_returns_names_and_masks_sensitive_values()
    {
        HttpResponseMessage r = await _admin.PostAsJsonAsync(Url, Full());
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        string raw = await r.Content.ReadAsStringAsync();
        JsonElement body = JsonDocument.Parse(raw).RootElement.GetProperty("data");

        Assert.Equal("PIS test rank", body.GetProperty("nsgRankName").GetString());
        Assert.Equal("13 SRG", body.GetProperty("nsgUnitName").GetString());
        Assert.Equal("11 SRG", body.GetProperty("sideSteppedFromNsgUnitName").GetString());
        Assert.Equal("2025-01-10", body.GetProperty("sideSteppedOn").GetString());   // date and unit stored separately
        Assert.Equal("Inspector(GD)", body.GetProperty("parentDepartmentRankName").GetString());
        Assert.Equal("HINDU", body.GetProperty("religionName").GetString());
        Assert.Equal("Test State", body.GetProperty("permanentStateName").GetString());
        Assert.Equal("Female", body.GetProperty("gender").GetString());
        Assert.Equal("ST", body.GetProperty("category").GetString());
        Assert.Equal("OPositive", body.GetProperty("bloodGroup").GetString());
        Assert.Equal(172, body.GetProperty("heightCm").GetInt32());
        Assert.Equal(81.96m, body.GetProperty("weightKg").GetDecimal());

        Assert.Equal("XXXXXXXX9532", body.GetProperty("aadhaarNumberMasked").GetString());
        Assert.Equal("XXXXXX625F", body.GetProperty("panNumberMasked").GetString());   // upper-cased before masking
        Assert.Equal("XXXXXXXX7321", body.GetProperty("pranNumberMasked").GetString());
        Assert.Equal("XXXXXXX9329", body.GetProperty("salaryAccountNumberMasked").GetString());
        foreach (string secret in new[] { Aadhaar, Pran, Account, "AGIPR3625F", "agipr3625f" })
            Assert.DoesNotContain(secret, raw);
        Assert.DoesNotContain("Encrypted", raw);
        Assert.DoesNotContain("Hash", raw);

        await using AppDbContext ctx = _factory.CreateContext();
        UserPis stored = await ctx.UsersPis.AsNoTracking().SingleAsync();
        Assert.NotNull(stored.AadhaarNumberEncrypted);
        Assert.DoesNotContain(Aadhaar, stored.AadhaarNumberEncrypted);
        Assert.DoesNotContain(Account, stored.SalaryAccountNumberEncrypted);
        Assert.Matches("^[0-9a-f]{64}$", stored.PanNumberHash);
        Assert.Matches("^[0-9a-f]{64}$", stored.SalaryAccountNumberHash);
        Assert.Equal(new DateOnly(2025, 1, 10), stored.SideSteppedOn);
        Assert.Equal(_otherUnit, stored.SideSteppedFromNsgUnitId);
    }

    [Fact]
    public async Task Create_ignores_client_supplied_server_managed_fields()
    {
        Dictionary<string, object?> body = Minimal();
        body["id"] = 777; body["mobileAppActivationStatus"] = "Approved"; body["isActive"] = false;
        body["createdBy"] = 1; body["createdAtIst"] = "2000-01-01T00:00:00Z"; body["panNumberHash"] = new string('a', 64);

        JsonElement created = await CreateAsync(body);

        Assert.NotEqual(777, IdOf(created));
        Assert.Equal("Pending", created.GetProperty("mobileAppActivationStatus").GetString());
        Assert.True(created.GetProperty("isActive").GetBoolean());
        Assert.Equal(9001, created.GetProperty("createdBy").GetInt32());
        Assert.True(created.GetProperty("createdAtIst").GetDateTime().Year >= 2026);
        await using AppDbContext ctx = _factory.CreateContext();
        Assert.Null((await ctx.UsersPis.AsNoTracking().SingleAsync()).PanNumberHash);
    }

    [Fact]
    public async Task Whitespace_only_text_is_stored_as_null_and_text_is_trimmed_while_blank_mobile_numbers_are_rejected()
    {
        Dictionary<string, object?> a = Minimal("B-1");
        a["fatherName"] = "   "; a["fullName"] = "  Trimmed Name ";

        JsonElement first = await CreateAsync(a);

        Assert.Equal("Trimmed Name", first.GetProperty("fullName").GetString());
        Assert.Equal(JsonValueKind.Null, first.GetProperty("fatherName").ValueKind);

        // A mobile number is either a valid 10-digit number or absent (null); an empty string is a validation error.
        Dictionary<string, object?> blankMobile = Minimal("B-2"); blankMobile["mobileNumber"] = "";
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.PostAsJsonAsync(Url, blankMobile)).StatusCode);

        // Two records without a mobile number may coexist (NULLs do not collide in the unique index).
        await CreateAsync(Minimal("B-3"));
        Assert.Equal(2, await CountAsync());
    }


    [Fact]
    public async Task Same_service_number_is_allowed_for_different_people()
    {
        await CreateAsync(Minimal("SN-1", "COMMON"));
        await CreateAsync(Minimal("SN-2", "COMMON"));
        Assert.Equal(2, await CountAsync());
    }

    // ── Validation ──

    public static TheoryData<string, string, object?> InvalidFields => new()
    {
        { "missing nsg number", "nsgNumber", "" },
        { "untrimmed nsg number", "nsgNumber", " N-9 " },
        { "overlong nsg number", "nsgNumber", new string('x', 31) },
        { "missing service number", "serviceNumber", "" },
        { "mobile starts with 5", "mobileNumber", "5876543210" },
        { "mobile with country code", "mobileNumber", "+919876543210" },
        { "mobile nine digits", "mobileNumber", "987654321" },
        { "next of kin mobile", "nextOfKinMobileNumber", "123" },
        { "pan wrong format", "panNumber", "AGIP3625FF" },
        { "aadhaar eleven digits", "aadhaarNumber", "12354678953" },
        { "aadhaar with letters", "aadhaarNumber", "12354678953A" },
        { "pran wrong length", "pranNumber", "3216549873" },
        { "salary account too short", "salaryAccountNumber", "12345678" },
        { "salary account non digits", "salaryAccountNumber", "3092-7989-329" },
        { "height zero", "heightCm", 0 },
        { "height above 255", "heightCm", 256 },
        { "height fractional", "heightCm", 172.5m },
        { "weight zero", "weightKg", 0 },
        { "weight above decimal(5,2)", "weightKg", 1000 },
        { "weight three decimals", "weightKg", 81.961m },
        { "undefined category", "category", 99 },
        { "unknown category name", "category", "EWS" },
        { "undefined blood group", "bloodGroup", 99 },
        { "birth in the future", "dateOfBirth", "2999-01-01" },
        { "bad date", "dateOfAme", "31-02-2026" },
        { "pin starts with zero", "permanentPinCode", "099144" },
        { "negative children", "numberOfChildren", -1 },
        { "negative leave", "earnedLeaveDue", -1 },
        { "non positive reference id", "nsgRankId", 0 },
        { "overlong name", "fullName", "x" }
    };

    [Theory, MemberData(nameof(InvalidFields))]
    public async Task Create_rejects_invalid_fields_with_400_and_stores_nothing(string _, string field, object? value)
    {
        Dictionary<string, object?> body = Minimal();
        body[field] = field == "fullName" ? new string('x', 201) : value;

        HttpResponseMessage r = await _admin.PostAsJsonAsync(Url, body);

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal(0, await CountAsync());
    }

    [Fact]
    public async Task Date_relationships_are_validated_when_both_dates_are_present()
    {
        Dictionary<string, object?> enrolledBeforeBirth = Minimal("D-1");
        enrolledBeforeBirth["dateOfBirth"] = "1992-02-04"; enrolledBeforeBirth["dateOfEnrollmentInGovtService"] = "1990-01-01";
        Dictionary<string, object?> repatriatedBeforeInduction = Minimal("D-2");
        repatriatedBeforeInduction["dateOfInductionInNsg"] = "2024-02-04"; repatriatedBeforeInduction["dateOfRepatriation"] = "2023-01-01";

        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.PostAsJsonAsync(Url, enrolledBeforeBirth)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.PostAsJsonAsync(Url, repatriatedBeforeInduction)).StatusCode);
        Assert.Equal(0, await CountAsync());
    }

    [Fact]
    public async Task Validation_errors_never_echo_sensitive_values()
    {
        Dictionary<string, object?> body = Minimal();
        body["aadhaarNumber"] = "SECRET-AADHAAR"; body["panNumber"] = "SECRET-PAN"; body["pranNumber"] = "SECRET-PRAN"; body["salaryAccountNumber"] = "SECRET-ACCT";

        HttpResponseMessage r = await _admin.PostAsJsonAsync(Url, body);

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.DoesNotContain("SECRET", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Unknown_references_return_400_naming_the_fields_and_store_nothing()
    {
        Dictionary<string, object?> body = Minimal();
        body["nsgRankId"] = 987654; body["nsgUnitId"] = 987654; body["religionId"] = 987654; body["permanentStateId"] = 987654;

        HttpResponseMessage r = await _admin.PostAsJsonAsync(Url, body);

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        string text = await r.Content.ReadAsStringAsync();
        foreach (string field in new[] { "NsgRankId", "NsgUnitId", "ReligionId", "PermanentStateId" })
            Assert.Contains(field, text);
        Assert.Equal(0, await CountAsync());
    }

    // ── Duplicates ──

    [Fact]
    public async Task Duplicate_unique_identifiers_return_409_without_echoing_the_value()
    {
        await CreateAsync(Full("U-1"));

        Dictionary<string, object?> dupNsg = Minimal("U-1");
        Dictionary<string, object?> dupMobile = Minimal("U-2"); dupMobile["mobileNumber"] = "6598741235";
        Dictionary<string, object?> dupPan = Minimal("U-3"); dupPan["panNumber"] = "AGIPR3625F";            // stored upper-case, differently typed
        Dictionary<string, object?> dupAccount = Minimal("U-4"); dupAccount["salaryAccountNumber"] = Account;

        foreach ((Dictionary<string, object?> body, string expected, string secret) in new[]
                 {
                     (dupNsg, "NSG number", "U-1"), (dupMobile, "mobile number", "6598741235"),
                     (dupPan, "PAN", "AGIPR3625F"), (dupAccount, "salary account number", Account)
                 })
        {
            HttpResponseMessage r = await _admin.PostAsJsonAsync(Url, body);
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            string text = await r.Content.ReadAsStringAsync();
            Assert.Contains(expected, text);
            Assert.DoesNotContain(secret, text);
        }
        Assert.Equal(1, await CountAsync());
    }

    // ── Read ──

    [Fact]
    public async Task Get_by_id_returns_the_record_or_404()
    {
        long id = IdOf(await CreateAsync(Full()));

        HttpResponseMessage found = await _admin.GetAsync($"{Url}/{id}");
        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
        Assert.Equal("123456", (await found.ReadDataAsync()).GetProperty("nsgNumber").GetString());

        HttpResponseMessage missing = await _admin.GetAsync($"{Url}/{id + 9999}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(404, (int)missing.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.GetAsync($"{Url}/abc")).StatusCode);
    }

    [Fact]
    public async Task List_paginates_orders_by_nsg_number_and_exposes_no_sensitive_fields()
    {
        foreach (string n in new[] { "L-3", "L-1", "L-5", "L-2", "L-4" })
        {
            Dictionary<string, object?> b = Minimal(n); b["panNumber"] = "AGIPR36" + n[^1] + "5F"; b["nsgRankId"] = _rank;
            await CreateAsync(b);
        }

        HttpResponseMessage r = await _admin.GetAsync($"{Url}?pageNumber=1&pageSize=2");
        string raw = await r.Content.ReadAsStringAsync();
        JsonElement page = JsonDocument.Parse(raw).RootElement.GetProperty("data");

        Assert.Equal(5, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(3, page.GetProperty("totalPages").GetInt32());
        Assert.Equal(new[] { "L-1", "L-2" }, page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("nsgNumber").GetString()).ToArray());
        Assert.Equal("PIS test rank", page.GetProperty("items")[0].GetProperty("nsgRankName").GetString());
        Assert.DoesNotContain("Masked", raw);
        Assert.DoesNotContain("AGIPR", raw);
        Assert.Equal(1, JsonDocument.Parse(await (await _admin.GetAsync($"{Url}?pageNumber=3&pageSize=2")).Content.ReadAsStringAsync())
            .RootElement.GetProperty("data").GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task List_search_and_filters_work_and_include_inactive_records_when_asked()
    {
        Dictionary<string, object?> a = Minimal("F-1"); a["fullName"] = "Anil Kumar"; a["mobileNumber"] = "9000000001"; a["nsgRankId"] = _rank; a["nsgUnitId"] = _unit; a["category"] = "SC";
        Dictionary<string, object?> b = Minimal("F-2"); b["fullName"] = "Bhanu Pratap"; b["mobileNumber"] = "9000000002"; b["nsgUnitId"] = _otherUnit; b["parentDepartmentRankId"] = _dept; b["category"] = "GEN";
        long idA = IdOf(await CreateAsync(a));
        long idB = IdOf(await CreateAsync(b));
        Assert.Equal(HttpStatusCode.OK, (await _admin.DeleteAsync($"{Url}/{idB}")).StatusCode);   // B becomes inactive
        await using (AppDbContext ctx = _factory.CreateContext())
            await ctx.Database.ExecuteSqlRawAsync("UPDATE `users_pis` SET `mobile_app_activation_status` = 1 WHERE `id` = {0}", idA);

        async Task<string[]> Ids(string query)
            => (await (await _admin.GetAsync($"{Url}?{query}")).ReadDataAsync())
                .GetProperty("items").EnumerateArray().Select(i => i.GetProperty("nsgNumber").GetString()!).ToArray();

        Assert.Equal(new[] { "F-1", "F-2" }, await Ids(""));                       // inactive records are listed by default
        Assert.Equal(new[] { "F-1" }, await Ids("isActive=true"));
        Assert.Equal(new[] { "F-2" }, await Ids("isActive=false"));
        Assert.Equal(new[] { "F-1" }, await Ids("search=anil"));                  // name, case-insensitive
        Assert.Equal(new[] { "F-2" }, await Ids("search=F-2"));                   // NSG number
        Assert.Equal(new[] { "F-2" }, await Ids("search=900000000" + 2));         // mobile
        Assert.Equal(new[] { "F-1" }, await Ids($"nsgRankId={_rank}"));
        Assert.Equal(new[] { "F-2" }, await Ids($"nsgUnitId={_otherUnit}"));
        Assert.Equal(new[] { "F-2" }, await Ids($"parentDepartmentRankId={_dept}"));
        Assert.Equal(new[] { "F-1" }, await Ids("category=SC"));
        Assert.Equal(new[] { "F-1" }, await Ids("mobileAppActivationStatus=Approved"));
        Assert.Equal(new[] { "F-2" }, await Ids("mobileAppActivationStatus=Pending"));
        Assert.Empty(await Ids("search=nobody"));
    }

    // ── Update ──

    [Fact]
    public async Task Update_replaces_fields_but_preserves_nsg_number_activation_status_and_creation_metadata()
    {
        JsonElement created = await CreateAsync(Full());
        long id = IdOf(created);
        await using (AppDbContext ctx = _factory.CreateContext())
            await ctx.Database.ExecuteSqlRawAsync("UPDATE `users_pis` SET `mobile_app_activation_status` = 1 WHERE `id` = {0}", id);

        Dictionary<string, object?> update = new()
        {
            ["serviceNumber"] = "NEW-SVC", ["fullName"] = "Changed Name", ["isActive"] = true,
            ["nsgNumber"] = "HACKED", ["mobileAppActivationStatus"] = "Rejected", ["createdBy"] = 1   // must be ignored
        };
        HttpResponseMessage r = await _admin.PutAsJsonAsync($"{Url}/{id}", update);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        JsonElement body = await r.ReadDataAsync();
        Assert.Equal("123456", body.GetProperty("nsgNumber").GetString());
        Assert.Equal("NEW-SVC", body.GetProperty("serviceNumber").GetString());
        Assert.Equal("Changed Name", body.GetProperty("fullName").GetString());
        Assert.Equal("Approved", body.GetProperty("mobileAppActivationStatus").GetString());   // not reset by the update
        Assert.Equal(9001, body.GetProperty("createdBy").GetInt32());
        Assert.Equal(created.GetProperty("createdAtIst").GetDateTime(), body.GetProperty("createdAtIst").GetDateTime());
        Assert.Equal(9001, body.GetProperty("updatedBy").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, body.GetProperty("updatedAtIst").ValueKind);
        // Non-sensitive fields not sent are cleared (full replace) ...
        Assert.Equal(JsonValueKind.Null, body.GetProperty("mobileNumber").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("nsgUnitId").ValueKind);
        // ... but sensitive identifiers not sent are kept.
        Assert.Equal("XXXXXXXX9532", body.GetProperty("aadhaarNumberMasked").GetString());
        Assert.Equal("XXXXXX625F", body.GetProperty("panNumberMasked").GetString());
        Assert.Equal(1, await AuditCountAsync(id, "UserPisUpdated"));
    }

    [Fact]
    public async Task Update_replaces_sensitive_identifiers_only_when_supplied_and_rehashes_them()
    {
        long id = IdOf(await CreateAsync(Full()));
        string oldHash;
        await using (AppDbContext ctx = _factory.CreateContext())
            oldHash = (await ctx.UsersPis.AsNoTracking().SingleAsync()).PanNumberHash!;

        Dictionary<string, object?> update = new() { ["serviceNumber"] = "S", ["isActive"] = true, ["panNumber"] = "ABCDE1234F", ["salaryAccountNumber"] = "123456789012" };
        JsonElement body = await (await _admin.PutAsJsonAsync($"{Url}/{id}", update)).ReadDataAsync();

        Assert.Equal("XXXXXX234F", body.GetProperty("panNumberMasked").GetString());
        Assert.Equal("XXXXXXXX9012", body.GetProperty("salaryAccountNumberMasked").GetString());
        Assert.Equal("XXXXXXXX9532", body.GetProperty("aadhaarNumberMasked").GetString());   // untouched
        await using AppDbContext verify = _factory.CreateContext();
        UserPis stored = await verify.UsersPis.AsNoTracking().SingleAsync();
        Assert.NotEqual(oldHash, stored.PanNumberHash);
        Assert.Matches("^[0-9a-f]{64}$", stored.PanNumberHash);
    }

    [Fact]
    public async Task Update_keeping_own_unique_values_succeeds_but_taking_another_records_values_returns_409()
    {
        long idA = IdOf(await CreateAsync(Full("A-1")));
        Dictionary<string, object?> b = Minimal("A-2"); b["mobileNumber"] = "9000000009"; b["panNumber"] = "ZZZZZ9999Z";
        long idB = IdOf(await CreateAsync(b));

        // A re-sends its own mobile and PAN: fine.
        Dictionary<string, object?> same = new() { ["serviceNumber"] = "S", ["isActive"] = true, ["mobileNumber"] = "6598741235", ["panNumber"] = "AGIPR3625F" };
        Assert.Equal(HttpStatusCode.OK, (await _admin.PutAsJsonAsync($"{Url}/{idA}", same)).StatusCode);

        // B tries to take A's mobile, then A's PAN, then A's account number.
        foreach ((string field, string value) in new[] { ("mobileNumber", "6598741235"), ("panNumber", "agipr3625f"), ("salaryAccountNumber", Account) })
        {
            Dictionary<string, object?> steal = new() { ["serviceNumber"] = "S", ["isActive"] = true, [field] = value };
            HttpResponseMessage r = await _admin.PutAsJsonAsync($"{Url}/{idB}", steal);
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.DoesNotContain(value, await r.Content.ReadAsStringAsync());
        }
        Assert.Equal("9000000009", (await GetAsync(idB)).GetProperty("mobileNumber").GetString());   // unchanged
    }

    [Fact]
    public async Task Update_unknown_id_is_404_invalid_body_is_400_and_unknown_reference_is_400()
    {
        long id = IdOf(await CreateAsync(Minimal()));
        Dictionary<string, object?> ok = new() { ["serviceNumber"] = "S", ["isActive"] = true };

        Assert.Equal(HttpStatusCode.NotFound, (await _admin.PutAsJsonAsync($"{Url}/{id + 9999}", ok)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.PutAsJsonAsync($"{Url}/{id}", new Dictionary<string, object?> { ["serviceNumber"] = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.PutAsJsonAsync($"{Url}/{id}", new Dictionary<string, object?> { ["serviceNumber"] = "S", ["mobileNumber"] = "123" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.PutAsJsonAsync($"{Url}/{id}", new Dictionary<string, object?> { ["serviceNumber"] = "S", ["religionId"] = 987654 })).StatusCode);
        Assert.Equal("N-1", (await GetAsync(id)).GetProperty("nsgNumber").GetString());
    }

    // ── Gender ──

    [Fact]
    public async Task Create_requires_a_valid_gender_and_accepts_exactly_Male_Female_Other()
    {
        foreach (object? bad in new object?[] { null, "", "Robot", "male ", 0, 4, 99 })
        {
            Dictionary<string, object?> body = Minimal("G-BAD"); body["gender"] = bad;
            Assert.Equal(HttpStatusCode.BadRequest, (await _admin.PostAsJsonAsync(Url, body)).StatusCode);
        }
        Dictionary<string, object?> missing = Minimal("G-BAD"); missing.Remove("gender");
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.PostAsJsonAsync(Url, missing)).StatusCode);
        Assert.Equal(0, await CountAsync());

        foreach (string gender in new[] { "Male", "Female", "Other" })
        {
            Dictionary<string, object?> body = Minimal("G-" + gender); body["gender"] = gender;
            Assert.Equal(gender, (await CreateAsync(body)).GetProperty("gender").GetString());
        }
    }

    [Fact]
    public async Task Update_changes_gender_when_supplied_and_keeps_it_when_omitted_or_invalid()
    {
        long id = IdOf(await CreateAsync(Minimal("G-U")));   // Male

        Dictionary<string, object?> omitted = new() { ["serviceNumber"] = "S", ["isActive"] = true };
        Assert.Equal("Male", (await (await _admin.PutAsJsonAsync($"{Url}/{id}", omitted)).ReadDataAsync()).GetProperty("gender").GetString());

        Dictionary<string, object?> invalid = new() { ["serviceNumber"] = "S", ["isActive"] = true, ["gender"] = "Robot" };
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.PutAsJsonAsync($"{Url}/{id}", invalid)).StatusCode);
        Assert.Equal("Male", (await GetAsync(id)).GetProperty("gender").GetString());

        Dictionary<string, object?> change = new() { ["serviceNumber"] = "S", ["isActive"] = true, ["gender"] = "Other" };
        JsonElement updated = await (await _admin.PutAsJsonAsync($"{Url}/{id}", change)).ReadDataAsync();
        Assert.Equal("Other", updated.GetProperty("gender").GetString());
        Assert.Equal("Other", (await GetAsync(id)).GetProperty("gender").GetString());
    }

    [Fact]
    public async Task Records_that_predate_gender_return_null_from_list_and_get_by_id()
    {
        await using (AppDbContext ctx = _factory.CreateContext())
            await ctx.Database.ExecuteSqlRawAsync("INSERT INTO `users_pis` (`nsg_number`,`service_number`,`is_active`,`created_at_ist`) VALUES ('OLD-1','S',1,NOW(6))");
        long id;
        await using (AppDbContext ctx = _factory.CreateContext())
            id = (await ctx.UsersPis.AsNoTracking().SingleAsync()).Id;

        Assert.Equal(JsonValueKind.Null, (await GetAsync(id)).GetProperty("gender").ValueKind);
        JsonElement page = await (await _admin.GetAsync(Url)).ReadDataAsync();
        Assert.Equal(JsonValueKind.Null, page.GetProperty("items")[0].GetProperty("gender").ValueKind);
    }

    // ── Deactivate ──

    [Fact]
    public async Task Delete_deactivates_keeps_the_row_is_idempotent_audited_and_reversible_by_update()
    {
        long id = IdOf(await CreateAsync(Full()));

        Assert.Equal(HttpStatusCode.OK, (await _admin.DeleteAsync($"{Url}/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _admin.DeleteAsync($"{Url}/{id}")).StatusCode);   // idempotent

        JsonElement after = await GetAsync(id);
        Assert.False(after.GetProperty("isActive").GetBoolean());
        Assert.Equal(9001, after.GetProperty("updatedBy").GetInt32());
        Assert.Equal("XXXXXXXX9532", after.GetProperty("aadhaarNumberMasked").GetString());   // data intact
        Assert.Equal(1, await CountAsync());
        Assert.Equal(1, await AuditCountAsync(id, "UserPisDeactivated"));

        Dictionary<string, object?> reactivate = new() { ["serviceNumber"] = "S", ["isActive"] = true };
        Assert.True((await (await _admin.PutAsJsonAsync($"{Url}/{id}", reactivate)).ReadDataAsync()).GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task Delete_unknown_id_returns_404()
        => Assert.Equal(HttpStatusCode.NotFound, (await _admin.DeleteAsync($"{Url}/424242")).StatusCode);

    // ── Side-stepping and audit ──

    [Fact]
    public async Task Side_stepping_date_and_unit_can_each_be_stored_without_the_other()
    {
        Dictionary<string, object?> dateOnly = Minimal("SS-1"); dateOnly["sideSteppedOn"] = "2025-01-10";
        Dictionary<string, object?> unitOnly = Minimal("SS-2"); unitOnly["sideSteppedFromNsgUnitId"] = _otherUnit;

        JsonElement a = await CreateAsync(dateOnly);
        JsonElement b = await CreateAsync(unitOnly);

        Assert.Equal(JsonValueKind.Null, a.GetProperty("sideSteppedFromNsgUnitId").ValueKind);
        Assert.Equal("2025-01-10", a.GetProperty("sideSteppedOn").GetString());
        Assert.Equal(JsonValueKind.Null, b.GetProperty("sideSteppedOn").ValueKind);
        Assert.Equal(_otherUnit, b.GetProperty("sideSteppedFromNsgUnitId").GetInt32());
    }

    [Fact]
    public async Task Audit_entries_record_only_the_operation_and_never_personnel_data()
    {
        long id = IdOf(await CreateAsync(Full()));
        await _admin.PutAsJsonAsync($"{Url}/{id}", new Dictionary<string, object?> { ["serviceNumber"] = "S", ["isActive"] = true, ["panNumber"] = "ABCDE1234F" });
        await _admin.DeleteAsync($"{Url}/{id}");

        await using AppDbContext ctx = _factory.CreateContext();
        List<AuditLog> logs = await ctx.AuditLogs.AsNoTracking().Where(a => a.EntityType == "UserPis").ToListAsync();

        Assert.Equal(3, logs.Count);
        Assert.All(logs, l =>
        {
            Assert.Equal(AuditAction.AdminAction, l.Action);
            Assert.Equal(9001, l.UserId);
            Assert.Equal(id.ToString(), l.EntityId);
            foreach (string secret in new[] { Aadhaar, Pran, Account, "AGIPR3625F", "ABCDE1234F", "Ranvir", "6598741235", "123456" })
                Assert.DoesNotContain(secret, l.AdditionalData ?? string.Empty);
        });
    }
}
