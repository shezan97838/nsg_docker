using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using NSG.Mobile.Domain.Common;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Infrastructure.Persistence;

namespace NSG.Mobile.Tests;

/// <summary>
/// HTTP-level authorization of the PIS admin endpoints on a real host and MySQL: anonymous callers are refused, callers without the
/// PIS_ADMIN role are refused, and a signed-in PIS Admin uploads with their own admin profile id recorded (never 0). Runs only when
/// NSG_TEST_MYSQL names a disposable "*_test" database.
/// </summary>
[Collection("mysql")]
public sealed class PisUploadAuthorizationDatabaseTests : IAsyncLifetime
{
    private const string Base = "/api/v1/admin/users-pis";
    private readonly ApiFactory _factory;
    private HttpClient _pisAdmin = null!, _superAdmin = null!, _employee = null!, _staffPisAdminToken = null!, _anonymous = null!;

    public PisUploadAuthorizationDatabaseTests(DatabaseFixture fixture) => _factory = fixture.Factory;

    public async Task InitializeAsync()
    {
        await ClearAsync();
        (_pisAdmin, _superAdmin) = await PisAdminSession.CreateClientsAsync(_factory);
        _employee = _factory.CreateClientFor(DomainConstants.Roles.Employee);
        _staffPisAdminToken = _factory.CreateClientFor(DomainConstants.Roles.PisAdmin);   // a non-Admin-Portal token that merely carries the role
        _anonymous = _factory.CreateClientFor(null);
    }

    public async Task DisposeAsync() => await ClearAsync();

    private async Task ClearAsync()
    {
        await using AppDbContext ctx = _factory.CreateContext();
        foreach (string table in new[] { "pis_ownership_transfers", "pis_upload_batch_errors", "pis_upload_batch_records", "pis_upload_batches", "admin_refresh_tokens", "admin_profiles", "users_pis" })
            await ctx.Database.ExecuteSqlRawAsync($"DELETE FROM `{table}`");
        await ctx.Database.ExecuteSqlRawAsync("ALTER TABLE `admin_profiles` AUTO_INCREMENT = 1");
    }

    private static MultipartFormDataContent File(params object?[][] people)
    {
        ByteArrayContent file = new(PisXlsx.Build(people));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        return new MultipartFormDataContent { { file, "file", "pis.xlsx" } };
    }

