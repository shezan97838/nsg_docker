using System.Net;
using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
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
        foreach (string table in new[] { "pis_upload_batch_errors", "pis_upload_batch_records", "pis_upload_batches", "admin_refresh_tokens", "admin_profiles", "users_pis" })
            await ctx.Database.ExecuteSqlRawAsync($"DELETE FROM `{table}`");
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
}
