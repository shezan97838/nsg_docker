using System.Net;
using System.Net.Http.Headers;
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

    // Real Admin Portal sessions (password + OTP sign-in) from ApprovalAdminSession: PIS Admin A (_admin) and B (_adminB) own what they create;
    // the Super Admin and the Registration Admin are authenticated admins of the WRONG role for these endpoints.
    private HttpClient _admin = null!, _adminB = null!, _superAdmin = null!, _registrationAdmin = null!;
    private long _adminId, _adminBId;

    private readonly HttpClient _employee;
    private readonly HttpClient _legacyPisAdmin;   // a legacy staff Bearer token that merely carries the PisAdmin role: not an Admin Portal session
    private readonly HttpClient _anonymous;

    private int _rank, _unit, _otherUnit, _dept, _education, _religion, _state;

    public UsersPisApiTests(DatabaseFixture fixture)
    {
        _factory = fixture.Factory;
        _legacyPisAdmin = _factory.CreateClientFor(DomainConstants.Roles.PisAdmin);
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

        ApprovalAdmins admins = await ApprovalAdminSession.CreateAsync(_factory);
        (_admin, _adminId, _adminB, _adminBId, _superAdmin, _registrationAdmin) = (admins.PisA, admins.PisAId, admins.PisB, admins.PisBId, admins.Super, admins.Registration);

        // ApprovalAdminSession seeds four personnel rows (RAS-0001..0004) only to provision and activate the admin accounts. Once the sessions exist
        // nothing in this class needs them (token validation reads admin_profiles/admin_refresh_tokens), and they would otherwise be counted by
        // every row-count and list assertion below, so they are removed to keep each test's data exactly its own.
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `users_pis` WHERE `nsg_number` LIKE 'RAS-%'");
    }

    public async Task DisposeAsync() => await CleanAsync();

    private async Task CleanAsync()
    {
        await _factory.ClearGeographyAsync(); // UsersPIS, Cities, States
        await using AppDbContext ctx = _factory.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `admin_refresh_tokens`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `admin_profiles`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `otp_requests`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `nsg_units`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `education_qualifications`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `religions`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `parent_department_ranks`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `audit_logs`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `rank_hierarchy` WHERE `sr_no` >= " + FirstTestSrNo);
    }

    // ── Helpers ──

    private Dictionary<string, object?> Minimal(string nsg = "N-1", string service = "S-1") => new() { ["nsgNumber"] = nsg, ["serviceNumber"] = service, ["gender"] = "Male", ["mobileNumber"] = TestPisData.Mobile() };

    private Dictionary<string, object?> Full(string nsg = "123456") => new()
    {
        ["nsgNumber"] = nsg, ["serviceNumber"] = "123456789", ["fullName"] = "Ranvir Singh", ["gender"] = "Female",
        ["nsgRank"] = "PIS test rank", ["parentDepartmentRank"] = "Inspector(GD)", ["nsgUnit"] = "13 SRG", ["education"] = "BA",
        ["dateOfInductionInNsg"] = "2024-02-04", ["sideSteppedDetails"] = "10.01.2025 FROM 11 SRG",
        ["dateOfRepatriation"] = "2031-01-31", ["dateOfBirth"] = "1992-02-04", ["dateOfEnrollmentInGovtService"] = "2012-03-04",
        ["dateOfSuperannuation"] = "2052-02-29", ["category"] = "ST", ["religion"] = "HINDU",
        ["fatherName"] = "SHRI MATHUR SINGH", ["motherName"] = "SMT BHAIRATHI DEVI", ["spouseName"] = "ASHA",
        ["nextOfKinMobileNumber"] = "9234567893", ["numberOfChildren"] = 0, ["mobileNumber"] = "6598741235",
        ["bloodGroup"] = "O+", ["heightCm"] = 172, ["weightKg"] = 81.96m,
        ["aadhaarNumber"] = Aadhaar, ["panNumber"] = "agipr3625f", ["pranNumber"] = Pran, ["salaryAccountNumber"] = Account,
        ["permanentVillage"] = "BAGAFA", ["permanentPostOffice"] = "SANTIR BAZAR", ["permanentPoliceStation"] = "BAGAFA",
        ["permanentDistrict"] = "SOUTH TRIPURA", ["permanentState"] = "Test State", ["permanentPinCode"] = "799144",
        ["dateOfAme"] = "2026-03-10", ["dateOfImmovablePropertyReturn"] = "2026-01-10",
        ["earnedLeaveDue"] = 220, ["halfPayLeaveDue"] = 360, ["paternityLeaveAvailed"] = "2 TIMES",
        ["maternityLeaveAvailed"] = "0", ["childCareLeaveAvailed"] = "0"
    };

    private async Task<JsonElement> CreateAsync(Dictionary<string, object?> body, HttpClient? client = null)
    {
        HttpResponseMessage r = await (client ?? _admin).PostAsJsonAsync(Url, body);
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
    public async Task Only_a_signed_in_PIS_admin_portal_session_reaches_the_protected_endpoints()
    {
        object body = Minimal();

        // Anonymous, employee and legacy staff tokens are not Admin Portal sessions: the wrong kind of token is 401 on every protected endpoint,
        // including the list and the reads, even when the legacy token carries the PisAdmin role.
        foreach (HttpClient client in new[] { _anonymous, _employee, _legacyPisAdmin })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Url)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"{Url}/1")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(Url, body)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PutAsJsonAsync($"{Url}/1", body)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.DeleteAsync($"{Url}/1")).StatusCode);
        }

        // An authenticated Admin Portal admin of another role is 403 (Super Admin and Registration Admin get no PIS read or write access).
        foreach (HttpClient client in new[] { _superAdmin, _registrationAdmin })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Url)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"{Url}/1")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(Url, body)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"{Url}/1", body)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"{Url}/1")).StatusCode);
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
        Assert.Equal((int)_adminId, body.GetProperty("createdBy").GetInt32());   // the signed-in admin's profile id from the JWT, not the request
        Assert.Equal(JsonValueKind.Null, body.GetProperty("updatedAtIst").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("updatedBy").ValueKind);
        Assert.Equal(1, await AuditCountAsync(id, "UserPisCreated"));

        await using AppDbContext ctx = _factory.CreateContext();
        Assert.Equal(_adminId, (await ctx.UsersPis.AsNoTracking().SingleAsync(p => p.Id == id)).AssignedTo);   // the creator owns the new record
    }

    [Fact]
    public async Task Create_full_record_stores_everything_returns_names_and_masks_sensitive_values()
    {
        HttpResponseMessage r = await _admin.PostAsJsonAsync(Url, Full());
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        string raw = await r.Content.ReadAsStringAsync();
        JsonElement body = JsonDocument.Parse(raw).RootElement.GetProperty("data");

        Assert.Equal("PIS test rank", body.GetProperty("nsgRank").GetString());
        Assert.Equal("13 SRG", body.GetProperty("nsgUnit").GetString());
        Assert.Equal("10.01.2025 FROM 11 SRG", body.GetProperty("sideSteppedDetails").GetString());   // one text value, never split
        Assert.Equal("Inspector(GD)", body.GetProperty("parentDepartmentRank").GetString());
        Assert.Equal("BA", body.GetProperty("education").GetString());
        Assert.Equal("HINDU", body.GetProperty("religion").GetString());
        Assert.Equal("Test State", body.GetProperty("permanentState").GetString());
        foreach (string gone in new[] { "nsgRankId", "nsgUnitId", "educationId", "religionId", "permanentStateId", "sideSteppedOn", "sideSteppedFromNsgUnitId", "nsgRankName" })
            Assert.False(body.TryGetProperty(gone, out _), gone + " must not be in the response any more");        Assert.Equal("Female", body.GetProperty("gender").GetString());
        Assert.Equal("ST", body.GetProperty("category").GetString());
        Assert.Equal("O+", body.GetProperty("bloodGroup").GetString());          // free text, stored as sent
        Assert.Equal("172", body.GetProperty("heightCm").GetString());           // numbers are accepted and kept as text
        Assert.Equal("81.96", body.GetProperty("weightKg").GetString());
        Assert.Equal("0", body.GetProperty("numberOfChildren").GetString());
        Assert.Equal("220", body.GetProperty("earnedLeaveDue").GetString());

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
        Assert.Equal("10.01.2025 FROM 11 SRG", stored.SideSteppedDetails);
        Assert.Equal("PIS test rank", stored.NsgRank);       // the master's name is stored as text
        Assert.Equal("13 SRG", stored.NsgUnit);
        Assert.Equal("Female", stored.Gender);
        Assert.Equal("ST", stored.Category);
        Assert.Equal("172", stored.HeightCm);
    }

    [Fact]
    public async Task Create_ignores_client_supplied_server_managed_fields()
    {
        Dictionary<string, object?> body = Minimal();
        body["id"] = 777; body["mobileAppActivationStatus"] = "Approved"; body["isActive"] = false;
        body["createdBy"] = 1; body["createdAtIst"] = "2000-01-01T00:00:00Z";

        JsonElement created = await CreateAsync(body);

        Assert.NotEqual(777, IdOf(created));
        Assert.Equal("Pending", created.GetProperty("mobileAppActivationStatus").GetString());
        Assert.True(created.GetProperty("isActive").GetBoolean());
        Assert.Equal((int)_adminId, created.GetProperty("createdBy").GetInt32());   // the request's createdBy = 1 is ignored
        Assert.True(created.GetProperty("createdAtIst").GetDateTime().Year >= 2026);
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
        { "pan wrong format", "panNumber", "AGIP3625FF" },
        { "aadhaar eleven digits", "aadhaarNumber", "12354678953" },
        { "aadhaar with letters", "aadhaarNumber", "12354678953A" },
        { "pran wrong length", "pranNumber", "3216549873" },
        { "salary account too short", "salaryAccountNumber", "12345678" },
        { "salary account non digits", "salaryAccountNumber", "3092-7989-329" },
        { "category sent as a number", "category", 99 },
        { "blood group sent as a number", "bloodGroup", 99 },
        { "overlong height text", "heightCm", "9999999999999999999999999999999999999999999999999999" },
        { "overlong rank text", "nsgRank", "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx" },
        { "birth in the future", "dateOfBirth", "2999-01-01" },
        { "bad date", "dateOfAme", "31-02-2026" },
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
    public async Task Out_of_order_dates_are_accepted_because_the_source_workbook_contains_them()
    {
        Dictionary<string, object?> enrolledBeforeBirth = Minimal("D-1");
        enrolledBeforeBirth["dateOfBirth"] = "1992-02-04"; enrolledBeforeBirth["dateOfEnrollmentInGovtService"] = "1990-01-01";
        Dictionary<string, object?> repatriatedBeforeInduction = Minimal("D-2");
        repatriatedBeforeInduction["dateOfInductionInNsg"] = "2024-02-04"; repatriatedBeforeInduction["dateOfRepatriation"] = "2023-01-01";
        Dictionary<string, object?> retiredBeforeBirth = Minimal("D-3");
        retiredBeforeBirth["dateOfBirth"] = "1990-05-01"; retiredBeforeBirth["dateOfSuperannuation"] = "1941-02-28";

        Assert.Equal(HttpStatusCode.Created, (await _admin.PostAsJsonAsync(Url, enrolledBeforeBirth)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await _admin.PostAsJsonAsync(Url, repatriatedBeforeInduction)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await _admin.PostAsJsonAsync(Url, retiredBeforeBirth)).StatusCode);
        Assert.Equal(3, await CountAsync());
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
    public async Task Lookup_text_is_stored_without_any_master_table_check()
    {
        Dictionary<string, object?> body = Minimal();
        body["nsgRank"] = "Rank that exists in no master"; body["nsgUnit"] = "Unit that exists in no master";
        body["religion"] = "Any religion text"; body["permanentState"] = "Any state text";

        JsonElement created = await CreateAsync(body);

        Assert.Equal("Rank that exists in no master", created.GetProperty("nsgRank").GetString());
        Assert.Equal("Any state text", created.GetProperty("permanentState").GetString());
    }

    // ── Duplicates ──

    [Fact]
    public async Task Duplicate_unique_identifiers_return_409_without_echoing_the_value()
    {
        await CreateAsync(Full("U-1"));

        Dictionary<string, object?> dupNsg = Minimal("U-1");
        Dictionary<string, object?> dupMobile = Minimal("U-2"); dupMobile["mobileNumber"] = "6598741235";

        foreach ((Dictionary<string, object?> body, string expected, string secret) in new[]
                 {
                     (dupNsg, "NSG number", "U-1"), (dupMobile, "mobile number", "6598741235")
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

    [Fact]
    public async Task The_same_pan_and_salary_account_may_be_stored_for_different_people()
    {
        await CreateAsync(Full("P-1"));
        Dictionary<string, object?> other = Minimal("P-2"); other["panNumber"] = "agipr3625f"; other["salaryAccountNumber"] = Account;

        Assert.Equal(HttpStatusCode.Created, (await _admin.PostAsJsonAsync(Url, other)).StatusCode);   // no unique constraint on the encrypted identifiers
        Assert.Equal(2, await CountAsync());
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
            Dictionary<string, object?> b = Minimal(n); b["panNumber"] = "AGIPR36" + n[^1] + "5F"; b["nsgRank"] = "PIS test rank";
            await CreateAsync(b);
        }

        HttpResponseMessage r = await _admin.GetAsync($"{Url}?pageNumber=1&pageSize=2");
        string raw = await r.Content.ReadAsStringAsync();
        JsonElement page = JsonDocument.Parse(raw).RootElement.GetProperty("data");

        Assert.Equal(5, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(3, page.GetProperty("totalPages").GetInt32());
        Assert.Equal(new[] { "L-1", "L-2" }, page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("nsgNumber").GetString()).ToArray());
        Assert.Equal("PIS test rank", page.GetProperty("items")[0].GetProperty("nsgRank").GetString());
        Assert.DoesNotContain("Masked", raw);
        Assert.DoesNotContain("AGIPR", raw);
        Assert.Equal(1, JsonDocument.Parse(await (await _admin.GetAsync($"{Url}?pageNumber=3&pageSize=2")).Content.ReadAsStringAsync())
            .RootElement.GetProperty("data").GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task List_search_and_filters_work_and_include_inactive_records_when_asked()
    {
        Dictionary<string, object?> a = Minimal("F-1"); a["fullName"] = "Anil Kumar"; a["mobileNumber"] = "9000000001"; a["nsgRank"] = "PIS test rank"; a["nsgUnit"] = "13 SRG"; a["category"] = "SC";
        Dictionary<string, object?> b = Minimal("F-2"); b["fullName"] = "Bhanu Pratap"; b["mobileNumber"] = "9000000002"; b["nsgUnit"] = "11 SRG"; b["parentDepartmentRank"] = "Inspector(GD)"; b["category"] = "GEN";
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
        Assert.Equal(new[] { "F-1" }, await Ids("nsgRank=" + Uri.EscapeDataString("PIS test rank")));
        Assert.Equal(new[] { "F-2" }, await Ids("nsgUnit=" + Uri.EscapeDataString("11 SRG")));
        Assert.Equal(new[] { "F-2" }, await Ids("parentDepartmentRank=" + Uri.EscapeDataString("Inspector(GD)")));
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
        Assert.Equal((int)_adminId, body.GetProperty("createdBy").GetInt32());   // never overwritten by an update
        Assert.Equal(created.GetProperty("createdAtIst").GetDateTime(), body.GetProperty("createdAtIst").GetDateTime());
        Assert.Equal((int)_adminId, body.GetProperty("updatedBy").GetInt32());   // the updating admin
        Assert.NotEqual(JsonValueKind.Null, body.GetProperty("updatedAtIst").ValueKind);
        // Non-sensitive fields not sent are cleared (full replace) ...
        Assert.False(string.IsNullOrEmpty(body.GetProperty("mobileNumber").GetString()));   // mandatory: kept when omitted
        Assert.Equal(JsonValueKind.Null, body.GetProperty("nsgUnit").ValueKind);
        // ... but sensitive identifiers not sent are kept.
        Assert.Equal("XXXXXXXX9532", body.GetProperty("aadhaarNumberMasked").GetString());
        Assert.Equal("XXXXXX625F", body.GetProperty("panNumberMasked").GetString());
        Assert.Equal(1, await AuditCountAsync(id, "UserPisUpdated"));
    }

    [Fact]
    public async Task Update_replaces_sensitive_identifiers_only_when_supplied()
    {
        long id = IdOf(await CreateAsync(Full()));
        string oldCipher;
        await using (AppDbContext ctx = _factory.CreateContext())
            oldCipher = (await ctx.UsersPis.AsNoTracking().SingleAsync()).PanNumberEncrypted!;

        Dictionary<string, object?> update = new() { ["serviceNumber"] = "S", ["isActive"] = true, ["panNumber"] = "ABCDE1234F", ["salaryAccountNumber"] = "123456789012" };
        JsonElement body = await (await _admin.PutAsJsonAsync($"{Url}/{id}", update)).ReadDataAsync();

        Assert.Equal("XXXXXX234F", body.GetProperty("panNumberMasked").GetString());
        Assert.Equal("XXXXXXXX9012", body.GetProperty("salaryAccountNumberMasked").GetString());
        Assert.Equal("XXXXXXXX9532", body.GetProperty("aadhaarNumberMasked").GetString());   // untouched
        await using AppDbContext verify = _factory.CreateContext();
        UserPis stored = await verify.UsersPis.AsNoTracking().SingleAsync();
        Assert.NotEqual(oldCipher, stored.PanNumberEncrypted);
    }

    [Fact]
    public async Task Update_keeping_own_unique_mobile_succeeds_but_taking_another_records_mobile_returns_409()
    {
        long idA = IdOf(await CreateAsync(Full("A-1")));
        Dictionary<string, object?> b = Minimal("A-2"); b["mobileNumber"] = "9000000009";
        long idB = IdOf(await CreateAsync(b));

        // A re-sends its own mobile: fine.
        Dictionary<string, object?> same = new() { ["serviceNumber"] = "S", ["isActive"] = true, ["mobileNumber"] = "6598741235", ["panNumber"] = "AGIPR3625F" };
        Assert.Equal(HttpStatusCode.OK, (await _admin.PutAsJsonAsync($"{Url}/{idA}", same)).StatusCode);

        // B tries to take A's mobile.
        foreach ((string field, string value) in new[] { ("mobileNumber", "6598741235") })
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
        Assert.Equal("N-1", (await GetAsync(id)).GetProperty("nsgNumber").GetString());
    }

    // ── Gender ──

    [Fact]
    public async Task Create_requires_a_gender_but_accepts_any_text_for_gender_category_and_blood_group()
    {
        foreach (object? bad in new object?[] { null, "", "   ", 0, 4, 99 })   // missing, blank, or not text
        {
            Dictionary<string, object?> body = Minimal("G-BAD"); body["gender"] = bad;
            Assert.Equal(HttpStatusCode.BadRequest, (await _admin.PostAsJsonAsync(Url, body)).StatusCode);
        }
        Dictionary<string, object?> missing = Minimal("G-BAD"); missing.Remove("gender");
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.PostAsJsonAsync(Url, missing)).StatusCode);
        Assert.Equal(0, await CountAsync());

        foreach (string gender in new[] { "Male", "Female", "Other", "M", "Transgender", "as the workbook writes it" })
        {
            Dictionary<string, object?> body = Minimal("G-" + gender.Length + gender[0]); body["gender"] = gender;
            body["category"] = "General"; body["bloodGroup"] = "O POSITIVE";
            JsonElement created = await CreateAsync(body);
            Assert.Equal(gender, created.GetProperty("gender").GetString());      // stored exactly as sent
            Assert.Equal("General", created.GetProperty("category").GetString());
            Assert.Equal("O POSITIVE", created.GetProperty("bloodGroup").GetString());
        }
    }
    [Fact]
    public async Task Update_changes_gender_when_supplied_and_keeps_it_when_omitted_or_too_long()
    {
        long id = IdOf(await CreateAsync(Minimal("G-U")));   // Male

        Dictionary<string, object?> omitted = new() { ["serviceNumber"] = "S", ["isActive"] = true };
        Assert.Equal("Male", (await (await _admin.PutAsJsonAsync($"{Url}/{id}", omitted)).ReadDataAsync()).GetProperty("gender").GetString());

        Dictionary<string, object?> tooLong = new() { ["serviceNumber"] = "S", ["isActive"] = true, ["gender"] = new string('x', 31) };
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.PutAsJsonAsync($"{Url}/{id}", tooLong)).StatusCode);
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
            await ctx.Database.ExecuteSqlRawAsync("INSERT INTO `users_pis` (`nsg_number`,`service_number`,`mobile_number`,`is_active`,`created_at_ist`,`assigned_to`) VALUES ('OLD-1','S','9876500201',1,NOW(6),{0})", _adminId);   // owned by the reader: a record assigned to nobody is invisible to PIS Admins
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
        Assert.Equal((int)_adminId, after.GetProperty("updatedBy").GetInt32());
        Assert.Equal((int)_adminId, after.GetProperty("createdBy").GetInt32());   // deactivation never touches the creator
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
    public async Task Side_stepped_details_are_one_free_text_value_stored_and_returned_as_supplied()
    {
        Dictionary<string, object?> withText = Minimal("SS-3"); withText["sideSteppedDetails"] = "  10.01.2025 FROM 11 SRG (verbal order)  ";
        Dictionary<string, object?> without = Minimal("SS-4");

        JsonElement a = await CreateAsync(withText);
        JsonElement b = await CreateAsync(without);

        Assert.Equal("10.01.2025 FROM 11 SRG (verbal order)", a.GetProperty("sideSteppedDetails").GetString());   // trimmed, never parsed
        Assert.Equal(JsonValueKind.Null, b.GetProperty("sideSteppedDetails").ValueKind);
    }

    [Fact]
    public async Task Numeric_looking_text_keeps_leading_and_trailing_zeros_through_create_and_update()
    {
        Dictionary<string, object?> body = Minimal("NUM-1");
        body["numberOfChildren"] = "02"; body["weightKg"] = "81.50"; body["earnedLeaveDue"] = "00220"; body["halfPayLeaveDue"] = "360 DAYS"; body["permanentPinCode"] = "799144";
        long id = IdOf(await CreateAsync(body));

        JsonElement created = await GetAsync(id);
        Assert.Equal("02", created.GetProperty("numberOfChildren").GetString());
        Assert.Equal("81.50", created.GetProperty("weightKg").GetString());
        Assert.Equal("00220", created.GetProperty("earnedLeaveDue").GetString());
        Assert.Equal("360 DAYS", created.GetProperty("halfPayLeaveDue").GetString());

        Dictionary<string, object?> update = new() { ["serviceNumber"] = "S", ["isActive"] = true, ["weightKg"] = 90.10m, ["heightCm"] = "5'10\"" };
        JsonElement updated = await (await _admin.PutAsJsonAsync($"{Url}/{id}", update)).ReadDataAsync();
        Assert.Equal("90.10", updated.GetProperty("weightKg").GetString());   // the trailing zero of a JSON number survives
        Assert.Equal("5'10\"", updated.GetProperty("heightCm").GetString());
    }

    [Fact]
    public async Task Post_requires_a_ten_digit_mobile_number_and_put_keeps_it_when_omitted()
    {
        Dictionary<string, object?> none = Minimal("MOB-1"); none.Remove("mobileNumber");
        Dictionary<string, object?> bad = Minimal("MOB-2"); bad["mobileNumber"] = "98765";
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.PostAsJsonAsync(Url, none)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.PostAsJsonAsync(Url, bad)).StatusCode);
        Assert.Equal(0, await CountAsync());

        Dictionary<string, object?> good = Minimal("MOB-3"); good["mobileNumber"] = "9123456780";
        long id = IdOf(await CreateAsync(good));
        Dictionary<string, object?> dup = Minimal("MOB-4"); dup["mobileNumber"] = "9123456780";
        Assert.Equal(HttpStatusCode.Conflict, (await _admin.PostAsJsonAsync(Url, dup)).StatusCode);

        Dictionary<string, object?> update = new() { ["serviceNumber"] = "S", ["isActive"] = true, ["fullName"] = "Renamed" };
        JsonElement updated = await (await _admin.PutAsJsonAsync($"{Url}/{id}", update)).ReadDataAsync();
        Assert.Equal("9123456780", updated.GetProperty("mobileNumber").GetString());
        Assert.Equal("Renamed", updated.GetProperty("fullName").GetString());
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
            Assert.Equal((int)_adminId, l.UserId);   // the Admin Portal admin's profile id, not NULL and not a legacy staff id
            Assert.Equal(id.ToString(), l.EntityId);
            foreach (string secret in new[] { Aadhaar, Pran, Account, "AGIPR3625F", "ABCDE1234F", "Ranvir", "6598741235", "123456" })
                Assert.DoesNotContain(secret, l.AdditionalData ?? string.Empty);
        });
    }

    // ── Several admins at once: identity, ownership and visibility ──
    // Not repeated here because they are covered where they belong: the integer-overflow / missing-id refusal of create, update, deactivate and
    // reads (PisOwnershipTests, PisImportTests - an id above int.MaxValue cannot be minted into a live Admin Portal session), the Excel upload
    // identity and ownership (PisUploadAuthorizationDatabaseTests, PisImportDatabaseTests), and the Super Admin transfer itself
    // (PisOwnershipTransferDatabaseTests).

    private const string AdminUrl = "/api/v1/admin/users-pis";

    private static async Task<(HttpStatusCode Status, JsonElement Body)> GetBodyAsync(HttpClient client, string url)
    {
        HttpResponseMessage response = await client.GetAsync(url);
        string text = await response.Content.ReadAsStringAsync();
        Assert.False(string.IsNullOrWhiteSpace(text), $"GET {url} returned a blank body ({(int)response.StatusCode}).");
        return (response.StatusCode, JsonDocument.Parse(text).RootElement.Clone());
    }

    private static string[] NsgNumbersOf(JsonElement items) => items.EnumerateArray().Select(i => i.GetProperty("nsgNumber").GetString()!).ToArray();

    /// <summary>Asserts a successful admin read and returns its <c>items</c> (paged endpoint) or the array itself (<c>/all</c>); an empty result must be a message, never an empty list.</summary>
    private static async Task<string[]> AdminNsgNumbersAsync(HttpClient client, string url)
    {
        (HttpStatusCode status, JsonElement body) = await GetBodyAsync(client, url);
        Assert.Equal(HttpStatusCode.OK, status);
        JsonElement data = body.GetProperty("data");
        if (data.ValueKind == JsonValueKind.Null)
        {
            Assert.Equal("No PIS records found.", body.GetProperty("message").GetString());
            return Array.Empty<string>();
        }
        return NsgNumbersOf(data.ValueKind == JsonValueKind.Array ? data : data.GetProperty("items"));
    }

    private async Task<UserPis> StoredAsync(string nsg)
    {
        await using AppDbContext ctx = _factory.CreateContext();
        return await ctx.UsersPis.AsNoTracking().SingleAsync(p => p.NsgNumber == nsg);
    }

    private async Task<JsonElement> ListAsync(HttpClient client, string query = "") => await (await client.GetAsync(Url + query)).ReadDataAsync();

    private static string[] ListedNsgNumbers(JsonElement page) => NsgNumbersOf(page.GetProperty("items"));

    [Fact]
    public async Task The_list_search_filters_pagination_and_total_are_scoped_to_the_calling_admin()
    {
        Dictionary<string, object?> Person(string nsg, string unit, string rank, string name)
        {
            Dictionary<string, object?> body = Minimal(nsg);
            body["nsgUnit"] = unit; body["nsgRank"] = rank; body["fullName"] = name;
            return body;
        }

        long a1 = IdOf(await CreateAsync(Person("LS-A1", "13 SRG", "Inspector", "Anil Kumar"), _admin));
        await CreateAsync(Person("LS-A2", "11 SRG", "Inspector", "Anil Verma"), _admin);
        await CreateAsync(Person("LS-A3", "13 SRG", "Constable", "Asha Rani"), _admin);
        long b1 = IdOf(await CreateAsync(Person("LS-B1", "13 SRG", "Inspector", "Anil Singh"), _adminB));   // same unit, rank and a name that also matches "anil"
        await CreateAsync(Person("LS-B2", "11 SRG", "Constable", "Bhanu"), _adminB);
        await using (AppDbContext ctx = _factory.CreateContext())
            await ctx.Database.ExecuteSqlRawAsync("INSERT INTO `users_pis` (`nsg_number`,`service_number`,`full_name`,`nsg_unit`,`nsg_rank`,`mobile_number`,`is_active`,`created_at_ist`) VALUES ('LS-NOBODY','S','Anil Nobody','13 SRG','Inspector','9876500401',1,NOW(6))");

        // The full list and its total: only A's three records (B's two and the unassigned one are never counted).
        JsonElement all = await ListAsync(_admin);
        Assert.Equal(new[] { "LS-A1", "LS-A2", "LS-A3" }, ListedNsgNumbers(all));
        Assert.Equal(3, all.GetProperty("totalCount").GetInt32());

        // Pagination and the page count are computed over A's records only.
        JsonElement page1 = await ListAsync(_admin, "?pageSize=2&pageNumber=1");
        JsonElement page2 = await ListAsync(_admin, "?pageSize=2&pageNumber=2");
        Assert.Equal((3, 2), (page1.GetProperty("totalCount").GetInt32(), page1.GetProperty("totalPages").GetInt32()));
        Assert.Equal(new[] { "LS-A1", "LS-A2" }, ListedNsgNumbers(page1));
        Assert.Equal(new[] { "LS-A3" }, ListedNsgNumbers(page2));

        // Search and every filter stay inside A's records, even though B's and the unassigned record match the same text and values.
        Assert.Equal(new[] { "LS-A1", "LS-A2" }, ListedNsgNumbers(await ListAsync(_admin, "?search=anil")));
        Assert.Equal(new[] { "LS-A1", "LS-A3" }, ListedNsgNumbers(await ListAsync(_admin, "?nsgUnit=" + Uri.EscapeDataString("13 SRG"))));
        Assert.Equal(new[] { "LS-A1", "LS-A2" }, ListedNsgNumbers(await ListAsync(_admin, "?nsgRank=Inspector")));
        Assert.Equal(new[] { "LS-A1" }, ListedNsgNumbers(await ListAsync(_admin, "?nsgUnit=" + Uri.EscapeDataString("13 SRG") + "&nsgRank=Inspector")));
        Assert.Equal(2, (await ListAsync(_admin, "?search=anil")).GetProperty("totalCount").GetInt32());
        Assert.Equal(HttpStatusCode.OK, (await _admin.DeleteAsync($"{Url}/{a1}")).StatusCode);
        Assert.Equal(new[] { "LS-A1" }, ListedNsgNumbers(await ListAsync(_admin, "?isActive=false")));
        Assert.Equal(new[] { "LS-A2", "LS-A3" }, ListedNsgNumbers(await ListAsync(_admin, "?isActive=true")));

        // Admin B sees only B's two records, with B's own total, search and filters.
        JsonElement mine = await ListAsync(_adminB);
        Assert.Equal(2, mine.GetProperty("totalCount").GetInt32());
        Assert.Equal(new[] { "LS-B1", "LS-B2" }, ListedNsgNumbers(mine));
        Assert.Equal(new[] { "LS-B1" }, ListedNsgNumbers(await ListAsync(_adminB, "?search=anil")));
        Assert.Equal(new[] { "LS-B1" }, ListedNsgNumbers(await ListAsync(_adminB, "?nsgUnit=" + Uri.EscapeDataString("13 SRG"))));

        // The audit rows of the creations name each creating admin's profile id.
        await using AppDbContext read = _factory.CreateContext();
        Dictionary<string, int?> auditUsers = await read.AuditLogs.AsNoTracking().Where(l => l.EntityType == "UserPis" && l.AdditionalData!.Contains("UserPisCreated"))
            .ToDictionaryAsync(l => l.EntityId!, l => l.UserId);
        Assert.Equal((int?)_adminId, auditUsers[a1.ToString()]);
        Assert.Equal((int?)_adminBId, auditUsers[b1.ToString()]);
    }

    [Fact]
    public async Task Each_admin_creates_with_their_own_identity_ownership_and_audit_user()
    {
        JsonElement a = await CreateAsync(Minimal("OWN-A"), _admin);
        JsonElement b = await CreateAsync(Minimal("OWN-B"), _adminB);

        Assert.Equal(((int)_adminId, (int)_adminBId), (a.GetProperty("createdBy").GetInt32(), b.GetProperty("createdBy").GetInt32()));
        Assert.NotEqual(_adminId, _adminBId);
        Assert.Equal((_adminId, (int?)_adminId, (int?)null), ((await StoredAsync("OWN-A")).AssignedTo, (await StoredAsync("OWN-A")).CreatedBy, (await StoredAsync("OWN-A")).UpdatedBy));
        Assert.Equal((_adminBId, (int?)_adminBId), ((await StoredAsync("OWN-B")).AssignedTo, (await StoredAsync("OWN-B")).CreatedBy));

        await using AppDbContext ctx = _factory.CreateContext();
        Dictionary<string, int?> auditUsers = await ctx.AuditLogs.AsNoTracking().Where(l => l.EntityType == "UserPis").ToDictionaryAsync(l => l.EntityId!, l => l.UserId);
        Assert.Equal((int?)_adminId, auditUsers[IdOf(a).ToString()]);
        Assert.Equal((int?)_adminBId, auditUsers[IdOf(b).ToString()]);
    }

    [Fact]
    public async Task Every_protected_read_returns_only_the_callers_own_records_and_hides_the_rest()
    {
        long a1 = IdOf(await CreateAsync(Minimal("RD-A1"), _admin));
        await CreateAsync(Minimal("RD-A2"), _admin);
        long b1 = IdOf(await CreateAsync(Minimal("RD-B1"), _adminB));

        // Admin A: paged list, search, filter, unpaged list.
        Assert.Equal(new[] { "RD-A1", "RD-A2" }, await AdminNsgNumbersAsync(_admin, AdminUrl));
        Assert.Equal(new[] { "RD-A1", "RD-A2" }, await AdminNsgNumbersAsync(_admin, AdminUrl + "/all"));
        Assert.Empty(await AdminNsgNumbersAsync(_admin, AdminUrl + "?search=RD-B1"));          // search cannot reach B's record
        Assert.Empty(await AdminNsgNumbersAsync(_admin, AdminUrl + "?nsgNumber=RD-B1"));       // nor can the exact filter
        (_, JsonElement paged) = await GetBodyAsync(_admin, AdminUrl + "?pageSize=1");
        Assert.Equal(2, paged.GetProperty("data").GetProperty("pagination").GetProperty("totalRecords").GetInt32());   // the total counts only A's records

        // Admin B sees only B's.
        Assert.Equal(new[] { "RD-B1" }, await AdminNsgNumbersAsync(_adminB, AdminUrl));
        Assert.Equal(new[] { "RD-B1" }, await AdminNsgNumbersAsync(_adminB, AdminUrl + "/all"));

        // Another admin's record by id / NSG number is 403 on both controllers; an own record is 200.
        Assert.Equal(HttpStatusCode.Forbidden, (await _admin.GetAsync($"{Url}/{b1}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _admin.GetAsync($"{AdminUrl}/{b1}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _admin.GetAsync($"{AdminUrl}/by-nsg-number/RD-B1")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _adminB.GetAsync($"{Url}/{a1}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _admin.GetAsync($"{Url}/{a1}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _admin.GetAsync($"{AdminUrl}/by-nsg-number/RD-A1")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _adminB.GetAsync($"{AdminUrl}/{b1}")).StatusCode);
    }

    [Fact]
    public async Task A_record_assigned_to_nobody_is_invisible_to_pis_admins()
    {
        await using (AppDbContext ctx = _factory.CreateContext())
            await ctx.Database.ExecuteSqlRawAsync("INSERT INTO `users_pis` (`nsg_number`,`service_number`,`mobile_number`,`is_active`,`created_at_ist`) VALUES ('NOBODY-1','S','9876500301',1,NOW(6))");
        long id = (await StoredAsync("NOBODY-1")).Id;
        await CreateAsync(Minimal("MINE-1"), _admin);

        Assert.Equal(new[] { "MINE-1" }, await AdminNsgNumbersAsync(_admin, AdminUrl));
        Assert.Equal(new[] { "MINE-1" }, await AdminNsgNumbersAsync(_admin, AdminUrl + "/all"));
        Assert.Empty(await AdminNsgNumbersAsync(_adminB, AdminUrl));
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.GetAsync($"{Url}/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.GetAsync($"{AdminUrl}/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.GetAsync($"{AdminUrl}/by-nsg-number/NOBODY-1")).StatusCode);
        // Not changeable either: ownership is only ever given by the Super Admin's transfer.
        Assert.Equal(HttpStatusCode.Forbidden, (await _admin.DeleteAsync($"{Url}/{id}")).StatusCode);
        Assert.True((await StoredAsync("NOBODY-1")).IsActive);
    }

    [Fact]
    public async Task An_admin_cannot_update_or_deactivate_another_admins_record_and_nothing_changes()
    {
        long id = IdOf(await CreateAsync(Minimal("OWN-B1"), _adminB));
        Dictionary<string, object?> change = new() { ["serviceNumber"] = "HIJACK", ["fullName"] = "Hijacked", ["isActive"] = false };

        Assert.Equal(HttpStatusCode.Forbidden, (await _admin.PutAsJsonAsync($"{Url}/{id}", change)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _admin.DeleteAsync($"{Url}/{id}")).StatusCode);

        UserPis stored = await StoredAsync("OWN-B1");
        Assert.Equal(("S-1", true, (int?)null, (long?)_adminBId), (stored.ServiceNumber, stored.IsActive, stored.UpdatedBy, stored.AssignedTo));
        Assert.Null(stored.UpdatedAtIst);
        Assert.Equal(0, await AuditCountAsync(id, "UserPisUpdated") + await AuditCountAsync(id, "UserPisDeactivated"));
    }

    [Fact]
    public async Task After_an_authorized_transfer_created_by_stays_the_creator_and_updated_by_becomes_the_new_owner()
    {
        long id = IdOf(await CreateAsync(Minimal("TR-1"), _admin));   // created by A

        HttpResponseMessage transfer = await _superAdmin.PostAsJsonAsync("/api/v1/admin/pis-personnel/transfer-ownership",
            new { personnelNsgNumber = "TR-1", targetPisAdminNsgNumber = "RAS-0003", reason = "reassigned" });
        Assert.Equal(HttpStatusCode.OK, transfer.StatusCode);

        // The previous owner is locked out at once; the new owner can read and change it.
        Assert.Equal(HttpStatusCode.Forbidden, (await _admin.GetAsync($"{Url}/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _admin.PutAsJsonAsync($"{Url}/{id}", new Dictionary<string, object?> { ["serviceNumber"] = "S-A", ["isActive"] = true })).StatusCode);
        HttpResponseMessage update = await _adminB.PutAsJsonAsync($"{Url}/{id}", new Dictionary<string, object?> { ["serviceNumber"] = "S-B", ["isActive"] = true });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        JsonElement body = await update.ReadDataAsync();
        Assert.Equal(((int)_adminId, (int)_adminBId), (body.GetProperty("createdBy").GetInt32(), body.GetProperty("updatedBy").GetInt32()));
        UserPis stored = await StoredAsync("TR-1");
        Assert.Equal(("S-B", (int?)_adminId, (int?)_adminBId, (long?)_adminBId), (stored.ServiceNumber, stored.CreatedBy, stored.UpdatedBy, stored.AssignedTo));

        await using AppDbContext ctx = _factory.CreateContext();
        AuditLog audit = await ctx.AuditLogs.AsNoTracking().SingleAsync(l => l.EntityType == "UserPis" && l.EntityId == id.ToString() && l.AdditionalData!.Contains("UserPisUpdated"));
        Assert.Equal((int?)_adminBId, audit.UserId);   // the update is attributed to the admin who made it
    }

    [Fact]
    public async Task Protected_reads_reject_legacy_tokens_and_a_deactivated_admins_session_dies_at_once()
    {
        long id = IdOf(await CreateAsync(Minimal("SES-1"), _admin));
        HttpClient garbage = _factory.CreateClientFor(null);
        garbage.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-token");

        string[] reads = { Url, $"{Url}/{id}", AdminUrl, AdminUrl + "/all", $"{AdminUrl}/{id}", $"{AdminUrl}/by-nsg-number/SES-1", AdminUrl + "/upload-batches" };
        foreach (string read in reads)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await _legacyPisAdmin.GetAsync(read)).StatusCode);   // legacy staff token with the PisAdmin role
            Assert.Equal(HttpStatusCode.Unauthorized, (await _employee.GetAsync(read)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await garbage.GetAsync(read)).StatusCode);          // not a token at all
            Assert.Equal(HttpStatusCode.OK, (await _admin.GetAsync(read)).StatusCode);                      // the genuine session still works
        }

        // Deactivating the account ends its session immediately: every PIS read and write is then refused.
        Assert.Equal(HttpStatusCode.OK, (await _superAdmin.PostAsync($"/api/v1/admin/accounts/{_adminId}/deactivate", null)).StatusCode);
        foreach (string read in reads)
            Assert.Equal(HttpStatusCode.Unauthorized, (await _admin.GetAsync(read)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _admin.PostAsJsonAsync(Url, Minimal("SES-2"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _admin.DeleteAsync($"{Url}/{id}")).StatusCode);
        Assert.True((await StoredAsync("SES-1")).IsActive);
    }

    [Fact]
    public async Task Parallel_requests_from_different_admins_never_mix_identities()
    {
        const int each = 8;
        List<(string Prefix, Task<HttpResponseMessage> Request)> requests = new();
        for (int i = 0; i < each; i++)
        {
            requests.Add(("PAR-A", _admin.PostAsJsonAsync(Url, Minimal($"PAR-A{i}"))));
            requests.Add(("PAR-B", _adminB.PostAsJsonAsync(Url, Minimal($"PAR-B{i}"))));
        }
        HttpResponseMessage[] responses = await Task.WhenAll(requests.Select(r => r.Request));
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));

        await using AppDbContext ctx = _factory.CreateContext();
        List<UserPis> stored = await ctx.UsersPis.AsNoTracking().ToListAsync();
        Assert.Equal(each * 2, stored.Count);
        Assert.All(stored.Where(p => p.NsgNumber.StartsWith("PAR-A")), p => Assert.Equal(((int?)_adminId, (long?)_adminId), (p.CreatedBy, p.AssignedTo)));
        Assert.All(stored.Where(p => p.NsgNumber.StartsWith("PAR-B")), p => Assert.Equal(((int?)_adminBId, (long?)_adminBId), (p.CreatedBy, p.AssignedTo)));

        Dictionary<string, int?> auditUsers = await ctx.AuditLogs.AsNoTracking().Where(l => l.EntityType == "UserPis").ToDictionaryAsync(l => l.EntityId!, l => l.UserId);
        Assert.Equal(each * 2, auditUsers.Count);
        Assert.All(stored, p => Assert.Equal(p.CreatedBy, auditUsers[p.Id.ToString()]));   // each record's audit row names the admin who created it
    }
}