    [MySqlFact]
    public async Task Anonymous_callers_are_rejected_on_both_upload_endpoints_and_nothing_is_written()
    {
        foreach (string route in new[] { "/upload", "/update-upload" })
        {
            HttpResponseMessage response = await _anonymous.PostAsync(Base + route, File(PisXlsx.Person("N1", "9811111101")));
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        await using AppDbContext ctx = _factory.CreateContext();
        Assert.Equal(0, await ctx.PisUploadBatches.CountAsync());
        Assert.False(await ctx.UsersPis.AnyAsync(p => p.NsgNumber == "N1"));
    }

    [MySqlFact]
    public async Task Callers_without_the_PIS_admin_role_are_rejected_on_both_upload_endpoints()
    {
        foreach (HttpClient client in new[] { _superAdmin, _employee, _staffPisAdminToken })
            foreach (string route in new[] { "/upload", "/update-upload" })
            {
                HttpResponseMessage response = await client.PostAsync(Base + route, File(PisXlsx.Person("N1", "9811111101")));
                Assert.True(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden, $"{route}: {(int)response.StatusCode}");
            }

        await using AppDbContext ctx = _factory.CreateContext();
        Assert.Equal(0, await ctx.PisUploadBatches.CountAsync());
    }

    [MySqlFact]
    public async Task Every_other_admin_endpoint_also_rejects_anonymous_callers()
    {
        foreach (string url in new[] { Base, Base + "/all", Base + "/1", Base + "/by-nsg-number/N1", Base + "/upload-batches", Base + "/upload-batches/1" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await _anonymous.GetAsync(url)).StatusCode);
    }

    [MySqlFact]
    public async Task A_signed_in_PIS_admin_uploads_through_both_endpoints_with_their_own_profile_id_recorded()
    {
        HttpResponseMessage insert = await _pisAdmin.PostAsync(Base + "/upload", File(PisXlsx.Person("N1", "9811111101")));
        Assert.Equal(HttpStatusCode.OK, insert.StatusCode);
        Assert.Equal(1, (await insert.ReadDataAsync()).GetProperty("inserted").GetInt32());

        HttpResponseMessage update = await _pisAdmin.PostAsync(Base + "/update-upload", File(PisXlsx.Person("N1", "9811111101", "Renamed")));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal(1, (await update.ReadDataAsync()).GetProperty("updated").GetInt32());

        await using AppDbContext ctx = _factory.CreateContext();
        long adminProfileId = await ctx.AdminProfiles.AsNoTracking().Where(a => a.NsgNumber == "PAS-0002").Select(a => a.Id).SingleAsync();
        List<PisUploadBatch> batches = await ctx.PisUploadBatches.AsNoTracking().OrderBy(b => b.Id).ToListAsync();
        Assert.Equal(2, batches.Count);
        Assert.All(batches, b => Assert.Equal(adminProfileId, b.UploadedBy));
        Assert.NotEqual(0, adminProfileId);
        UserPis person = await ctx.UsersPis.AsNoTracking().SingleAsync(p => p.NsgNumber == "N1");
        Assert.Equal(((int)adminProfileId, (int)adminProfileId, "Renamed"), (person.CreatedBy, person.UpdatedBy, person.FullName));

        // The admin sees their own batches through the history endpoint; the super admin (a different identity) does not get access.
        Assert.Equal(HttpStatusCode.OK, (await _pisAdmin.GetAsync(Base + "/upload-batches")).StatusCode);
        Assert.True((await _superAdmin.GetAsync(Base + "/upload-batches")).StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
    }

    [MySqlFact]
    public async Task Update_upload_persists_updated_by_and_updated_at_ist_for_admin_profile_ids_2_and_5_across_multiple_records()
    {
        await using (AppDbContext checkAdmin2 = _factory.CreateContext())
        {
            long admin2Id = await checkAdmin2.AdminProfiles.AsNoTracking().Where(a => a.NsgNumber == "PAS-0002").Select(a => a.Id).SingleAsync();
            Assert.Equal(2L, admin2Id);
        }

        // Provision PAS-0003, PAS-0004, and PAS-0005 so PAS-0005 gets admin_profile_id = 5.
        await PisAdminSession.CreateAndSignInPisAdminAsync(_factory, _superAdmin, "PAS-0003", "9822220003");
        await PisAdminSession.CreateAndSignInPisAdminAsync(_factory, _superAdmin, "PAS-0004", "9822220004");
        HttpClient admin5 = await PisAdminSession.CreateAndSignInPisAdminAsync(_factory, _superAdmin, "PAS-0005", "9822220005");

        await using (AppDbContext checkAdmin5 = _factory.CreateContext())
        {
            long admin5Id = await checkAdmin5.AdminProfiles.AsNoTracking().Where(a => a.NsgNumber == "PAS-0005").Select(a => a.Id).SingleAsync();
            Assert.Equal(5L, admin5Id);
        }

        // 1. Insert two personnel records as Admin 2.
        DateTime beforeInsert = IndianStandardTime.Now.AddSeconds(-2);
        HttpResponseMessage insert = await _pisAdmin.PostAsync(Base + "/upload",
            File(PisXlsx.Person("M1", "9811111201", "Initial One"), PisXlsx.Person("M2", "9811111202", "Initial Two")));
        DateTime afterInsert = IndianStandardTime.Now.AddSeconds(2);
        Assert.Equal(HttpStatusCode.OK, insert.StatusCode);
        Assert.Equal(2, (await insert.ReadDataAsync()).GetProperty("inserted").GetInt32());

        await using (AppDbContext afterInsertCtx = _factory.CreateContext())
        {
            List<UserPis> inserted = await afterInsertCtx.UsersPis.AsNoTracking().Where(p => p.NsgNumber == "M1" || p.NsgNumber == "M2").OrderBy(p => p.NsgNumber).ToListAsync();
            Assert.Equal(2, inserted.Count);
            Assert.All(inserted, p =>
            {
                Assert.Equal((2, (int?)null, (DateTime?)null, (long?)2L), (p.CreatedBy, p.UpdatedBy, p.UpdatedAtIst, p.AssignedTo));
                Assert.InRange(p.CreatedAtIst, beforeInsert, afterInsert);
            });
        }

        // 2. Update both records via POST /update-upload as Admin 2 (leave M1 ServiceNumber blank to verify blank preservation).
        object?[] m1ByAdmin2 = PisXlsx.Person("M1", "9811111201", "Admin2 One");
        m1ByAdmin2[2] = null;
        object?[] m2ByAdmin2 = PisXlsx.Person("M2", "9811111202", "Admin2 Two");

        DateTime beforeUpdate2 = IndianStandardTime.Now.AddSeconds(-2);
        HttpResponseMessage update2 = await _pisAdmin.PostAsync(Base + "/update-upload", File(m1ByAdmin2, m2ByAdmin2));
        DateTime afterUpdate2 = IndianStandardTime.Now.AddSeconds(2);
        Assert.Equal(HttpStatusCode.OK, update2.StatusCode);
        Assert.Equal(2, (await update2.ReadDataAsync()).GetProperty("updated").GetInt32());

        await using (AppDbContext afterUpdate2Ctx = _factory.CreateContext())
        {
            List<UserPis> rows = await afterUpdate2Ctx.UsersPis.AsNoTracking().Where(p => p.NsgNumber == "M1" || p.NsgNumber == "M2").OrderBy(p => p.NsgNumber).ToListAsync();
            Assert.All(rows, p =>
            {
                Assert.Equal((2, 2, (long?)2L), (p.CreatedBy, p.UpdatedBy, p.AssignedTo));
                Assert.NotNull(p.UpdatedAtIst);
                Assert.InRange(p.UpdatedAtIst!.Value, beforeUpdate2, afterUpdate2);
            });
            Assert.Equal(("Admin2 One", "IRLA-M1"), (rows[0].FullName, rows[0].ServiceNumber));
            Assert.Equal("Admin2 Two", rows[1].FullName);
        }

        // 3. Transfer both records to Admin 5 and update them via POST /update-upload as Admin 5 (leave M2 DOB blank).
        foreach (string nsg in new[] { "M1", "M2" })
        {
            HttpResponseMessage transfer = await _superAdmin.PostAsJsonAsync("/api/v1/admin/pis-personnel/transfer-ownership",
                new { personnelNsgNumber = nsg, targetPisAdminNsgNumber = "PAS-0005", reason = "Reassign for update test" });
            Assert.Equal(HttpStatusCode.OK, transfer.StatusCode);
        }

        object?[] m1ByAdmin5 = PisXlsx.Person("M1", "9811111201", "Admin5 One");
        object?[] m2ByAdmin5 = PisXlsx.Person("M2", "9811111202", "Admin5 Two");
        m2ByAdmin5[PisXlsx.Dob] = null;

        DateTime beforeUpdate5 = IndianStandardTime.Now.AddSeconds(-2);
        HttpResponseMessage update5 = await admin5.PostAsync(Base + "/update-upload", File(m1ByAdmin5, m2ByAdmin5));
        DateTime afterUpdate5 = IndianStandardTime.Now.AddSeconds(2);
        Assert.Equal(HttpStatusCode.OK, update5.StatusCode);
        Assert.Equal(2, (await update5.ReadDataAsync()).GetProperty("updated").GetInt32());

        await using AppDbContext afterUpdate5Ctx = _factory.CreateContext();
        List<UserPis> finalRows = await afterUpdate5Ctx.UsersPis.AsNoTracking().Where(p => p.NsgNumber == "M1" || p.NsgNumber == "M2").OrderBy(p => p.NsgNumber).ToListAsync();
        Assert.Equal(2, finalRows.Count);
        Assert.All(finalRows, p =>
        {
            Assert.Equal((2, 5, (long?)5L), (p.CreatedBy, p.UpdatedBy, p.AssignedTo));
            Assert.NotNull(p.UpdatedAtIst);
            Assert.InRange(p.UpdatedAtIst!.Value, beforeUpdate5, afterUpdate5);
        });
        Assert.Equal("Admin5 One", finalRows[0].FullName);
        Assert.Equal(("Admin5 Two", new DateOnly(1985, 6, 15)), (finalRows[1].FullName, finalRows[1].DateOfBirth));
    }
}
